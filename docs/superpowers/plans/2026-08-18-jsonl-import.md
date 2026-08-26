# Codex JSONL Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Import validated external Codex rollout JSONL files into a chosen existing or newly created local Codex project so they appear in the Codex sidebar after restart.

**Architecture:** A Core import service parses and previews external JSONL files without writing. A separate apply operation, guarded by an exited-Codex process check in the UI, copies validated rollouts, writes state/catalog indexes, updates global project state atomically, and restores a timestamped backup on failure. A modal WPF dialog drives file selection, destination selection, duplicate-ID handling, and explicit confirmation.

**Tech Stack:** .NET 8, WPF, `System.Text.Json.Nodes`, `Microsoft.Data.Sqlite`, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-18-jsonl-import-design.md`

## Global Constraints

- Do not read, display, or modify API keys, `auth.json`, attachments, project source files, or the external source JSONL files.
- Apply is permitted only after Codex and ChatGPT have fully exited.
- Reject malformed UTF-8 JSONL, a missing `session_meta`, invalid/missing UUID IDs, and duplicate IDs unless the user explicitly chooses generated IDs.
- Preserve event content; only the session meta provider and explicit duplicate-ID references may be transformed.
- Back up overwritten files plus `state_5.sqlite`, `sqlite/codex-dev.db` when present, and `.codex-global-state.json`; restore on every failed apply.
- No Git repository is present in this workspace, so record test evidence instead of committing.

---

### Task 1: Import Domain Models And Read-Only JSONL Preview

**Files:**
- Create: `src/CodexConversationManager.Core/Import/ConversationImportModels.cs`
- Create: `src/CodexConversationManager.Core/Import/ConversationImportPreviewService.cs`
- Test: `tests/CodexConversationManager.Tests/Import/ConversationImportPreviewServiceTests.cs`

**Interfaces:**
- Produces `ConversationImportCandidate`, `ConversationImportIssue`, `ConversationImportPreview`, `ImportProviderMode`, and `DuplicateIdResolution`.
- Produces `Task<ConversationImportPreview> PreviewAsync(IReadOnlyList<string> sourcePaths, string currentProvider, IReadOnlySet<string> existingIds, DuplicateIdResolution duplicateResolution, CancellationToken cancellationToken = default)`.

- [ ] **Step 1: Write failing tests for a valid rollout preview and invalid JSONL rejection**

```csharp
[Fact]
public async Task Preview_reads_session_meta_and_does_not_modify_source_file()
{
    var path = await WriteJsonlAsync("rollout.jsonl", ValidRollout("11111111-1111-7111-8111-111111111111"));
    var before = await File.ReadAllTextAsync(path);

    var preview = await new ConversationImportPreviewService().PreviewAsync([path], "openai", new HashSet<string>(), DuplicateIdResolution.Reject);

    var candidate = Assert.Single(preview.Candidates);
    Assert.Equal("11111111-1111-7111-8111-111111111111", candidate.SourceId);
    Assert.Equal("openai", candidate.TargetProvider);
    Assert.Equal(before, await File.ReadAllTextAsync(path));
}

[Fact]
public async Task Preview_rejects_jsonl_without_a_valid_session_meta_uuid()
{
    var path = await WriteJsonlAsync("invalid.jsonl", "{\"type\":\"event_msg\"}\n");

    var preview = await new ConversationImportPreviewService().PreviewAsync([path], "openai", new HashSet<string>(), DuplicateIdResolution.Reject);

    Assert.Empty(preview.Candidates);
    Assert.Contains(preview.Issues, issue => issue.Message.Contains("session_meta"));
}
```

- [ ] **Step 2: Run the preview tests to verify they fail**

Run: `& '.\build-tools\dotnet\dotnet.exe' test 'tests\CodexConversationManager.Tests\CodexConversationManager.Tests.csproj' --filter 'FullyQualifiedName~ConversationImportPreviewServiceTests' --no-restore`

Expected: compilation failure because import service and models do not exist.

- [ ] **Step 3: Implement the read-only parser and domain records**

```csharp
public enum DuplicateIdResolution { Reject, GenerateNewId }
public enum ImportProviderMode { CurrentLogin, PreserveSource }

public sealed record ConversationImportCandidate(
    string SourcePath, string SourceId, string TargetId, string Title,
    string Cwd, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    string SourceProvider, string TargetProvider, bool HasDuplicateId);

