using CatClawMusic.Core.Interfaces;
using CatClawMusic.Maui.Services;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>通用设置页面，用于配置缓存清理、下载路径等通用选项。</summary>
public partial class GeneralSettingsPage : ContentPage
{
    private readonly GeneralSettingsViewModel _viewModel;
    private readonly DownloadManager _downloadManager;
    private readonly IPermissionService _permissionService;

    /// <summary>初始化 <see cref="GeneralSettingsPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="viewModel">通用设置页面对应的视图模型。</param>
    /// <param name="downloadManager">下载管理器（用于保存下载路径设置）。</param>
    /// <param name="permissionService">权限服务（更改下载路径需所有文件访问权限）。</param>
    public GeneralSettingsPage(GeneralSettingsViewModel viewModel, DownloadManager downloadManager, IPermissionService permissionService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _downloadManager = downloadManager;
        _permissionService = permissionService;
        BindingContext = viewModel;
    }

    /// <summary>当页面显示在屏幕上时触发，刷新缓存占用与下载路径（从文件夹浏览器返回后路径可能已变化）。</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.RefreshCacheSizeAsync();
        _viewModel.RefreshDownloadPath();
    }

    /// <summary>更改下载路径：Android 走自研文件管理器（需所有文件访问权限），Windows 走系统文件夹选择器。</summary>
    private async void OnChangeDownloadPathClicked(object? sender, EventArgs e)
    {
#if ANDROID
        var granted = await _permissionService.CheckManageStoragePermissionAsync();
        if (!granted)
        {
            var goToSettings = await DisplayAlert(
                "需要所有文件访问权限",
                "更改下载位置需要授予「所有文件访问」权限（管理所有文件），请在系统设置中开启。",
                "去设置", "仍要进入");
            if (goToSettings)
            {
                _permissionService.RequestManageStoragePermissionAsync();
                return;
            }
        }
        await Shell.Current.GoToAsync("folderbrowser?mode=download&title=选择下载文件夹");
#elif WINDOWS
        var path = await Platforms.Windows.WindowsFolderPicker.PickFolderAsync();
        if (!string.IsNullOrEmpty(path))
        {
            _downloadManager.SetDownloadFolderPath(path);
            _viewModel.RefreshDownloadPath();
        }
        else
        {
            await DisplayAlert("提示", "未能获取所选文件夹，请重试。", "确定");
        }
#endif
    }
}
