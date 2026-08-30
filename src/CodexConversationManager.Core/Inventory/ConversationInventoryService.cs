using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using CodexConversationManager.Core.AppServer;
using CodexConversationManager.Core.Domain;
using CodexConversationManager.Core.LocalData;

namespace CodexConversationManager.Core.Inventory;

public sealed class ConversationInventoryService(
    IAppServerInventorySource appServer,
    ISessionEvidenceSource sessions,
    IStateEvidenceSource state,
    ICatalogEvidenceSource catalog,
    IGlobalStateEvidenceSource globalState,
    ConversationClassifier classifier,
    ISessionIndexEvidenceSource? sessionIndex = null,
    IThreadRelationshipEvidenceSource? relationships = null) : ILocalFirstConversationInventoryProvider
{
    public async Task<InventorySnapshot> RefreshAsync(
        InventoryMode mode,
        CancellationToken cancellationToken = default)
    {
        var localSnapshot = await RefreshLocalAsync(mode, cancellationToken).ConfigureAwait(false);
        return await ReconcileAppServerAsync(localSnapshot, mode, cancellationToken).ConfigureAwait(false);
    }

    public async Task<InventorySnapshot> RefreshLocalAsync(
        InventoryMode mode,
        CancellationToken cancellationToken = default)
    {
        var errors = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sessionsTask = CaptureAsync(
            "sessions", () => sessions.ScanAsync(cancellationToken), (IReadOnlyList<SessionEvidence>)[], errors);
        var stateTask = CaptureAsync(
            "state-db", () => state.ReadThreadsAsync(cancellationToken), (IReadOnlyList<StateThreadEvidence>)[], errors);
        var catalogTask = CaptureAsync(
            "catalog-db", () => catalog.ReadCatalogAsync(cancellationToken), (IReadOnlyList<CatalogThreadEvidence>)[], errors);
        var globalTask = CaptureAsync(
            "global-state", () => globalState.ReadReferencesAsync(cancellationToken), (IReadOnlyList<GlobalStateReference>)[], errors);
        var sessionIndexTask = CaptureAsync(
            "session-index", () => sessionIndex?.ReadEntriesAsync(cancellationToken) ?? Task.FromResult((IReadOnlyList<SessionIndexEvidence>)[]),
            (IReadOnlyList<SessionIndexEvidence>)[], errors);
        var relationshipTask = CaptureAsync(
            "thread-relationships", () => relationships?.ReadAsync(cancellationToken) ?? Task.FromResult((IReadOnlyList<ThreadRelationshipEvidence>)[]),
            (IReadOnlyList<ThreadRelationshipEvidence>)[], errors);

        await Task.WhenAll(sessionsTask, stateTask, catalogTask, globalTask, sessionIndexTask, relationshipTask)
            .ConfigureAwait(false);

        var merged = new Dictionary<string, EvidenceAccumulator>(StringComparer.OrdinalIgnoreCase);
        AddSessions(sessionsTask.Result, merged);
        AddState(stateTask.Result, merged);
        AddCatalog(catalogTask.Result, merged);
        AddGlobal(globalTask.Result, merged);
        AddSessionIndex(sessionIndexTask.Result, merged);
        AddRelationships(relationshipTask.Result, merged);

        var readAt = DateTimeOffset.Now;
        var diagnostics = new[]
        {
            new InventoryDiagnostic("app-server-active", 0, readAt, null, InventoryReadStatus.Pending),
            new InventoryDiagnostic("app-server-archived", 0, readAt, null, InventoryReadStatus.Pending),
            Diagnostic("sessions", sessionsTask.Result.Count),
            Diagnostic("state-db", stateTask.Result.Count),
            Diagnostic("catalog-db", catalogTask.Result.Count),
            Diagnostic("global-state", globalTask.Result.Count),
            Diagnostic("session-index", sessionIndexTask.Result.Count),
            Diagnostic("thread-relationships", relationshipTask.Result.Count)
        };
        return BuildSnapshot(merged, errors, diagnostics);

        InventoryDiagnostic Diagnostic(string source, int count) =>
            new(source, count, readAt, errors.TryGetValue(source, out var error) ? error : null,
                errors.ContainsKey(source) ? InventoryReadStatus.Failed : InventoryReadStatus.Completed);
    }

    public async Task<InventorySnapshot> ReconcileAppServerAsync(
        InventorySnapshot localSnapshot,
        InventoryMode mode,
        CancellationToken cancellationToken = default)
    {
        var errors = new ConcurrentDictionary<string, string>(localSnapshot.SourceErrors, StringComparer.OrdinalIgnoreCase);
        var useStateDbOnly = mode == InventoryMode.LiveCodex;
        var activeTask = CaptureAsync(
            "app-server-active", () => appServer.ListAllThreadsAsync(false, useStateDbOnly, cancellationToken),
            new ThreadListResult([], null), errors);
        var archivedTask = CaptureAsync(
            "app-server-archived", () => appServer.ListAllThreadsAsync(true, useStateDbOnly, cancellationToken),
            new ThreadListResult([], null), errors);
        await Task.WhenAll(activeTask, archivedTask).ConfigureAwait(false);

        var merged = new Dictionary<string, EvidenceAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in localSnapshot.Records) AddEvidence(record.Evidence, merged);
        AddAppServer(activeTask.Result.Threads, false, merged);
        AddAppServer(archivedTask.Result.Threads, true, merged);

        var readAt = DateTimeOffset.Now;
        var diagnostics = localSnapshot.Diagnostics
            .Where(item => item.Source is not "app-server-active" and not "app-server-archived")
            .Append(Diagnostic("app-server-active", activeTask.Result.Threads.Count))
            .Append(Diagnostic("app-server-archived", archivedTask.Result.Threads.Count))
            .ToList();
        return BuildSnapshot(merged, errors, diagnostics);

        InventoryDiagnostic Diagnostic(string source, int count) =>
            new(source, count, readAt, errors.TryGetValue(source, out var error) ? error : null,
                errors.ContainsKey(source) ? InventoryReadStatus.Failed : InventoryReadStatus.Completed);
    }

    private InventorySnapshot BuildSnapshot(
        IDictionary<string, EvidenceAccumulator> merged,
        IReadOnlyDictionary<string, string> errors,
        IReadOnlyList<InventoryDiagnostic> diagnostics)
    {
        var records = merged.Values
            .Select(value => classifier.Classify(value.Build()))
            .OrderByDescending(record => record.UpdatedAt ?? record.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenBy(record => record.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var counts = Enum.GetValues<ConversationCategory>()
            .ToDictionary(category => category, category => records.Count(record => record.Category == category));
        return new InventorySnapshot(records, errors, counts, diagnostics);
    }

    private static async Task<T> CaptureAsync<T>(
        string source,
        Func<Task<T>> operation,
        T fallback,
        ConcurrentDictionary<string, string> errors,
        string? cancellationError = null)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationError is not null)
        {
            errors[source] = cancellationError;
            return fallback;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            errors[source] = exception.Message;
            return fallback;
        }
    }

    private static EvidenceAccumulator Get(
        IDictionary<string, EvidenceAccumulator> merged,
        string id)
    {
        if (!merged.TryGetValue(id, out var value))
        {
            value = new EvidenceAccumulator(id);
            merged.Add(id, value);
        }

        return value;
    }

    private static void AddAppServer(
        IEnumerable<AppServerThread> threads,
        bool archived,
        IDictionary<string, EvidenceAccumulator> merged)
    {
        foreach (var thread in threads)
        {
            var value = Get(merged, thread.Id);
            value.AppServerListed = true;
            value.IsArchived |= archived;
            value.SourceKind ??= ReadSource(thread.Raw["source"]);
            value.IsSubAgent |= IsSubAgent(ReadSource(thread.Raw["source"]), null);
            value.Cwd ??= ReadString(thread.Raw, "cwd");
            value.PreferTitle(ReadString(thread.Raw, "name"));
            value.AddTitle(ReadString(thread.Raw, "preview"));
            value.CreatedAt = Earlier(value.CreatedAt, ReadUnixSeconds(thread.Raw, "createdAt"));
            value.UpdatedAt = Later(value.UpdatedAt, ReadUnixSeconds(thread.Raw, "updatedAt"));
        }
    }

    private static void AddSessions(
        IEnumerable<SessionEvidence> rows,
        IDictionary<string, EvidenceAccumulator> merged)
    {
        foreach (var row in rows.Where(row => !string.IsNullOrWhiteSpace(row.Id)))
        {
            var value = Get(merged, row.Id);
            (row.IsArchived ? value.ArchivedPaths : value.ActivePaths).Add(row.Path);
            value.IsArchived |= row.IsArchived;
            value.SourceKind ??= row.SourceKind;
            value.ThreadSource ??= row.ThreadSource;
            value.IsSubAgent |= IsSubAgent(row.SourceKind, row.ThreadSource);
            value.Cwd ??= row.Cwd;
            value.CreatedAt = Earlier(value.CreatedAt, row.CreatedAt);
            if (row.ParseError is not null)
            {
                value.ParseErrors.Add($"{row.Path}: {row.ParseError}");
            }
        }
    }

    private static bool IsSubAgent(StateThreadEvidence row) =>
        IsSubAgent(row.SourceKind, row.ThreadSource);

    private static bool IsSubAgent(string? sourceKind, string? threadSource) =>
        sourceKind?.Contains("subagent", StringComparison.OrdinalIgnoreCase) == true ||
        string.Equals(threadSource, "subagent", StringComparison.OrdinalIgnoreCase);

    private static void AddState(
        IEnumerable<StateThreadEvidence> rows,
        IDictionary<string, EvidenceAccumulator> merged)
    {
        foreach (var row in rows)
        {
            var value = Get(merged, row.Id);
            value.StateRows++;
            value.IsArchived |= row.IsArchived;
            value.SourceKind ??= row.SourceKind;
            value.ThreadSource ??= row.ThreadSource;
            value.IsSubAgent |= IsSubAgent(row.SourceKind, row.ThreadSource);
            value.Cwd ??= row.Cwd;
            value.AddTitle(row.Title);
            value.CreatedAt = Earlier(value.CreatedAt, row.CreatedAt);
            value.UpdatedAt = Later(value.UpdatedAt, row.UpdatedAt);
        }
    }

    private static void AddCatalog(
        IEnumerable<CatalogThreadEvidence> rows,
        IDictionary<string, EvidenceAccumulator> merged)
    {
        foreach (var row in rows)
        {
            var value = Get(merged, row.Id);
            value.CatalogRows++;
            value.SourceKind ??= row.SourceKind;
            value.ThreadSource ??= row.ThreadSource;
            value.IsSubAgent |= IsSubAgent(row.SourceKind, row.ThreadSource);
            value.Cwd ??= row.Cwd;
            value.PreferTitle(row.DisplayTitle);
            value.CreatedAt = Earlier(value.CreatedAt, row.CreatedAt);
            value.UpdatedAt = Later(value.UpdatedAt, row.UpdatedAt);
        }
    }

    private static void AddGlobal(
        IEnumerable<GlobalStateReference> rows,
        IDictionary<string, EvidenceAccumulator> merged)
    {
        foreach (var row in rows)
        {
            // Global state also contains project, sidebar and UI UUIDs. It can corroborate a
            // conversation discovered elsewhere, but cannot by itself establish a deletable thread.
            if (merged.TryGetValue(row.Id, out var value))
            {
                value.GlobalReferenceCount++;
            }
        }
    }

    private static void AddSessionIndex(
        IEnumerable<SessionIndexEvidence> rows,
        IDictionary<string, EvidenceAccumulator> merged)
    {
        foreach (var row in rows)
        {
            var value = Get(merged, row.Id);
            value.SessionIndexRows++;
            if (!string.IsNullOrWhiteSpace(row.SourcePath)) value.SessionIndexPaths.Add(row.SourcePath);
            value.PreferTitle(row.Title);
            value.UpdatedAt = Later(value.UpdatedAt, row.UpdatedAt);
        }
    }

    private static void AddRelationships(
        IReadOnlyList<ThreadRelationshipEvidence> edges,
        IDictionary<string, EvidenceAccumulator> merged)
    {
        var children = edges
            .Where(edge => !string.IsNullOrWhiteSpace(edge.ParentId) && !string.IsNullOrWhiteSpace(edge.ChildId))
            .GroupBy(edge => edge.ParentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.ChildId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var (parent, accumulator) in merged)
        {
            var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<string>();
            pending.Push(parent);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (!children.TryGetValue(current, out var directChildren)) continue;
                foreach (var child in directChildren)
                {
                    if (discovered.Add(child)) pending.Push(child);
                }
            }

            accumulator.DescendantIds.AddRange(discovered.Where(id => !string.Equals(id, parent, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private static void AddEvidence(
        ConversationEvidence evidence,
        IDictionary<string, EvidenceAccumulator> merged)
    {
        var value = Get(merged, evidence.Id);
        value.AppServerListed |= evidence.AppServerListed;
        value.IsRecent |= evidence.IsRecent;
        value.IsSubAgent |= evidence.IsSubAgent;
        value.IsArchived |= evidence.IsArchived;
        value.ActivePaths.AddRange(evidence.ActiveSessionPaths);
        value.ArchivedPaths.AddRange(evidence.ArchivedSessionPaths);
        value.StateRows += evidence.StateRows;
        value.SessionIndexRows += evidence.SessionIndexRows;
        value.SessionIndexPaths.AddRange(evidence.SessionIndexPaths);
        value.CatalogRows += evidence.CatalogRows;
        value.GlobalReferenceCount += evidence.GlobalReferenceCount;
        value.SourceKind ??= evidence.SourceKind;
        value.ThreadSource ??= evidence.ThreadSource;
        value.Cwd ??= evidence.Cwd;
        value.CreatedAt = Earlier(value.CreatedAt, evidence.CreatedAt);
        value.UpdatedAt = Later(value.UpdatedAt, evidence.UpdatedAt);
        value.ParseErrors.AddRange(evidence.ParseErrors);
        value.DescendantIds.AddRange(evidence.DescendantIds);
        foreach (var title in evidence.Titles) value.AddTitle(title);
    }

    private static string? ReadSource(JsonNode? node) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonObject value => ReadString(value, "type"),
        _ => null
    };

    private static string? ReadString(JsonObject value, string property) =>
        value[property] is JsonValue node && node.TryGetValue<string>(out var text) ? text : null;

    private static DateTimeOffset? ReadUnixSeconds(JsonObject value, string property)
    {
        if (value[property] is not JsonValue node || !node.TryGetValue<long>(out var seconds))
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }

    private static DateTimeOffset? Earlier(DateTimeOffset? left, DateTimeOffset? right) =>
        left is null ? right : right is null || left <= right ? left : right;

    private static DateTimeOffset? Later(DateTimeOffset? left, DateTimeOffset? right) =>
        left is null ? right : right is null || left >= right ? left : right;

    private sealed class EvidenceAccumulator(string id)
    {
        public bool AppServerListed { get; set; }
        public bool IsRecent { get; set; }
        public bool IsSubAgent { get; set; }
        public bool IsArchived { get; set; }
        public List<string> ActivePaths { get; } = [];
        public List<string> ArchivedPaths { get; } = [];
        public int StateRows { get; set; }
        public int SessionIndexRows { get; set; }
        public List<string> SessionIndexPaths { get; } = [];
        public int CatalogRows { get; set; }
        public int GlobalReferenceCount { get; set; }
        public string? SourceKind { get; set; }
        public string? ThreadSource { get; set; }
        public List<string> ParseErrors { get; } = [];
        public List<string> DescendantIds { get; } = [];
        public List<string> Titles { get; } = [];
        public string? Cwd { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }

        public void AddTitle(string? title)
        {
            if (!string.IsNullOrWhiteSpace(title) && !Titles.Contains(title, StringComparer.Ordinal))
            {
                Titles.Add(title);
            }
        }

        public void PreferTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            Titles.RemoveAll(existing => string.Equals(existing, title, StringComparison.Ordinal));
            Titles.Insert(0, title);
        }

        public ConversationEvidence Build() => new()
        {
            Id = id,
            AppServerListed = AppServerListed,
            IsRecent = IsRecent,
            IsSubAgent = IsSubAgent,
            IsArchived = IsArchived,
            ActiveSessionPaths = ActivePaths,
            ArchivedSessionPaths = ArchivedPaths,
            StateRows = StateRows,
            SessionIndexRows = SessionIndexRows,
            SessionIndexPaths = SessionIndexPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            CatalogRows = CatalogRows,
            GlobalReferenceCount = GlobalReferenceCount,
            SourceKind = SourceKind,
            ThreadSource = ThreadSource,
            ParseErrors = ParseErrors,
            DescendantIds = DescendantIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Titles = Titles,
            Cwd = Cwd,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
    }
}
