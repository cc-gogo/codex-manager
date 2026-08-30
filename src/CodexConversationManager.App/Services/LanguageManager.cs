using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CodexConversationManager.App.Services;

public enum AppLanguage
{
    Chinese,
    English
}

public sealed class LanguageManager : INotifyPropertyChanged
{
    private static readonly IReadOnlyDictionary<string, (string Zh, string En)> Texts =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["AppTitle"] = ("Codex 对话管理器", "Codex Conversation Manager"),
            ["AppSubtitle"] = ("按分类、项目和文件夹浏览本地对话", "Browse local conversations by category, project, and folder"),
            ["Refresh"] = ("刷新", "Refresh"),
            ["ExitCodex"] = ("退出 Codex", "Exit Codex"),
            ["RestartCodex"] = ("重启 Codex", "Restart Codex"),
            ["ExportMarkdown"] = ("导出 Markdown", "Export Markdown"),
            ["ImportConversation"] = ("导入对话", "Import Conversation"),
            ["ProviderSync"] = ("同步到当前登录模式", "Sync to Current Login"),
            ["ProviderTooltip"] = ("将本地对话的 provider 标签在 API 登录和账号登录之间同步，使两种登录模式都能识别对应的本地对话。执行前必须完全退出 Codex。", "Sync local conversation provider labels between API and account login modes. Completely exit Codex before running."),
            ["Categories"] = ("分类", "Categories"),
            ["Normal"] = ("普通", "Normal"),
            ["SubAgent"] = ("子代理", "Sub-agents"),
            ["Residual"] = ("残留对话", "Residual"),
            ["Archived"] = ("归档", "Archived"),
            ["Damaged"] = ("异常对话", "Damaged"),
            ["Duplicate"] = ("重复", "Duplicate"),
            ["Projects"] = ("项目", "Projects"),
            ["PermanentDelete"] = ("永久删除", "Permanent Delete"),
            ["Backup"] = ("备份对话", "Back Up Conversations"),
            ["ManualBackup"] = ("手动备份（保留时间记录）", "Manual Backup (Keep History)"),
            ["AutoBackup"] = ("选择目录并开启自动备份", "Choose Folder and Enable Auto Backup"),
            ["StopAutoBackup"] = ("停止自动备份", "Stop Auto Backup"),
            ["AutoBackupInterval"] = ("间隔：", "Interval: "),
            ["AutoBackupOff"] = ("自动备份未开启", "Auto backup is off"),
            ["Diagnostics"] = ("刷新诊断", "Refresh Diagnostics"),
            ["SelectVisible"] = ("全选当前列表", "Select All Visible"),
            ["Footer"] = ("浏览不会修改任何 Codex 数据；永久删除后请重启 Codex，以使 Codex 左侧列表生效。", "Browsing does not modify Codex data. Restart Codex after permanent deletion to refresh the sidebar."),
            ["Language"] = ("语言", "Language"),
            ["Chinese"] = ("中文", "Chinese"),
            ["English"] = ("English", "English")
            , ["CheckUpdates"] = ("检查更新", "Check for Updates")
            , ["VersionStatus"] = ("当前版本", "Current version")
            , ["SyncBackupFolder"] = ("同步备份目录", "Sync backup folder")
            , ["ChooseFolder"] = ("选择目录", "Choose Folder")
            , ["CheckingUpdates"] = ("正在检查...", "Checking...")
            , ["LatestVersion"] = ("有新版本", "New version available")
            , ["UpToDate"] = ("已是最新", "Up to date")
            , ["UpdateFailed"] = ("检查失败", "Check failed")
            , ["ImportTitle"] = ("导入 Codex 对话", "Import Codex Conversations")
            , ["ImportDescription"] = ("导入外部 Codex JSONL。执行时必须完全退出 Codex；成功后请重启 Codex。导入前会自动备份。", "Import external Codex JSONL. Completely exit Codex before importing; restart Codex after success. A backup is created first.")
            , ["ChooseJsonl"] = ("选择 JSONL 文件", "Choose JSONL Files")
            , ["CheckAndRefresh"] = ("重新检查并刷新", "Check and Refresh")
            , ["Preview"] = ("预览", "Preview")
            , ["ImportDestination"] = ("导入目标", "Import Destination")
            , ["ProjectlessRecent"] = ("普通最近对话", "General Recent Conversations")
            , ["ExistingProject"] = ("已有项目", "Existing Project")
            , ["NewProject"] = ("新建项目", "New Project")
            , ["ChooseParent"] = ("选择新项目父文件夹", "Choose New Project Parent Folder")
            , ["LoginMode"] = ("登录模式", "Login Mode")
            , ["CurrentLogin"] = ("使用当前登录模式", "Use Current Login")
            , ["PreserveProvider"] = ("保留来源 provider", "Keep Source Provider")
            , ["DuplicateId"] = ("重复 ID", "Duplicate ID")
            , ["RejectDuplicate"] = ("发现重复时拒绝", "Reject Duplicates")
            , ["GenerateDuplicate"] = ("生成新 ID 导入副本", "Generate New ID Copy")
            , ["Issues"] = ("问题", "Issues")
            , ["Cancel"] = ("取消", "Cancel")
            , ["StartImport"] = ("开始导入", "Start Import")
            , ["DeleteProgress"] = ("正在处理删除结果", "Processing Deletion Results")
            , ["Close"] = ("关闭", "Close")
            , ["ConfirmDelete"] = ("确认永久删除", "Confirm Permanent Deletion")
            , ["DeleteConversation"] = ("永久删除对话", "Permanently Delete Conversations")
            , ["ConfirmDeleteButton"] = ("永久删除", "Permanently Delete")
            , ["SyncLogin"] = ("同步登录模式", "Sync Login Mode")
            , ["StartSync"] = ("开始同步", "Start Sync")
            , ["SyncDescription"] = ("此操作只修改本地对话的 provider 标签，不上传内容，也不修改标题、正文或项目。执行前会备份并在失败时恢复。", "This only changes local provider labels. It does not upload content or modify titles, messages, or projects. A backup is created and restored on failure.")
        };

    private AppLanguage _currentLanguage;

    public LanguageManager(AppLanguage currentLanguage = AppLanguage.Chinese) => _currentLanguage = currentLanguage;

    public static LanguageManager Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppLanguage CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage == value) return;
            _currentLanguage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
    }

    public string this[string key] => Get(key);

    public string Get(string key)
    {
        if (!Texts.TryGetValue(key, out var value)) return key;
        return CurrentLanguage == AppLanguage.English ? value.En : value.Zh;
    }
}
