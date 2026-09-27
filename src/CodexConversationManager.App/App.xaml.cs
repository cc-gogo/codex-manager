using System.Windows;
using System.IO;
using CodexConversationManager.App.ViewModels;
using CodexConversationManager.App.Services;
using CodexConversationManager.Core.Deletion;
using CodexConversationManager.Core.Domain;
using CodexConversationManager.Core.Inventory;
using CodexConversationManager.Core.Import;
using CodexConversationManager.Core.LocalData;
using CodexConversationManager.Core.Sync;

namespace CodexConversationManager.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var portablePaths = new PortablePathService();
        AppSettings settings;
        try
        {
            settings = await new SettingsService(portablePaths).ReadAsync();
        }
        catch
        {
            settings = new AppSettings();
        }
        if (Enum.TryParse<AppLanguage>(settings.Language, true, out var language))
            LanguageManager.Instance.CurrentLanguage = language;

        var codexHome = CodexHomeResolver.Resolve(
            e.Args,
            settings,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var paths = CodexPaths.FromRoot(codexHome);
        var ownedAppServerPids = new HashSet<int>();
        var inventory = new ConversationInventoryService(
            null,
            new SessionScanner(paths),
            new StateDatabaseReader(paths.StateDatabase),
            new CatalogDatabaseReader(paths.CatalogDatabase),
            new GlobalStateReader(paths.GlobalState),
            new ConversationClassifier(),
            new SessionIndexReader(Path.Combine(codexHome, "session_index.jsonl")),
            new ThreadRelationshipDatabaseReader(paths.StateDatabase));
        var processGuard = new ExternalCodexProcessGuard(new SystemProcessSnapshotSource());
        IConversationDetailProvider detailProvider = new ConversationDetailService(null);
        Func<DeletionPlan, IReadOnlyList<ConversationRecord>, Task<IPermanentDeleteExecutor>> deletionFactory =
            async (_, records) =>
            {
                var sessionPathsById = records
                    .GroupBy(record => record.Id, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => (IReadOnlyList<string>)group.SelectMany(record => record.Evidence.ActiveSessionPaths)
                            .Concat(group.SelectMany(record => record.Evidence.ArchivedSessionPaths))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList(),
                        StringComparer.OrdinalIgnoreCase);
                var processState = await processGuard.CheckAsync(ownedAppServerPids).ConfigureAwait(false);
                if (!processState.IsSafe)
                    throw new InvalidOperationException("请完全退出 Codex 后再删除对话。");
                IPermanentDeleteExecutor executor = new LocalPermanentDeleteService(
                    new GhostResidualCleaner(paths), sessionPathsById);
                return executor;
            };
        var providerSync = new ProviderSyncService(paths, Path.Combine(codexHome, "config.toml"), Path.Combine(AppContext.BaseDirectory, "backups", "provider-sync"));
        var sidebarProvider = new CodexProjectSidebarReader(paths.GlobalState, paths.StateDatabase);
        async Task<ConversationImportViewModel> CreateImportViewModelAsync()
        {
            var sidebar = await sidebarProvider.ReadAsync();
            var provider = await providerSync.ReadConfiguredProviderAsync() ?? "openai";
            return new ConversationImportViewModel(
                new ConversationImportPreviewService(),
                new ConversationImportService(paths, Path.Combine(AppContext.BaseDirectory, "backups", "conversation-import")),
                processGuard,
                ownedAppServerPids,
                (await new StateDatabaseReader(paths.StateDatabase).ReadThreadsAsync()).Select(thread => thread.Id).ToHashSet(StringComparer.OrdinalIgnoreCase),
                sidebar.Projects.Select(project => new ImportProjectOption(project.Id, project.Name)).ToList(),
                provider,
                cancellationToken => new CodexDesktopRestartService().StopCodexAsync(cancellationToken),
                cancellationToken => new CodexDesktopRestartService().RestartCodexAsync(cancellationToken));
        }
        var window = new MainWindow(new MainViewModel(
            inventory,
            processGuard,
            ownedAppServerPids,
            detailProvider,
            sidebarProvider,
            action => Dispatcher.InvokeAsync(action).Task), deletionFactory, providerSync, processGuard, ownedAppServerPids, CreateImportViewModelAsync);
        MainWindow = window;
        window.Show();
    }

}
