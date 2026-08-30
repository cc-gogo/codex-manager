using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CodexConversationManager.App.Services;
using CodexConversationManager.App.ViewModels;
using CodexConversationManager.App.Views;
using CodexConversationManager.Core.Deletion;
using CodexConversationManager.Core.Domain;
using CodexConversationManager.Core.Export;
using CodexConversationManager.Core.Inventory;
using CodexConversationManager.Core.Sync;
using CodexConversationManager.Core.Backup;

namespace CodexConversationManager.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Func<DeletionPlan, IReadOnlyList<ConversationRecord>, Task<IPermanentDeleteExecutor>>? _deletionFactory;
    private readonly ProviderSyncService? _providerSync;
    private readonly IDeletionProcessGuard? _processGuard;
    private readonly IReadOnlySet<int> _ownedPids;
    private readonly Func<Task<ConversationImportViewModel>>? _importFactory;
    private readonly DispatcherTimer _autoBackupTimer;
    private readonly SettingsService _settingsService = new(new PortablePathService());
    private AppSettings _settings = new();
    private bool _languageReady;
    private string? _autoBackupRoot;
    private bool _autoBackupBusy;
    private readonly UpdateCheckService _updateCheckService = new();

    public MainWindow(
        MainViewModel viewModel,
        Func<DeletionPlan, IReadOnlyList<ConversationRecord>, Task<IPermanentDeleteExecutor>>? deletionFactory = null,
        ProviderSyncService? providerSync = null,
        IDeletionProcessGuard? processGuard = null,
        IReadOnlySet<int>? ownedPids = null,
        Func<Task<ConversationImportViewModel>>? importFactory = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _deletionFactory = deletionFactory;
        _providerSync = providerSync; _processGuard = processGuard; _ownedPids = ownedPids ?? new HashSet<int>();
        _importFactory = importFactory;
        DataContext = viewModel;
        _autoBackupTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _autoBackupTimer.Tick += AutoBackupTimer_Tick;
        Loaded += async (_, _) =>
        {
            await _viewModel.RefreshAsync();
            VersionStatus.Text = $"{LanguageManager.Instance.Get("VersionStatus")} {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}";
            await RestoreAutoBackupAsync();
        };
        Closed += (_, _) => _autoBackupTimer.Stop();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await _viewModel.RefreshAsync();

    private async void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_languageReady) return;
        if (LanguageComboBox.SelectedItem is not ComboBoxItem { Tag: string tag } ||
            !Enum.TryParse<AppLanguage>(tag, true, out var language)) return;
        LanguageManager.Instance.CurrentLanguage = language;
        _settings = _settings with { Language = language.ToString() };
        await _settingsService.SaveAsync(_settings);
        if (VersionStatus is not null)
            VersionStatus.Text = $"{LanguageManager.Instance.Get("VersionStatus")} {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}";
    }

    private async void ManualBackup_Click(object sender, RoutedEventArgs e)
    {
        var selected = _viewModel.Rows.Where(row => row.IsSelected).Select(row => row.Record).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "请先在左侧勾选要备份的对话。", "备份对话", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择对话备份文件夹",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var result = await new ConversationBackupService().BackupAsync(selected, dialog.FolderName,
                mode: ConversationBackupMode.CurrentAndHistory);
            MessageBox.Show(this,
                $"已备份 {result.ConversationCount} 条对话。\n\n镜像备份：{result.CurrentPath}\n历史备份：{result.HistoryPath}\n复制 JSONL：{result.CopiedFileCount} 个\n未找到正文：{result.MissingFileCount} 条",
                "备份完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"备份失败：{exception.Message}", "备份对话", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ChooseAutoBackup_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedRecords();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "请先在左侧勾选要自动备份的对话。", "自动备份", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择自动备份文件夹", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        _autoBackupRoot = dialog.FolderName;
        var minutes = AutoBackupIntervalComboBox.SelectedItem is ComboBoxItem item &&
                      int.TryParse(item.Tag?.ToString(), out var value) ? value : 30;
        _autoBackupTimer.Interval = TimeSpan.FromMinutes(minutes);
        _autoBackupTimer.Start();
        _settings = _settings with { AutoBackupRoot = _autoBackupRoot, AutoBackupEnabled = true, AutoBackupIntervalMinutes = minutes };
        await _settingsService.SaveAsync(_settings);
        AutoBackupStatus.Text = $"自动备份已开启：每 {minutes} 分钟覆盖更新一次\n{_autoBackupRoot}";
        await RunAutoBackupAsync(selected, showErrors: true);
    }

    private void StopAutoBackup_Click(object sender, RoutedEventArgs e)
    {
        _autoBackupTimer.Stop();
        _autoBackupRoot = null;
        _settings = _settings with { AutoBackupRoot = null, AutoBackupEnabled = false };
        _ = _settingsService.SaveAsync(_settings);
        AutoBackupStatus.Text = "自动备份未开启";
    }

    private async Task RestoreAutoBackupAsync()
    {
        try
        {
            _settings = await _settingsService.ReadAsync();
            LanguageComboBox.SelectedIndex = LanguageManager.Instance.CurrentLanguage == AppLanguage.English ? 1 : 0;
            _languageReady = true;
            if (!_settings.AutoBackupEnabled || string.IsNullOrWhiteSpace(_settings.AutoBackupRoot) ||
                !Directory.Exists(_settings.AutoBackupRoot)) return;
            _autoBackupRoot = _settings.AutoBackupRoot;
            var minutes = _settings.AutoBackupIntervalMinutes is 15 or 30 or 60 ? _settings.AutoBackupIntervalMinutes : 30;
            _autoBackupTimer.Interval = TimeSpan.FromMinutes(minutes);
            AutoBackupIntervalComboBox.SelectedIndex = minutes switch { 15 => 0, 60 => 2, _ => 1 };
            _autoBackupTimer.Start();
            AutoBackupStatus.Text = $"自动备份已开启：每 {minutes} 分钟覆盖更新一次\n{_autoBackupRoot}";
            await RunAutoBackupAsync(SelectedRecords(), showErrors: false);
        }
        catch (Exception exception)
        {
            AutoBackupStatus.Text = $"自动备份设置读取失败：{exception.Message}";
        }
    }

    private async void AutoBackupTimer_Tick(object? sender, EventArgs e) =>
        await RunAutoBackupAsync(SelectedRecords(), showErrors: false);

    private async Task RunAutoBackupAsync(IReadOnlyList<ConversationRecord> records, bool showErrors)
    {
        if (_autoBackupBusy || string.IsNullOrWhiteSpace(_autoBackupRoot) || records.Count == 0) return;
        _autoBackupBusy = true;
        try
        {
            var result = await new ConversationBackupService().BackupAsync(records, _autoBackupRoot,
                mode: ConversationBackupMode.CurrentOnly);
            AutoBackupStatus.Text = $"自动备份已更新：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{result.CopiedFileCount} 个正文文件";
        }
        catch (Exception exception)
        {
            AutoBackupStatus.Text = $"自动备份失败：{exception.Message}";
            if (showErrors) MessageBox.Show(this, AutoBackupStatus.Text, "自动备份", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _autoBackupBusy = false; }
    }

    private IReadOnlyList<ConversationRecord> SelectedRecords() =>
        _viewModel.Rows.Where(row => row.IsSelected).Select(row => row.Record).ToList();

    private async void StopCodex_Click(object sender, RoutedEventArgs e)
    {
        var result = await new CodexDesktopRestartService().StopCodexAsync();
        var message = result.Warnings.Count == 0 ? "Codex 已退出。" :
            $"已尝试退出 Codex，但有 {result.Warnings.Count} 个进程未能关闭。";
        MessageBox.Show(this, message, "退出 Codex", MessageBoxButton.OK,
            result.Warnings.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async void RestartCodex_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = MessageBox.Show(this,
            "这会关闭正在运行的 Codex/ChatGPT，并重新打开 Codex。不会删除任何对话。是否继续？",
            "重启 Codex", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            var result = await new CodexDesktopRestartService().RestartAsync();
            if (result.Warnings.Count > 0)
            {
                MessageBox.Show(this,
                    $"已请求重新打开 Codex，但有 {result.Warnings.Count} 个旧进程未能自动关闭。请手动退出 Codex 后再打开。",
                    "Codex 对话管理器", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"重启 Codex 失败：{exception.Message}", "Codex 对话管理器",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ProviderSync_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ProviderSyncCoreAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                $"同步窗口发生未处理错误：{exception.Message}",
                "同步到当前登录模式", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ProviderSyncCoreAsync()
    {
        if (_providerSync is null || _processGuard is null) return;

        ProcessGuardResult processState;
        try
        {
            processState = await _processGuard.CheckAsync(_ownedPids);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                $"无法检查 Codex 是否已退出：{exception.Message}\n\n请手动确认 Codex / ChatGPT 已完全退出后再重试。",
                "同步到当前登录模式", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!processState.IsSafe)
        {
            var names = string.Join(", ", processState.BlockingProcesses.Select(process => $"{process.ProcessName} ({process.ProcessId})"));
            MessageBox.Show(this,
                $"检测到 Codex / ChatGPT 仍在运行：\n{names}\n\n请先完全退出后，再点击同步。",
                "同步到当前登录模式", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirmation = MessageBox.Show(this,
            "已确认 Codex / ChatGPT 未运行。此功能用于同步 API 登录和账号登录的本地对话。\n\n执行同步前，必须完全退出 Codex / ChatGPT；请保持其关闭。点击确定后继续读取同步计划。",
            "同步到当前登录模式",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.OK) return;

        var defaultBackup = _settings.ProviderSyncBackupRoot ?? Path.Combine(AppContext.BaseDirectory, "backups", "provider-sync");
        var syncViewModel = new ProviderSyncViewModel(_providerSync, _processGuard, _ownedPids, defaultBackup);
        var dialog = new ProviderSyncDialog(syncViewModel) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _settings = _settings with { ProviderSyncBackupRoot = syncViewModel.BackupRoot };
            await _settingsService.SaveAsync(_settings);
            await _viewModel.RefreshAsync();
        }
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        VersionStatus.Text = LanguageManager.Instance.Get("CheckingUpdates");
        try
        {
            var result = await _updateCheckService.CheckAsync();
            var english = LanguageManager.Instance.CurrentLanguage == AppLanguage.English;
            VersionStatus.Text = result.IsUpdateAvailable
                ? $"{LanguageManager.Instance.Get("LatestVersion")}: {result.LatestVersion}"
                : $"{LanguageManager.Instance.Get("VersionStatus")} {result.CurrentVersion} ({LanguageManager.Instance.Get("UpToDate")})";
            if (result.IsUpdateAvailable && MessageBox.Show(this, english ? $"Version {result.LatestVersion} is available. Open the download page?" : $"发现新版本 {result.LatestVersion}，是否打开下载页面？", "Codex Manager", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(result.ReleaseUrl) { UseShellExecute = true });
        }
        catch (Exception ex) { VersionStatus.Text = $"{LanguageManager.Instance.Get("UpdateFailed")}: {ex.Message}"; }
    }

    private async void ImportConversation_Click(object sender, RoutedEventArgs e)
    {
        if (_importFactory is null) return;
        try
        {
            var importViewModel = await _importFactory();
            var dialog = new ConversationImportDialog(importViewModel) { Owner = this };
            if (dialog.ShowDialog() == true) await _viewModel.RefreshAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"无法打开导入功能：{exception.Message}", "Codex 对话管理器", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExportMarkdown_Click(object sender, RoutedEventArgs e)
    {
        var selected = _viewModel.Rows.Where(row => row.IsSelected).ToList();
        if (selected.Count == 0 && _viewModel.SelectedRow is not null)
        {
            selected.Add(_viewModel.SelectedRow);
        }

        if (selected.Count == 0)
        {
            MessageBox.Show(this, "请先勾选要导出的对话。", "Codex 对话管理器", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string? outputPath = null;
        if (selected.Count == 1)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Markdown 文件 (*.md)|*.md",
                FileName = $"Codex对话-{selected[0].Id}.md"
            };
            if (dialog.ShowDialog(this) != true) return;
            outputPath = dialog.FileName;
        }
        else
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择 Markdown 导出文件夹",
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return;
            outputPath = dialog.FolderName;
        }

        try
        {
            var exporter = new ConversationMarkdownExporter();
            foreach (var row in selected)
            {
                var detail = await _viewModel.LoadDetailAsync(row);
                var filePath = selected.Count == 1
                    ? outputPath
                    : Path.Combine(outputPath!, $"Codex对话-{row.Id}.md");
                await exporter.ExportAsync(row.Record, detail, filePath!);
            }

            var message = selected.Count == 1
                ? "已导出 Markdown。"
                : $"已导出 {selected.Count} 条 Markdown。";
            MessageBox.Show(this, message, "Codex 对话管理器", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"导出 Markdown 失败：{exception.Message}", "Codex 对话管理器", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name } && Enum.TryParse<ConversationCategory>(name, out var category))
        {
            _viewModel.SelectedCategory = category;
        }
    }

    private async void ProjectTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is ConversationTreeNodeViewModel node)
        {
            if (node.Conversation is not null)
            {
                await _viewModel.SelectAsync(node.Conversation);
            }
            else
            {
                _viewModel.SelectedProjectNode = node;
            }
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var selected = _viewModel.Rows.Where(row => row.IsSelected).Select(row => row.Record).ToList();
        if (selected.Count == 0 || _deletionFactory is null)
        {
            return;
        }

        var plan = new DeletionPlanBuilder().Build(selected);
        var confirmation = new PermanentDeleteDialog(new PermanentDeleteViewModel(plan, selected)) { Owner = this };
        if (confirmation.ShowDialog() != true)
        {
            return;
        }

        IPermanentDeleteExecutor executor;
        try
        {
            executor = await _deletionFactory(plan, selected);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"无法启动删除服务：{exception.Message}", "Codex 对话管理器", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var progressViewModel = new DeleteProgressViewModel(executor, plan);
        var progress = new DeleteProgressDialog(progressViewModel) { Owner = this };
        progress.Show();
        await progressViewModel.ExecuteAsync();
        await _viewModel.RefreshAsync();
    }

    private void SelectVisible_Click(object sender, RoutedEventArgs e) => _viewModel.SelectVisibleRows();
}
