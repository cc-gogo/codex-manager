# 项目目录树浏览 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 以工作目录树浏览 Codex 对话，并使分类、文件夹和中栏对话列表严格联动。

**Architecture:** 新增轻量的树节点视图模型，由 `MainViewModel.Rows` 按 `ConversationRecord.Cwd` 构建。树节点只保存显示名、规范化路径、子节点与未归属标记；中栏继续绑定 `VisibleRows`，但增加当前树节点范围筛选。XAML 改为紧凑三栏：分类/目录树、对话列表、详情与删除。

**Tech Stack:** .NET 8、WPF、xUnit。

## Global Constraints

- 目录树仅使用已读取的 `Cwd` 创建内存视图，绝不修改用户文件系统。
- 分类筛选适用于所有类别，包括归档。
- 目录缺失或 `Cwd` 为空的记录进入“未归属对话”。
- 继续使用已有永久删除、进程守卫和详情读取逻辑。

---

### Task 1: 目录树视图模型与筛选

**Files:**
- Create: `src/CodexConversationManager.App/ViewModels/ConversationTreeNodeViewModel.cs`
- Modify: `src/CodexConversationManager.App/ViewModels/MainViewModel.cs`
- Modify: `tests/CodexConversationManager.Tests/ViewModels/MainViewModelTests.cs`

**Interfaces:**
- Produces `ConversationTreeNodeViewModel(string name, string? fullPath, bool isUnassigned)` with `Children`.
- Produces `MainViewModel.ProjectTree`, `SelectedProjectNode` and category/tree-aware `VisibleRows`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task Category_filtered_project_tree_keeps_archived_paths_and_unassigned_records()
{
    var archived = Record("a", "Archived", ConversationCategory.Archived, "D:\\work\\one");
    var noCwd = Record("b", "No cwd", ConversationCategory.Archived, null);
    var viewModel = new MainViewModel(new FakeInventory([archived, noCwd]), new SafeGuard());
    await viewModel.RefreshAsync();

    viewModel.SelectedCategory = ConversationCategory.Archived;

    Assert.Contains(viewModel.ProjectTree, node => node.Name == "D:");
    Assert.Contains(viewModel.ProjectTree, node => node.IsUnassigned);
}
```

```csharp
[Fact]
public async Task Selecting_a_project_node_limits_visible_rows_to_its_directory()
{
    var first = Record("a", "One", ConversationCategory.Normal, "D:\\work\\one");
    var second = Record("b", "Two", ConversationCategory.Normal, "D:\\work\\two");
    var viewModel = new MainViewModel(new FakeInventory([first, second]), new SafeGuard());
    await viewModel.RefreshAsync();
    viewModel.SelectedProjectNode = FindNode(viewModel.ProjectTree, "D:\\work\\one");

    Assert.Equal("a", Assert.Single(viewModel.VisibleRows).Id);
}
```

- [ ] **Step 2: Run the focused tests to verify failure**

Run: `powershell.exe -NoProfile -File .\tools\run-tests.ps1 --filter MainViewModelTests`

Expected: compilation failure because the new tree model/properties do not exist.

- [ ] **Step 3: Implement the minimal tree model and filtering**

```csharp
public ObservableCollection<ConversationTreeNodeViewModel> ProjectTree { get; } = [];
public ConversationTreeNodeViewModel? SelectedProjectNode { get; set; }

private bool MatchesProject(ConversationRowViewModel row) =>
    SelectedProjectNode is null || SelectedProjectNode.Matches(row.Cwd);
```

Rebuild `ProjectTree` after refresh and whenever `SelectedCategory` changes. Normalize directory separators case-insensitively and retain parent nodes only when a category-matching descendant exists.

- [ ] **Step 4: Run the focused tests to verify success**

Run: `powershell.exe -NoProfile -File .\tools\run-tests.ps1 --filter MainViewModelTests`

Expected: all `MainViewModelTests` pass.

### Task 2: 重做主窗口浏览布局

**Files:**
- Modify: `src/CodexConversationManager.App/MainWindow.xaml`
- Modify: `src/CodexConversationManager.App/MainWindow.xaml.cs`
- Modify: `tests/CodexConversationManager.Tests/ViewModels/MainWindowLayoutTests.cs`

**Interfaces:**
- Consumes `ProjectTree`, `SelectedProjectNode`, `VisibleRows` and `ConversationRowViewModel`.
- Produces TreeView selection forwarding to `MainViewModel.SelectedProjectNode`.

- [ ] **Step 1: Write the failing layout test**

```csharp
[Fact]
public void Navigation_uses_project_tree_instead_of_recent_conversations()
{
    var xaml = LoadXaml();
    Assert.Contains("项目", xaml);
    Assert.Contains("TreeView", xaml);
    Assert.Contains("ProjectTree", xaml);
    Assert.DoesNotContain("最近对话", xaml);
    Assert.DoesNotContain("RecentRows", xaml);
}
```

- [ ] **Step 2: Run the focused test to verify failure**

Run: `powershell.exe -NoProfile -File .\tools\run-tests.ps1 --filter MainWindowLayoutTests`

Expected: failure because the XAML still contains “最近对话”.

- [ ] **Step 3: Implement the three-column tree browser**

Replace the left “最近对话” ListBox with a `TreeView` bound to `ProjectTree`; on TreeView selection update `SelectedProjectNode`. Replace the central `DataGrid` with a ListBox using title, time, category and short ID; keep a two-way checkbox binding. Keep the right detail and deletion panel intact. Use restrained borders, compact spacing and clear selected-row states.

- [ ] **Step 4: Run the focused layout test to verify success**

Run: `powershell.exe -NoProfile -File .\tools\run-tests.ps1 --filter MainWindowLayoutTests`

Expected: all layout tests pass.

### Task 3: 完整验证和便携发布

**Files:**
- Modify: `docs/superpowers/specs/2026-08-15-project-tree-browser-design.md` only if verification exposes a requirement gap.
- Output: `publish/CodexConversationManager.exe`

**Interfaces:**
- Consumes the finished WPF app and test suite.
- Produces a self-contained Windows release package.

- [ ] **Step 1: Run complete tests and Release build**

Run: `powershell.exe -NoProfile -File .\tools\run-tests.ps1` then `& .\build-tools\dotnet\dotnet.exe build .\CodexConversationManager.sln -c Release --nologo -m:1`

Expected: all tests pass; build exits `0` with no errors.

- [ ] **Step 2: Preserve the prior publish folder and build a new package**

Move existing `publish` to a unique `publish-previous-<timestamp>` folder after confirming the EXE is not running, then run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build-portable.ps1`.

- [ ] **Step 3: Run portable acceptance checks**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Acceptance.Tests.ps1`

Expected: exit code `0` and the package contains `CodexConversationManager.exe`, `README.md`, and `LICENSE`.
