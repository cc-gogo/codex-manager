using Xunit;

namespace CodexConversationManager.Tests.Views;

public sealed class ConversationImportDialogMarkupTests
{
    [Fact]
    public void Read_only_provider_binding_is_explicitly_one_way()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "CodexConversationManager.App", "Views", "ConversationImportDialog.xaml");
        var markup = File.ReadAllText(Path.GetFullPath(path));
        Assert.Contains("Text=\"{Binding TargetProvider, Mode=OneWay}\"", markup, StringComparison.Ordinal);
    }
}
