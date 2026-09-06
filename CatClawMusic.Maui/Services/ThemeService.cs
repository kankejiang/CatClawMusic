using CatClawMusic.Core.Interfaces;
using CoreAppTheme = CatClawMusic.Core.Interfaces.AppTheme;
using MauiAppTheme = Microsoft.Maui.ApplicationModel.AppTheme;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System.Collections.Concurrent;
using System.IO;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// MAUI 主题管理服务，支持 10 种颜色主题和明/暗/跟随系统三种模式。
/// 通过 MAUI ResourceDictionary 动态切换主题色与背景图（5 套主题内置星空/天空静态背景）。
/// </summary>
public class ThemeService : IThemeService
{
    private const string KeyTheme = "theme_index";
    private const string KeyDarkMode = "dark_mode";
    private const string KeyCustomBgPath = "custom_bg_path";
    private const string KeyCustomBgOpacity = "custom_bg_opacity";
    private const string KeyFrostedBg = "frosted_bg_enabled";
    private const string KeyMonetBg = "monet_bg_enabled";
    private const string KeyCoverBg = "cover_bg_enabled";

    private CoreAppTheme _currentTheme;
    private DarkModeSetting _darkModeSetting;
    private string? _customBackgroundPath;
    private double _customBackgroundOpacity = 0.5;
    private bool _frostedBackgroundEnabled = true;
    private bool _monetBackgroundEnabled = false;
    private bool _coverBackgroundEnabled = false;

    // ═══ 动态封面取色（当前歌曲封面主/次色，随歌切换）═══
    // 静态存储：ThemeService 为单例，GetBackgroundDesign 静态渲染链直接读取（与 MonetPalette 同构）。
    private static (byte R, byte G, byte B)? s_coverPrimary;
    private static (byte R, byte G, byte B)? s_coverSecondary;
    private static string? s_coverFingerprint;   // 调色板指纹（主次色 hex 直拼；色调不变则不重刷背景）

    // ═══ 统一全局背景（GlobalBackgroundService）状态 ═══
    // 供 GlobalBackgroundService 在每次主题/背景切换后，把当前生效的背景一次性绘制到 Window 层。
    // 优先级：CustomBackgroundPath（用户自定义图） > ThemeBackgroundPng（主题内置渐变图）> 无（纯色兜底）。
    // Includes the-current theme code drawn PNG bytes and the custom file path, plus an enabled flag.
    public static bool CurrentBackgroundEnabled { get; private set; }
    public static byte[]? CurrentThemeBackgroundPng { get; private set; }
    public static string? CurrentCustomBackgroundPath { get; private set; }
    /// <summary>当前是否深色模式（供 GlobalBackgroundService 判定系统栏图标颜色，不受透明笔刷影响）。</summary>
    public static bool CurrentIsDark { get; private set; }

    /// <summary>主题色定义（唯一主题：樱粉奶油）</summary>
    private static readonly Dictionary<CoreAppTheme, ThemeColors> ThemeMap = new()
    {
        [CoreAppTheme.Pink] = new ThemeColors("#FF8FB8", "#FFD9E7", "#F56FA0"),
    };

    // ======================================================================
    // 主题背景渐变设计（2 套：唯一樱粉主题 × 深/浅模式）—— 萌系糖果版
    //   深色 = 「夜猫模式」：暖紫黑底 + 粉/薄荷光晕
    //   浅色 = 「奶油底」：奶白 → 樱粉的柔和过渡 + 白色高光
    // 每套 = 1 层多停靠线性渐变（主基调，From→To 为归一化坐标 0~1）
    //      + 1~2 层径向光晕（中心带透明度色 → 全透明边缘，Cx/Cy/Radius 归一化）。
    // 颜色统一 #AARRGGBB 格式，便于 Android Color.ParseColor / Win2D 直接解析。
    // ======================================================================
    private sealed record BgStop(string Argb, float Offset);
    private sealed record BgGlow(float Cx, float Cy, float Radius, string CenterArgb, string EdgeArgb = "#00000000");
    private sealed record BgDesign((float X, float Y) From, (float X, float Y) To, BgStop[] Stops, BgGlow[] Glows);

    // 方案B「冷色统一」（2026-09-05）：原稿右上下冰青光晕(20%)把上半页染成钢青蓝(S 0.35)，
    // 左下樱粉光晕(14%)与蓝底混出脏紫(hue 245-263°)——一条背景横跨青→紫两个色相家族。
    // 现改为：青光晕减弱 1/3(13%)，粉光晕换薄荷 #7ED8C3(8%，与深色 HeroBrush 同源)，
    // 全背景收敛为单一冷色家族，底色不变。
    private static readonly BgDesign VioletDusk = new((0, 0), (0, 1), new[]
    {
        new BgStop("#FF11141D", 0f), new BgStop("#FF171C2A", 0.55f), new BgStop("#FF1E2536", 1f),
    }, new[]
    {
        new BgGlow(0.80f, 0.18f, 0.52f, "#2A55D6FF"),
        new BgGlow(0.24f, 0.86f, 0.70f, "#147ED8C3"),
    });

    private static readonly BgDesign MorningBlush = new((0, 0), (0, 1), new[]
    {
        new BgStop("#FFFFF7FA", 0f), new BgStop("#FFFFEFF5", 0.55f), new BgStop("#FFFFE4EE", 1f),
    }, new[]
    {
        new BgGlow(0.80f, 0.14f, 0.70f, "#F2FFFFFF"),
        new BgGlow(0.20f, 0.88f, 0.60f, "#387ED8C3"),
    });

    /// <summary>主题 + 深浅模式 → 背景渐变设计。
    /// bgKey 前缀分派：cover_ = 当前歌曲封面取色（动态随歌）；monet_ = 壁纸取色（Material You）；
    /// 其余（std）= 唯一樱粉主题的深/浅标准两套。</summary>
    private static BgDesign GetBackgroundDesign(CoreAppTheme theme, bool isDark, string bgKey)
    {
        if (bgKey.StartsWith("cover_", StringComparison.Ordinal) && s_coverPrimary is { } cp)
        {
            // 次色缺失或与主色同色时旋转 60° 合成（与莫奈次色缺失的处理一致，保持双光晕层次）
            var cs = (s_coverSecondary is { } s2 && s2 != cp) ? s2 : MonetPalette.RotateHue(cp, 60);
            return GetPaletteDesign(isDark, cp, cs);
        }
        if (bgKey.StartsWith("monet_", StringComparison.Ordinal) && MonetPalette.Current != null)
        {
            return GetPaletteDesign(isDark,
                ParseHexColor(MonetPalette.Current.Primary),
                ParseHexColor(MonetPalette.Current.Secondary));
        }
        return isDark ? VioletDusk : MorningBlush;
    }

    // ═══ 动态取色背景（Material You 结构语言）：调色板主/次色 → 渐变 + 光晕 ═══
    // 结构语言与标准两套一致：1 层 3 停靠线性渐变 + 2 层径向光晕；
    // 底色饱和度固定压低档（背景不抢内容），光晕保留主色相、饱和度钳制防荧光。
    // 莫奈（壁纸）与封面取色共用同一设计数学，仅颜色来源不同。

