using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CodexConversationManager.Core.AppServer;

public static class CodexExecutableLocator
{
    private static readonly Regex VersionPattern = new(
        @"(?:codex(?:-cli)?\s+)?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<pre>[0-9A-Za-z.-]+))?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static string Locate(
        IEnumerable<string>? pathDirectories = null,
        string? localAppData = null,
        Func<string, string?>? versionProbe = null)
    {
        var probe = versionProbe ?? ProbeVersion;
        var candidates = DiscoverCandidates(pathDirectories ?? GetPathDirectories(), localAppData)
            .DistinctBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => new VersionedCandidate(candidate, TryParseVersion(probe(candidate.Path))))
            .Where(candidate => candidate.Version is not null)
            .ToList();

        var selected = candidates
            .OrderByDescending(candidate => candidate.Version)
            .ThenByDescending(candidate => candidate.Candidate.IsDesktop)
            .ThenBy(candidate => candidate.Candidate.Path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return selected?.Candidate.Path
            ?? throw new FileNotFoundException(
                "Could not locate a usable codex.exe. Install Codex Desktop or a current Codex CLI.");
    }

    private static IEnumerable<ExecutableCandidate> DiscoverCandidates(
        IEnumerable<string> pathDirectories,
        string? localAppData)
    {
        var directories = pathDirectories
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var directory in directories)
        {
            var npmPackage = Path.Combine(directory, "node_modules", "@openai", "codex");
            if (File.Exists(Path.Combine(directory, "codex.cmd")) && Directory.Exists(npmPackage))
            {
                IEnumerable<string> nativeBinaries;
                try
                {
                    nativeBinaries = Directory.EnumerateFiles(npmPackage, "codex.exe", SearchOption.AllDirectories)
                        .Where(path => path.Contains("codex-win32-", StringComparison.OrdinalIgnoreCase));
                }
                catch (UnauthorizedAccessException)
                {
                    nativeBinaries = [];
                }

                foreach (var nativeBinary in nativeBinaries)
                {
                    yield return new ExecutableCandidate(Path.GetFullPath(nativeBinary), IsDesktop: false);
                }
            }

            var pathCandidate = Path.Combine(directory, "codex.exe");
            if (File.Exists(pathCandidate))
            {
                yield return new ExecutableCandidate(Path.GetFullPath(pathCandidate), IsDesktop: false);
            }
        }

        var desktopRoot = Path.Combine(
            localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenAI", "Codex", "bin");
        if (!Directory.Exists(desktopRoot))
        {
            yield break;
        }

        IEnumerable<string> desktopBinaries;
        try
        {
            desktopBinaries = Directory.EnumerateFiles(desktopRoot, "codex.exe", SearchOption.AllDirectories);
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var desktopBinary in desktopBinaries)
        {
            yield return new ExecutableCandidate(Path.GetFullPath(desktopBinary), IsDesktop: true);
        }
    }

    private static string? ProbeVersion(string path)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path, "--version")
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            if (process is null || !process.WaitForExit(5_000) || process.ExitCode != 0)
            {
                return null;
            }

            return process.StandardOutput.ReadToEnd();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static CodexVersion? TryParseVersion(string? text)
    {
        var match = text is null ? null : VersionPattern.Match(text);
        if (match is null || !match.Success ||
            !int.TryParse(match.Groups["major"].Value, out var major) ||
            !int.TryParse(match.Groups["minor"].Value, out var minor) ||
            !int.TryParse(match.Groups["patch"].Value, out var patch))
        {
            return null;
        }

        return new CodexVersion(major, minor, patch, match.Groups["pre"].Value);
    }

    private static IEnumerable<string> GetPathDirectories() =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private sealed record ExecutableCandidate(string Path, bool IsDesktop);

    private sealed record VersionedCandidate(ExecutableCandidate Candidate, CodexVersion? Version);

    private sealed record CodexVersion(int Major, int Minor, int Patch, string PreRelease) : IComparable<CodexVersion>
    {
        public int CompareTo(CodexVersion? other)
        {
            if (other is null)
            {
                return 1;
            }

            var releaseComparison = Major.CompareTo(other.Major);
            if (releaseComparison != 0) return releaseComparison;
            releaseComparison = Minor.CompareTo(other.Minor);
            if (releaseComparison != 0) return releaseComparison;
            releaseComparison = Patch.CompareTo(other.Patch);
            if (releaseComparison != 0) return releaseComparison;

            if (string.IsNullOrEmpty(PreRelease)) return string.IsNullOrEmpty(other.PreRelease) ? 0 : 1;
            if (string.IsNullOrEmpty(other.PreRelease)) return -1;

            var left = PreRelease.Split('.');
            var right = other.PreRelease.Split('.');
            for (var index = 0; index < Math.Max(left.Length, right.Length); index++)
            {
                if (index == left.Length) return -1;
                if (index == right.Length) return 1;
                var comparison = ComparePreReleasePart(left[index], right[index]);
                if (comparison != 0) return comparison;
            }

            return 0;
        }

        private static int ComparePreReleasePart(string left, string right)
        {
            var leftIsNumber = int.TryParse(left, out var leftNumber);
            var rightIsNumber = int.TryParse(right, out var rightNumber);
            if (leftIsNumber && rightIsNumber) return leftNumber.CompareTo(rightNumber);
            if (leftIsNumber) return -1;
            if (rightIsNumber) return 1;
            return StringComparer.OrdinalIgnoreCase.Compare(left, right);
        }
    }
}
