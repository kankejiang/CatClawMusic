using CatClawMusic.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Storage;
using System.IO;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 外观设置页 ViewModel：管理深色模式、启动页选择、自定义背景图与背景透明度等外观相关配置。
/// </summary>
public partial class AppearanceSettingsViewModel : ObservableObject
{
    private readonly IThemeService? _themeService;
    private bool _isLoadingTheme = false;

    /// <summary>是否启用深色模式</summary>
    [ObservableProperty]
    private bool _isDarkMode = false;

    /// <summary>启动页可选项列表</summary>
    [ObservableProperty]
    private List<string> _startupPageOptions = new() { "音乐库", "探索", "播放" };

    /// <summary>当前选中的启动页索引</summary>
    [ObservableProperty]
    private int _selectedStartupPageIndex = 0;

    /// <summary>是否存在自定义背景图</summary>
    [ObservableProperty]
    private bool _hasCustomBackground = false;

    /// <summary>自定义背景图预览</summary>
    [ObservableProperty]
    private ImageSource? _customBackgroundPreview = null;

    /// <summary>自定义背景透明度（0.0 - 1.0）</summary>
    [ObservableProperty]
    private double _backgroundOpacity = 0.5;

    /// <summary>自定义背景文件名</summary>
    [ObservableProperty]
    private string _customBackgroundName = string.Empty;

    /// <summary>是否启用雾面动态背景（播放页/歌词页）</summary>
    [ObservableProperty]
    private bool _frostedBackgroundEnabled = true;

    /// <summary>是否启用莫奈取色背景（背景色跟随系统壁纸，Material You）</summary>
    [ObservableProperty]
    private bool _monetBackgroundEnabled = false;

    /// <summary>是否开启动态封面取色背景（背景色跟随当前歌曲封面）</summary>
    [ObservableProperty]
    private bool _coverBackgroundEnabled = false;

    /// <summary>
    /// 初始化 <see cref="AppearanceSettingsViewModel"/> 实例。
    /// </summary>
    /// <param name="themeService">主题服务，可为空（设计时支持）</param>
    public AppearanceSettingsViewModel(IThemeService? themeService = null)
    {
        _themeService = themeService;
    }

    partial void OnIsDarkModeChanged(bool value)
    {
        if (_themeService != null)
        {
            _themeService.SetDarkModeSetting(value ? DarkModeSetting.Dark : DarkModeSetting.Light);
            _themeService.ApplyTheme();
        }
    }

    partial void OnSelectedStartupPageIndexChanged(int value)
    {
        Preferences.Default.Set("StartupPageIndex", value);
    }

    partial void OnBackgroundOpacityChanged(double value)
    {
        if (_isLoadingTheme) return;
        if (_themeService != null && _themeService.HasCustomBackground)
        {
            _themeService.SetCustomBackgroundOpacity(value);
        }
    }

    partial void OnFrostedBackgroundEnabledChanged(bool value)
    {
        if (_isLoadingTheme) return;
        _themeService?.SetFrostedBackgroundEnabled(value);
        // 动态封面背景依赖雾面：关闭雾面时联动关闭动态封面（service 内已级联持久化，这里同步 UI 状态）
        if (!value && CoverBackgroundEnabled)
            CoverBackgroundEnabled = false;
    }

    partial void OnMonetBackgroundEnabledChanged(bool value)
    {
        if (_isLoadingTheme) return;
        // 单选互斥：开启莫奈时自动关闭动态封面（属性赋值会链式触发 OnCoverBackgroundEnabledChanged → service 持久化）
        if (value && CoverBackgroundEnabled)
            CoverBackgroundEnabled = false;
        _themeService?.SetMonetBackgroundEnabled(value);
    }

    partial void OnCoverBackgroundEnabledChanged(bool value)
    {
        if (_isLoadingTheme) return;
        if (value)
        {
            // 动态封面依赖雾面：雾面未开时回弹为关（UI Switch 已禁用，此为逻辑层兜底）
            if (_themeService != null && !_themeService.FrostedBackgroundEnabled)
            {
                CoverBackgroundEnabled = false;
                return;
            }
            // 单选互斥：开启动态封面时自动关闭莫奈
            if (MonetBackgroundEnabled)
                MonetBackgroundEnabled = false;
        }
        _themeService?.SetCoverBackgroundEnabled(value);
    }