    /// <summary>深色：主色 hue 的暗夜底 + 主/次色光晕（仿 VioletDusk 结构）</summary>
    private static BgDesign GetPaletteDesign(bool isDark, (byte R, byte G, byte B) primary, (byte R, byte G, byte B) secondary)
    {
        var (ph, ps, _) = MonetPalette.ToHsl(primary);
        var (sh, ss, _) = MonetPalette.ToHsl(secondary);
        ps = Math.Clamp(ps, 0.15, 0.60);
        ss = Math.Clamp(ss, 0.15, 0.60);

        if (isDark)
        {
            return new BgDesign((0, 0), (0, 1), new[]
            {
                new BgStop(ArgbWithAlpha(MonetPalette.FromHsl(ph, 0.26, 0.075), 0xFF), 0f),
                new BgStop(ArgbWithAlpha(MonetPalette.FromHsl(ph, 0.26, 0.105), 0xFF), 0.55f),
                new BgStop(ArgbWithAlpha(MonetPalette.FromHsl(ph, 0.26, 0.145), 0xFF), 1f),
            }, new[]
            {
                new BgGlow(0.80f, 0.18f, 0.52f, ArgbWithAlpha(MonetPalette.FromHsl(ph, ps, 0.62), 0x2A)),
                new BgGlow(0.24f, 0.86f, 0.70f, ArgbWithAlpha(MonetPalette.FromHsl(sh, ss, 0.62), 0x14)),
            });
        }

        return new BgDesign((0, 0), (0, 1), new[]
        {
            new BgStop(ArgbWithAlpha(MonetPalette.FromHsl(ph, 0.40, 0.97), 0xFF), 0f),
            new BgStop(ArgbWithAlpha(MonetPalette.FromHsl(ph, 0.42, 0.945), 0xFF), 0.55f),
            new BgStop(ArgbWithAlpha(MonetPalette.FromHsl(ph, 0.44, 0.91), 0xFF), 1f),
        }, new[]
        {
            new BgGlow(0.80f, 0.14f, 0.70f, "#F2FFFFFF"),
            new BgGlow(0.20f, 0.88f, 0.60f, ArgbWithAlpha(MonetPalette.FromHsl(sh, ss, 0.70), 0x38)),
        });
    }

    private static (byte R, byte G, byte B) ParseHexColor(string hex)
        => ((byte)Convert.ToInt32(hex.Substring(1, 2), 16),
            (byte)Convert.ToInt32(hex.Substring(3, 2), 16),
            (byte)Convert.ToInt32(hex.Substring(5, 2), 16));

    private static string ArgbWithAlpha((byte R, byte G, byte B) c, byte a)
        => $"#{a:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>获取当前主题色枚举</summary>
    public CoreAppTheme CurrentTheme => _currentTheme;
    /// <summary>获取当前暗黑模式设置</summary>
    public DarkModeSetting DarkModeSetting => _darkModeSetting;
    /// <summary>获取自定义背景图片的绝对路径；未设置时为 null</summary>
    public string? CustomBackgroundPath => _customBackgroundPath;
    /// <summary>获取自定义背景的不透明度（0.1 ~ 1.0）</summary>
    public double CustomBackgroundOpacity => _customBackgroundOpacity;
    /// <summary>获取是否存在有效的自定义背景图片</summary>
    public bool HasCustomBackground => !string.IsNullOrEmpty(_customBackgroundPath) && File.Exists(_customBackgroundPath);

    /// <summary>获取是否启用雾面动态背景（播放页/歌词页）</summary>
    public bool FrostedBackgroundEnabled => _frostedBackgroundEnabled;

    /// <summary>获取是否启用莫奈取色背景（背景色跟随系统壁纸，Material You）</summary>
    public bool MonetBackgroundEnabled => _monetBackgroundEnabled;

    /// <summary>主题/背景应用完成事件：ApplyTheme 结束时触发。页面侧据此强制重刷原生背景。</summary>
    public event Action? Applied;

    /// <summary>
    /// 静态版本的主题刷新通知：供 FrostedBackground 等控件自行订阅。
    /// Release+裁剪下 DynamicResource 对 Source 的运行时推送并不可靠，
    /// 需由背景控件在收到本通知后显式重映射，才能让自定义背景立即生效。
    /// </summary>
    public static event Action? StaticApplied;

    private void NotifyApplied()
    {
        try { Applied?.Invoke(); } catch { }
        try { StaticApplied?.Invoke(); } catch { }
    }

    /// <summary>可选主题列表（唯一：樱粉）</summary>
    public List<CoreAppTheme> AvailableThemes => new() { CoreAppTheme.Pink };

    /// <summary>构造函数，加载持久化设置并立即应用主题</summary>
    public ThemeService()
    {
        LoadSettings();
        // 莫奈取色：启动时取一次壁纸色；之后监听壁纸变化（Android）自动重取重画
        MonetPalette.Refresh();
        MonetPalette.Watch(OnMonetColorsChanged);
        // 启动时立即设置 UserAppTheme，确保 RequestedTheme 正确
        if (Application.Current != null)
        {
            Application.Current.UserAppTheme = _darkModeSetting switch
            {
                DarkModeSetting.Light => MauiAppTheme.Light,
                DarkModeSetting.Dark => MauiAppTheme.Dark,
                _ => MauiAppTheme.Unspecified,
            };
        }
    }

    /// <summary>设置自定义背景图片及不透明度</summary>
    /// <param name="imagePath">背景图片路径；传 null 或空字符串表示清除背景</param>
    /// <param name="opacity">不透明度（0.1 ~ 1.0），自动钳制到范围内</param>
    public void SetCustomBackground(string? imagePath, double opacity = 0.5)
    {
        _customBackgroundPath = imagePath;
        _customBackgroundOpacity = Math.Clamp(opacity, 0.1, 1.0);
        if (string.IsNullOrEmpty(imagePath))
        {
            Preferences.Default.Remove(KeyCustomBgPath);
            Preferences.Default.Remove(KeyCustomBgOpacity);
        }
        else
        {
            Preferences.Default.Set(KeyCustomBgPath, imagePath);
            Preferences.Default.Set(KeyCustomBgOpacity, _customBackgroundOpacity);
        }
        ApplyTheme();
    }

    /// <summary>仅更新自定义背景的不透明度（滑条拖动快路径：只改 opacity 资源键，
    /// 不做 ApplyTheme 全量重刷——那会重写约 40 个资源键并触发全 App 绑定重求值，
    /// 拖动每帧调用会明显卡顿）</summary>
    /// <param name="opacity">不透明度（0.1 ~ 1.0），自动钳制到范围内</param>
    public void SetCustomBackgroundOpacity(double opacity)
    {
        _customBackgroundOpacity = Math.Clamp(opacity, 0.1, 1.0);
        Preferences.Default.Set(KeyCustomBgOpacity, _customBackgroundOpacity);
        try
        {
            if (Application.Current?.Resources != null)
                Application.Current.Resources["CustomBackgroundOpacity"] = _customBackgroundOpacity;
        }
        catch { }
    }

    /// <summary>清除自定义背景图片设置</summary>
    public void ClearCustomBackground()
    {
        SetCustomBackground(null);
    }

    /// <summary>设置雾面动态背景开关并持久化。
    /// 关闭时级联关闭动态封面背景（动态封面依赖雾面：只有雾面开启才允许生效）。</summary>
    /// <param name="enabled">是否启用雾面背景</param>
    public void SetFrostedBackgroundEnabled(bool enabled)
    {
        _frostedBackgroundEnabled = enabled;
        Preferences.Default.Set(KeyFrostedBg, enabled);
        if (!enabled && _coverBackgroundEnabled)
        {
            _coverBackgroundEnabled = false;
            Preferences.Default.Set(KeyCoverBg, false);
        }
        ApplyTheme();
    }

