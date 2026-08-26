# Cross-Platform Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace numbered Windows release outputs with stable overwrite locations and add a full macOS client.

**Architecture:** The WPF Windows application remains unchanged. A new Avalonia project consumes the existing Core inventory, import, export, backup, deletion, and provider-sync services on macOS. PowerShell scripts publish fixed Windows/macOS output paths, while Inno Setup consumes the fixed Windows publish directory.

**Tech Stack:** .NET 8, WPF, Avalonia, xUnit, Inno Setup, PowerShell.

**Spec:** `docs/superpowers/specs/2026-08-26-macos-client-design.md`

## Global Constraints

- Never modify or remove Codex conversation data.
- Windows output uses `publish` and `installer-output/CodexConversationManager-Setup.exe`.
- macOS destructive writes require Codex to be fully closed.
- Verify all existing tests before delivery.

---

### Task 1: Stable Windows Release Script

**Files:**
- Create: `tools/publish-windows.ps1`
- Modify: `installer/CodexConversationManager.iss`

**Interfaces:**
- Produces: a repeatable script that publishes to `publish` and compiles `installer-output/CodexConversationManager-Setup.exe`.

- [ ] **Step 1: Write the failing acceptance assertion**

Add assertions that the installer script reads `publish` and the release script mentions the fixed output directory.

- [ ] **Step 2: Run acceptance test to verify it fails**

Run: `pwsh tests/Acceptance.Tests.ps1`

Expected: FAIL because the script and fixed installer source do not exist.

- [ ] **Step 3: Implement stable publishing**

Create `tools/publish-windows.ps1` using the bundled `build-tools/dotnet/dotnet.exe`, call `dotnet publish` with `-o publish`, then execute Inno Setup with `installer/CodexConversationManager.iss`. Update `[Files]` to use `..\\publish\\*`.

- [ ] **Step 4: Run acceptance test to verify it passes**

Run: `pwsh tests/Acceptance.Tests.ps1`

Expected: PASS.

### Task 2: macOS Full Client

**Files:**
- Create: `src/CodexConversationManager.Mac/CodexConversationManager.Mac.csproj`
- Create: `src/CodexConversationManager.Mac/Program.cs`
- Create: `src/CodexConversationManager.Mac/App.axaml`
- Create: `src/CodexConversationManager.Mac/App.axaml.cs`
- Create: `src/CodexConversationManager.Mac/MainWindow.axaml`
- Create: `src/CodexConversationManager.Mac/MainWindow.axaml.cs`
- Modify: `CodexConversationManager.sln`
- Test: `tests/CodexConversationManager.Tests/Mac/MacInventoryTests.cs`

**Interfaces:**
- Consumes: `CodexPaths.FromRoot`, `SessionScanner`, `StateDatabaseReader`, `CatalogDatabaseReader`, `GlobalStateReader`, `ConversationClassifier`, and `ConversationInventoryService`.
- Produces: an Avalonia executable with local inventory, export, backup, import, delete, provider-sync, and Codex exit/restart workflows.

- [ ] **Step 1: Write a failing read-only inventory test**

Create a temp root, call the macOS inventory factory, and assert no file appears in the temp root after refresh.

- [ ] **Step 2: Run test to verify it fails**

Run: `build-tools/dotnet/dotnet.exe test CodexConversationManager.sln --no-restore --filter MacInventoryTests`

Expected: FAIL because the factory does not exist.

- [ ] **Step 3: Implement the factory and Avalonia window**

Create the shared local inventory factory and bind results to category, row, and details controls. Reuse Core services for export, backup, import, delete, and provider synchronization, with process guards for write operations.

- [ ] **Step 4: Run focused and full tests**

Run: `build-tools/dotnet/dotnet.exe test CodexConversationManager.sln --no-restore`

Expected: PASS.

### Task 3: macOS Publish And Bundle Script

**Files:**
- Create: `tools/package-macos.ps1`
- Test: `tests/Acceptance.Tests.ps1`

**Interfaces:**
- Consumes: `src/CodexConversationManager.Mac/CodexConversationManager.Mac.csproj`.
- Produces: `publish-macos-arm64/Codex Conversation Manager.app` and `publish-macos-x64/Codex Conversation Manager.app`.

- [ ] **Step 1: Write a failing script-content acceptance assertion**

Assert that `tools/package-macos.ps1` publishes both `osx-arm64` and `osx-x64` and creates an `.app` bundle.

- [ ] **Step 2: Run acceptance test to verify it fails**

Run: `pwsh tests/Acceptance.Tests.ps1`

Expected: FAIL because the macOS script does not exist.

- [ ] **Step 3: Implement packaging script**

Publish self-contained single-file binaries for both runtime identifiers, create `Contents/MacOS`, `Contents/Resources`, and `Contents/Info.plist`, then copy the published executable into each app bundle.

- [ ] **Step 4: Verify compile and Windows artifacts**

Run: `build-tools/dotnet/dotnet.exe test CodexConversationManager.sln --no-restore` and `pwsh tools/publish-windows.ps1`.

Expected: tests pass and `publish/CodexConversationManager.App.exe` plus `installer-output/CodexConversationManager-Setup.exe` exist.
