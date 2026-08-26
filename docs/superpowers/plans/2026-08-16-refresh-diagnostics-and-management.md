# Refresh Diagnostics and Conversation Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make refresh immediately reflect local Codex data, explain source discrepancies, and safely expose export and delete-preview workflows.

**Architecture:** Inventory refresh will split local evidence collection from optional App Server reconciliation, retaining an explicit per-source diagnostic snapshot. The UI will render that snapshot and use the same evidence model to show source chains and deletion impact. Export consumes existing structured detail blocks and writes a user-selected Markdown file without changing Codex data.

**Tech Stack:** .NET 8, WPF, Microsoft.Data.Sqlite, xUnit.

## Global Constraints

- Read local Codex data before calling the App Server.
- Never modify Codex data during refresh, diagnosis, source inspection, or export.
- Permanent deletion remains blocked while external Codex or ChatGPT processes are running.
- Do not expose API keys, auth data, or configuration bodies in the UI.

---

### Task 1: Local-first refresh diagnostics

**Files:**
- Modify: `src/CodexConversationManager.Core/Inventory/ConversationInventoryService.cs`
- Create: `src/CodexConversationManager.Core/Inventory/InventoryDiagnostic.cs`
- Modify: `tests/CodexConversationManager.Tests/Inventory/ConversationInventoryServiceTests.cs`

**Interfaces:**
- Produces `InventoryDiagnostic(string Source, int RecordCount, DateTimeOffset ReadAt, string? Error)`.
- Extends `InventorySnapshot` with `IReadOnlyList<InventoryDiagnostic> Diagnostics`.

- [ ] Write a failing test asserting local session/database results are returned even when App Server reconciliation fails.
- [ ] Run the focused test and confirm it fails because diagnostics are unavailable.
- [ ] Collect sessions, state, catalog, and global state before App Server tasks; capture each source result count and exception independently.
- [ ] Run focused and full tests.

### Task 2: Refresh status and diagnostic panel

**Files:**
- Modify: `src/CodexConversationManager.App/ViewModels/MainViewModel.cs`
- Modify: `src/CodexConversationManager.App/MainWindow.xaml`
- Modify: `tests/CodexConversationManager.Tests/ViewModels/MainViewModelTests.cs`

**Interfaces:**
- Consumes `InventorySnapshot.Diagnostics`.
- Produces `RefreshDiagnostics` and a compact human-readable `RefreshStatus`.

- [ ] Write a failing ViewModel test that verifies a source error is surfaced without hiding locally read conversations.
- [ ] Run the focused test and confirm it fails.
- [ ] Populate diagnostic view models after refresh and render source name, row count, and latest error in an expandable status area.
- [ ] Run focused and full tests.

### Task 3: Source chain and deletion preview

**Files:**
- Modify: `src/CodexConversationManager.App/ViewModels/MainViewModel.cs`
- Modify: `src/CodexConversationManager.App/MainWindow.xaml`
- Modify: `src/CodexConversationManager.App/ViewModels/PermanentDeleteViewModel.cs`
- Modify: `src/CodexConversationManager.App/Views/PermanentDeleteDialog.xaml`
- Modify: `tests/CodexConversationManager.Tests/ViewModels/PermanentDeleteViewModelTests.cs`

**Interfaces:**
- Consumes `ConversationEvidence` paths and source counters.
- Shows only paths and source labels, never file contents or credentials.

- [ ] Write a failing test for source-chain labels derived from rollout, state, catalog, and global evidence.
- [ ] Write a failing test that deletion confirmation includes each target source path.
- [ ] Add source-chain properties and deletion plan preview text.
- [ ] Run focused and full tests.

### Task 4: Markdown export

**Files:**
- Create: `src/CodexConversationManager.Core/Export/ConversationMarkdownExporter.cs`
- Modify: `src/CodexConversationManager.App/MainWindow.xaml.cs`
- Modify: `src/CodexConversationManager.App/MainWindow.xaml`
- Create: `tests/CodexConversationManager.Tests/Export/ConversationMarkdownExporterTests.cs`

**Interfaces:**
- Produces `Task ExportAsync(ConversationRecord, ConversationDetail, string outputPath, CancellationToken)`.
- Export contains title, task ID, timestamps, source label, and escaped message blocks.

- [ ] Write a failing export test that verifies Markdown content and leaves the input conversation unchanged.
- [ ] Run the focused test and confirm it fails because the exporter is absent.
- [ ] Implement the exporter and add a Save File dialog command that operates only on the currently loaded detail.
- [ ] Run focused and full tests.

### Task 5: Release verification

**Files:**
- Modify: release artifacts only.

- [ ] Run `tools/run-tests.ps1`.
- [ ] Run Release build with `build-tools/dotnet/dotnet.exe build CodexConversationManager.sln -c Release --nologo -m:1`.
- [ ] Confirm the desktop app is not running, move the old `publish` directory to a timestamped `publish-previous-*` directory, run `tools/build-portable.ps1`, then run `tests/Acceptance.Tests.ps1`.