    /// <summary>设置莫奈取色背景开关并持久化（背景色跟随系统壁纸）。
    /// 与动态封面背景单选互斥：开启莫奈时自动关闭动态封面。</summary>
    /// <param name="enabled">是否启用莫奈取色背景</param>
    public void SetMonetBackgroundEnabled(bool enabled)
    {
        _monetBackgroundEnabled = enabled;
        Preferences.Default.Set(KeyMonetBg, enabled);
        if (enabled && _coverBackgroundEnabled)
        {
            _coverBackgroundEnabled = false;
            Preferences.Default.Set(KeyCoverBg, false);
        }
        MonetPalette.Refresh();
        ApplyTheme();
    }

    /// <summary>壁纸颜色变化回调（MonetPalette.Watch）：重取色 → 指纹变化时重新应用主题，
    /// 背景 PNG 缓存按指纹分键，自动失效重画。</summary>
    private void OnMonetColorsChanged()
    {
        MonetPalette.Refresh();
        MainThread.BeginInvokeOnMainThread(ApplyTheme);
    }

    /// <summary>获取是否开启动态封面取色背景（背景色跟随当前歌曲封面）</summary>
    public bool CoverBackgroundEnabled => _coverBackgroundEnabled;

    /// <summary>设置动态封面取色背景开关并持久化（背景色跟随当前歌曲封面）。
    /// 约束：①与莫奈单选互斥——开启动态封面时自动关闭莫奈；
    /// ②依赖雾面动态背景——雾面关闭时开关无效（调用方 UI 已禁用，此处兜底拒绝）。</summary>
    public void SetCoverBackgroundEnabled(bool enabled)
    {
        if (enabled && !_frostedBackgroundEnabled) return;   // 雾面未开：拒绝开启
        _coverBackgroundEnabled = enabled;
        Preferences.Default.Set(KeyCoverBg, enabled);
        if (enabled && _monetBackgroundEnabled)
        {
            _monetBackgroundEnabled = false;
            Preferences.Default.Set(KeyMonetBg, false);
        }
        ApplyTheme();
    }

    /// <summary>封面取色回传（NowPlayingViewModel 取色完成后调用，须在主线程）。
    /// primaryRgb=0xRRGGBB；传 0 表示当前无封面（清空封面色，背景回退莫奈/标准）。
    /// 指纹（主次色 hex 直拼）不变时不重刷——同封面重复取色零开销。</summary>
    public void UpdateCoverPalette(int primaryRgb, int secondaryRgb)
    {
        if (primaryRgb == 0)
        {
            if (s_coverFingerprint == null) return;   // 已是空态，无需重刷
            s_coverPrimary = null;
            s_coverSecondary = null;
            s_coverFingerprint = null;
            if (_coverBackgroundEnabled) ApplyTheme();
            return;
        }

        var p = ((byte)((primaryRgb >> 16) & 0xFF), (byte)((primaryRgb >> 8) & 0xFF), (byte)(primaryRgb & 0xFF));
        var s = secondaryRgb == 0
            ? MonetPalette.RotateHue(p, 60)
            : ((byte)((secondaryRgb >> 16) & 0xFF), (byte)((secondaryRgb >> 8) & 0xFF), (byte)(secondaryRgb & 0xFF));
        var fp = $"{p.Item1:X2}{p.Item2:X2}{p.Item3:X2}{s.Item1:X2}{s.Item2:X2}{s.Item3:X2}";
        if (fp == s_coverFingerprint) return;         // 同调色板（换歌但封面同色系）→ 背景不动

        s_coverPrimary = p;
        s_coverSecondary = s;
        s_coverFingerprint = fp;
        // 开关关闭时仅暂存（滑块打开立即可用），不触发无意义的全量重刷
        if (_coverBackgroundEnabled) ApplyTheme();
    }

    /// <summary>切换主题色并持久化</summary>
    /// <param name="theme">目标主题色枚举</param>
    public void SetTheme(CoreAppTheme theme)
    {
        _currentTheme = theme;
        SaveSetting(KeyTheme, (int)theme);
        ApplyTheme();
    }

    /// <summary>设置暗黑模式（明/暗/跟随系统）并持久化</summary>
    /// <param name="setting">暗黑模式选项</param>
    public void SetDarkModeSetting(DarkModeSetting setting)
    {
        _darkModeSetting = setting;
        SaveSetting(KeyDarkMode, (int)setting);

        Application.Current!.UserAppTheme = setting switch
        {
            DarkModeSetting.Light => MauiAppTheme.Light,
            DarkModeSetting.Dark => MauiAppTheme.Dark,
            _ => MauiAppTheme.Unspecified,
        };

        ApplyTheme();
    }

    /// <summary>应用当前主题色与暗黑模式到应用资源字典，刷新所有绑定</summary>
    public void ApplyTheme()
    {
        try
        {
            var app = Application.Current;
            if (app?.Resources == null) return;

            var colors = ThemeMap[_currentTheme];
            var isDark = IsEffectivelyDark();
            CurrentIsDark = isDark;

            app.Resources["PrimaryColor"] = Color.FromArgb(colors.Primary);
            app.Resources["PrimaryLightColor"] = Color.FromArgb(colors.Light);
            app.Resources["PrimaryDarkColor"] = Color.FromArgb(colors.Dark);
            app.Resources["AccentColor"] = Color.FromArgb(GetAccentColor(_currentTheme));
            // 雾面动态背景开关（供播放页/歌词页 DynamicResource 绑定）
            app.Resources["FrostedBackgroundEnabled"] = _frostedBackgroundEnabled;

            // 播放器条/播放页控件颜色（深浅两套，供 DynamicResource 绑定——
            // 避免 AppThemeBinding 在 Windows XamlC 上的兼容性问题）。
            // 浅色用暖棕调（#4A3A44 系），替代冷蓝灰的工具感
            app.Resources["PlayerIconColor"] = isDark ? Color.FromArgb("#FFFFFF") : Color.FromArgb("#4A3A44");
            app.Resources["PlayerLikeColor"] = isDark ? Color.FromArgb("#FFFFFF") : Color.FromArgb("#FF7AAE");
            app.Resources["PlayerPlayBtnBg"] = isDark ? Color.FromArgb("#26FFFFFF") : Color.FromArgb("#22000000");
            app.Resources["PlayerTitleColor"] = isDark ? Color.FromArgb("#FFFFFF") : Color.FromArgb("#4A3A44");
            app.Resources["PlayerSubColor"] = isDark ? Color.FromArgb("#CCFFFFFF") : Color.FromArgb("#866B77");
            // 浅色模式下拇指与已播进度使用当前主题主色（避免固定深色"黑点"难看，跟随 5 套主题）
            app.Resources["PlayerSliderThumb"] = isDark ? Color.FromArgb("#FFFFFF") : Color.FromArgb(colors.Primary);
            app.Resources["PlayerSliderProgress"] = isDark ? Color.FromArgb("#FFFFFF") : Color.FromArgb(colors.Primary);
            app.Resources["PlayerSliderTrack"] = isDark ? Color.FromArgb("#40FFFFFF") : Color.FromArgb("#33000000");
            app.Resources["PlayerSliderTrackDim"] = isDark ? Color.FromArgb("#24FFFFFF") : Color.FromArgb("#14000000");

            if (isDark)
            {
                ApplyDarkPalette(app.Resources, colors);
            }
            else
            {
                ApplyLightPalette(app.Resources, colors);
            }

            // 设置主题内置背景图（5 个主题有静态星空/天空图，其余回退渐变）
            ApplyThemeBackgroundImage(app.Resources, _currentTheme, isDark);

            ApplyCustomBackground(app.Resources, isDark);

            UpdatePlatformStatusBar(isDark);

            // 资源键已全部更新完毕，通知关注方强制重刷原生背景
            // （修复 ThemeBackgroundImage 上 DynamicResource 不实时刷新、返回主页背景丢失的问题）。
            NotifyApplied();
        }
        catch (Exception ex)
        {
            Log.Debug("ThemeService", $"[ThemeService] ApplyTheme failed: {ex.Message}");
        }
    }

