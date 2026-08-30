# Codex 0.151 Compatibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Release Codex Manager `v0.2.0` with reliable Codex Desktop `0.151.x` discovery, modern history reading, real sub-agent safety, schema-aware projects/import, and complete deletion verification.

**Architecture:** Add capability-based readers around each Codex store and merge their evidence by thread ID. Keep portable imports in legacy-compatible JSONL form, but make all reads understand both legacy and paginated records. Route destructive operations through backup, schema inspection, mutation, rollback, and symmetric residual verification.

**Tech Stack:** .NET 8, C# 12, WPF, Avalonia, Microsoft.Data.Sqlite, xUnit, PowerShell, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-08-30-codex-0151-compatibility-design.md`

## Global Constraints

- Release version is exactly `0.2.0`.
- Legacy Codex stores must remain supported when new tables or columns are absent.
- Tests never write to the user's real `.codex` directory.
- The original imported JSONL is never modified.
- Every destructive operation fails closed before rollout deletion if backup or database lock acquisition fails.
- No unrelated UI redesign is included.

---

### Task 1: Select the newest usable Codex App Server

**Files:**
- Modify: `src/CodexConversationManager.Core/AppServer/CodexExecutableLocator.cs`
- Test: `tests/CodexConversationManager.Tests/AppServer/CodexExecutableLocatorTests.cs`

**Interfaces:**
- Consumes: PATH directories and `%LOCALAPPDATA%\OpenAI\Codex\bin\*\codex.exe`.
- Produces: `CodexExecutableLocator.Locate(IEnumerable<string>? pathDirectories, string? localAppData, Func<string, string?>? versionProbe)` returning the highest-version usable executable.

- [ ] **Step 1: Write failing candidate-selection tests**

Add tests using temporary fake files and a deterministic probe:

