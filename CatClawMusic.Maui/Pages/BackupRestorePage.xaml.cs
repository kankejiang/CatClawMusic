using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>备份与恢复页面，用于管理音乐库备份文件的创建、恢复与删除。</summary>
public partial class BackupRestorePage : ContentPage
{
    private readonly BackupRestoreViewModel _viewModel;

    /// <summary>初始化 <see cref="BackupRestorePage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="viewModel">备份恢复页面对应的视图模型。</param>
    public BackupRestorePage(BackupRestoreViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <summary>当页面显示在屏幕上时触发，加载可用的备份文件列表。</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadBackupFilesCommand.ExecuteAsync(null);
    }

    /// <summary>点击删除备份按钮时触发，弹出确认对话框，确认后删除对应的备份文件。</summary>
    /// <param name="sender">事件源，通常为携带备份文件上下文的按钮。</param>
    /// <param name="e">事件参数。</param>
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