public sealed class ConversationImportPreviewService
{
    public async Task<ConversationImportPreview> PreviewAsync(...)
    {
        // Read with FileShare.ReadWrite, parse every line, locate session_meta,
        // validate the UUID, and only create candidates for complete files.
    }
}
```

Read `session_meta.payload` fields structurally. Use the candidate's UUID as `TargetId` unless it already exists and `GenerateNewId` is selected. Keep raw source lines internal to the preview/service rather than displaying them.

- [ ] **Step 4: Add failing tests for duplicate handling and current-provider rewrite selection**

```csharp
[Fact]
public async Task Preview_generates_a_new_target_id_only_when_explicitly_requested()
{
    var sourceId = "11111111-1111-7111-8111-111111111111";
    var path = await WriteJsonlAsync("duplicate.jsonl", ValidRollout(sourceId, "other"));
    var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { sourceId };

    var rejected = await service.PreviewAsync([path], "openai", ids, DuplicateIdResolution.Reject);
    var copied = await service.PreviewAsync([path], "openai", ids, DuplicateIdResolution.GenerateNewId);

    Assert.Empty(rejected.Candidates);
    Assert.NotEqual(sourceId, Assert.Single(copied.Candidates).TargetId);
}
```

- [ ] **Step 5: Implement duplicate reporting and provider modes; re-run the preview test suite**

Run the command in Step 2.

Expected: all preview tests pass.

### Task 2: Atomic Import Into State, Catalog, And Global Project State

**Files:**
- Create: `src/CodexConversationManager.Core/Import/ConversationImportService.cs`
- Create: `src/CodexConversationManager.Core/Import/ImportBackupService.cs`
- Create: `src/CodexConversationManager.Core/Import/GlobalProjectStateWriter.cs`
- Test: `tests/CodexConversationManager.Tests/Import/ConversationImportServiceTests.cs`

**Interfaces:**
- Consumes `CodexPaths`, `ConversationImportPreview`, `ConversationImportCandidate`, and `DuplicateIdResolution` from Task 1.
- Produces `Task<ConversationImportResult> ApplyAsync(ConversationImportRequest request, CancellationToken cancellationToken = default)`.
- `ConversationImportRequest` selects `ExistingProject(string ProjectId)`, `Projectless`, or `NewProject(string ParentDirectory, string ProjectName)` and has an explicit `ImportProviderMode`.

- [ ] **Step 1: Write failing test for importing into an existing project**

```csharp
[Fact]
public async Task Apply_copies_rollout_registers_thread_and_assigns_existing_project()
{
    await CreateCodexRootAsync(root, projectId: "daily", projectName: "日常对话");
    var source = await WriteSourceAsync(ValidRollout(sourceId));
    var request = Request(source, new ExistingProjectDestination("daily"));

    var result = await new ConversationImportService(CodexPaths.FromRoot(root), backupRoot).ApplyAsync(request);

    Assert.True(File.Exists(result.ImportedFiles.Single()));
    Assert.True(await ThreadExistsAsync(Path.Combine(root, "state_5.sqlite"), sourceId));
    Assert.Equal("daily", await ReadProjectAssignmentAsync(Path.Combine(root, ".codex-global-state.json"), sourceId));
}
```

- [ ] **Step 2: Run the service test to verify it fails**

Run: `& '.\build-tools\dotnet\dotnet.exe' test 'tests\CodexConversationManager.Tests\CodexConversationManager.Tests.csproj' --filter 'FullyQualifiedName~ConversationImportServiceTests' --no-restore`

Expected: compilation failure because import service and destination records do not exist.

- [ ] **Step 3: Implement backup and file-copy stage**

```csharp
public sealed class ImportBackupService
{
    public Task<ImportBackup> CreateAsync(CodexPaths paths, IEnumerable<string> destinationFiles, string backupRoot, CancellationToken cancellationToken = default);
    public Task RestoreAsync(ImportBackup backup, CancellationToken cancellationToken = default);
}
```

Copy source JSONL to a temporary file below `paths.Sessions`, transform only `session_meta.payload.model_provider` and all JSON string values equal to the source UUID when a generated ID is requested, validate reparse, then atomically move to `rollout-<target-id>.jsonl`.

- [ ] **Step 4: Implement transactional state/catalog registration**

Use `PRAGMA table_info` to require the columns present in the current Codex schema. Insert the complete `threads` row with `archived = 0`, `rollout_path`, title/cwd/timestamps/provider, non-empty preview, and recency from updated time. Insert a `local_thread_catalog` row only when that table and required columns exist. Wrap each database's changes in one SQLite transaction. Validate inserted rows by their target ID before committing the overall operation.

- [ ] **Step 5: Implement global state project assignment and projectless registration**

Use `JsonNode` and a temporary sibling file. Existing-project import adds an assignment object and target id to that project's order list. Projectless import adds the id only to `projectless-thread-ids`. Always remove stale occurrences from the opposite structures before atomically replacing `.codex-global-state.json`.

- [ ] **Step 6: Add and run failing tests for new-project creation and rollback**

```csharp
[Fact]
public async Task Apply_creates_requested_directory_and_registers_new_project() { /* assert Directory.Exists and local-projects entry */ }

