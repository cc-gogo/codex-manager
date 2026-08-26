# Codex 项目侧栏同步浏览 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让项目区按 Codex 的项目名称、项目顺序和会话归属显示，并在底部提供真正可用的最近对话筛选。

**Architecture:** 新增只读 `CodexProjectSidebarReader`，从全局状态解析项目元数据和线程归属。`MainViewModel` 在刷新后用该快照将会话分组，XAML 仅渲染项目名、相对目录和最近项；对话和删除服务保持不变。

**Tech Stack:** .NET 8、WPF、System.Text.Json、xUnit。

## Global Constraints

- 仅读取 `.codex-global-state.json`，绝不修改 Codex 项目数据或用户文件夹。
- 项目显示名和顺序以 Codex 侧栏元数据为准，不显示盘符和项目 UUID。
- 对话条目固定两行，完整标题通过 Tooltip 提供。
- 分类逻辑不改变，项目与最近对话随分类联动。

---

### Task 1: 解析 Codex 项目侧栏元数据

**Files:**
- Create: `src/CodexConversationManager.Core/LocalData/CodexProjectSidebarReader.cs`
- Create: `src/CodexConversationManager.Core/LocalData/CodexProjectSidebarSnapshot.cs`
- Modify: `tests/CodexConversationManager.Tests/LocalData/LocalEvidenceReaderTests.cs`

**Interfaces:**
- Produces `CodexProjectSidebarSnapshot(IReadOnlyList<CodexProject> Projects, IReadOnlyDictionary<string, string> ThreadProjectIds, IReadOnlyDictionary<string, IReadOnlyList<string>> SidebarThreadOrders)`.
- `CodexProject` contains `Id`, `Name`, `RootPaths`, `Order`.

- [ ] **Step 1: Add a failing reader test**

```csharp
[Fact]
public async Task Project_sidebar_reader_uses_names_order_and_thread_assignments()
{
    var snapshot = await new CodexProjectSidebarReader(GlobalStateFixture).ReadAsync();
    Assert.Equal("nextsay", snapshot.Projects.Single(x => x.Id == "8506526a-c320-4ee0-9327-831ee85c0ef7").Name);
    Assert.Equal("8506526a-c320-4ee0-9327-831ee85c0ef7", snapshot.ThreadProjectIds["019fd250-d93d-7ef1-9c46-925273ffd37d"]);
}
```

- [ ] **Step 2: Run the focused test and observe its failure**

Run: `dotnet test tests/CodexConversationManager.Tests/CodexConversationManager.Tests.csproj --filter FullyQualifiedName~LocalEvidenceReaderTests`

Expected: compilation error because `CodexProjectSidebarReader` is absent.

- [ ] **Step 3: Implement the read-only parser**

Use `JsonNode.ParseAsync` with `FileShare.ReadWrite | FileShare.Delete`. Parse `local-projects`, `project-order`, `thread-project-assignments`, and `sidebar-project-thread-orders`; omit malformed entries rather than manufacturing project names.

- [ ] **Step 4: Re-run the focused test**

Run: same command as Step 2.

Expected: reader tests pass.

### Task 2: 按 Codex 项目分组并提供最近筛选

**Files:**
- Modify: `src/CodexConversationManager.App/ViewModels/ConversationTreeNodeViewModel.cs`
- Modify: `src/CodexConversationManager.App/ViewModels/MainViewModel.cs`
- Modify: `src/CodexConversationManager.App/App.xaml.cs`
- Modify: `tests/CodexConversationManager.Tests/ViewModels/MainViewModelTests.cs`

**Interfaces:**
- Consumes `CodexProjectSidebarSnapshot` during `MainViewModel.RefreshAsync`.
- Produces project tree nodes named from Codex metadata and an `IsRecent` node with the most recent 30 matching rows.

- [ ] **Step 1: Add failing grouping tests**

```csharp
[Fact]
public async Task Project_tree_displays_codex_project_name_not_id_or_drive()
{
    var viewModel = CreateViewModelWithProject("8506526a-c320-4ee0-9327-831ee85c0ef7", "nextsay", "D:\\codex\\nextsay");
    await viewModel.RefreshAsync();
    Assert.Contains(viewModel.ProjectTree, node => node.Name == "nextsay");
    Assert.DoesNotContain(viewModel.ProjectTree, node => node.Name is "D:" or "8506526a-c320-4ee0-9327-831ee85c0ef7");
}
```

```csharp
[Fact]
public async Task Recent_node_at_bottom_limits_rows_to_thirty_newest_records()
{
    var viewModel = CreateViewModelWithThirtyOneTimedRecords();
    await viewModel.RefreshAsync();
    viewModel.SelectedProjectNode = Assert.Single(viewModel.ProjectTree.Where(x => x.IsRecent));
    Assert.Equal(30, viewModel.VisibleRows.Count);
}
```

- [ ] **Step 2: Run the focused tests and observe failure**

Run: `dotnet test tests/CodexConversationManager.Tests/CodexConversationManager.Tests.csproj --filter FullyQualifiedName~MainViewModelTests`

Expected: failure because project metadata and a recent node are not yet used.

- [ ] **Step 3: Implement grouping and recent filtering**

Resolve explicit assignment first, then path-under-root fallback. Add only project names to top-level tree nodes; build folder nodes from paths relative to each project root. Append “未归属对话”, then “最近对话” last. `Matches` for a recent node accepts only its stored thread IDs.

- [ ] **Step 4: Re-run focused tests**

Run: same command as Step 2.

Expected: all `MainViewModelTests` pass.

### Task 3: 固定两行列表与发布

**Files:**
- Modify: `src/CodexConversationManager.App/MainWindow.xaml`
- Modify: `tests/CodexConversationManager.Tests/ViewModels/MainWindowLayoutTests.cs`
- Output: `publish/CodexConversationManager.exe`

- [ ] **Step 1: Add a failing layout test**

```csharp
[Fact]
public void Conversation_rows_use_single_line_title_and_bottom_recent_navigation()
{
    var xaml = LoadXaml();
    Assert.Contains("TextTrimming=\"CharacterEllipsis\"", xaml);
    Assert.Contains("ToolTip=\"{Binding Title}\"", xaml);
    Assert.Contains("最近对话", xaml);
}
```

- [ ] **Step 2: Run the layout test and observe failure**

Run: `dotnet test tests/CodexConversationManager.Tests/CodexConversationManager.Tests.csproj --filter FullyQualifiedName~MainWindowLayoutTests`

Expected: failure because the title Tooltip and final recent navigation styling are absent.

- [ ] **Step 3: Implement compact rows**

Give each ListBox item a stable height, set title `TextWrapping="NoWrap"`, `TextTrimming="CharacterEllipsis"`, and `ToolTip="{Binding Title}"`; preserve its time/short-ID metadata row.

- [ ] **Step 4: Verify and publish**

Run full tests, a Release build, preserve the existing `publish` folder by renaming it, run `tools/build-portable.ps1`, then run `tests/Acceptance.Tests.ps1`.

Expected: all tests pass, Release build has no errors, acceptance exits `0`.
