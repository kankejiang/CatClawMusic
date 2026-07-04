using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>文件夹浏览页面，用于在文件系统中浏览并选择文件夹或文件。</summary>
public partial class FolderBrowserPage : ContentPage
{
    private readonly FolderBrowserViewModel _viewModel;

    /// <summary>初始化 <see cref="FolderBrowserPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="viewModel">文件夹浏览页面对应的视图模型。</param>
    public FolderBrowserPage(FolderBrowserViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <summary>在列表中选中某个文件系统项时触发，清除选中状态并打开该项。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">选择变更事件参数。</param>
    private async void OnItemSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is FileSystemItem item)
        {
            if (sender is CollectionView cv)
            {
                cv.SelectedItem = null;
            }
            await _viewModel.OpenItemCommand.ExecuteAsync(item);
        }
    }
}
