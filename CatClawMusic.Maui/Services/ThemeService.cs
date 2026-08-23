using CatClawMusic.Core.Interfaces;
using CoreAppTheme = CatClawMusic.Core.Interfaces.AppTheme;
using MauiAppTheme = Microsoft.Maui.ApplicationModel.AppTheme;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
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

    private CoreAppTheme _currentTheme;
    private DarkModeSetting _darkModeSetting;
    private string? _customBackgroundPath;
    private double _customBackgroundOpacity = 0.5;
    private bool _frostedBackgroundEnabled = true;

    // ═══ 统一全局背景（GlobalBackgroundService）状态 ═══
    // 供 GlobalBackgroundService 在每次主题/背景切换后，把当前生效的背景一次性绘制到 Window 层。
    // 优先级：CustomBackgroundPath（用户自定义图） > ThemeBackgroundPng（主题内置渐变图）> 无（纯色兜底）。
    // Includes the-current theme code drawn PNG bytes and the custom file path, plus an enabled flag.
    public static bool CurrentBackgroundEnabled { get; private set; }
    public static byte[]? CurrentThemeBackgroundPng { get; private set; }
    public static string? CurrentCustomBackgroundPath { get; private set; }
    /// <summary>当前是否深色模式（供 GlobalBackgroundService 判定系统栏图标颜色，不受透明笔刷影响）。</summary>
    public static bool CurrentIsDark { get; private set; }

    /// <summary>主题色定义（5 种主题：紫、粉、蓝、橙、青）</summary>
    private static readonly Dictionary<CoreAppTheme, ThemeColors> ThemeMap = new()
    {
        [CoreAppTheme.Purple] = new ThemeColors("#9B7ED8", "#E8E0FF", "#7C5DCE"),
        [CoreAppTheme.Pink] = new ThemeColors("#EC407A", "#FFE0EB", "#D81B60"),
        [CoreAppTheme.Blue] = new ThemeColors("#42A5F5", "#D6E8FF", "#1E88E5"),
        [CoreAppTheme.Orange] = new ThemeColors("#FF7043", "#FFE0D6", "#F4511E"),
        [CoreAppTheme.Teal] = new ThemeColors("#26A69A", "#D6F5F0", "#00897B"),
    };

    // ======================================================================
    // 主题背景渐变设计（10 套：5 主题 × 深/浅模式）
    // 与 docs/theme-backgrounds-10.html 原型一一对应：
    //   Purple → 深:深空蓝 / 浅:薰衣草雾    Pink → 深:暗紫罗兰 / 浅:晨雾粉
    //   Blue   → 深:石墨钢蓝 / 浅:晴空蓝    Orange → 深:暗夜绯红 / 浅:奶油米
    //   Teal   → 深:墨夜青 / 浅:薄荷青
    // 每套 = 1 层多停靠线性渐变（主基调，From→To 为归一化坐标 0~1）
    //      + 1~2 层径向光晕（中心带透明度色 → 全透明边缘，Cx/Cy/Radius 归一化）。
    // 颜色统一 #AARRGGBB 格式，便于 Android Color.ParseColor / Win2D 直接解析。
    // ======================================================================
    private sealed record BgStop(string Argb, float Offset);
    private sealed record BgGlow(float Cx, float Cy, float Radius, string CenterArgb, string EdgeArgb = "#00000000");
    private sealed record BgDesign((float X, float Y) From, (float X, float Y) To, BgStop[] Stops, BgGlow[] Glows);

    private static readonly BgDesign DeepSpace = new((0, 1), (1, 0), new[]
    {
        new BgStop("#FF070A18", 0f), new BgStop("#FF141B3A", 0.48f), new BgStop("#FF2B2E60", 1f),
    }, new[]
    {
        new BgGlow(0.74f, 0.16f, 0.70f, "#6A8C7BFF"),
        new BgGlow(0.20f, 0.30f, 0.50f, "#3355D6FF"),
    });

    private static readonly BgDesign VioletDusk = new((0, 0), (0, 1), new[]
    {
        new BgStop("#FF170F26", 0f), new BgStop("#FF331A4A", 0.55f), new BgStop("#FF5A2770", 1f),
    }, new[]
    {
        new BgGlow(0.24f, 0.86f, 0.70f, "#61A842E2"),
        new BgGlow(0.80f, 0.20f, 0.50f, "#2EEC91FF"),
    });

    private static readonly BgDesign TealAbyss = new((1, 0.15f), (0, 0.85f), new[]
    {
        new BgStop("#FF05141B", 0f), new BgStop("#FF0B2A33", 0.55f), new BgStop("#FF0F3D46", 1f),
    }, new[]
    {
        new BgGlow(0.84f, 0.82f, 0.70f, "#6126A69A"),
        new BgGlow(0.18f, 0.22f, 0.50f, "#2955D6FF"),
    });

    private static readonly BgDesign EmberNoir = new((0, 0), (1, 1), new[]
    {
        new BgStop("#FF190B0F", 0f), new BgStop("#FF3A1520", 0.55f), new BgStop("#FF6A1F2E", 1f),
    }, new[]
    {
        new BgGlow(0.58f, 0.92f, 0.70f, "#52FF7043"),
        new BgGlow(0.78f, 0.18f, 0.45f, "#22FFCA9E"),
    });

    private static readonly BgDesign GraphiteSteel = new((0, 0), (1, 1), new[]
    {
        new BgStop("#FF0D1017", 0f), new BgStop("#FF1E2530", 0.50f), new BgStop("#FF36404F", 1f),
    }, new[]
    {
        new BgGlow(0.30f, 0.08f, 0.70f, "#426096CD"),
        new BgGlow(0.82f, 0.88f, 0.55f, "#29788CAA"),
    });

    private static readonly BgDesign MorningBlush = new((0, 0), (0, 1), new[]
    {
        new BgStop("#FFFFF3F5", 0f), new BgStop("#FFFFDDE4", 0.55f), new BgStop("#FFFFC6D2", 1f),
    }, new[]
    {
        new BgGlow(0.80f, 0.14f, 0.70f, "#F2FFFFFF"),
    });

    private static readonly BgDesign SkyBreeze = new((0, 0.10f), (1, 0.90f), new[]
    {
        new BgStop("#FFF0FAFF", 0f), new BgStop("#FFCFE9FF", 0.55f), new BgStop("#FF9FD2FF", 1f),
    }, new[]
    {
        new BgGlow(0.70f, 0.18f, 0.70f, "#F5FFFFFF"),
        new BgGlow(0.90f, 0.80f, 0.55f, "#3878BEFF"),
    });

    private static readonly BgDesign CreamVanilla = new((0, 0), (1, 0), new[]
    {
        new BgStop("#FFFFFCF3", 0f), new BgStop("#FFFBF1D6", 0.55f), new BgStop("#FFF2DFBB", 1f),
    }, new[]
    {
        new BgGlow(0.50f, 1.00f, 0.75f, "#F2FFF4D6"),
        new BgGlow(0.82f, 0.16f, 0.50f, "#CCFFFFFF"),
    });

    private static readonly BgDesign MintFresh = new((1, 0.15f), (0, 0.85f), new[]
    {
        new BgStop("#FFF2FCF8", 0f), new BgStop("#FFD7F4E7", 0.55f), new BgStop("#FFB4E7CF", 1f),
    }, new[]
    {
        new BgGlow(0.18f, 0.86f, 0.70f, "#E0FFFFFF"),
        new BgGlow(0.80f, 0.20f, 0.55f, "#338CE1BE"),
    });

    private static readonly BgDesign LavenderMist = new((0, 1), (1, 0), new[]
    {
        new BgStop("#FFF8F5FF", 0f), new BgStop("#FFE9E1FF", 0.55f), new BgStop("#FFD2C2FF", 1f),
    }, new[]
    {
        new BgGlow(0.75f, 0.14f, 0.70f, "#F0FFFFFF"),
        new BgGlow(0.20f, 0.88f, 0.60f, "#42C8AAFF"),
    });

    /// <summary>主题 + 深浅模式 → 背景渐变设计（10 套映射）</summary>
    private static BgDesign GetBackgroundDesign(CoreAppTheme theme, bool isDark)
        => (theme, isDark) switch
        {
            (CoreAppTheme.Purple, true) => DeepSpace,
            (CoreAppTheme.Pink, true) => VioletDusk,
            (CoreAppTheme.Blue, true) => GraphiteSteel,
            (CoreAppTheme.Orange, true) => EmberNoir,
            (CoreAppTheme.Teal, true) => TealAbyss,
            (CoreAppTheme.Purple, false) => LavenderMist,
            (CoreAppTheme.Pink, false) => MorningBlush,
            (CoreAppTheme.Blue, false) => SkyBreeze,
            (CoreAppTheme.Orange, false) => CreamVanilla,
            (CoreAppTheme.Teal, false) => MintFresh,
            _ => DeepSpace,
        };

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

    /// <summary>获取所有可选主题列表</summary>
    public List<CoreAppTheme> AvailableThemes => Enum.GetValues<CoreAppTheme>().ToList();

    /// <summary>构造函数，加载持久化设置并立即应用主题</summary>
    public ThemeService()
    {
        LoadSettings();
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

    /// <summary>设置雾面动态背景开关并持久化</summary>
    /// <param name="enabled">是否启用雾面背景</param>
    public void SetFrostedBackgroundEnabled(bool enabled)
    {
        _frostedBackgroundEnabled = enabled;
        Preferences.Default.Set(KeyFrostedBg, enabled);
        ApplyTheme();
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
            // 避免 AppThemeBinding 在 Windows XamlC 上的兼容性问题）
            app.Resources["PlayerIconColor"] = isDark ? Color.FromArgb("#FFFFFF") : Color.FromArgb("#1A1F3A");
            app.Resources["PlayerLikeColor"] = isDark ? Color.FromArgb("#FFFFFF") : Color.FromArgb("#E91E63");
            app.Resources["PlayerPlayBtnBg"] = isDark ? Color.FromArgb("#26FFFFFF") : Color.FromArgb("#22000000");
            app.Resources["PlayerTitleColor"] = isDark ? Color.FromArgb("#FFFFFF") : Color.FromArgb("#1A1F3A");
            app.Resources["PlayerSubColor"] = isDark ? Color.FromArgb("#CCFFFFFF") : Color.FromArgb("#4A5278");
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
    /// </summary>
    private void ApplyThemeBackgroundImage(ResourceDictionary resources, CoreAppTheme theme, bool isDark)
    {
        var png = RenderThemeBackgroundPng(theme, isDark);
        resources["ThemeBackgroundImage"] = GetOrCreateBackgroundImage(theme, isDark, png);
        resources["ThemeBackgroundEnabled"] = true;
        // 同步全局背景状态：主题内置图生效，清空自定义路径
        CurrentThemeBackgroundPng = png;
        CurrentCustomBackgroundPath = null;
        CurrentBackgroundEnabled = true;
    }

    /// <summary>代码绘制背景缓存（主题色 + 深/浅模式 双键），带容量上限的 LRU：
    /// 每套 1080×1920 ARGB ≈ 8MB 解码内存，10 套全缓存常驻几十 MB（低端机伤）。
    /// 最多保留 4 套（当前主题的深浅两套 + 最近切换过的），最久未用的先淘汰。</summary>
    private static readonly Dictionary<(CoreAppTheme Theme, bool IsDark), ImageSource> BackgroundImageCache = new();
    private static readonly LinkedList<(CoreAppTheme Theme, bool IsDark)> BackgroundImageLru = new();
    private const int BackgroundImageCacheLimit = 4;

    /// <summary>获取（或生成并缓存）代码绘制的主题背景 ImageSource</summary>
    private static ImageSource GetOrCreateBackgroundImage(CoreAppTheme theme, bool isDark, byte[] png)
    {
        var key = (theme, isDark);
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
    private static byte[] RenderThemeBackgroundPng(CoreAppTheme theme, bool isDark)
    {
        // 1080x1920（2K 竖屏）：手机端约 1:1 显示；PC 横屏拉伸后仍保持清晰。
        // 光晕半径按 max(w,h) 计算，PC 宽屏下光晕也能铺开，避免竖屏参数拉伸后不明显。
        const int width = 1080, height = 1920;
        var design = GetBackgroundDesign(theme, isDark);

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
            _currentTheme = (CoreAppTheme)Preferences.Default.Get(KeyTheme, 0);
            _darkModeSetting = (DarkModeSetting)Preferences.Default.Get(KeyDarkMode, 2);
            _customBackgroundPath = Preferences.Default.Get<string?>(KeyCustomBgPath, null);
            _customBackgroundOpacity = Preferences.Default.Get(KeyCustomBgOpacity, 0.5);
            _frostedBackgroundEnabled = Preferences.Default.Get(KeyFrostedBg, true);
            if (_customBackgroundPath != null && !File.Exists(_customBackgroundPath))
                _customBackgroundPath = null;

            if (!ThemeMap.ContainsKey(_currentTheme))
                _currentTheme = CoreAppTheme.Purple;
        }
        catch
        {
            _currentTheme = CoreAppTheme.Purple;
            _darkModeSetting = DarkModeSetting.FollowSystem;
            _customBackgroundPath = null;
            _customBackgroundOpacity = 0.5;
        }
    }

    private void SaveSetting(string key, int value)
    {
        try { Preferences.Default.Set(key, value); } catch { }
    }

    #endregion

    private static void ApplyDarkPalette(ResourceDictionary resources, ThemeColors colors)
    {
        var primary = Color.FromArgb(colors.Primary);
        var darkBase = Color.FromArgb("#1A1838");
        var midTone = Color.FromArgb("#0F1228");
        var primaryTint = primary.WithAlpha(0.18f);
        var accentTint = Color.FromArgb(GetAccentColor(_currentThemeStatic(colors.Primary))).WithAlpha(0.1f);

        resources["WindowBackgroundColor"] = Colors.Transparent; // 完全透明：仅透出统一全局背景（Window 层），无遮罩（Android）
#if WINDOWS
        resources["WindowBackgroundColor"] = Color.FromArgb("#12102B"); // Windows 无独立背景层，用主题深色底替换原生灰
#endif
        resources["WindowBackgroundAltColor"] = Color.FromArgb("#1E1C42");
        resources["SurfaceColor"] = Color.FromArgb("#2A2755");
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
        resources["GlassCardTintColor"] = Blend(Colors.White.WithAlpha(0.12f), primary.WithAlpha(0.07f));
        resources["GlassCardStrokeColor"] = primary.WithAlpha(0.18f);
        resources["GlassCardHighlightColor"] = Colors.White.WithAlpha(0.22f);
        resources["GlassCardButtonBgColor"] = Colors.White.WithAlpha(0.10f);
        resources["ChipInactiveColor"] = Color.FromArgb("#15FFFFFF");
        resources["ChipActiveColor"] = Color.FromArgb(colors.Primary);
        resources["ChipInactiveTextColor"] = Color.FromArgb("#C8CDE8");
        resources["ChipActiveTextColor"] = Colors.White;
        resources["BadgeBackgroundColor"] = Color.FromArgb("#14FFFFFF");
        resources["BadgeStrokeColor"] = Color.FromArgb("#28FFFFFF");
        resources["CardOverlayColor"] = Color.FromArgb("#0AFFFFFF");
        resources["ButtonOverlayColor"] = Color.FromArgb("#12FFFFFF");
        resources["ProgressTrackColor"] = Color.FromArgb("#20FFFFFF");
        resources["RowPressOverlayColor"] = Color.FromArgb("#26FFFFFF");
        resources["TextPrimaryColor"] = Color.FromArgb("#F5F6FF");
        resources["TextSecondaryColor"] = Color.FromArgb("#BCC0DD");
        resources["TextHintColor"] = Color.FromArgb("#868CAE");
        resources["TabActiveColor"] = Color.FromArgb(colors.Primary);
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

        // 英雄卡/顶部卡片：半透明毛玻璃（主题色低透明渐变，透出背景图）
        resources["HeroBrush"] = BuildLinearBrush($"{AlphaHex(0x59)}{colors.Primary[1..]}", $"{AlphaHex(0x24)}{GetAccentColorHex(colors.Primary)[1..]}", 0.0f, 1.0f);
        // 主操作按钮：半透明毛玻璃底（保留主题色调，白字仍可读）
        resources["PrimaryButtonBackgroundColor"] = primary.WithAlpha(0.55f);
        resources["PrimaryGlowBrush"] = BuildRadialBrush($"{AlphaHex(0x5A)}{colors.Primary[1..]}", $"{AlphaHex(0x00)}{colors.Primary[1..]}");
        var accent = GetAccentColor(_currentThemeStatic(colors.Primary));
        resources["AccentGlowBrush"] = BuildRadialBrush($"{AlphaHex(0x45)}{accent[1..]}", $"{AlphaHex(0x00)}{accent[1..]}");
        resources["GlassHighlightBrush"] = BuildLinearBrush("#28FFFFFF", "#04FFFFFF");
    }

    private static void ApplyLightPalette(ResourceDictionary resources, ThemeColors colors)
    {
        var primary = Color.FromArgb(colors.Primary);
        var primaryLight = Color.FromArgb(colors.Light);
        var lightBase = Color.FromArgb("#F8F7FF");
        var primaryWash = primaryLight.WithAlpha(0.6f);
        var accent = Color.FromArgb(GetAccentColor(_currentThemeStatic(colors.Primary))).WithAlpha(0.22f);

        resources["WindowBackgroundColor"] = Colors.Transparent; // 完全透明：仅透出统一全局背景（Window 层），无遮罩（Android）
#if WINDOWS
        resources["WindowBackgroundColor"] = Color.FromArgb("#F8F7FF"); // Windows 无独立背景层，用主题浅色底替换原生白
#endif
        resources["WindowBackgroundAltColor"] = Color.FromArgb("#EEEBFF");
        resources["SurfaceColor"] = Color.FromArgb("#FFFFFFFF");
        // 浅色模式卡片改为半透明毛玻璃（透出背景图，与深色模式一致）
        resources["CardBackgroundColor"] = Color.FromArgb("#8CFFFFFF");
        resources["CardBackgroundStrongColor"] = Color.FromArgb("#B3FFFFFF");
        resources["GlassButtonColor"] = Color.FromArgb("#99FFFFFF");
        resources["InputBackgroundColor"] = Color.FromArgb("#F0F2FF");
        resources["InputBorderColor"] = Color.FromArgb("#30000000");
        resources["DividerColor"] = Color.FromArgb("#1A000000");
        resources["GlassStrokeColor"] = Color.FromArgb("#28000000");
        resources["GlassStrokeStrongColor"] = Color.FromArgb("#50000000");
        // 桌面浮层卡（侧栏/播放条）毛玻璃配色：白色磨砂 + 淡主题色调
        resources["GlassCardTintColor"] = Blend(Colors.White.WithAlpha(0.70f), primary.WithAlpha(0.08f));
        resources["GlassCardStrokeColor"] = primary.WithAlpha(0.16f);
        resources["GlassCardHighlightColor"] = Colors.White.WithAlpha(0.85f);
        resources["GlassCardButtonBgColor"] = Colors.Black.WithAlpha(0.05f);
        resources["ChipInactiveColor"] = Color.FromArgb("#E8ECFF");
        resources["ChipActiveColor"] = Color.FromArgb(colors.Primary);
        resources["ChipInactiveTextColor"] = Color.FromArgb("#4A5278");
        resources["ChipActiveTextColor"] = Colors.White;
        resources["BadgeBackgroundColor"] = accent;
        resources["BadgeStrokeColor"] = accent;
        resources["CardOverlayColor"] = Color.FromArgb("#08000000");
        resources["ButtonOverlayColor"] = Color.FromArgb("#12000000");
        resources["ProgressTrackColor"] = Color.FromArgb("#18000000");
        resources["RowPressOverlayColor"] = Color.FromArgb("#22000000");
        resources["TextPrimaryColor"] = Color.FromArgb("#1A1F3A");
        resources["TextSecondaryColor"] = Color.FromArgb("#4A5278");
        resources["TextHintColor"] = Color.FromArgb("#6B7399");
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
        return new Color(
            baseColor.Red * (1 - a) + overlay.Red * a,
            baseColor.Green * (1 - a) + overlay.Green * a,
            baseColor.Blue * (1 - a) + overlay.Blue * a,
            1f);
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
                (isDark ? Color.FromArgb("#080914") : Color.FromArgb("#F8F7FF")).WithAlpha((float)overlayAlpha));
            resources["WindowBackgroundColor"] = (isDark ? Color.FromArgb("#080914") : Color.FromArgb("#F8F7FF")).WithAlpha((float)overlayAlpha);
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

    private static string GetAccentColor(CoreAppTheme theme) => theme switch
    {
        CoreAppTheme.Purple => "#55D6FF",
        CoreAppTheme.Pink => "#FFB86E",
        CoreAppTheme.Blue => "#5AE4FF",
        CoreAppTheme.Orange => "#FFD36E",
        CoreAppTheme.Teal => "#80CBC4",
        _ => "#55D6FF"
    };

    private static string GetAccentColorHex(string primaryHex)
        => primaryHex switch
        {
            "#9B7ED8" => "#55D6FF",
            "#EC407A" => "#FFB86E",
            "#42A5F5" => "#5AE4FF",
            "#FF7043" => "#FFD36E",
            "#26A69A" => "#80CBC4",
            _ => "#55D6FF"
        };

    private static CoreAppTheme _currentThemeStatic(string primaryHex)
        => primaryHex switch
        {
            "#9B7ED8" => CoreAppTheme.Purple,
            "#EC407A" => CoreAppTheme.Pink,
            "#42A5F5" => CoreAppTheme.Blue,
            "#FF7043" => CoreAppTheme.Orange,
            "#26A69A" => CoreAppTheme.Teal,
            _ => CoreAppTheme.Purple
        };

    private static string AlphaHex(byte alpha) => alpha.ToString("X2");

    /// <summary>主题颜色组</summary>
    private record ThemeColors(string Primary, string Light, string Dark);
}





