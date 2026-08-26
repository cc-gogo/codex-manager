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
}
