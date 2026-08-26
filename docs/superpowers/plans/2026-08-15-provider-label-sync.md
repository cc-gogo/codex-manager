# Provider 标签同步 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在桌面管理器中安全地把本地历史对话的 `model_provider` 标签同步到当前配置 provider。

**Architecture:** Core 层新增 provider 迁移服务，负责只读预览、备份、JSONL/SQLite 精确更新、验证和回滚；App 层以异步确认对话框调用服务。现有删除与浏览流程不改变。

**Tech Stack:** .NET 8、WPF、System.Text.Json、Microsoft.Data.Sqlite、xUnit。

## Global Constraints

- 不修改 API Key、`auth.json`、对话正文、标题、时间戳和项目文件。
- 只修改严格匹配源 provider 的 `model_provider` 字段。
- 执行前要求外部 Codex/ChatGPT 完全退出，并创建可验证备份。
- 预览不写入；Apply 失败回滚；二次执行幂等。

---

### Task 1: Provider 扫描与迁移 Core 服务

**Files:**
- Create: `src/CodexConversationManager.Core/Sync/ProviderSyncService.cs`
- Create: `src/CodexConversationManager.Core/Sync/ProviderSyncModels.cs`
- Modify: `src/CodexConversationManager.Core/LocalData/CodexPaths.cs`
- Test: `tests/CodexConversationManager.Tests/Sync/ProviderSyncServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Test a fixture containing one `openai` and one custom rollout plus matching SQLite rows. Assert preview counts, dry-run byte hashes, apply target values, non-target preservation, and second apply count zero.

- [ ] **Step 2: Run the focused tests and verify failure**

Run: `dotnet test tests/CodexConversationManager.Tests/CodexConversationManager.Tests.csproj --filter FullyQualifiedName~ProviderSyncServiceTests`

Expected: compilation failure because the sync service does not exist.

- [ ] **Step 3: Implement minimal service**

Read `config.toml` provider without exposing its contents; scan `session_meta` semantically; inspect optional SQLite files and columns; build a `ProviderSyncPlan`. Apply copies exact target files to a timestamped backup directory, updates only target fields transactionally, validates destination counts, and restores backups on exception.

- [ ] **Step 4: Verify focused tests pass**

Run the same command; expect all provider sync tests pass.

### Task 2: WPF preview and confirmation flow

**Files:**
- Create: `src/CodexConversationManager.App/Views/ProviderSyncDialog.xaml`
- Create: `src/CodexConversationManager.App/Views/ProviderSyncDialog.xaml.cs`
- Create: `src/CodexConversationManager.App/ViewModels/ProviderSyncViewModel.cs`
- Modify: `src/CodexConversationManager.App/MainWindow.xaml`
- Modify: `src/CodexConversationManager.App/MainWindow.xaml.cs`
- Modify: `src/CodexConversationManager.App/App.xaml.cs`
- Test: `tests/CodexConversationManager.Tests/ViewModels/ProviderSyncViewModelTests.cs`

- [ ] **Step 1: Write failing ViewModel test**

Assert preview text exposes source provider and per-source counts, while Apply requires the exact confirmation phrase `同步 provider`.

- [ ] **Step 2: Run focused ViewModel tests and observe failure**

Run: `dotnet test tests/CodexConversationManager.Tests/CodexConversationManager.Tests.csproj --filter FullyQualifiedName~ProviderSyncViewModelTests`

- [ ] **Step 3: Implement dialog and command wiring**

Add a clearly labelled “同步到当前登录模式” button. Open a read-only preview first; enable Apply only after process guard is safe and confirmation text matches; show progress and result counts, then refresh inventory.

- [ ] **Step 4: Verify layout and ViewModel tests**

Run the focused tests plus `MainWindowLayoutTests`; expect all pass.

### Task 3: Full verification and portable release

**Files:**
- Modify: `tests/CodexConversationManager.Tests/ViewModels/MainWindowLayoutTests.cs`
- Output: `publish/CodexConversationManager.exe`

- [ ] **Step 1: Run full tests and Release build**

Run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\run-tests.ps1` and Release build with `-m:1`; expect zero failures and zero errors.

- [ ] **Step 2: Preserve current publish and build portable package**

After confirming the app is not running, move `publish` to a timestamped `publish-previous-*` directory and run `tools/build-portable.ps1`.

- [ ] **Step 3: Run acceptance**

Run `tests/Acceptance.Tests.ps1`; expect exit code `0` and the new EXE in `publish`.
