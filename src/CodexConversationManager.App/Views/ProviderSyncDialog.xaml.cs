using System.Windows;
using CodexConversationManager.App.ViewModels;

namespace CodexConversationManager.App.Views;

public partial class ProviderSyncDialog : Window
{
    private readonly ProviderSyncViewModel _viewModel;
    public ProviderSyncDialog(ProviderSyncViewModel viewModel)
    {
        InitializeComponent(); _viewModel = viewModel; DataContext = viewModel;
        Loaded += async (_, _) => await _viewModel.LoadAsync();
    }
    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (await _viewModel.ApplyAsync()) DialogResult = true;
    }
    private void ChooseBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择同步备份目录", Multiselect = false };
        if (dialog.ShowDialog(this) == true) _viewModel.SetBackupRoot(dialog.FolderName);
    }
}
