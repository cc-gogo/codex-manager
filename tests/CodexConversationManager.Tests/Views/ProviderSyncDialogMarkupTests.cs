using Xunit;

namespace CodexConversationManager.Tests.Views;

public sealed class ProviderSyncDialogMarkupTests
{
    [Fact]
    public void Read_only_backup_root_binding_is_one_way()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "CodexConversationManager.App", "Views", "ProviderSyncDialog.xaml");
        var markup = File.ReadAllText(Path.GetFullPath(path));

        Assert.Contains("Text=\"{Binding BackupRoot, Mode=OneWay}\"", markup, StringComparison.Ordinal);
    }
}
