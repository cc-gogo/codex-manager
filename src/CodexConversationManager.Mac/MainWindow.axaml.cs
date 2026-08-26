using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CodexConversationManager.Core.Backup;
using CodexConversationManager.Core.Deletion;
using CodexConversationManager.Core.Domain;
using CodexConversationManager.Core.Export;
using CodexConversationManager.Core.Import;
using CodexConversationManager.Core.Inventory;
using CodexConversationManager.Core.LocalData;
using CodexConversationManager.Core.Sync;

namespace CodexConversationManager.Mac;

public sealed partial class MainWindow : Window, INotifyPropertyChanged
{
    private string _codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    private string _status = "请选择 Codex 数据目录后刷新";
    private string _detailText = "请选择一条对话";
    private string _sourcePaths = "";
    private string? _selectedCategory = "全部";
    private MacConversationRow? _selectedRow;
    private int _languageIndex;
    public new event PropertyChangedEventHandler? PropertyChanged;
    public string CodexHome { get=>_codexHome; set { _codexHome=value; OnPropertyChanged(); } }
    public string Status { get=>_status; private set { _status=value; OnPropertyChanged(); } }
    public string DetailText { get=>_detailText; private set { _detailText=value; OnPropertyChanged(); } }
    public string SourcePaths { get=>_sourcePaths; private set { _sourcePaths=value; OnPropertyChanged(); } }
    public int LanguageIndex { get=>_languageIndex; set { _languageIndex=value; OnPropertyChanged(); } }
    public ObservableCollection<string> Categories { get; } = ["全部","普通对话","子代理","残留对话","归档对话","异常对话","重复对话"];
    public string? SelectedCategory { get=>_selectedCategory; set { _selectedCategory=value; OnPropertyChanged(); OnPropertyChanged(nameof(VisibleRows)); } }
    public ObservableCollection<MacConversationRow> Rows { get; } = [];
    public IEnumerable<MacConversationRow> VisibleRows => Rows.Where(r=>SelectedCategory is null or "全部" || r.CategoryName==SelectedCategory);
    public MacConversationRow? SelectedRow { get=>_selectedRow; set { _selectedRow=value; OnPropertyChanged(); ShowDetails(value); } }
    public ICommand RefreshCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand BackupCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand SyncCommand { get; }
    public MainWindow()
    {
        InitializeComponent();
        RefreshCommand=new AsyncCommand(RefreshAsync); ExportCommand=new AsyncCommand(ExportAsync);
        BackupCommand=new AsyncCommand(BackupAsync); DeleteCommand=new AsyncCommand(DeleteAsync);
        ImportCommand=new AsyncCommand(ImportAsync); ExitCommand=new AsyncCommand(()=>new MacCodexProcessController().StopAsync()); RestartCommand=new AsyncCommand(RestartAsync);
        SyncCommand=new AsyncCommand(SyncAsync);
        DataContext=this;
    }
    private async Task RefreshAsync()
    {
        try { var s=await ReadOnlyConversationInventory.Create(CodexHome).RefreshLocalAsync(InventoryMode.LiveCodex); Rows.Clear(); foreach(var r in s.Records) Rows.Add(new MacConversationRow(r)); OnPropertyChanged(nameof(VisibleRows)); Status=$"已读取 {Rows.Count} 条对话"; }
        catch(Exception e){ Status=$"读取失败：{e.Message}"; }
    }
    private IReadOnlyList<ConversationRecord> SelectedRecords()=>Rows.Where(r=>r.IsSelected).Select(r=>r.Record).ToList();
    private async Task ExportAsync()
    {
        var selected=SelectedRecords(); if(selected.Count==0 && SelectedRow is not null) selected=[SelectedRow.Record]; if(selected.Count==0){Status="请先选择要导出的对话。";return;}
        var folder=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions{Title="选择 Markdown 导出文件夹",AllowMultiple=false}); if(folder.Count==0)return;
        var detailProvider=new ConversationDetailService(null); foreach(var r in selected) await new ConversationMarkdownExporter().ExportAsync(r,await detailProvider.LoadAsync(r),Path.Combine(folder[0].Path.LocalPath,$"{r.Id}.md")); Status=$"已导出 {selected.Count} 条 Markdown。";
    }
    private async Task BackupAsync()
    {
        var selected=SelectedRecords(); if(selected.Count==0){Status="请先勾选要备份的对话。";return;} var folder=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions{Title="选择备份文件夹",AllowMultiple=false}); if(folder.Count==0)return;
        var r=await new ConversationBackupService().BackupAsync(selected,folder[0].Path.LocalPath,mode:ConversationBackupMode.CurrentAndHistory); Status=$"已备份 {r.ConversationCount} 条，对话正文 {r.CopiedFileCount} 个。";
    }
    private async Task DeleteAsync()
    {
        var selected=SelectedRecords(); if(selected.Count==0){Status="请先勾选要删除的对话。";return;} var paths=CodexPaths.FromRoot(Path.GetFullPath(CodexHome)); var safe=await new ExternalCodexProcessGuard(new SystemProcessSnapshotSource()).CheckAsync(new HashSet<int>()); if(!safe.IsSafe){Status="请先完全退出 Codex 后再删除。";return;}
        var map=selected.ToDictionary(r=>r.Id,r=>(IReadOnlyList<string>)r.Evidence.ActiveSessionPaths.Concat(r.Evidence.ArchivedSessionPaths).ToList(),StringComparer.OrdinalIgnoreCase); var results=await new LocalPermanentDeleteService(new GhostResidualCleaner(paths),map).ExecuteAsync(new DeletionPlanBuilder().Build(selected)); Status=$"已处理 {results.Count} 条删除请求。"; await RefreshAsync();
    }
    private async Task ImportAsync()
    {
        var files=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title="导入 Codex 对话",AllowMultiple=true,FileTypeFilter=[new FilePickerFileType("JSONL"){Patterns=["*.jsonl"]}]}); if(files.Count==0)return;
        var paths=CodexPaths.FromRoot(Path.GetFullPath(CodexHome)); var preview=await new ConversationImportPreviewService().PreviewAsync(files.Select(f=>f.Path.LocalPath).ToList(),"openai",Rows.Select(r=>r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase),DuplicateIdResolution.GenerateNewId); if(preview.Candidates.Count==0){Status="没有发现可导入的有效对话。";return;}
        var result=await new ConversationImportService(paths,Path.Combine(AppContext.BaseDirectory,"backups","conversation-import")).ApplyAsync(new ConversationImportRequest(preview,new ProjectlessDestination(),ImportProviderMode.CurrentLogin)); Status=$"已导入 {result.ImportedCount} 条对话。请重启 Codex 刷新左侧列表。"; await RefreshAsync();
    }
    private async Task SyncAsync()
    {
        var paths=CodexPaths.FromRoot(Path.GetFullPath(CodexHome));
        var safe=await new ExternalCodexProcessGuard(new SystemProcessSnapshotSource()).CheckAsync(new HashSet<int>());
        if(!safe.IsSafe){Status="同步前必须完全退出 Codex。";return;}
        var service=new ProviderSyncService(paths,Path.Combine(paths.Root,"config.toml"),Path.Combine(AppContext.BaseDirectory,"backups","provider-sync"));
        var plan=await service.PreviewAsync();
        if(plan.TotalCount==0){Status="当前登录模式无需同步。";return;}
        var result=await service.ApplyAsync(plan); Status=$"已同步 {result.UpdatedCount} 处本地记录。请重启 Codex。";
    }
    private async Task RestartAsync(){var controller=new MacCodexProcessController();await controller.StopAsync();await controller.LaunchAsync();Status="已请求重新打开 Codex。";}
    private void ShowDetails(MacConversationRow? row){ if(row is null){DetailText="请选择一条对话";SourcePaths="";return;} DetailText=$"标题：{row.DisplayTitle}\nID：{row.Id}\n分类：{row.CategoryName}\n项目路径：{row.Cwd??"（无）"}"; SourcePaths="原文件路径：\n"+string.Join("\n",row.Record.Evidence.ActiveSessionPaths.Concat(row.Record.Evidence.ArchivedSessionPaths)); }
    private void OnPropertyChanged([CallerMemberName]string? n=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(n));
    private sealed class AsyncCommand(Func<Task> f):ICommand{public event EventHandler? CanExecuteChanged{add{}remove{}} public bool CanExecute(object? p)=>true; public async void Execute(object? p)=>await f();}
    public sealed class MacConversationRow(ConversationRecord record):INotifyPropertyChanged{private bool _isSelected; public event PropertyChangedEventHandler? PropertyChanged; public ConversationRecord Record{get;}=record; public bool IsSelected{get=>_isSelected;set{_isSelected=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(IsSelected)));}} public string Id=>Record.Id; public string DisplayTitle=>Record.DisplayTitle; public string? Cwd=>Record.Cwd; public string CategoryName=>Record.Category switch{ConversationCategory.Normal=>"普通对话",ConversationCategory.SubAgent=>"子代理",ConversationCategory.Residual=>"残留对话",ConversationCategory.Archived=>"归档对话",ConversationCategory.Damaged=>"异常对话",ConversationCategory.Duplicate=>"重复对话",_=>Record.Category.ToString()};}
}
internal sealed class MacCodexProcessController{public async Task StopAsync(){foreach(var p in System.Diagnostics.Process.GetProcesses().Where(p=>p.ProcessName.Equals("Codex",StringComparison.OrdinalIgnoreCase)||p.ProcessName.Equals("ChatGPT",StringComparison.OrdinalIgnoreCase))){try{p.CloseMainWindow();await Task.Delay(250);if(!p.HasExited)p.Kill(true);}catch{}finally{p.Dispose();}}} public Task LaunchAsync(){System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("open", "-a Codex"){UseShellExecute=false});return Task.CompletedTask;}}
