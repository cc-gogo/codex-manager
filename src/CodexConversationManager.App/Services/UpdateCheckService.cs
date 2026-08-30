using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexConversationManager.App.Services;

public sealed record UpdateCheckResult(string CurrentVersion, string LatestVersion, string ReleaseUrl)
{
    public bool IsUpdateAvailable => Version.TryParse(CurrentVersion, out var current) &&
                                     Version.TryParse(LatestVersion, out var latest) && latest > current;
}

public sealed class UpdateCheckService
{
    private const string ReleaseApi = "https://api.github.com/repos/cc-gogo/codex-manager/releases/latest";
    private const string LatestReleasePage = "https://github.com/cc-gogo/codex-manager/releases/latest";
    private readonly HttpClient _client;

    public UpdateCheckService(HttpClient? client = null) {
        _client = client ?? new HttpClient();
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("Codex-Manager-Update-Checker");
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        string latest;
        string url;
        using var response = await _client.GetAsync(ReleaseApi, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var tag = json.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;
            latest = tag.Trim().TrimStart('v', 'V');
            url = json.RootElement.GetProperty("html_url").GetString() ?? "https://github.com/cc-gogo/codex-manager/releases";
        }
        else if ((int)response.StatusCode == 403)
        {
            // The unauthenticated API is rate limited. The public latest-release redirect
            // does not consume the API quota and still gives us the tag and download page.
            using var fallback = await _client.GetAsync(LatestReleasePage, cancellationToken).ConfigureAwait(false);
            fallback.EnsureSuccessStatusCode();
            url = fallback.Headers.Location?.ToString()
                ?? fallback.RequestMessage?.RequestUri?.ToString()
                ?? LatestReleasePage;
            var match = Regex.Match(url, @"/tag/v?(?<version>\d+(?:\.\d+){1,3})(?:$|[/?#])", RegexOptions.IgnoreCase);
            if (!match.Success) throw new InvalidDataException("GitHub latest release did not include a version tag.");
            latest = match.Groups["version"].Value;
        }
        else
        {
            response.EnsureSuccessStatusCode();
            throw new InvalidOperationException("GitHub version check failed.");
        }
        var current = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
        return new UpdateCheckResult(current, latest, url);
    }
}
