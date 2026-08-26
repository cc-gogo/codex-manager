using System.IO;

namespace CodexConversationManager.App.Services;

public static class CodexHomeResolver
{
    public static string Resolve(IReadOnlyList<string> arguments, AppSettings settings, string userProfile)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(userProfile);
        for (var index = 0; index < arguments.Count; index++)
        {
            if (!string.Equals(arguments[index], "--codex-home", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= arguments.Count || !Path.IsPathFullyQualified(arguments[index + 1]))
            {
                throw new ArgumentException("--codex-home requires an absolute path.", nameof(arguments));
            }

            return Path.GetFullPath(arguments[index + 1]);
        }

        if (!string.IsNullOrWhiteSpace(settings.CodexHome) && Path.IsPathFullyQualified(settings.CodexHome))
        {
            return Path.GetFullPath(settings.CodexHome);
        }

        return Path.Combine(userProfile, ".codex");
    }
}
