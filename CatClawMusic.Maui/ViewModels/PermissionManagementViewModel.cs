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

    [ObservableProperty]
    private bool _isRefreshing;

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
    public string Title { get; }
    public string Description { get; }
    public string Icon { get; }
    public PermissionKind Kind { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    [NotifyPropertyChangedFor(nameof(ActionText))]
    private bool _isGranted;

    public string StatusText => IsGranted ? "已授予" : "未授权";
    public string StatusColor => IsGranted ? "#4CAF50" : "#FF9800";
    public string ActionText => IsGranted ? "查看" : "去授权";

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
    Storage,
    ManageStorage,
    Overlay,
    Notification
}
