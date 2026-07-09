using CatClawMusic.Maui.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 桌面歌词设置页 ViewModel：管理桌面歌词开关、字号、颜色、锁定、背景透明度等设置。
/// </summary>
public partial class DesktopLyricViewModel : ObservableObject
{
    private readonly DesktopLyricManager _manager;
    private readonly LyricsSettingsService _settings;
    private bool _isInternalUpdate;

    /// <summary>是否已获得悬浮窗权限</summary>
    [ObservableProperty] private bool _hasPermission = true;
    /// <summary>权限状态描述文本</summary>
    [ObservableProperty] private string _permissionStatus = "";
    /// <summary>桌面歌词是否已开启</summary>
    [ObservableProperty] private bool _isDesktopLyricEnabled;
    /// <summary>桌面歌词字号</summary>
    [ObservableProperty] private double _desktopFontSize;
    /// <summary>背景透明度</summary>
    [ObservableProperty] private double _bgOpacity;
    /// <summary>是否锁定位置</summary>
    [ObservableProperty] private bool _isLocked;
    /// <summary>文字颜色</summary>
    [ObservableProperty] private string _textColor;
    /// <summary>高亮颜色</summary>
    [ObservableProperty] private string _highlightColor;
    /// <summary>预览歌词文本</summary>
    [ObservableProperty] private string _previewText = "猫爪音乐 · 桌面歌词预览";

    /// <summary>可选颜色列表</summary>
    public List<string> ColorOptions { get; } = new()
    {
        "#B3FFFFFF", "#FFFFFFFF", "#B3000000", "#FF64B5F6",
        "#FFFFE082", "#FFFF7043", "#FFEC407A", "#FFAB47BC",
        "#FF66BB6A", "#FFEF5350", "#FF26C6DA", "#FFFFCA28"
    };

    public DesktopLyricViewModel(DesktopLyricManager manager)
    {
        _manager = manager;
        _settings = LyricsSettingsService.Instance;

        // 从设置加载
        IsDesktopLyricEnabled = _settings.DesktopLyricEnabled;
        DesktopFontSize = _settings.DesktopFontSize;
        BgOpacity = _settings.DesktopBgOpacity;
        IsLocked = _settings.DesktopLocked;
        TextColor = _settings.DesktopTextColor;
        HighlightColor = _settings.DesktopHighlightColor;
    }

    /// <summary>页面显示时检查权限状态</summary>
    public async Task OnAppearingAsync()
    {
        await CheckPermissionAsync();
    }

    /// <summary>检查悬浮窗权限</summary>
    private async Task CheckPermissionAsync()
    {
#if ANDROID
        HasPermission = await _manager.CheckPermissionAsync();
        PermissionStatus = HasPermission ? "已授权" : "未授权 - 点击请求权限";
#else
        HasPermission = true;
        PermissionStatus = "当前平台不支持桌面歌词";
#endif
    }

    /// <summary>开关变化时自动响应（由 Switch 双向绑定触发）</summary>
    partial void OnIsDesktopLyricEnabledChanged(bool value)
    {
        if (_isInternalUpdate) return;
        if (value)
        {
#if ANDROID
            _ = EnableWithPermissionCheckAsync();
#else
            _isInternalUpdate = true;
            IsDesktopLyricEnabled = false;
            _isInternalUpdate = false;
#endif
        }
        else
        {
            _manager.Disable();
        }
    }

#if ANDROID
    private async Task EnableWithPermissionCheckAsync()
    {
        var success = await _manager.EnableAsync();
        if (!success)
        {
            // 权限不足，回退开关
            _isInternalUpdate = true;
            IsDesktopLyricEnabled = false;
            _isInternalUpdate = false;
            await CheckPermissionAsync();
        }
    }
#endif

    /// <summary>请求悬浮窗权限</summary>
    [RelayCommand]
    private async Task RequestPermission()
    {
        await _manager.RequestPermissionAsync();
    }

    /// <summary>选择文字颜色</summary>
    [RelayCommand]
    private void SelectTextColor(string color)
    {
        TextColor = color;
    }

    /// <summary>选择高亮颜色</summary>
    [RelayCommand]
    private void SelectHighlightColor(string color)
    {
        HighlightColor = color;
    }

    /// <summary>字号变化时保存设置并应用</summary>
    partial void OnDesktopFontSizeChanged(double value)
    {
        _settings.DesktopFontSize = value;
        _manager.ApplySettings();
    }

    /// <summary>背景透明度变化时保存设置并应用</summary>
    partial void OnBgOpacityChanged(double value)
    {
        _settings.DesktopBgOpacity = value;
        _manager.ApplySettings();
    }

    /// <summary>锁定状态变化时保存设置并应用</summary>
    partial void OnIsLockedChanged(bool value)
    {
        _settings.DesktopLocked = value;
        _manager.ApplySettings();
    }

    /// <summary>文字颜色变化时保存设置并应用</summary>
    partial void OnTextColorChanged(string value)
    {
        _settings.DesktopTextColor = value;
        _manager.ApplySettings();
    }

    /// <summary>高亮颜色变化时保存设置并应用</summary>
    partial void OnHighlightColorChanged(string value)
    {
        _settings.DesktopHighlightColor = value;
        _manager.ApplySettings();
    }
}
