using Xunit;

namespace CodexConversationManager.Tests.ViewModels;

public sealed class MainWindowLayoutTests
{
    [Fact]
    public void Conversation_list_keeps_checkbox_binding_editable()
    {
        var xaml = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "CodexConversationManager.App", "MainWindow.xaml"));

        Assert.Contains("IsChecked=\"{Binding Conversation.IsSelected, Mode=TwoWay}\"", xaml);
    }

    [Fact]
    public void Navigation_uses_a_project_tree_instead_of_recent_conversations()
    {
        var xaml = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "CodexConversationManager.App", "MainWindow.xaml"));

        Assert.Contains("Key=Projects", xaml);
        Assert.Contains("TreeView", xaml);
        Assert.Contains("ProjectTree", xaml);
        Assert.DoesNotContain("最近对话", xaml);
        Assert.DoesNotContain("RecentRows", xaml);
    }

    [Fact]
    public void Conversation_tree_and_reader_have_stable_navigation_surfaces()
    {
        var xaml = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "CodexConversationManager.App", "MainWindow.xaml"));

        Assert.Contains("x:Key=\"ConversationItem\"", xaml);
        Assert.Contains("Property=\"Height\" Value=\"62\"", xaml);
        Assert.Contains("Conversation.IsSelected", xaml);
        Assert.Contains("ItemsSource=\"{Binding DetailBlocks}\"", xaml);
        Assert.Contains("ScrollViewer", xaml);
    }

    [Fact]
    public void Middle_reader_text_is_read_only_and_selectable()
    {
        var xaml = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "CodexConversationManager.App", "MainWindow.xaml"));

        Assert.Contains("x:Key=\"SelectableReaderText\"", xaml);
        Assert.Contains("IsReadOnly\" Value=\"True\"", xaml);
        Assert.Contains("Style=\"{StaticResource SelectableReaderText}\"", xaml);
        Assert.Contains("Text=\"{Binding DetailStatus, Mode=OneWay}\"", xaml);
        Assert.Contains("Text=\"{Binding Text, Mode=OneWay}\"", xaml);
    }

    [Fact]
    public void Category_navigation_includes_a_subagent_button()
    {
        var xaml = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "CodexConversationManager.App", "MainWindow.xaml"));

        Assert.Contains("Key=SubAgent", xaml);
    }

    [Fact]
    public void Category_navigation_has_a_selected_state_binding()
    {
        var xaml = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "CodexConversationManager.App", "MainWindow.xaml"));

        Assert.Contains("IsNormalCategorySelected", xaml);
        Assert.Contains("IsResidualCategorySelected", xaml);
        Assert.Contains("#DCEBFA", xaml);
    }

    [Fact]
    public void Provider_sync_button_explains_login_mode_sync_and_requires_exit_prompt()
    {
        var xaml = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "CodexConversationManager.App", "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "CodexConversationManager.App", "MainWindow.xaml.cs"));

        Assert.Contains("Key=ProviderTooltip", xaml);
        Assert.Contains("完全退出 Codex", code);
    }

    [Fact]
    public void Import_dialog_contains_preview_destination_and_confirmation_controls()
    {
        var xaml = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "CodexConversationManager.App", "Views", "ConversationImportDialog.xaml"));

        Assert.Contains("Key=ChooseJsonl", xaml);
        Assert.Contains("Key=ExistingProject", xaml);
        Assert.Contains("Key=NewProject", xaml);
        Assert.Contains("Key=GenerateDuplicate", xaml);
        Assert.DoesNotContain("确认执行请输入：导入对话", xaml);
        Assert.Contains("IsEnabled=\"{Binding CanApply}\"", xaml);
        Assert.Contains("Click=\"StopCodex_Click\"", xaml);
        Assert.Contains("Key=CheckAndRefresh", xaml);
        Assert.Contains("Click=\"RestartCodex_Click\"", xaml);
        Assert.Contains("IsEnabled=\"{Binding CanRestart}\"", xaml);
        Assert.Contains("GroupName=\"ImportDestination\"", xaml);
        Assert.Contains("GroupName=\"ImportProvider\"", xaml);
        Assert.Contains("GroupName=\"DuplicateResolution\"", xaml);
    }

    [Fact]
    public void Main_window_exposes_an_import_conversation_command()
    {
        var xaml = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "CodexConversationManager.App", "MainWindow.xaml"));

        Assert.Contains("Key=ImportConversation", xaml);
        Assert.Contains("ImportConversation_Click", xaml);
    }
}
