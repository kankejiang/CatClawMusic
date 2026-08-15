using CatClawMusic.Core.Interfaces;
using CatClawMusic.Maui.Services;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// 下载管理页：展示与管理下载任务（新建 URL 下载、暂停/继续/取消/重试/删除、更改下载路径）。
/// </summary>
public partial class DownloadsPage : ContentPage
{
    private readonly DownloadsViewModel _vm;
    private readonly DownloadManager _manager;
    private readonly IPermissionService _permissionService;

    public DownloadsPage(DownloadsViewModel vm, DownloadManager manager, IPermissionService permissionService)
    {
        InitializeComponent();
        _vm = vm;
        _manager = manager;
        _permissionService = permissionService;
        BindingContext = vm;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Transient 页面即将销毁：解除事件订阅，避免单例 DownloadManager 长引用本页 VM
        _vm.Dispose();
    }

    /// <summary>右上角 ＋ ：新建下载任务（支持 http/https 直链与 magnet: 磁力链接）</summary>
    private async void OnAddDownloadTapped(object? sender, TappedEventArgs e)
    {
        var url = await DisplayPromptAsync("新建下载", "输入下载地址\n支持：http/https 直链、magnet: 磁力链接", "开始下载", "取消",
            placeholder: "https://... 或 magnet:?xt=urn:btih:...", keyboard: Keyboard.Url);
        if (string.IsNullOrWhiteSpace(url)) return;

        string? name = null;
        if (!url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            name = await DisplayPromptAsync("文件名称", "输入保存文件名（留空自动识别）", "开始下载", "取消",
                placeholder: "文件名.mp3");
        }
        _vm.AddUrlDownload(url, string.IsNullOrWhiteSpace(name) ? null : name);
    }

    /// <summary>更改下载位置：Android 走自研文件管理器（需所有文件访问权限），Windows 走系统文件夹选择器</summary>
    private async void OnChangePathClicked(object? sender, EventArgs e)
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
            _manager.SetDownloadFolderPath(path);
            _vm.RefreshStats();
        }
        else
        {
            await DisplayAlert("提示", "未能获取所选文件夹，请重试。", "确定");
        }
#endif
    }
}
