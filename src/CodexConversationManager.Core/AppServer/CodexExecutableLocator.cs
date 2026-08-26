namespace CodexConversationManager.Core.AppServer;

public static class CodexExecutableLocator
{
    public static string Locate(IEnumerable<string>? pathDirectories = null)
    {
        var directories = (pathDirectories ?? GetPathDirectories())
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var directory in directories)
        {
            var npmPackage = Path.Combine(directory, "node_modules", "@openai", "codex");
            if (!File.Exists(Path.Combine(directory, "codex.cmd")) || !Directory.Exists(npmPackage))
            {
                continue;
            }

            try
            {
                var nativeBinary = Directory.EnumerateFiles(npmPackage, "codex.exe", SearchOption.AllDirectories)
                    .FirstOrDefault(path => path.Contains("codex-win32-", StringComparison.OrdinalIgnoreCase));
                if (nativeBinary is not null)
                {
                    return Path.GetFullPath(nativeBinary);
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        foreach (var directory in directories)
        {
            var candidate = Path.Combine(directory, "codex.exe");
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new FileNotFoundException("Could not locate codex.exe on PATH.");
    }

    private static IEnumerable<string> GetPathDirectories() =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
