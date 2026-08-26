using CodexConversationManager.Core.Diagnostics;
using Xunit;

namespace CodexConversationManager.Tests.Diagnostics;

public sealed class RedactingFileLoggerTests
{
    [Fact]
    public async Task Log_redacts_secrets_and_never_writes_conversation_body()
    {
        var directory = CreateDirectory();
        try
        {
            var logger = new RedactingFileLogger(directory, maximumFiles: 5, maximumFileBytes: 1024);
            await logger.WriteAsync(new DiagnosticLogEntry(
                "refresh",
                "11111111-1111-7111-8111-111111111111",
                "C:\\Users\\ASUS\\.codex\\sessions",
                "failed: api_key=sk-abcdefghijklmnopqrstuvwxyz123456; Cookie: session=abcdef; Authorization: Bearer secret-token",
                "Conversation body must not be stored"));

            var log = await File.ReadAllTextAsync(Assert.Single(Directory.EnumerateFiles(directory, "*.log")));
            Assert.Contains("11111111-1111-7111-8111-111111111111", log);
            Assert.Contains("[REDACTED]", log);
            Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz123456", log);
            Assert.DoesNotContain("session=abcdef", log);
            Assert.DoesNotContain("secret-token", log);
            Assert.DoesNotContain("Conversation body must not be stored", log);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Rotation_retains_at_most_configured_file_count()
    {
        var directory = CreateDirectory();
        try
        {
            var logger = new RedactingFileLogger(directory, maximumFiles: 2, maximumFileBytes: 80);
            for (var index = 0; index < 6; index++)
            {
                await logger.WriteAsync(new DiagnosticLogEntry("refresh", $"id-{index}", "path", new string('x', 60), null));
            }

            Assert.True(Directory.EnumerateFiles(directory, "*.log").Count() <= 2);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "logger-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
