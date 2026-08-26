using System.Text;
using System.Text.RegularExpressions;

namespace CodexConversationManager.Core.Diagnostics;

public sealed record DiagnosticLogEntry(
    string Operation,
    string? ConversationId,
    string? Path,
    string Result,
    string? IgnoredBody);

public sealed partial class RedactingFileLogger(
    string directory,
    int maximumFiles = 5,
    long maximumFileBytes = 2 * 1024 * 1024)
{
    private static long _sequence;
    private readonly string _directory = Path.GetFullPath(directory);

    public async Task WriteAsync(DiagnosticLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (maximumFiles < 1 || maximumFileBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFiles));
        }

        Directory.CreateDirectory(_directory);
        var line = string.Join(" | ", new[]
        {
            DateTimeOffset.UtcNow.ToString("O"),
            Redact(entry.Operation),
            Redact(entry.ConversationId ?? string.Empty),
            Redact(entry.Path ?? string.Empty),
            Redact(entry.Result)
        }) + Environment.NewLine;
        var bytes = Encoding.UTF8.GetByteCount(line);
        var path = SelectLogPath(bytes);
        await File.AppendAllTextAsync(path, line, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        TrimOldFiles();
    }

    private string SelectLogPath(int nextLineBytes)
    {
        var latest = Directory.EnumerateFiles(_directory, "*.log")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        if (latest is not null && latest.Length + nextLineBytes <= maximumFileBytes)
        {
            return latest.FullName;
        }

        return Path.Combine(_directory, $"diagnostics-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Interlocked.Increment(ref _sequence):D6}.log");
    }

    private void TrimOldFiles()
    {
        foreach (var file in Directory.EnumerateFiles(_directory, "*.log")
                     .Select(path => new FileInfo(path))
                     .OrderByDescending(file => file.LastWriteTimeUtc)
                     .ThenByDescending(file => file.Name, StringComparer.Ordinal)
                     .Skip(maximumFiles))
        {
            file.Delete();
        }
    }

    private static string Redact(string value) => AuthorizationPattern().Replace(
        CookiePattern().Replace(
            ApiKeyPattern().Replace(
                SessionPattern().Replace(value, "$1[REDACTED]"),
                "$1[REDACTED]"),
            "$1[REDACTED]"),
        "$1[REDACTED]");

    [GeneratedRegex("(?i)(api[_-]?key\\s*[=:]\\s*)([^\\s;]+)")]
    private static partial Regex ApiKeyPattern();

    [GeneratedRegex("(?i)(session\\s*=\\s*)([^\\s;]+)")]
    private static partial Regex SessionPattern();

    [GeneratedRegex("(?i)(cookie\\s*:\\s*)([^\\r\\n;]+)")]
    private static partial Regex CookiePattern();

    [GeneratedRegex("(?i)(authorization\\s*:\\s*(?:bearer\\s+)?)([^\\s;]+)")]
    private static partial Regex AuthorizationPattern();
}
