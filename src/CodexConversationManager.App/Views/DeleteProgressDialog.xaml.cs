using System.ComponentModel;
using System.Windows;
using CodexConversationManager.App.ViewModels;

namespace CodexConversationManager.App.Views;

public partial class DeleteProgressDialog : Window
{
    private readonly DeleteProgressViewModel _viewModel;

    public DeleteProgressDialog(DeleteProgressViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e) => e.Cancel = _viewModel.IsRunning;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void RestartCodex_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsRunning)
        {
            return;
        }

        var confirmation = MessageBox.Show(this,
            "这会关闭正在运行的 Codex/ChatGPT，并重新打开 Codex。是否继续？",
            "重启 Codex", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            var result = await new Services.CodexDesktopRestartService().RestartAsync();
            if (result.Warnings.Count > 0)
            {
                MessageBox.Show(this,
                    $"已请求重新打开 Codex，但有 {result.Warnings.Count} 个旧进程未能自动关闭。请手动退出 Codex 后再打开。",
                    "Codex 对话管理器", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"重启 Codex 失败：{exception.Message}", "Codex 对话管理器",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