    /// <summary>选择自定义背景图：通过文件选择器选取图片并应用为应用背景</summary>
    [RelayCommand]
    public async Task SelectBackgroundAsync()
    {
        try
        {
            var options = new PickOptions
            {
                FileTypes = FilePickerFileType.Images,
                PickerTitle = "选择背景图片"
            };

            var result = await FilePicker.Default.PickAsync(options);
            if (result == null) return;

            var appDataDir = FileSystem.AppDataDirectory;
            var bgDir = Path.Combine(appDataDir, "backgrounds");
            Directory.CreateDirectory(bgDir);
            var destPath = Path.Combine(bgDir, $"custom_bg{Path.GetExtension(result.FileName)}");

            using (var src = await result.OpenReadAsync())
            using (var dst = File.Create(destPath))
            {
                await src.CopyToAsync(dst);
            }

            _themeService?.SetCustomBackground(destPath, BackgroundOpacity);
            LoadCurrentTheme();
        }
        catch (Exception ex)
        {
            Log.Debug("AppearanceSettingsViewModel", $"[AppearanceVM] SelectBackground failed: {ex.Message}");
        }
    }

    /// <summary>清除自定义背景图：移除当前背景并恢复默认外观</summary>
    [RelayCommand]
    public void ClearBackground()
    {
        _themeService?.ClearCustomBackground();
        LoadCurrentTheme();
    }

    /// <summary>使用默认背景：清除自定义图片并恢复主题渐变背景</summary>
    [RelayCommand]
    public void UseDefaultBackground()
    {
        _themeService?.ClearCustomBackground();
        LoadCurrentTheme();
    }

    /// <summary>从主题服务加载当前外观配置，同步到各绑定属性</summary>
    public void LoadCurrentTheme()
    {
        _isLoadingTheme = true;
        if (_themeService == null) { _isLoadingTheme = false; return; }
        IsDarkMode = _themeService.DarkModeSetting == DarkModeSetting.Dark;
        SelectedStartupPageIndex = Preferences.Default.Get("StartupPageIndex", 2);

        HasCustomBackground = _themeService.HasCustomBackground;
        BackgroundOpacity = _themeService.CustomBackgroundOpacity;
        FrostedBackgroundEnabled = _themeService.FrostedBackgroundEnabled;
        MonetBackgroundEnabled = _themeService.MonetBackgroundEnabled;
        CoverBackgroundEnabled = _themeService.CoverBackgroundEnabled;
            if (HasCustomBackground && _themeService.CustomBackgroundPath != null)
            {
                try
                {
                    // 关键修复：自定义背景预览同样不能走 FromStream 全分辨率解码（同 ThemeService 崩溃根因）。
                    // 改用 FromFile 走 CachingFileImageSourceService 降采样，避免 196MB 大图在设置页绘制时崩溃。
                    CustomBackgroundPreview = ImageSource.FromFile(_themeService.CustomBackgroundPath);
                    CustomBackgroundName = Path.GetFileName(_themeService.CustomBackgroundPath);
            }
            catch
            {
                CustomBackgroundPreview = null;
                CustomBackgroundName = string.Empty;
            }
        }
        else
        {
            CustomBackgroundPreview = null;
            CustomBackgroundName = string.Empty;
        }
        _isLoadingTheme = false;
    }

    /// <summary>将启动页索引映射为应用主 Tab 的索引</summary>
    /// <param name="startupIndex">启动页索引（0=音乐库, 1=探索, 2=播放）</param>
    /// <returns>主 Tab 索引</returns>
    public static int MapStartupIndexToTabIndex(int startupIndex)
    {
        return startupIndex switch
        {
            0 => 3,
            1 => 1,
            2 => 0,
            _ => 0
        };
    }
}