```csharp
[Fact]
public void Locate_prefers_newer_desktop_binary_over_older_path_cli()
{
    var selected = CodexExecutableLocator.Locate(
        [_pathBin], _localAppData,
        path => path.Contains("desktop-0151") ? "codex-cli 0.151.0-alpha.7.2" : "codex-cli 0.130.0");
    Assert.Contains("desktop-0151", selected);
}

[Fact]
public void Locate_skips_candidates_that_cannot_report_a_version()
{
    var selected = CodexExecutableLocator.Locate(
        [_pathBin], _localAppData,
        path => path.Contains("broken") ? null : "codex-cli 0.150.0-alpha.8");
    Assert.DoesNotContain("broken", selected);
    Assert.Contains("working", selected);
}
```

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
.\build-tools\dotnet\dotnet.exe test tests\CodexConversationManager.Tests\CodexConversationManager.Tests.csproj --no-restore --filter FullyQualifiedName~CodexExecutableLocatorTests
```

Expected: FAIL because the current locator only considers PATH order and has no Desktop/version probe.

- [ ] **Step 3: Implement candidate discovery and semantic pre-release comparison**

Keep parsing private and small. Probe candidates with `codex --version` by default, collect valid versions, order by numeric major/minor/patch and pre-release numeric segments, then Desktop priority for ties. Preserve the existing single-argument call through optional parameters.

- [ ] **Step 4: Run locator and App Server tests**

Expected: all locator and `CodexAppServerClientTests` pass.

- [ ] **Step 5: Commit**

```powershell
git add src/CodexConversationManager.Core/AppServer/CodexExecutableLocator.cs tests/CodexConversationManager.Tests/AppServer/CodexExecutableLocatorTests.cs
git commit -m "Select newest Codex App Server binary"
```

### Task 2: Share modern and legacy rollout message parsing

**Files:**
- Create: `src/CodexConversationManager.Core/LocalData/RolloutMessageExtractor.cs`
- Modify: `src/CodexConversationManager.Core/Inventory/ConversationDetailService.cs`
- Modify: `src/CodexConversationManager.Core/Import/ConversationImportPreviewService.cs`
- Modify: `src/CodexConversationManager.Core/Import/ConversationImportService.cs`
- Test: `tests/CodexConversationManager.Tests/Inventory/ConversationDetailServiceTests.cs`
- Test: `tests/CodexConversationManager.Tests/Import/ConversationImportPreviewServiceTests.cs`
- Test: `tests/CodexConversationManager.Tests/Import/ConversationImportServiceTests.cs`

**Interfaces:**
- Produces: `RolloutDisplayMessage(string Role, string Kind, string Text, string? Identity)`.
- Produces: `RolloutMessageExtractor.TryExtract(JsonElement root, out RolloutDisplayMessage message)` for legacy events, modern completed items, and displayable response items.
- Consumes: the extractor in detail display, title preview, and compatibility-event generation.

- [ ] **Step 1: Write failing modern detail tests**

Cover `UserMessage`, commentary and final `AgentMessage`, mixed-case `Text/text`, and a file containing both modern and generated legacy copies. Assert user/assistant roles and no adjacent duplicate blocks.

- [ ] **Step 2: Write failing modern import-title test**

```csharp
[Fact]
public async Task Preview_uses_modern_user_message_when_metadata_has_no_title()
{
    var preview = await PreviewModernAsync("A modern title");
    Assert.Equal("A modern title", Assert.Single(preview.Candidates).Title);
}
```

- [ ] **Step 3: Run focused tests and verify RED**

Expected: modern detail/title assertions fail against the legacy-only readers.

- [ ] **Step 4: Implement the shared extractor**

Map `UserMessage` to `user`, `AgentMessage` to `assistant`, assemble textual content entries, preserve phase as kind, and use item ID as identity. Keep malformed or non-text tool items non-displayable.

- [ ] **Step 5: Replace duplicated parsing and deduplicate compatibility copies**

Use the shared extractor in all three consumers. Detail display skips an immediately duplicated same-role/same-text legacy compatibility event; it does not globally collapse legitimately repeated messages.

- [ ] **Step 6: Run all detail and import tests**

Expected: focused tests pass, including existing `v0.1.11` conversion coverage.

- [ ] **Step 7: Commit**

```powershell
git add src/CodexConversationManager.Core/LocalData/RolloutMessageExtractor.cs src/CodexConversationManager.Core/Inventory/ConversationDetailService.cs src/CodexConversationManager.Core/Import tests/CodexConversationManager.Tests/Inventory/ConversationDetailServiceTests.cs tests/CodexConversationManager.Tests/Import
git commit -m "Read modern Codex rollout messages"
```

### Task 3: Read real spawn relationships and modern sidebar state

**Files:**
- Create: `src/CodexConversationManager.Core/LocalData/ThreadRelationshipDatabaseReader.cs`
- Create: `src/CodexConversationManager.Core/LocalData/IThreadRelationshipEvidenceSource.cs`
- Modify: `src/CodexConversationManager.Core/Domain/ConversationEvidence.cs`
- Modify: `src/CodexConversationManager.Core/Inventory/ConversationInventoryService.cs`
- Modify: `src/CodexConversationManager.Core/LocalData/CodexProjectSidebarReader.cs`
- Modify: `src/CodexConversationManager.Core/LocalData/CodexProjectSidebarSnapshot.cs`
- Modify: `src/CodexConversationManager.App/App.xaml.cs`
- Modify: `src/CodexConversationManager.App/ViewModels/MainViewModel.cs`
- Modify: `src/CodexConversationManager.App/ViewModels/ConversationTreeNodeViewModel.cs`
- Test: `tests/CodexConversationManager.Tests/LocalData/LocalEvidenceReaderTests.cs`
- Test: `tests/CodexConversationManager.Tests/Inventory/ConversationInventoryServiceTests.cs`
- Test: `tests/CodexConversationManager.Tests/Deletion/DeletionSafetyTests.cs`

**Interfaces:**
- Produces: `ThreadRelationshipEvidence(string ParentId, string ChildId)`.
- Produces: `IThreadRelationshipEvidenceSource.ReadAsync(CancellationToken)`.
- Adds: relationship source as an optional final dependency of `ConversationInventoryService` for legacy test compatibility.

- [ ] **Step 1: Write failing relationship tests**

Create fixture edges `parent -> child -> grandchild`. Assert the parent evidence contains both descendants and deletion planning blocks the parent even when only the parent is selected.

- [ ] **Step 2: Write failing sidebar compatibility tests**

Create a state database with `projects`, `project_roots`, `threads.project_id`, `thread_sections`, `thread_section_id`, `is_pinned`, `preview`, and `recency_at_ms`. Assert modern populated values override conflicting legacy JSON, while an old schema still reads legacy projects.

- [ ] **Step 3: Write failing recent-membership tests**

Assert visible unarchived rows are returned in recency order, empty-preview and archived rows are excluded, and a visible sub-agent is not automatically excluded. Remove the old arbitrary archived `LIMIT 6` expectation.

- [ ] **Step 4: Run focused tests and verify RED**

Expected: no relationship reader exists and current sidebar behavior fails modern precedence/recent tests.

- [ ] **Step 5: Implement schema-aware relationship and sidebar readers**

Inspect table/column existence before queries. Build the transitive descendant closure in memory with cycle protection. Merge modern projects and assignments over legacy fallbacks without requiring modern tables.

- [ ] **Step 6: Wire evidence into inventory and UI composition**

Populate `DescendantIds` during merge. Extend the sidebar snapshot with pinned IDs and named section memberships, then render pinned/section nodes without changing the existing category names or removing the sub-agent category button.

- [ ] **Step 7: Run local-data, inventory, deletion, and ViewModel tests**

Expected: all pass for both old and modern fixtures.

- [ ] **Step 8: Commit**

```powershell
git add src/CodexConversationManager.Core/LocalData src/CodexConversationManager.Core/Domain/ConversationEvidence.cs src/CodexConversationManager.Core/Inventory/ConversationInventoryService.cs src/CodexConversationManager.App/App.xaml.cs tests/CodexConversationManager.Tests
git commit -m "Track modern projects and subagent relationships"
```

### Task 4: Back up, delete, and audit every modern thread store

**Files:**
- Modify: `src/CodexConversationManager.Core/LocalData/CodexPaths.cs`
- Create: `src/CodexConversationManager.Core/Deletion/ThreadDeletionBackupService.cs`
- Create: `src/CodexConversationManager.Core/Deletion/ResidualAuditReport.cs`
- Modify: `src/CodexConversationManager.Core/Deletion/GhostResidualCleaner.cs`
- Modify: `src/CodexConversationManager.Core/Deletion/ResidualAuditor.cs`
- Modify: `src/CodexConversationManager.Core/Deletion/LocalPermanentDeleteService.cs`
- Modify: `src/CodexConversationManager.App/App.xaml.cs`
- Test: `tests/CodexConversationManager.Tests/Deletion/GhostResidualCleanerTests.cs`
- Test: `tests/CodexConversationManager.Tests/Deletion/PermanentDeleteServiceTests.cs`
- Test: `tests/CodexConversationManager.Tests/Deletion/DeletionSafetyTests.cs`

**Interfaces:**
- Adds: `CodexPaths.ThreadHistoryDatabase` resolving `.codex/thread_history_1.sqlite`.
- Produces: `ThreadDeletionBackupService.CreateAsync(id, paths, knownSessionPaths, cancellationToken)` and `RestoreAsync(...)`.
- Produces: `ResidualAuditReport(IReadOnlyList<string> Stores)` with `HasResiduals`.

- [ ] **Step 1: Write failing modern residual tests**

Build temporary `thread_history_1.sqlite` tables and insert the target into `thread_turns`, `thread_items`, `thread_realtime_items`, and projection state. Assert each is reported and an unrelated thread remains untouched.

- [ ] **Step 2: Write failing rollback test**

Simulate a failure after database cleanup but before rollout removal. Assert database bytes/rows, global state, session index, and rollout are restored from the safety backup.

- [ ] **Step 3: Write failing lock-before-delete test**

Hold a write lock on a required database and assert the operation fails without deleting the rollout file.

- [ ] **Step 4: Run deletion tests and verify RED**

Expected: current cleaner ignores history tables and deletes rollout files before complete mutation safety is established.

- [ ] **Step 5: Implement backup and symmetric schema-aware cleanup**

Back up existing mutable stores, acquire required database write transactions, remove target rows only, commit, then remove rollout/global/index references. On any exception, restore backups and return an actionable failure. Check table existence before statements.

- [ ] **Step 6: Implement exact residual reporting**

Check rollout, state, spawn edges, history tables, catalog, global JSON, backup JSON reference where applicable, and session index. Retain the existing Boolean interface by delegating to the detailed report.

- [ ] **Step 7: Run all deletion and backup tests**

Expected: no target residue and no unrelated-row changes.

- [ ] **Step 8: Commit**

```powershell
git add src/CodexConversationManager.Core/Deletion src/CodexConversationManager.Core/LocalData/CodexPaths.cs src/CodexConversationManager.App/App.xaml.cs tests/CodexConversationManager.Tests/Deletion
git commit -m "Safely clean modern Codex thread stores"
```

### Task 5: Make import and provider sync schema-aware

**Files:**
- Modify: `src/CodexConversationManager.Core/Import/ConversationImportService.cs`
- Modify: `src/CodexConversationManager.Core/Import/ImportBackupService.cs`
- Modify: `src/CodexConversationManager.Core/Sync/ProviderSyncService.cs`
- Test: `tests/CodexConversationManager.Tests/Import/ConversationImportServiceTests.cs`
- Test: `tests/CodexConversationManager.Tests/Sync/ProviderSyncServiceTests.cs`

**Interfaces:**
- Consumes: shared rollout extractor from Task 2 and modern project IDs from Task 3.
- Preserves: `ConversationImportService.ApplyAsync(...)` public contract.

- [ ] **Step 1: Write failing modern-schema import tests**

Assert an imported thread sets `history_mode = legacy`, retains modern records, generates display events, assigns `threads.project_id` when the selected project exists in modern tables, and continues writing legacy global assignment.

- [ ] **Step 2: Write failing old-schema fallback test**

Use the existing old test schema without `history_mode` or `project_id`; assert import still succeeds and no unsupported column is referenced.

- [ ] **Step 3: Write failing safety-backup coverage test**

Assert every store mutated by import/provider synchronization is present in its operation backup and restored after a forced validation failure.

- [ ] **Step 4: Run focused tests and verify RED**

Expected: current fixed INSERT and project assignment logic fail modern/fallback assertions.

- [ ] **Step 5: Implement column-aware INSERT and validation**

Read `PRAGMA table_info(threads)` and compose only supported fields. Set modern optional fields explicitly when available. Validate title, provider, history mode, rollout path, and project assignment after insertion.

- [ ] **Step 6: Keep provider sync metadata-only and capability-based**

Continue updating only tables containing `model_provider`; include each modified store in backup and verification. Do not rewrite history item JSON.

- [ ] **Step 7: Run all import and synchronization tests**

- [ ] **Step 8: Commit**

```powershell
git add src/CodexConversationManager.Core/Import src/CodexConversationManager.Core/Sync tests/CodexConversationManager.Tests/Import tests/CodexConversationManager.Tests/Sync
git commit -m "Adapt import and sync to modern Codex schemas"
```

### Task 6: Add sanitized Codex 0.151 acceptance fixtures

**Files:**
- Create: `tests/Fixtures/codex-0151/state_5.sqlite`
- Create: `tests/Fixtures/codex-0151/thread_history_1.sqlite`
- Create: `tests/Fixtures/codex-0151/sqlite/codex-dev.db`
- Create: `tests/Fixtures/codex-0151/sessions/2026/08/29/rollout-2026-08-29T17-36-30-11111111-1111-7111-8111-111111111111.jsonl`
- Create: `tests/Fixtures/codex-0151/.codex-global-state.json`
- Modify: `tests/CodexConversationManager.Tests/Acceptance/RealInventoryReadOnlyTests.cs`
- Modify: `tests/CodexConversationManager.Tests/Mac/ReadOnlyConversationInventoryTests.cs`

**Interfaces:**
- Fixture IDs and text are synthetic; schema shapes match `0.151.x` capabilities used by Tasks 2–5.

- [ ] **Step 1: Add the sanitized fixture and failing acceptance assertions**

Assert modern normal, archived, child-agent, project, and detail behavior from one copied fixture root. Assert all fixture files remain byte-identical after read-only inventory.

- [ ] **Step 2: Run acceptance tests and verify RED for the complete modern fixture**

Run:

```powershell
.\build-tools\dotnet\dotnet.exe test tests\CodexConversationManager.Tests\CodexConversationManager.Tests.csproj --no-restore --filter "FullyQualifiedName~RealInventoryReadOnlyTests|FullyQualifiedName~ReadOnlyConversationInventoryTests"
```

Expected before the final compatibility correction: FAIL only on a concrete mismatch between the sanitized `0.151` fixture and the merged inventory result; if it passes, no production correction is needed for this step.

- [ ] **Step 3: Correct only a failure demonstrated by the fixture**

For each failed assertion, trace the missing evidence to its reader, add one focused failing unit test beside that reader, implement the smallest schema-capability correction, and rerun both the unit test and the acceptance command. Do not add schema support without a failing fixture or unit test.

- [ ] **Step 4: Run complete test suite**

```powershell
.\build-tools\dotnet\dotnet.exe test CodexConversationManager.sln --no-restore
```

- [ ] **Step 5: Commit**

```powershell
git add tests/Fixtures/codex-0151 tests/CodexConversationManager.Tests/Acceptance tests/CodexConversationManager.Tests/Mac
git commit -m "Cover Codex 0.151 local data layout"
```

### Task 7: Version, package, and document v0.2.0

**Files:**
- Modify: `src/CodexConversationManager.App/CodexConversationManager.App.csproj`
- Modify: `installer/CodexConversationManager.iss`
- Modify: `tools/package-macos.ps1`
- Create: `.github/workflows/macos-release.yml`
- Modify: `README.md`
- Modify: `README-使用说明.md`

**Interfaces:**
- Produces Windows self-contained installer, macOS arm64 ZIP, and macOS x64 ZIP named with `v0.2.0`.
- Consumes optional GitHub secrets for Developer ID signing/notarization; otherwise creates an explicitly labeled ad-hoc-signed preview.

- [ ] **Step 1: Set every product version to `0.2.0`**

Update assembly, installer, bundle short version, and bundle build version together.

- [ ] **Step 2: Add macOS-native build workflow**

On `macos-15`, publish both RIDs, construct `.app`, preserve executable mode, run `codesign --force --deep --sign -` when Developer ID secrets are absent, verify with `codesign --verify --deep --strict`, zip with `ditto`, and upload artifacts. When signing secrets exist, use Developer ID, `notarytool`, and `stapler`.

- [ ] **Step 3: Document compatibility and macOS trust status**

State supported Codex layouts, backup warning, unsigned/ad-hoc preview limitation, and notarized-release behavior accurately in both languages.

- [ ] **Step 4: Run verification-before-completion checks**

Run full tests, Windows packaging, XML/plist parsing, version searches, `git diff --check`, and inspect artifacts. Do not claim macOS notarization unless Apple returns success.

- [ ] **Step 5: Commit**

```powershell
git add src/CodexConversationManager.App/CodexConversationManager.App.csproj installer tools/package-macos.ps1 .github README.md README-使用说明.md
git commit -m "Prepare Codex Manager v0.2.0"
```

- [ ] **Step 6: Push and create GitHub release**

Push `main`, tag `v0.2.0`, upload the verified Windows and macOS assets, and include migration, safety, signing, and reinstall guidance in release notes.
