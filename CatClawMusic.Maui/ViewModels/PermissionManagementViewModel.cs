using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 权限管理页 ViewModel：展示存储 / 全文件管理 / 悬浮窗 / 通知权限状态，
/// 提供跳转授权入口。
/// </summary>
public partial class PermissionManagementViewModel : ObservableObject
{
    private readonly IPermissionService _permissionService;

    /// <summary>权限项列表</summary>
    public ObservableCollection<PermissionItem> Permissions { get; } = new();

    /// <summary>是否正在刷新权限状态</summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>
    /// 初始化 <see cref="PermissionManagementViewModel"/> 实例。
    /// </summary>
    /// <param name="permissionService">权限服务，用于检查与请求各类权限</param>
    public PermissionManagementViewModel(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    /// <summary>页面出现时加载权限状态</summary>
    public async Task OnAppearingAsync()
    {
        await RefreshAsync();
    }

    /// <summary>刷新所有权限状态</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        try
        {
            var storage = await _permissionService.CheckStoragePermissionAsync();
            var manageStorage = await _permissionService.CheckManageStoragePermissionAsync();
            var overlay = await _permissionService.CheckOverlayPermissionAsync();
            var notification = await CheckNotificationPermissionAsync();

            Permissions.Clear();
            Permissions.Add(new PermissionItem(
                "存储 / 媒体读取",
                "读取本地音乐文件所需的基础权限",
                "ic_folder",
                storage,
                PermissionKind.Storage));
            Permissions.Add(new PermissionItem(
                "管理所有文件",
                "Android 11+ 用于绕过 Scoped Storage，扫描完整音乐库",
                "ic_lock_locked",
                manageStorage,
                PermissionKind.ManageStorage));
            Permissions.Add(new PermissionItem(
                "悬浮窗权限",
                "显示桌面歌词悬浮窗所需",
                "ic_equalizer",
                overlay,
                PermissionKind.Overlay));
            Permissions.Add(new PermissionItem(
                "通知权限",
                "显示播放控制通知所需（Android 13+）",
                "ic_play",
                notification,
                PermissionKind.Notification));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PermissionManagement] Refresh 失败: {ex.Message}");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>请求指定权限</summary>
    [RelayCommand]
    public async Task RequestPermissionAsync(PermissionItem? item)
    {
        if (item == null) return;
        try
        {
            switch (item.Kind)
            {
                case PermissionKind.Storage:
                    await _permissionService.RequestStoragePermissionAsync();
                    break;
                case PermissionKind.ManageStorage:
                    await _permissionService.RequestManageStoragePermissionAsync();
                    break;
                case PermissionKind.Overlay:
                    await _permissionService.RequestOverlayPermissionAsync();
                    break;
                case PermissionKind.Notification:
                    await RequestNotificationPermissionAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PermissionManagement] Request 失败: {ex.Message}");
        }

        // 跳转授权后给系统一点时间
        await Task.Delay(300);
        await RefreshAsync();
    }

    /// <summary>打开应用系统设置页</summary>
    [RelayCommand]
    public void OpenAppSettings()
    {
        try { _permissionService.OpenAppSettings(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PermissionManagement] OpenAppSettings 失败: {ex.Message}"); }
    }

    private static async Task<bool> CheckNotificationPermissionAsync()
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            var check = await Microsoft.Maui.ApplicationModel.Permissions.CheckStatusAsync<Microsoft.Maui.ApplicationModel.Permissions.PostNotifications>();
            return check == PermissionStatus.Granted;
        }
        return true;
#else
        return true;
#endif
    }

    private static async Task RequestNotificationPermissionAsync()
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            await Microsoft.Maui.ApplicationModel.Permissions.RequestAsync<Microsoft.Maui.ApplicationModel.Permissions.PostNotifications>();
        }
#endif
        await Task.CompletedTask;
    }
}

/// <summary>权限项展示模型</summary>
public partial class PermissionItem : ObservableObject
{
    /// <summary>权限标题</summary>
    public string Title { get; }
    /// <summary>权限描述</summary>
    public string Description { get; }
    /// <summary>权限项图标资源名称</summary>
    public string Icon { get; }
    /// <summary>权限类型</summary>
    public PermissionKind Kind { get; }

    /// <summary>该权限是否已授予</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    [NotifyPropertyChangedFor(nameof(ActionText))]
    private bool _isGranted;

    /// <summary>权限状态展示文本（已授予 / 未授权）</summary>
    public string StatusText => IsGranted ? "已授予" : "未授权";
    /// <summary>权限状态展示颜色（已授予绿色 / 未授权橙色）</summary>
    public string StatusColor => IsGranted ? "#4CAF50" : "#FF9800";
    /// <summary>操作按钮文本（已授予时为“查看”，未授权时为“去授权”）</summary>
    public string ActionText => IsGranted ? "查看" : "去授权";

    /// <summary>
    /// 初始化 <see cref="PermissionItem"/> 实例。
    /// </summary>
    /// <param name="title">权限标题</param>
    /// <param name="description">权限描述</param>
    /// <param name="icon">图标资源名称</param>
    /// <param name="isGranted">是否已授予</param>
    /// <param name="kind">权限类型</param>
    public PermissionItem(string title, string description, string icon, bool isGranted, PermissionKind kind)
    {
        Title = title;
        Description = description;
        Icon = icon;
        IsGranted = isGranted;
        Kind = kind;
    }
}

/// <summary>权限类型枚举</summary>
public enum PermissionKind
{
    /// <summary>存储 / 媒体读取权限</summary>
    Storage,
    /// <summary>管理所有文件权限（Android 11+）</summary>
    ManageStorage,
    /// <summary>悬浮窗权限</summary>
    Overlay,
    /// <summary>通知权限（Android 13+）</summary>
    Notification
}
