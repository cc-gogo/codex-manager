using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace CodexConversationManager.App.Services;

public sealed record UpdateCheckResult(string CurrentVersion, string LatestVersion, string ReleaseUrl)
{
    public bool IsUpdateAvailable => Version.TryParse(CurrentVersion, out var current) &&
                                     Version.TryParse(LatestVersion, out var latest) && latest > current;
}

public sealed class UpdateCheckService
{
    private const string ReleaseApi = "https://api.github.com/repos/cc-gogo/codex-manager/releases/latest";
    private readonly HttpClient _client;

    public UpdateCheckService(HttpClient? client = null) {
        _client = client ?? new HttpClient();
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("Codex-Manager-Update-Checker");
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _client.GetAsync(ReleaseApi, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var tag = json.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;
        var url = json.RootElement.GetProperty("html_url").GetString() ?? "https://github.com/cc-gogo/codex-manager/releases";
        var latest = tag.Trim().TrimStart('v', 'V');
        var current = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
        return new UpdateCheckResult(current, latest, url);
    }
}