[Fact]
public async Task Apply_restores_files_databases_and_global_state_when_validation_fails() { /* inject a failure after state write and compare snapshots */ }
```

- [ ] **Step 7: Implement new-project creation and failure rollback; re-run service tests**

Create only the requested leaf directory after validating the normalized parent directory. Create a UUID project id, append it to `local-projects` and `project-order`, then use the existing-project assignment routine. On every exception restore backup and remove only the newly-created empty project directory.

Run the command in Step 2.

Expected: all import service tests pass.

### Task 3: Import Dialog View Model And Guarded Apply Flow

**Files:**
- Create: `src/CodexConversationManager.App/ViewModels/ConversationImportViewModel.cs`
- Create: `src/CodexConversationManager.App/Views/ConversationImportDialog.xaml`
- Create: `src/CodexConversationManager.App/Views/ConversationImportDialog.xaml.cs`
- Test: `tests/CodexConversationManager.Tests/ViewModels/ConversationImportViewModelTests.cs`

**Interfaces:**
- Consumes `ConversationImportPreviewService`, `ConversationImportService`, `ICodexProjectSidebarProvider`, `IDeletionProcessGuard`, and `CodexPaths`.
- Produces `LoadFilesAsync(IReadOnlyList<string>)`, `PreviewAsync()`, `ApplyAsync()`, `CanApply`, `Status`, candidate/issue lists, destination selection, and confirmation text.

- [ ] **Step 1: Write failing view-model test for an exited-Codex guard**

```csharp
[Fact]
public async Task Apply_refuses_to_write_when_codex_is_running()
{
    var viewModel = CreateViewModel(new BlockingGuard());
    await viewModel.LoadFilesAsync([validFile]);
    viewModel.Confirmation = "导入对话";

    var applied = await viewModel.ApplyAsync();

    Assert.False(applied);
    Assert.Contains("完全退出 Codex", viewModel.Status);
}
```

- [ ] **Step 2: Run the view-model test to verify it fails**

Run: `& '.\build-tools\dotnet\dotnet.exe' test 'tests\CodexConversationManager.Tests\CodexConversationManager.Tests.csproj' --filter 'FullyQualifiedName~ConversationImportViewModelTests' --no-restore`

Expected: compilation failure because the import view model does not exist.

- [ ] **Step 3: Implement view-model state and guard**

`CanApply` must require a successful preview with at least one candidate, no unresolved conflict, a valid destination, `Confirmation == "导入对话"`, and no active work. `ApplyAsync` runs `processGuard.CheckAsync` immediately before calling the core service and exposes the backup path and imported count in its result status.

- [ ] **Step 4: Implement the WPF dialog with preview before confirmation**

The dialog has a file picker limited to `.jsonl`, a visible list of accepted/rejected files, radio choices for current provider/preserve provider and reject/generate duplicate IDs, existing project / normal recent / new project destination controls, and a confirmation text box. New project controls select a parent folder and provide a project name. The apply button is disabled until `CanApply` is true.

- [ ] **Step 5: Re-run view-model tests and add XAML layout assertions**

Add assertions for the confirmation binding, `CanApply` binding, current-provider text, and new-project controls in `MainWindowLayoutTests` or a new `ConversationImportDialogLayoutTests` file. Run the command in Step 2.

Expected: all import view-model and layout tests pass.

### Task 4: Application Composition, Main Window Entry Point, And End-To-End Verification

**Files:**
- Modify: `src/CodexConversationManager.App/App.xaml.cs`
- Modify: `src/CodexConversationManager.App/MainWindow.xaml`
- Modify: `src/CodexConversationManager.App/MainWindow.xaml.cs`
- Modify: `README-使用说明.md`
- Test: `tests/CodexConversationManager.Tests/ViewModels/MainWindowLayoutTests.cs`

**Interfaces:**
- Consumes the Task 2 service and Task 3 dialog.
- Produces a top-level `导入对话` command that opens the dialog and refreshes inventory only after a successful import.

- [ ] **Step 1: Write a failing layout test for the import entry point**

```csharp
[Fact]
public void Main_window_exposes_an_import_conversation_command()
{
    var xaml = ReadMainWindowXaml();
    Assert.Contains("Content=\"导入对话\"", xaml);
    Assert.Contains("ImportConversation_Click", xaml);
}
```

- [ ] **Step 2: Run the layout test to verify it fails**

Run: `& '.\build-tools\dotnet\dotnet.exe' test 'tests\CodexConversationManager.Tests\CodexConversationManager.Tests.csproj' --filter 'FullyQualifiedName~Main_window_exposes_an_import' --no-restore`

Expected: test failure because the button is absent.

- [ ] **Step 3: Compose services and add the guarded entry point**

Create the import preview/service instances from the same `CodexPaths`, global state path, and app backup directory used by the rest of the app. The click handler displays a warning that Codex must be fully exited, opens the dialog only after acknowledgement, and calls `_viewModel.RefreshAsync()` only when `ShowDialog()` returns `true`.

- [ ] **Step 4: Update user-facing documentation**

Document that the importer supports only Codex-format rollout JSONL, requires full Codex exit, creates backups, rejects duplicates by default, and requires a Codex restart after success. Include the importer backup directory location.

- [ ] **Step 5: Run all tests and publish a new non-overwriting build**

Run: `& '.\build-tools\dotnet\dotnet.exe' test 'CodexConversationManager.sln' --no-restore`

Expected: zero failures.

Run: `& '.\build-tools\dotnet\dotnet.exe' publish 'src\CodexConversationManager.App\CodexConversationManager.App.csproj' -c Release -o '.\publish-next-33' --no-restore`

Expected: `publish-next-33\CodexConversationManager.App.exe` exists.
