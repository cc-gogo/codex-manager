using System.Security.Cryptography;
using CodexConversationManager.Core.LocalData;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodexConversationManager.Tests.LocalData;

public sealed class LocalEvidenceReaderTests
{
    private static readonly string FixtureRoot = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "local-state");

    [Fact]
    public void FromRoot_resolves_known_paths_and_rejects_relative_roots()
    {
        var paths = CodexPaths.FromRoot(FixtureRoot);

        Assert.Equal(Path.GetFullPath(FixtureRoot), paths.Root);
        Assert.Equal(Path.Combine(paths.Root, "sessions"), paths.Sessions);
        Assert.Equal(Path.Combine(paths.Root, "archived_sessions"), paths.ArchivedSessions);
        Assert.Equal(Path.Combine(paths.Root, "state_5.sqlite"), paths.StateDatabase);
        Assert.Equal(Path.Combine(paths.Root, "sqlite", "codex-dev.db"), paths.CatalogDatabase);
        Assert.Equal(Path.Combine(paths.Root, ".codex-global-state.json"), paths.GlobalState);
        Assert.Throws<ArgumentException>(() => CodexPaths.FromRoot("relative-root"));
    }

    [Fact]
    public async Task Session_scan_keeps_archived_duplicate_and_malformed_evidence()
    {
        var scanner = new SessionScanner(CodexPaths.FromRoot(FixtureRoot));

        var records = await scanner.ScanAsync();

        Assert.Equal(5, records.Count);
        Assert.Single(records, x => x.Id == "11111111-1111-7111-8111-111111111111" && !x.IsArchived);
        Assert.Single(records, x => x.Id == "22222222-2222-7222-8222-222222222222" && x.IsArchived);
        Assert.Equal(2, records.Count(x => x.Id == "33333333-3333-7333-8333-333333333333"));

        var malformed = Assert.Single(records, x => x.Id == "44444444-4444-7444-8444-444444444444");
        Assert.NotNull(malformed.ParseError);
        Assert.Contains("JSON", malformed.ParseError, StringComparison.OrdinalIgnoreCase);

        var active = Assert.Single(records, x => x.Id == "11111111-1111-7111-8111-111111111111");
        Assert.Equal("vscode", active.SourceKind);
        Assert.Equal("interactive", active.ThreadSource);
        Assert.Equal("D:\\work\\alpha", active.Cwd);
        Assert.Equal(DateTimeOffset.Parse("2026-08-15T10:00:00Z"), active.CreatedAt);
    }

    [Fact]
    public async Task State_reader_uses_read_only_mode_and_preserves_thread_fields()
    {
        var reader = new StateDatabaseReader(CodexPaths.FromRoot(FixtureRoot).StateDatabase);

        var builder = new SqliteConnectionStringBuilder(reader.ConnectionString);
        var rows = await reader.ReadThreadsAsync();

        Assert.Equal(SqliteOpenMode.ReadOnly, builder.Mode);
        Assert.Equal(3, rows.Count);
        var row = Assert.Single(rows, x => x.Id == "11111111-1111-7111-8111-111111111111");
        Assert.Equal("vscode", row.SourceKind);
        Assert.Equal("interactive", row.ThreadSource);
        Assert.Equal("D:\\work\\alpha", row.Cwd);
        Assert.Equal("Alpha title", row.Title);
        Assert.False(row.IsArchived);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_755_252_800_000), row.CreatedAt);
    }

    [Fact]
    public async Task Catalog_reader_uses_read_only_mode_and_keeps_missing_candidates()
    {
        var reader = new CatalogDatabaseReader(CodexPaths.FromRoot(FixtureRoot).CatalogDatabase);

        var builder = new SqliteConnectionStringBuilder(reader.ConnectionString);
        var rows = await reader.ReadCatalogAsync();

        Assert.Equal(SqliteOpenMode.ReadOnly, builder.Mode);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, x => x.Id == "11111111-1111-7111-8111-111111111111" && !x.IsMissingCandidate);
        Assert.Contains(rows, x => x.Id == "44444444-4444-7444-8444-444444444444" && x.IsMissingCandidate);
    }

    [Fact]
    public async Task Session_index_reader_keeps_valid_index_entries()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "{\"id\":\"11111111-1111-7111-8111-111111111111\",\"thread_name\":\"Index title\",\"updated_at\":\"2026-03-29T09:37:03Z\"}\n{}");

            var rows = await new SessionIndexReader(path).ReadEntriesAsync();

            var row = Assert.Single(rows);
            Assert.Equal("11111111-1111-7111-8111-111111111111", row.Id);
            Assert.Equal("Index title", row.Title);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Global_state_reader_finds_exact_uuid_properties_and_values_with_paths()
    {
        var reader = new GlobalStateReader(CodexPaths.FromRoot(FixtureRoot).GlobalState);

        var references = await reader.ReadReferencesAsync();

        Assert.Equal(3, references.Count);
        Assert.Contains(references, x =>
            x.Id == "11111111-1111-7111-8111-111111111111" &&
            x.JsonPath == "$.threadTitles['11111111-1111-7111-8111-111111111111']");
        Assert.Contains(references, x =>
            x.Id == "22222222-2222-7222-8222-222222222222" &&
            x.JsonPath == "$.recentThreadIds[0]");
        Assert.DoesNotContain(references, x => x.Id.Contains("not-a-real", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Global_state_reader_retries_when_codex_is_mid_write()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "{");
            var writer = Task.Run(async () =>
            {
                await Task.Delay(25);
                await File.WriteAllTextAsync(path, "{ \"recent\": [\"11111111-1111-7111-8111-111111111111\"] }");
            });

            var references = await new GlobalStateReader(path).ReadReferencesAsync();
            await writer;

            Assert.Contains(references, reference => reference.Id == "11111111-1111-7111-8111-111111111111");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Project_sidebar_reader_uses_names_order_assignments_and_projectless_thread_order()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """
            {
              "local-projects": {
                "8506526a-c320-4ee0-9327-831ee85c0ef7": {
                  "id": "8506526a-c320-4ee0-9327-831ee85c0ef7",
                  "name": "nextsay",
                  "rootPaths": ["D:\\codex\\nextsay"]
                }
              },
              "project-order": ["8506526a-c320-4ee0-9327-831ee85c0ef7"],
              "thread-project-assignments": {
                "019fd250-d93d-7ef1-9c46-925273ffd37d": { "projectId": "8506526a-c320-4ee0-9327-831ee85c0ef7" }
              },
            "sidebar-project-thread-orders": {
                "8506526a-c320-4ee0-9327-831ee85c0ef7": { "threadIds": ["019fd250-d93d-7ef1-9c46-925273ffd37d"] }
              },
              "projectless-thread-ids": [
                "019fd250-d93d-7ef1-9c46-925273ffd37d",
                "not-a-thread-id",
                "019fd251-d93d-7ef1-9c46-925273ffd37d"
              ]
            }
            """);

            var snapshot = await new CodexProjectSidebarReader(path).ReadAsync();

            Assert.Equal("nextsay", Assert.Single(snapshot.Projects).Name);
            Assert.Equal("8506526a-c320-4ee0-9327-831ee85c0ef7", snapshot.ThreadProjectIds["019fd250-d93d-7ef1-9c46-925273ffd37d"]);
            Assert.Equal(["019fd250-d93d-7ef1-9c46-925273ffd37d", "019fd251-d93d-7ef1-9c46-925273ffd37d"], snapshot.ProjectlessThreadIds);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Project_sidebar_reader_lists_all_unarchived_threads_for_recent_sidebar_candidates()
    {
        var globalStatePath = Path.GetTempFileName();
        var statePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(globalStatePath, "{}");
            await using (var connection = new SqliteConnection($"Data Source={statePath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE threads (id TEXT, archived INTEGER, recency_at INTEGER, recency_at_ms INTEGER);
                    INSERT INTO threads VALUES ('019fd250-d93d-7ef1-9c46-925273ffd37d', 0, 100, NULL);
                    INSERT INTO threads VALUES ('019fd251-d93d-7ef1-9c46-925273ffd37d', 1, 300, NULL);
                    INSERT INTO threads VALUES ('019fd252-d93d-7ef1-9c46-925273ffd37d', 0, 200, NULL);
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var snapshot = await new CodexProjectSidebarReader(globalStatePath, statePath).ReadAsync();

            Assert.Equal(
                ["019fd252-d93d-7ef1-9c46-925273ffd37d", "019fd250-d93d-7ef1-9c46-925273ffd37d"],
                snapshot.RecentThreadIds);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(globalStatePath);
            File.Delete(statePath);
        }
    }

    [Fact]
    public async Task Project_sidebar_reader_keeps_every_recent_thread_without_a_fixed_window()
    {
        var globalStatePath = Path.GetTempFileName();
        var statePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(globalStatePath, "{}");
            await using (var connection = new SqliteConnection($"Data Source={statePath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE threads (id TEXT, archived INTEGER, preview TEXT, recency_at_ms INTEGER);
                    INSERT INTO threads VALUES ('00000000-0000-7000-8000-000000000001', 0, 'oldest', 1000);
                    INSERT INTO threads VALUES ('00000000-0000-7000-8000-000000000002', 0, 'older', 2000);
                    INSERT INTO threads VALUES ('00000000-0000-7000-8000-000000000003', 0, 'fourth', 3000);
                    INSERT INTO threads VALUES ('00000000-0000-7000-8000-000000000004', 0, 'third', 4000);
                    INSERT INTO threads VALUES ('00000000-0000-7000-8000-000000000005', 0, 'second', 5000);
                    INSERT INTO threads VALUES ('00000000-0000-7000-8000-000000000006', 0, 'newest', 6000);
                    INSERT INTO threads VALUES ('00000000-0000-7000-8000-000000000007', 0, 'residual', 7000);
                    INSERT INTO threads VALUES ('00000000-0000-7000-8000-000000000008', 0, 'also visible in recent', 8000);
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var snapshot = await new CodexProjectSidebarReader(globalStatePath, statePath).ReadAsync();

            Assert.Equal(8, snapshot.RecentThreadIds!.Count);
            Assert.Contains("00000000-0000-7000-8000-000000000001", snapshot.RecentThreadIds);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(globalStatePath);
            File.Delete(statePath);
        }
    }

    [Fact]
    public async Task Project_sidebar_reader_does_not_count_pinned_or_sectioned_threads_as_recent_but_keeps_project_threads()
    {
        var globalStatePath = Path.GetTempFileName();
        var statePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(globalStatePath, "{}");
            await using (var connection = new SqliteConnection($"Data Source={statePath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE threads (
                        id TEXT, archived INTEGER, preview TEXT, recency_at_ms INTEGER,
                        project_id TEXT, thread_section_id TEXT, is_pinned INTEGER);
                    INSERT INTO threads VALUES ('00000000-0000-7000-8000-000000000001', 0, 'project', 9000, 'project-1', NULL, 0);
                    INSERT INTO threads VALUES ('00000000-0000-7000-8000-000000000002', 0, 'pinned', 8000, NULL, NULL, 1);
                    INSERT INTO threads VALUES ('00000000-0000-7000-8000-000000000003', 0, 'sectioned', 7000, NULL, 'section-1', 0);
                    INSERT INTO threads VALUES ('00000000-0000-7000-8000-000000000004', 0, 'recent one', 6000, NULL, NULL, 0);
                    INSERT INTO threads VALUES ('00000000-0000-7000-8000-000000000005', 0, 'recent two', 5000, NULL, NULL, 0);
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var snapshot = await new CodexProjectSidebarReader(globalStatePath, statePath).ReadAsync();

            Assert.Equal(
                [
                    "00000000-0000-7000-8000-000000000001",
                    "00000000-0000-7000-8000-000000000004",
                    "00000000-0000-7000-8000-000000000005"
                ],
                snapshot.RecentThreadIds);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(globalStatePath);
            File.Delete(statePath);
        }
    }

    [Fact]
    public async Task Project_sidebar_reader_does_not_drop_unarchived_threads_missing_from_projectless_list()
    {
        var globalStatePath = Path.GetTempFileName();
        var statePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(globalStatePath, """
                { "projectless-thread-ids": ["019fd252-d93d-7ef1-9c46-925273ffd37d"] }
                """);
            await using (var connection = new SqliteConnection($"Data Source={statePath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE threads (id TEXT, archived INTEGER, recency_at INTEGER, recency_at_ms INTEGER);
                    INSERT INTO threads VALUES ('019fd250-d93d-7ef1-9c46-925273ffd37d', 0, 300, NULL);
                    INSERT INTO threads VALUES ('019fd251-d93d-7ef1-9c46-925273ffd37d', 0, 200, NULL);
                    INSERT INTO threads VALUES ('019fd252-d93d-7ef1-9c46-925273ffd37d', 0, 100, NULL);
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var snapshot = await new CodexProjectSidebarReader(globalStatePath, statePath).ReadAsync();

            Assert.Equal(
                [
                    "019fd250-d93d-7ef1-9c46-925273ffd37d",
                    "019fd251-d93d-7ef1-9c46-925273ffd37d",
                    "019fd252-d93d-7ef1-9c46-925273ffd37d"
                ],
                snapshot.RecentThreadIds);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(globalStatePath);
            File.Delete(statePath);
        }
    }

    [Fact]
    public async Task Project_sidebar_reader_preserves_the_explicit_codex_recent_list_without_state_filtering()
    {
        var globalStatePath = Path.GetTempFileName();
        var statePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(globalStatePath, """
                {
                  "projectless-thread-ids": [
                    "11111111-1111-7111-8111-111111111111",
                    "22222222-2222-7222-8222-222222222222",
                    "33333333-3333-7333-8333-333333333333",
                    "44444444-4444-7444-8444-444444444444",
                    "55555555-5555-7555-8555-555555555555",
                    "66666666-6666-7666-8666-666666666666"
                  ]
                }
                """);
            await using (var connection = new SqliteConnection($"Data Source={statePath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE threads (id TEXT, archived INTEGER, recency_at INTEGER, recency_at_ms INTEGER, source TEXT, thread_source TEXT);
                    INSERT INTO threads VALUES ('11111111-1111-7111-8111-111111111111', 0, 700, NULL, 'vscode', 'user');
                    INSERT INTO threads VALUES ('22222222-2222-7222-8222-222222222222', 0, 600, NULL, 'vscode', 'user');
                    INSERT INTO threads VALUES ('33333333-3333-7333-8333-333333333333', 0, 500, NULL, 'vscode', 'user');
                    INSERT INTO threads VALUES ('44444444-4444-7444-8444-444444444444', 0, 400, NULL, 'vscode', 'user');
                    INSERT INTO threads VALUES ('55555555-5555-7555-8555-555555555555', 0, 300, NULL, 'vscode', 'user');
                    INSERT INTO threads VALUES ('66666666-6666-7666-8666-666666666666', 0, 200, NULL, 'vscode', 'user');
                    INSERT INTO threads VALUES ('77777777-7777-7777-8777-777777777777', 0, 100, NULL, 'vscode', 'user');
                    INSERT INTO threads VALUES ('88888888-8888-7888-8888-888888888888', 0, 800, NULL, '{"subagent":true}', 'subagent');
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var snapshot = await new CodexProjectSidebarReader(globalStatePath, statePath).ReadAsync();

            Assert.Equal(
                [
                    "88888888-8888-7888-8888-888888888888",
                    "11111111-1111-7111-8111-111111111111",
                    "22222222-2222-7222-8222-222222222222",
                    "33333333-3333-7333-8333-333333333333",
                    "44444444-4444-7444-8444-444444444444",
                    "55555555-5555-7555-8555-555555555555",
                    "66666666-6666-7666-8666-666666666666",
                    "77777777-7777-7777-8777-777777777777"
                ],
                snapshot.RecentThreadIds);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(globalStatePath);
            File.Delete(statePath);
        }
    }

    [Fact]
    public async Task Project_sidebar_reader_excludes_only_threads_explicitly_ordered_in_project_sidebar()
    {
        var globalStatePath = Path.GetTempFileName();
        var statePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(globalStatePath, """
                {
                  "sidebar-project-thread-orders": {
                    "project-1": { "threadIds": ["00000000-0000-7000-8000-000000000001"] }
                  }
                }
                """);
            await using (var connection = new SqliteConnection($"Data Source={statePath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE threads (
                        id TEXT, archived INTEGER, preview TEXT, recency_at_ms INTEGER,
                        project_id TEXT, thread_section_id TEXT, is_pinned INTEGER);
                    INSERT INTO threads VALUES ('00000000-0000-7000-8000-000000000001', 0, 'project sidebar', 3000, 'project-1', NULL, 0);
                    INSERT INTO threads VALUES ('00000000-0000-7000-8000-000000000002', 0, 'project assignment only', 2000, 'project-1', NULL, 0);
                    INSERT INTO threads VALUES ('00000000-0000-7000-8000-000000000003', 0, 'unassigned', 1000, NULL, NULL, 0);
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var snapshot = await new CodexProjectSidebarReader(globalStatePath, statePath).ReadAsync();

            Assert.Equal(
                [
                    "00000000-0000-7000-8000-000000000002",
                    "00000000-0000-7000-8000-000000000003"
                ],
                snapshot.RecentThreadIds);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(globalStatePath);
            File.Delete(statePath);
        }
    }

    [Fact]
    public async Task Project_sidebar_reader_keeps_archived_subagents_in_the_archived_recent_list()
    {
        var globalStatePath = Path.GetTempFileName();
        var statePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(globalStatePath, "{}");
            await using (var connection = new SqliteConnection($"Data Source={statePath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE threads (id TEXT, archived INTEGER, recency_at INTEGER, recency_at_ms INTEGER, source TEXT, thread_source TEXT);
                    INSERT INTO threads VALUES ('11111111-1111-7111-8111-111111111111', 1, 100, NULL, 'vscode', 'user');
                    INSERT INTO threads VALUES ('22222222-2222-7222-8222-222222222222', 1, 200, NULL, '{"subagent":true}', 'subagent');
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var snapshot = await new CodexProjectSidebarReader(globalStatePath, statePath).ReadAsync();

            Assert.Empty(snapshot.RecentThreadIds!);
            Assert.Equal(
                ["22222222-2222-7222-8222-222222222222", "11111111-1111-7111-8111-111111111111"],
                snapshot.ArchivedRecentThreadIds);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(globalStatePath);
            File.Delete(statePath);
        }
    }

    [Fact]
    public async Task Project_sidebar_reader_prefers_modern_projects_and_keeps_visible_subagents_in_recent_order()
    {
        var globalStatePath = Path.GetTempFileName();
        var statePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(globalStatePath, """
                {
                  "local-projects": {
                    "legacy-project": { "id": "legacy-project", "name": "Legacy project", "rootPaths": ["D:\\legacy"] }
                  },
                  "thread-project-assignments": {
                    "019fd250-d93d-7ef1-9c46-925273ffd37d": { "projectId": "legacy-project" }
                  }
                }
                """);
            await using (var connection = new SqliteConnection($"Data Source={statePath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE projects (id TEXT, name TEXT);
                    CREATE TABLE project_roots (project_id TEXT, path TEXT);
                    CREATE TABLE thread_sections (id TEXT, name TEXT);
                    CREATE TABLE threads (
                        id TEXT, archived INTEGER, preview TEXT, recency_at_ms INTEGER,
                        project_id TEXT, thread_section_id TEXT, is_pinned INTEGER, thread_source TEXT);
                    INSERT INTO projects VALUES ('modern-project', 'Modern project');
                    INSERT INTO project_roots VALUES ('modern-project', 'D:\modern');
                    INSERT INTO thread_sections VALUES ('section-1', 'Research');
                    INSERT INTO threads VALUES ('019fd250-d93d-7ef1-9c46-925273ffd37d', 0, 'Visible parent', 100, 'modern-project', 'section-1', 1, 'user');
                    INSERT INTO threads VALUES ('019fd251-d93d-7ef1-9c46-925273ffd37d', 0, 'Visible child', 200, NULL, NULL, 0, 'subagent');
                    INSERT INTO threads VALUES ('019fd252-d93d-7ef1-9c46-925273ffd37d', 0, '', 300, NULL, NULL, 0, 'user');
                    INSERT INTO threads VALUES ('019fd253-d93d-7ef1-9c46-925273ffd37d', 1, 'Archived', 400, NULL, NULL, 0, 'user');
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var snapshot = await new CodexProjectSidebarReader(globalStatePath, statePath).ReadAsync();

            var project = Assert.Single(snapshot.Projects);
            Assert.Equal("modern-project", project.Id);
            Assert.Equal("Modern project", project.Name);
            Assert.Equal(["D:\\modern"], project.RootPaths);
            Assert.Equal("modern-project", snapshot.ThreadProjectIds["019fd250-d93d-7ef1-9c46-925273ffd37d"]);
            Assert.Equal(
                ["019fd251-d93d-7ef1-9c46-925273ffd37d"],
                snapshot.RecentThreadIds);
            Assert.Equal(["019fd253-d93d-7ef1-9c46-925273ffd37d"], snapshot.ArchivedRecentThreadIds);
            Assert.Equal(["019fd250-d93d-7ef1-9c46-925273ffd37d"], snapshot.PinnedThreadIds);
            Assert.Equal("section-1", snapshot.ThreadSectionIds["019fd250-d93d-7ef1-9c46-925273ffd37d"]);
            Assert.Equal(new CodexThreadSection("section-1", "Research"), Assert.Single(snapshot.ThreadSections));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(globalStatePath);
            File.Delete(statePath);
        }
    }

    [Fact]
    public async Task Readers_do_not_modify_any_fixture()
    {
        var before = HashFixtureFiles();
        var paths = CodexPaths.FromRoot(FixtureRoot);

        await new SessionScanner(paths).ScanAsync();
        await new StateDatabaseReader(paths.StateDatabase).ReadThreadsAsync();
        await new CatalogDatabaseReader(paths.CatalogDatabase).ReadCatalogAsync();
        await new GlobalStateReader(paths.GlobalState).ReadReferencesAsync();

        Assert.Equal(before, HashFixtureFiles());
    }

    private static SortedDictionary<string, string> HashFixtureFiles() =>
        Directory.EnumerateFiles(FixtureRoot, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(FixtureRoot, path),
                path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                StringComparer.Ordinal)
            .ToSortedDictionary(StringComparer.Ordinal);
}

internal static class DictionaryTestExtensions
{
    public static SortedDictionary<TKey, TValue> ToSortedDictionary<TKey, TValue>(
        this IDictionary<TKey, TValue> source,
        IComparer<TKey> comparer)
        where TKey : notnull => new(source, comparer);
}
