using CatClawMusic.Core.Interfaces;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>本地音乐设置页面，用于管理本地音乐文件夹扫描等设置。</summary>
public partial class LocalMusicSettingsPage : ContentPage
{
    private readonly LocalMusicSettingsViewModel _viewModel;
    private readonly IPermissionService _permissionService;

    /// <summary>初始化 <see cref="LocalMusicSettingsPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="viewModel">本地音乐设置页面对应的视图模型。</param>
    /// <param name="permissionService">权限服务，用于自研文件管理器索要所有文件访问权限。</param>
    public LocalMusicSettingsPage(LocalMusicSettingsViewModel viewModel, IPermissionService permissionService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _permissionService = permissionService;
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
            // 自研文件管理器基于真实文件系统路径读取目录，Android 11+ 需要「所有文件访问」权限（MANAGE_EXTERNAL_STORAGE）
            var granted = await _permissionService.CheckManageStoragePermissionAsync();
            if (!granted)
            {
                var goToSettings = await DisplayAlert(
                    "需要所有文件访问权限",
                    "使用文件管理器选择文件夹需要授予「所有文件访问」权限（管理所有文件），请在系统设置中开启。",
                    "去设置", "仍要进入");
                if (goToSettings)
                {
                    _permissionService.RequestManageStoragePermissionAsync();
                    return;
                }
            }

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
