using System.Windows;
using CodexConversationManager.App.ViewModels;

namespace CodexConversationManager.App.Views;

public partial class PermanentDeleteDialog : Window
{
    public PermanentDeleteDialog(PermanentDeleteViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
