using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>本地音乐设置页面，用于管理本地音乐文件夹扫描等设置。</summary>
public partial class LocalMusicSettingsPage : ContentPage
{
    private readonly LocalMusicSettingsViewModel _viewModel;

    /// <summary>初始化 <see cref="LocalMusicSettingsPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="viewModel">本地音乐设置页面对应的视图模型。</param>
    public LocalMusicSettingsPage(LocalMusicSettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <summary>点击浏览文件系统按钮时触发，导航到文件夹浏览页面以选择音乐文件夹。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnBrowseFilesystemClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("folderbrowser?mode=music&title=选择音乐文件夹");
    }

    /// <summary>当页面显示在屏幕上时触发，加载已保存的音乐文件夹列表。</summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.LoadSavedFoldersAsync();
    }
}
