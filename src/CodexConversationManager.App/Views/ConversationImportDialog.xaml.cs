using System.Windows;
using CodexConversationManager.App.ViewModels;
using CodexConversationManager.Core.Import;

namespace CodexConversationManager.App.Views;

public partial class ConversationImportDialog : Window
{
    private readonly ConversationImportViewModel _viewModel;

    public ConversationImportDialog(ConversationImportViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private async void SelectFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Codex JSONL (*.jsonl)|*.jsonl|所有文件 (*.*)|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) == true) await _viewModel.LoadFilesAsync(dialog.FileNames);
    }

    private async void StopCodex_Click(object sender, RoutedEventArgs e) => await _viewModel.StopCodexAsync();

    private async void CheckCodex_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.CheckCodexExitAsync();
        await _viewModel.RefreshPreviewAsync();
    }

    private void Destination_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<ImportDestinationKind>(tag, out var value))
            _viewModel.DestinationKind = value;
    }

    private void ProviderMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<ImportProviderMode>(tag, out var value))
            _viewModel.ProviderMode = value;
    }

    private void DuplicateResolution_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<DuplicateIdResolution>(tag, out var value))
            _viewModel.DuplicateResolution = value;
    }

    private void ChooseParent_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择新项目父文件夹", Multiselect = false };
        if (dialog.ShowDialog(this) == true) _viewModel.NewProjectParent = dialog.FolderName;
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.ApplyAsync();
    }

    private async void RestartCodex_Click(object sender, RoutedEventArgs e) => await _viewModel.RestartCodexAsync();

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = _viewModel.HasImported;
}