    private static void UpdatePlatformStatusBar(bool isDark)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
#if ANDROID
            if (Platform.CurrentActivity is global::CatClawMusic.Maui.MainActivity activity)
            {
                activity.UpdateDecorViewBackground();
            }
#endif
#if WINDOWS
            // 用 ApplyTheme 已算好的最终生效值，而不是 RequestedTheme——
            // 后者在"跟随系统"以外的场景可能与应用设置不一致，会让窗口 chrome 跑偏。
            try { CatClawMusic.Maui.App.UpdateWindowsTheme(isDark); }
            catch { }
#endif
        });
    }

    /// <summary>
    /// 根据当前主题色与深/浅模式，设置 ThemeBackgroundImage 资源。
    /// v1.7.8 起不再使用静态星空/天空图片，改为代码绘制的渐变位图
    /// （深色模式：主题色氛围渐变 + 星空点；浅色模式：主题浅色渐变 + 柔和光晕）。
    /// 性能(每日首启卡顿主因修复)：渲染结果做「内存 + 磁盘」两级缓存 ——
    /// 旧版每次冷启动都在主线程同步渲染 1080×1920 PNG 两遍(Android PNG 编码 / Windows Win2D 同步等待),
    /// 现在同一主题×模式一生只渲染一次,之后内存/磁盘直接命中,零主线程开销。
    /// </summary>
    private void ApplyThemeBackgroundImage(ResourceDictionary resources, CoreAppTheme theme, bool isDark)
    {
        // 缓存键第三维 bgKey：封面取色时带调色板指纹（换歌 → 指纹变化 → 缓存自动失效重画），
        // 莫奈取色时带壁纸指纹；标准渐变固定 "std"。修掉"改配色后旧 PNG 命中"的坑：
        // 指纹不进 key 就必须人肉递增版本号。
        var bgKey = CurrentBgKey();
        var key = (theme, isDark, bgKey);

        // 1) 内存命中：立即上屏(主题切换来回零成本)
        if (BackgroundPngMemory.TryGetValue(key, out var memPng))
        {
            SetThemePng(resources, theme, isDark, bgKey, memPng);
            return;
        }

        // 2) 磁盘命中(每天第一次启动走这里,仅一次几十 KB 文件读):后台读取后上屏。
        //    期间保留旧图 —— 同主题同模式的旧图与新图像素一致,无闪烁。
        var diskPath = ThemeBackgroundDiskPath(theme, isDark, bgKey);
        if (File.Exists(diskPath))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var png = await File.ReadAllBytesAsync(diskPath);
                    BackgroundPngMemory[key] = png;
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        var app = Application.Current;
                        if (app?.Resources == null) return;
                        // 上屏前校验主题/模式/bgKey 未再变化,且用户未设置自定义背景
                        if (_currentTheme != theme || IsEffectivelyDark() != isDark || CurrentBgKey() != bgKey) return;
                        if (CurrentCustomBackgroundPath != null) return;
                        SetThemePng(app.Resources, theme, isDark, bgKey, png);
                        NotifyApplied();
                    });
                }
                catch (Exception ex) { Log.Debug("ThemeService", $"[ThemeBg] 磁盘缓存读取失败: {ex.Message}"); }
            });
            return;
        }

        // 3) 首次生成(每主题×模式×bgKey一生一次,之后永远走缓存):
        //    已有旧图在屏 → 后台渲染+写盘后换图;完全无图 → 同步渲染兜底(仅影响首次安装)。
        bool hasStale = resources["ThemeBackgroundImage"] != null;
        if (hasStale)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var png = RenderThemeBackgroundPng(theme, isDark, bgKey);
                    TryWriteBackgroundDiskCache(diskPath, png);
                    PruneStaleDynamicBackgrounds(bgKey);
                    BackgroundPngMemory[key] = png;
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        var app = Application.Current;
                        if (app?.Resources == null) return;
                        if (_currentTheme != theme || IsEffectivelyDark() != isDark || CurrentBgKey() != bgKey) return;
                        if (CurrentCustomBackgroundPath != null) return;
                        SetThemePng(app.Resources, theme, isDark, bgKey, png);
                        NotifyApplied();
                    });
                }
                catch (Exception ex) { Log.Debug("ThemeService", $"[ThemeBg] 后台渲染失败: {ex.Message}"); }
            });
            return;
        }

        var syncPng = RenderThemeBackgroundPng(theme, isDark, bgKey);
        TryWriteBackgroundDiskCache(diskPath, syncPng);
        BackgroundPngMemory[key] = syncPng;
        SetThemePng(resources, theme, isDark, bgKey, syncPng);
    }

    /// <summary>标准渐变背景的缓存键（非莫奈）</summary>
    private const string BgKeyStandard = "std";

    /// <summary>当前背景缓存键（单选互斥）：动态封面开启（需雾面已开）且有封面色 → cover_{指纹}；
    /// 莫奈开启且取到壁纸色 → monet_{指纹}；否则 std。
    /// 两者由设置层保证至多一个开启；封面色取不到（无封面歌曲/未播放）自动回退标准渐变。</summary>
    private string CurrentBgKey()
    {
        if (_frostedBackgroundEnabled && _coverBackgroundEnabled && s_coverPrimary != null && s_coverFingerprint != null)
            return $"cover_{s_coverFingerprint}";
        if (_monetBackgroundEnabled && MonetPalette.Current != null)
            return $"monet_{MonetPalette.Fingerprint}";
        return BgKeyStandard;
    }

    /// <summary>清理旧指纹的动态背景磁盘缓存（莫奈随壁纸、封面随歌曲切换，防止 PNG 无限堆积；
    /// 只删 monet_/cover_ 前缀且非当前键的文件，标准 std 文件不动）</summary>
    private static void PruneStaleDynamicBackgrounds(string currentBgKey)
    {
        try
        {
            var dir = Path.Combine(FileSystem.AppDataDirectory, "theme_bg");
            if (!Directory.Exists(dir)) return;
            foreach (var pattern in new[] { "bg_v2_*_monet_*.png", "bg_v2_*_cover_*.png" })
            {
                foreach (var file in Directory.GetFiles(dir, pattern))
                {
                    if (file.Contains($"_{currentBgKey}_")) continue;
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }

    /// <summary>PNG 字节缓存(主题×深浅模式×bgKey,渲染结果确定,最多若干份 ≈ 几 MB)</summary>
    private static readonly ConcurrentDictionary<(CoreAppTheme Theme, bool IsDark, string BgKey), byte[]> BackgroundPngMemory = new();

    private static string ThemeBackgroundDiskPath(CoreAppTheme theme, bool isDark, string bgKey)
    {
        // 放 AppDataDirectory 而非 CacheDirectory：系统「清除缓存」会删掉主题背景 PNG，
        // 下次 ApplyTheme 就得在主线程同步重渲染 1080×1920 位图（清缓存后启动卡顿因素之一）
        var dir = Path.Combine(FileSystem.AppDataDirectory, "theme_bg");
        Directory.CreateDirectory(dir);
        // 文件名含 bgKey：莫奈取色时为 monet_{壁纸指纹}，壁纸变化自动换文件；
        // 标准渐变为 std（历史文件名为 bg_v2_{theme}_{mode}.png，std 命名与其同构，互不影响）。
        return Path.Combine(dir, $"bg_v2_{theme}_{bgKey}_{(isDark ? "dark" : "light")}.png");
    }

    private static void TryWriteBackgroundDiskCache(string path, byte[] png)
    {
        try { File.WriteAllBytes(path, png); }
        catch (Exception ex) { Log.Debug("ThemeService", $"[ThemeBg] 写磁盘缓存失败: {ex.Message}"); }
    }

    private void SetThemePng(ResourceDictionary resources, CoreAppTheme theme, bool isDark, string bgKey, byte[] png)
    {
        resources["ThemeBackgroundImage"] = GetOrCreateBackgroundImage(theme, isDark, bgKey, png);
        resources["ThemeBackgroundEnabled"] = true;
        CurrentThemeBackgroundPng = png;
        CurrentCustomBackgroundPath = null;
        CurrentBackgroundEnabled = true;
    }

    /// <summary>代码绘制背景缓存（主题色 + 深/浅模式 + bgKey 三键），带容量上限的 LRU：
    /// 每套 1080×1920 ARGB ≈ 8MB 解码内存，全缓存常驻几十 MB（低端机伤）。
    /// 最多保留 4 套（当前主题的深浅两套 + 最近切换过的），最久未用的先淘汰。</summary>
    private static readonly Dictionary<(CoreAppTheme Theme, bool IsDark, string BgKey), ImageSource> BackgroundImageCache = new();
    private static readonly LinkedList<(CoreAppTheme Theme, bool IsDark, string BgKey)> BackgroundImageLru = new();
    private const int BackgroundImageCacheLimit = 4;

    /// <summary>获取（或生成并缓存）代码绘制的主题背景 ImageSource</summary>
    private static ImageSource GetOrCreateBackgroundImage(CoreAppTheme theme, bool isDark, string bgKey, byte[] png)
    {
        var key = (theme, isDark, bgKey);
        if (BackgroundImageCache.TryGetValue(key, out var cached))
        {
            // 命中：移到 LRU 尾部（最近使用）
            BackgroundImageLru.Remove(key);
            BackgroundImageLru.AddLast(key);
            return cached;
        }

        var source = ImageSource.FromStream(() => new MemoryStream(png));
        BackgroundImageCache[key] = source;
        BackgroundImageLru.AddLast(key);
        while (BackgroundImageCache.Count > BackgroundImageCacheLimit)
        {
            var evict = BackgroundImageLru.First!.Value;
            BackgroundImageLru.RemoveFirst();
            BackgroundImageCache.Remove(evict);
        }
        return source;
    }

    /// <summary>
    /// 用代码绘制主题背景 PNG（10 套渐变设计，见 GetBackgroundDesign）：
    /// 每套 = 1 层多停靠线性渐变（主基调）+ 1~2 层径向光晕（透明度叠加），
    /// 与 docs/theme-backgrounds-10.html 原型配色一致。
    /// Windows 用 Win2D（项目已引用 Microsoft.Graphics.Win2D），Android 用系统 Canvas。
    /// </summary>
    private static byte[] RenderThemeBackgroundPng(CoreAppTheme theme, bool isDark, string bgKey)
    {
        // 1080x1920（2K 竖屏）：手机端约 1:1 显示；PC 横屏拉伸后仍保持清晰。
        // 光晕半径按 max(w,h) 计算，PC 宽屏下光晕也能铺开，避免竖屏参数拉伸后不明显。
        const int width = 1080, height = 1920;
        var design = GetBackgroundDesign(theme, isDark, bgKey);

#if ANDROID
        using var bitmap = Android.Graphics.Bitmap.CreateBitmap(width, height, Android.Graphics.Bitmap.Config.Argb8888);
        using var canvas = new Android.Graphics.Canvas(bitmap);
        using var paint = new Android.Graphics.Paint { AntiAlias = true };

        // 1) 主基调：多停靠线性渐变（From→To 归一化坐标映射到位图尺寸）
        var stopColors = new int[design.Stops.Length];
        var stopOffsets = new float[design.Stops.Length];
        for (int i = 0; i < design.Stops.Length; i++)
        {
            stopColors[i] = Android.Graphics.Color.ParseColor(design.Stops[i].Argb);
            stopOffsets[i] = design.Stops[i].Offset;
        }
        using var shader = new Android.Graphics.LinearGradient(
            design.From.X * width, design.From.Y * height,
            design.To.X * width, design.To.Y * height,
            stopColors, stopOffsets, Android.Graphics.Shader.TileMode.Clamp);
        paint.SetShader(shader);
        canvas.DrawRect(0, 0, width, height, paint);

        // 2) 径向光晕叠加：中心色 → 全透明边缘（Clamp 保证圆外为边缘透明色）
        // 半径按 max(w,h) 归一化，宽屏（PC）下光晕铺得更开、层次更明显
        float unit = Math.Max(width, height);
        foreach (var glow in design.Glows)
        {
            using var glowShader = new Android.Graphics.RadialGradient(
                glow.Cx * width, glow.Cy * height, glow.Radius * unit,
                Android.Graphics.Color.ParseColor(glow.CenterArgb),
                Android.Graphics.Color.ParseColor(glow.EdgeArgb),
                Android.Graphics.Shader.TileMode.Clamp);
            paint.SetShader(glowShader);
            canvas.DrawRect(0, 0, width, height, paint);
        }

        using var stream = new MemoryStream();
        bitmap.Compress(Android.Graphics.Bitmap.CompressFormat.Png, 100, stream);
        return stream.ToArray();
#elif WINDOWS
        try
        {
            return RenderThemeBackgroundPngWin2D(design, width, height);
        }
        catch (Exception winEx)
        {
            // 记录确切异常消息（输出窗口搜索 "[ThemeBg] Win2D" 即可定位），
            // 失败时回退 1x1 透明 PNG，不影响其余主题逻辑（背景图缺失，页面用纯渐变兜底）
            Log.Debug("ThemeService", $"[ThemeBg] Win2D render failed: {winEx}");
            return Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
        }
#else
        // 其他平台兜底：返回 1x1 透明 PNG
        return Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
#endif
    }

#if WINDOWS
    /// <summary>Windows 端 Win2D 渲染主题背景 PNG（10 套渐变设计）</summary>
    private static byte[] RenderThemeBackgroundPngWin2D(BgDesign design, int width, int height)
    {
        var device = Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();
        // CanvasDevice 只实现 ICanvasResourceCreator（无 Dpi 变体），用带 DPI 参数的构造函数重载
        using var renderTarget = new Microsoft.Graphics.Canvas.CanvasRenderTarget(
            (Microsoft.Graphics.Canvas.ICanvasResourceCreator)device, width, height, 96f);
        using (var ds = renderTarget.CreateDrawingSession())
        {
            // 1) 主基调：多停靠线性渐变（Win2D 1.3 通过 CanvasGradientStop[] 构造传入）
            var gradientStops = design.Stops
                .Select(s => new Microsoft.Graphics.Canvas.Brushes.CanvasGradientStop
                {
                    Color = WindowsColorFromHex(s.Argb),
                    Position = s.Offset,
                })
                .ToArray();
            using var gradient = new Microsoft.Graphics.Canvas.Brushes.CanvasLinearGradientBrush(renderTarget, gradientStops)
            {
                StartPoint = new System.Numerics.Vector2(design.From.X * width, design.From.Y * height),
                EndPoint = new System.Numerics.Vector2(design.To.X * width, design.To.Y * height),
            };
            ds.FillRectangle(0, 0, width, height, gradient);

            // 2) 径向光晕叠加：中心色 → 全透明边缘
            // 半径按 max(w,h) 归一化，宽屏（PC）下光晕铺得更开、层次更明显
            float unit = Math.Max(width, height);
            foreach (var glow in design.Glows)
            {
                using var glowBrush = new Microsoft.Graphics.Canvas.Brushes.CanvasRadialGradientBrush(
                    renderTarget, WindowsColorFromHex(glow.CenterArgb), WindowsColorFromHex(glow.EdgeArgb))
                {
                    Center = new System.Numerics.Vector2(glow.Cx * width, glow.Cy * height),
                    RadiusX = glow.Radius * unit,
                    RadiusY = glow.Radius * unit,
                };
                ds.FillRectangle(0, 0, width, height, glowBrush);
            }
        }

        // 导出 PNG 字节（经 InMemoryRandomAccessStream 中转，SaveAsync 需要 IRandomAccessStream）
        var ras = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        renderTarget.SaveAsync(ras, Microsoft.Graphics.Canvas.CanvasBitmapFileFormat.Png).AsTask().GetAwaiter().GetResult();
        ras.Seek(0);
        using var reader = new Windows.Storage.Streams.DataReader(ras.GetInputStreamAt(0));
        reader.LoadAsync((uint)ras.Size).AsTask().GetAwaiter().GetResult();
        var bytes = new byte[ras.Size];
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static Windows.UI.Color WindowsColorFromArgb(byte a, byte r, byte g, byte b)
        => Windows.UI.Color.FromArgb(a, r, g, b);

    private static Windows.UI.Color WindowsColorFromHex(string hex)
    {
        hex = hex.TrimStart('#');
        byte a = hex.Length >= 8 ? Convert.ToByte(hex[..2], 16) : (byte)0xFF;
        int offset = hex.Length >= 8 ? 2 : 0;
        byte r = Convert.ToByte(hex.Substring(offset, 2), 16);
        byte g = Convert.ToByte(hex.Substring(offset + 2, 2), 16);
        byte b = Convert.ToByte(hex.Substring(offset + 4, 2), 16);
        return Windows.UI.Color.FromArgb(a, r, g, b);
    }
#endif

    /// <summary>获取系统当前是否处于暗黑模式</summary>
    /// <returns>系统暗黑模式返回 true；否则返回 false</returns>
    public bool IsSystemDarkMode()
    {
        return Application.Current?.RequestedTheme == MauiAppTheme.Dark;
    }

    /// <summary>获取应用最终生效的暗黑状态（综合用户设置与系统状态）</summary>
    /// <returns>暗黑模式生效返回 true；否则返回 false</returns>
    public bool IsEffectivelyDark()
    {
        return _darkModeSetting switch
        {
            DarkModeSetting.Dark => true,
            DarkModeSetting.Light => false,
            _ => IsSystemDarkMode(),
        };
    }

    #region 持久化

    private void LoadSettings()
    {
        try
        {
            _currentTheme = (CoreAppTheme)Preferences.Default.Get(KeyTheme, (int)CoreAppTheme.Pink);
            _darkModeSetting = (DarkModeSetting)Preferences.Default.Get(KeyDarkMode, 2);
            _customBackgroundPath = Preferences.Default.Get<string?>(KeyCustomBgPath, null);
            _customBackgroundOpacity = Preferences.Default.Get(KeyCustomBgOpacity, 0.5);
            _frostedBackgroundEnabled = Preferences.Default.Get(KeyFrostedBg, true);
            _monetBackgroundEnabled = Preferences.Default.Get(KeyMonetBg, false);
            _coverBackgroundEnabled = Preferences.Default.Get(KeyCoverBg, false);
            // 收敛历史遗留：莫奈与动态封面现为单选互斥，同开时保留动态封面（封面取色更具体）；
            // 动态封面依赖雾面，雾面关闭时一并关闭。
            if (_monetBackgroundEnabled && _coverBackgroundEnabled)
            {
                _monetBackgroundEnabled = false;
                Preferences.Default.Set(KeyMonetBg, false);
            }
            if (!_frostedBackgroundEnabled && _coverBackgroundEnabled)
            {
                _coverBackgroundEnabled = false;
                Preferences.Default.Set(KeyCoverBg, false);
            }
            if (_customBackgroundPath != null && !File.Exists(_customBackgroundPath))
                _customBackgroundPath = null;

            if (!ThemeMap.ContainsKey(_currentTheme))
                _currentTheme = CoreAppTheme.Pink;
        }
        catch
        {
            _currentTheme = CoreAppTheme.Pink;
            _darkModeSetting = DarkModeSetting.FollowSystem;
            _customBackgroundPath = null;
            _customBackgroundOpacity = 0.5;
            _monetBackgroundEnabled = false;
            _coverBackgroundEnabled = false;
        }
    }

    private void SaveSetting(string key, int value)
    {
        try { Preferences.Default.Set(key, value); } catch { }
    }

    #endregion

    private static void ApplyDarkPalette(ResourceDictionary resources, ThemeColors colors)
    {
        // 深色模式整体转冷色系:墨蓝夜底 + 冰青主色 #55D6FF。
        // 樱粉压在深底上明度对比不足会发灰发脏,故深色主色不用粉;
        // 粉仅保留在红心(LikeColor)与微弱的品牌光晕里。
        var primary = Color.FromArgb("#55D6FF");
        resources["PrimaryColor"] = primary;
        var darkBase = Color.FromArgb("#12151F");
        var midTone = Color.FromArgb("#0E1119");
        var accentTint = Color.FromArgb(GetAccentColor(_currentThemeStatic(colors.Primary))).WithAlpha(0.1f);

        resources["WindowBackgroundColor"] = Colors.Transparent; // 完全透明：仅透出统一全局背景（Window 层），无遮罩（Android）
#if WINDOWS
        resources["WindowBackgroundColor"] = Color.FromArgb("#12151F"); // Windows 无独立背景层，用主题深色底替换原生灰
#endif
        resources["WindowBackgroundAltColor"] = Color.FromArgb("#1A1F2C");
        resources["SurfaceColor"] = Color.FromArgb("#232938");
        // 卡片/玻璃：深/浅模式下都尽量"不着色"，避免叠加出横贯半透明白边（尤其底部贴边卡片）
        resources["CardBackgroundColor"] = Color.FromArgb("#08FFFFFF");         // ~3%，极简底色
        resources["CardBackgroundStrongColor"] = Color.FromArgb("#0FFFFFFF");   // ~6%
        resources["GlassButtonColor"] = Color.FromArgb("#12FFFFFF");
        resources["InputBackgroundColor"] = Color.FromArgb("#0DFFFFFF");
        resources["InputBorderColor"] = Color.FromArgb("#18FFFFFF");
        resources["DividerColor"] = Color.FromArgb("#14FFFFFF");
        resources["GlassStrokeColor"] = Color.FromArgb("#14FFFFFF");            // 白描边从 16% 降到 8%
        resources["GlassStrokeStrongColor"] = Color.FromArgb("#24FFFFFF");
        // 桌面浮层卡（侧栏/播放条）毛玻璃配色：白色磨砂基底 + 主题色微光
        resources["GlassCardTintColor"] = Blend(Colors.White.WithAlpha(0.30f), primary.WithAlpha(0.07f));
        resources["GlassCardStrokeColor"] = primary.WithAlpha(0.18f);
        resources["GlassCardHighlightColor"] = Colors.White.WithAlpha(0.22f);
        resources["GlassCardButtonBgColor"] = Colors.White.WithAlpha(0.10f);
        resources["ChipInactiveColor"] = Color.FromArgb("#15FFFFFF");
        resources["ChipActiveColor"] = primary;
        resources["ChipInactiveTextColor"] = Color.FromArgb("#C2C8E0");
        resources["ChipActiveTextColor"] = Colors.White;
        resources["BadgeBackgroundColor"] = Color.FromArgb("#14FFFFFF");
        resources["BadgeStrokeColor"] = Color.FromArgb("#28FFFFFF");
        resources["CardOverlayColor"] = Color.FromArgb("#0AFFFFFF");
        resources["ButtonOverlayColor"] = Color.FromArgb("#12FFFFFF");
        resources["ProgressTrackColor"] = Color.FromArgb("#20FFFFFF");
        resources["RowPressOverlayColor"] = Color.FromArgb("#26FFFFFF");
        resources["TextPrimaryColor"] = Color.FromArgb("#F5F7FF");
        resources["TextSecondaryColor"] = Color.FromArgb("#C2C8E0");
        resources["TextHintColor"] = Color.FromArgb("#8D93B7");
        resources["TabActiveColor"] = primary;
        resources["TabInactiveColor"] = Color.FromArgb("#FFFFFF"); // 深色模式：未选中图标/文字为白色
        // 底部导航栏毛玻璃底：半透明白色叠加（透出内容，磨砂质感）
        resources["TabBarBackgroundColor"] = Color.FromArgb("#30FFFFFF");
        // 底部播放器条背景：深色模式与页面同底（#1A1838），衔接无缝
        resources["PlayerBarBackgroundColor"] = darkBase;
        // 导航栏毛玻璃色调：深色模式=白色反差，浅色模式=主题色
        resources["TabBarGlassTint"] = Colors.White; // 深色模式：浅色毛玻璃（白底反差）
        resources["BottomBarTintOpacity"] = 0.18; // 深色模式：白色着色 18%（通透磨砂）
        resources["BottomBarDimAmount"] = 0.0;    // 深色模式：不暗化
        resources["BottomBarStrokeColor"] = primary.WithAlpha(0.50f); // 深色模式：主题色 50% 描边

        // 深色模式基底：完全透明（无遮罩），背景图通过 Window 层全局透出
        resources["PageBackgroundBrush"] = new LinearGradientBrush(new GradientStopCollection
        {
            new(Colors.Transparent, 0f),
            new(Colors.Transparent, 0.35f),
            new(Colors.Transparent, 0.65f),
            new(Colors.Transparent, 0.88f),
            new(Colors.Transparent, 1f)
        }, new Point(0.5, 0), new Point(0.5, 1));

        // 主题背景图遮罩：深色模式下用低透明黑色微微压暗图片（保持背景层次，不再黑漆漆）
        resources["CustomBackgroundMaskColor"] = Colors.Transparent; // 纯色渐变不需要遮罩

        // 英雄卡/顶部卡片：半透明毛玻璃（冰青 × 薄荷，冷色体系）
        resources["HeroBrush"] = BuildLinearBrush($"{AlphaHex(0x59)}55D6FF", $"{AlphaHex(0x24)}7ED8C3", 0.0f, 1.0f);
        // 主操作按钮：半透明毛玻璃底（保留主题色调，白字仍可读）
        resources["PrimaryButtonBackgroundColor"] = primary.WithAlpha(0.55f);
        resources["PrimaryGlowBrush"] = BuildRadialBrush($"{AlphaHex(0x5A)}55D6FF", $"{AlphaHex(0x00)}55D6FF");
        var accent = GetAccentColor(_currentThemeStatic(colors.Primary));
        resources["AccentGlowBrush"] = BuildRadialBrush($"{AlphaHex(0x45)}{accent[1..]}", $"{AlphaHex(0x00)}{accent[1..]}");
        resources["GlassHighlightBrush"] = BuildLinearBrush("#28FFFFFF", "#04FFFFFF");
    }

    private static void ApplyLightPalette(ResourceDictionary resources, ThemeColors colors)
    {
        var primary = Color.FromArgb(colors.Primary);
        var primaryLight = Color.FromArgb(colors.Light);
        var lightBase = Color.FromArgb("#FFF7FA");
        var primaryWash = primaryLight.WithAlpha(0.6f);
        var accent = Color.FromArgb(GetAccentColor(_currentThemeStatic(colors.Primary))).WithAlpha(0.22f);

        resources["WindowBackgroundColor"] = Colors.Transparent; // 完全透明：仅透出统一全局背景（Window 层），无遮罩（Android）
#if WINDOWS
        resources["WindowBackgroundColor"] = Color.FromArgb("#FFF7FA"); // Windows 无独立背景层，用主题浅色底替换原生白
#endif
        resources["WindowBackgroundAltColor"] = Color.FromArgb("#FFEAF1");
        resources["SurfaceColor"] = Color.FromArgb("#FFFFFFFF");
        // 浅色模式卡片改为半透明毛玻璃（透出背景图，与深色模式一致）
        resources["CardBackgroundColor"] = Color.FromArgb("#8CFFFFFF");
        resources["CardBackgroundStrongColor"] = Color.FromArgb("#B3FFFFFF");
        resources["GlassButtonColor"] = Color.FromArgb("#99FFFFFF");
        resources["InputBackgroundColor"] = Color.FromArgb("#FFF3F7");
        resources["InputBorderColor"] = Color.FromArgb("#33D9AFC4");
        resources["DividerColor"] = Color.FromArgb("#1ED9AFC4");
        // 暖粉棕描边（替代冷黑描边，去掉"工具感"）
        resources["GlassStrokeColor"] = Color.FromArgb("#26D9AFC4");
        resources["GlassStrokeStrongColor"] = Color.FromArgb("#4DD9AFC4");
        // 桌面浮层卡（侧栏/播放条）毛玻璃配色：白色磨砂 + 淡主题色调
        resources["GlassCardTintColor"] = Blend(Colors.White.WithAlpha(0.85f), primary.WithAlpha(0.08f));
        resources["GlassCardStrokeColor"] = primary.WithAlpha(0.16f);
        resources["GlassCardHighlightColor"] = Colors.White.WithAlpha(0.85f);
        resources["GlassCardButtonBgColor"] = Colors.Black.WithAlpha(0.05f);
        resources["ChipInactiveColor"] = Color.FromArgb("#FFE9F1");
        resources["ChipActiveColor"] = Color.FromArgb(colors.Primary);
        resources["ChipInactiveTextColor"] = Color.FromArgb("#8A6B7A");
        resources["ChipActiveTextColor"] = Colors.White;
        resources["BadgeBackgroundColor"] = accent;
        resources["BadgeStrokeColor"] = accent;
        resources["CardOverlayColor"] = Color.FromArgb("#08000000");
        resources["ButtonOverlayColor"] = Color.FromArgb("#12000000");
        resources["ProgressTrackColor"] = Color.FromArgb("#18000000");
        resources["RowPressOverlayColor"] = Color.FromArgb("#22000000");
        resources["TextPrimaryColor"] = Color.FromArgb("#4A3A44");
        resources["TextSecondaryColor"] = Color.FromArgb("#866B77");
        resources["TextHintColor"] = Color.FromArgb("#B39AA6");
        resources["TabActiveColor"] = Color.FromArgb(colors.Primary);
        resources["TabInactiveColor"] = Color.FromArgb("#9AA0B4"); // 浅色模式：未选中图标/文字为灰色
        // 底部导航栏毛玻璃底：半透明白色叠加（透出内容，磨砂质感）
        resources["TabBarBackgroundColor"] = Color.FromArgb("#A6FFFFFF");
        // 底部播放器条背景：浅色模式纯白
        resources["PlayerBarBackgroundColor"] = Color.FromArgb("#FFFFFF");
        // 导航栏毛玻璃色调：浅色模式提亮（与页面遮罩同为白色），深色模式压暗（黑色）
        resources["TabBarGlassTint"] = primary; // 浅色模式：主题色毛玻璃
        resources["BottomBarTintOpacity"] = 0.12; // 浅色模式：主题色着色 12%（通透磨砂）
        resources["BottomBarDimAmount"] = 0.0;    // 浅色模式：不暗化
        resources["BottomBarStrokeColor"] = primary.WithAlpha(0.35f); // 浅色模式：主题色 35% 描边

        // 浅色模式基底：完全透明（无遮罩），背景图通过 Window 层全局透出
        resources["PageBackgroundBrush"] = new LinearGradientBrush(new GradientStopCollection
        {
            new(Colors.Transparent, 0f),
            new(Colors.Transparent, 0.5f),
            new(Colors.Transparent, 1f)
        }, new Point(0.5, 0), new Point(0.5, 1));

        // 主题背景图遮罩：浅色模式下用半透明白色提亮图片，确保文字可读
        resources["CustomBackgroundMaskColor"] = Colors.Transparent; // 纯色渐变不需要遮罩

        // 英雄卡/顶部卡片：半透明毛玻璃（主题色低透明渐变，透出背景图）
        resources["HeroBrush"] = BuildLinearBrush($"{AlphaHex(0x66)}{colors.Primary[1..]}", $"{AlphaHex(0x33)}{colors.Light[1..]}", 0.0f, 1.0f);
        // 主操作按钮：半透明毛玻璃底（保留主题色调，白字仍可读）
        resources["PrimaryButtonBackgroundColor"] = primary.WithAlpha(0.55f);
        resources["PrimaryGlowBrush"] = BuildRadialBrush($"{AlphaHex(0x4A)}{colors.Primary[1..]}", $"{AlphaHex(0x00)}{colors.Primary[1..]}");
        var accentCol = GetAccentColor(_currentThemeStatic(colors.Primary));
        resources["AccentGlowBrush"] = BuildRadialBrush($"{AlphaHex(0x35)}{accentCol[1..]}", $"{AlphaHex(0x00)}{accentCol[1..]}");
        resources["GlassHighlightBrush"] = BuildLinearBrush("#55FFFFFF", "#10FFFFFF");
    }

    private static Color Blend(Color baseColor, Color overlay)
    {
        var a = overlay.Alpha;
        // 保留 base 的 alpha：毛玻璃 TintColor 必须保持半透明才能透出下层背景。
        // 若强制 alpha=1 会变成不透明实心色块，把毛玻璃通感完全盖死。
        return new Color(
            baseColor.Red * (1 - a) + overlay.Red * a,
            baseColor.Green * (1 - a) + overlay.Green * a,
            baseColor.Blue * (1 - a) + overlay.Blue * a,
            baseColor.Alpha);
    }

    private void ApplyCustomBackground(ResourceDictionary resources, bool isDark)
    {
        bool hasBg = HasCustomBackground;
        resources["CustomBackgroundEnabled"] = hasBg;
        resources["CustomBackgroundOpacity"] = _customBackgroundOpacity;

            if (hasBg)
            {
                try
                {
                    // 关键修复：用户自定义背景可能是一张超大图（如 7168×7168 ≈ 196MB）。
                    // 原 ImageSource.FromStream 走 MAUI 默认 StreamImageSource 处理器，不进行下采样，
                    // 整图全分辨率解码后交给 ImageView 绘制，触发
                    // “Canvas: trying to draw too large bitmap” 崩溃（三星崩溃日志）。
                    // 改用 FromFile 走自定义 CachingFileImageSourceService：按 ImageView 实际尺寸降采样（≤1024px），
                    // 且 BitmapFactory 用 InSampleSize 局部解码，绝不分配整图内存，彻底规避崩溃与 OOM。
                    // 同时不再 File.ReadAllBytes 把整张图读进 byte[]（196MB 瞬时分配）。
                    // _customBackgroundPath 已在 LoadSettings 中校验存在且可读。
                    var customImg = ImageSource.FromFile(_customBackgroundPath!);
                    resources["CustomBackgroundImage"] = customImg;
                    // 用户自定义背景优先级最高：覆盖主题内置背景图
                    resources["ThemeBackgroundImage"] = customImg;
                    resources["ThemeBackgroundEnabled"] = true;
                    // 同步全局背景状态：自定义图优先于主题内置图
                    CurrentCustomBackgroundPath = _customBackgroundPath;
                    CurrentBackgroundEnabled = true;
                }
            catch
            {
                resources["CustomBackgroundEnabled"] = false;
                resources["CustomBackgroundImage"] = null;
                // 加载失败时恢复主题背景图
                ApplyThemeBackgroundImage(resources, _currentTheme, isDark);
                RestorePageBackground(resources, isDark);
                return;
            }

            double maskAlpha = isDark ? 0.25 : 0.35;
            resources["CustomBackgroundMaskColor"] = isDark
                ? Colors.Black.WithAlpha((float)maskAlpha)
                : Colors.White.WithAlpha((float)maskAlpha);

            double overlayAlpha = 0.0; // 完全透明：叠加蒙版去除，仅透出 Window 层自定义背景图
            resources["PageBackgroundBrush"] = new SolidColorBrush(
                (isDark ? Color.FromArgb("#080914") : Color.FromArgb("#FFF7FA")).WithAlpha((float)overlayAlpha));
            resources["WindowBackgroundColor"] = (isDark ? Color.FromArgb("#080914") : Color.FromArgb("#FFF7FA")).WithAlpha((float)overlayAlpha);
        }
        else
        {
            resources["CustomBackgroundImage"] = null;
            // 无自定义背景时确保主题图片恢复（ApplyTheme 中已设置，此处为防御性恢复）
            if (!resources.ContainsKey("ThemeBackgroundEnabled") || !(bool)resources["ThemeBackgroundEnabled"])
            {
                ApplyThemeBackgroundImage(resources, _currentTheme, isDark);
            }
        }
    }

    private void RestorePageBackground(ResourceDictionary resources, bool isDark)
    {
        var colors = ThemeMap[_currentTheme];
        if (isDark)
            ApplyDarkPalette(resources, colors);
        else
            ApplyLightPalette(resources, colors);
    }

    private static LinearGradientBrush BuildLinearBrush(string startHex, string endHex, float startOffset = 0f, float endOffset = 1f)
        => new(new GradientStopCollection
        {
            new(Color.FromArgb(startHex), startOffset),
            new(Color.FromArgb(endHex), endOffset)
        }, new Point(0, 0), new Point(1, 1));

    private static LinearGradientBrush BuildLinearBrush(string startHex, string middleHex, string endHex)
        => new(new GradientStopCollection
        {
            new(Color.FromArgb(startHex), 0f),
            new(Color.FromArgb(middleHex), 0.55f),
            new(Color.FromArgb(endHex), 1f)
        }, new Point(0, 0), new Point(1, 1));

    private static RadialGradientBrush BuildRadialBrush(string centerHex, string edgeHex)
        => new(new GradientStopCollection
        {
            new(Color.FromArgb(centerHex), 0f),
            new(Color.FromArgb(edgeHex), 1f)
        })
        {
            Center = new Point(0.5, 0.5),
            Radius = 0.9f
        };

    /// <summary>主题辅助强调色（唯一主题：樱粉 × 薄荷）</summary>
    private static string GetAccentColor(CoreAppTheme theme) => "#7ED8C3";

    private static string GetAccentColorHex(string primaryHex) => "#7ED8C3";

    private static CoreAppTheme _currentThemeStatic(string primaryHex) => CoreAppTheme.Pink;

    private static string AlphaHex(byte alpha) => alpha.ToString("X2");

    /// <summary>主题颜色组</summary>
    private record ThemeColors(string Primary, string Light, string Dark);
}





