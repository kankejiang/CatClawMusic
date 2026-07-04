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

    /// <summary>点击添加文件夹按钮时触发，根据「使用 SAF 文件夹扫描」开关决定使用系统 SAF 选择器还是自研文件管理器。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnAddFolderClicked(object? sender, EventArgs e)
    {
        if (_viewModel.UseSafScan)
        {
            await _viewModel.SelectFolderAsync();
        }
        else
        {
            await Shell.Current.GoToAsync("folderbrowser?mode=music&title=选择音乐文件夹");
        }
    }

    /// <summary>当页面显示在屏幕上时触发，加载已保存的音乐文件夹列表。</summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.LoadSavedFoldersAsync();
    }
}
