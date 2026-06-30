using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

public partial class BackupRestorePage : ContentPage
{
    private readonly BackupRestoreViewModel _viewModel;

    public BackupRestorePage(BackupRestoreViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadBackupFilesCommand.ExecuteAsync(null);
    }

    private async void OnDeleteBackupClicked(object? sender, EventArgs e)
    {
        if (sender is Button button && button.BindingContext is BackupFileInfo file)
        {
            var confirm = await DisplayAlert(
                "删除备份",
                $"确认删除备份文件？\n\n{file.FileName}\n\n此操作无法撤销。",
                "删除", "取消");

            if (confirm)
            {
                await _viewModel.DeleteBackupCommand.ExecuteAsync(file);
            }
        }
    }
}
