using CodexConversationManager.App.Services;
using Xunit;

namespace CodexConversationManager.Tests.Services;

public sealed class LanguageManagerTests
{
    [Fact]
    public void Switching_language_updates_localized_text()
    {
        var manager = new LanguageManager(AppLanguage.Chinese);
        Assert.Equal("刷新", manager.Get("Refresh"));

        manager.CurrentLanguage = AppLanguage.English;

        Assert.Equal("Refresh", manager.Get("Refresh"));
    }
}
