using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Helpers;

/// <summary>
/// 将 MauiImage 资源名（如 "ic_play"）在运行时正确转换为 ImageSource。
/// </summary>
/// <remarks>
/// XAML 字面量 Source="ic_play" 能工作是因为编译期 ImageSourceConverter。
/// 绑定 string 到 ImageSource 在某些版本不会自动解析，且 ImageSource.FromFile
/// 会把资源名当成文件路径导致 E_NETWORK_ERROR。此 helper 显式走 Converter。
/// </remarks>
public static class ImageSourceHelper
{
    private static readonly ImageSourceConverter _converter = new();

    /// <summary>
    /// ImageSource 实例缓存。图标集很小（~15 个名称），缓存后避免每次状态切换都创建新对象，减少 GC 压力。
    /// ImageSource 是不可变描述符（只含 File 路径等属性），跨控件共享安全。
    /// </summary>
    private static readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// 已有 _light 变体的图标白名单。这些图标在浅色模式下使用深色填充 (#1B2140)，
    /// 在深色模式下使用白色填充 (#FFFFFF/#C8C8C8)。
    /// 不在此列表中的图标（如 ic_favorite 使用彩色）在两种模式下都使用原始版本。
    /// </summary>
    private static readonly HashSet<string> _themedIcons = new()
    {
        "ic_play", "ic_pause",
        "ic_skip_previous", "ic_skip_next",
        "ic_repeat_all", "ic_repeat_one", "ic_shuffle", "ic_infinite",
        "ic_search", "ic_refresh",
        "ic_arrow_forward", "ic_arrow_left", "ic_check",
    };

    public static ImageSource? FromName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        // 命中缓存直接返回，避免重复创建 ImageSource 实例
        if (_cache.TryGetValue(name, out var cached)) return cached;

        try
        {
            // 显式走 ImageSourceConverter 解析 MauiImage 资源名（与 XAML 字面量 Source="ic_xxx" 同一路径）。
            // 注意：ImageSource.FromFile 会把资源名当成本地文件路径，在 Windows 未找到时会触发 E_NETWORK_ERROR。
            // MAUI 11 Preview Windows: 运行时 Converter 经常解析不到生成的 scale PNG，
            // 对未打包 WinUI 3 应用直接指向输出目录中的 scale-100 PNG 更可靠。
            var resolvedName = name;
            ImageSource? result;
            if (OperatingSystem.IsWindows())
            {
                if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    resolvedName = name + ".png";

                var baseName = name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    ? name.Substring(0, name.Length - 4)
                    : name;
                // 按当前 DPI 密度选最接近的 scale 变体（100/125/150/200/400）：
                // 固定 scale-100（24×24）在高 DPI 下放大显示会锯齿
                var scaleFile = FindBestScaleFile(baseName);
                if (scaleFile != null)
                {
                    result = ImageSource.FromFile(scaleFile);
                    _cache[name] = result;
                    return result;
                }
            }
            result = (ImageSource?)_converter.ConvertFromInvariantString(resolvedName);
            _cache[name] = result;
            return result;
        }
        catch (Exception ex)
        {
            Log.Debug("ImageSourceHelper", $"[ImageSourceHelper] FromName({name}) failed: {ex.Message}");
            _cache[name] = null;
            return null;
        }
    }

    /// <summary>按显示器 DPI 密度选择最接近的 scale PNG（100/125/150/200/400），找不到逐级降级。</summary>
    private static string? FindBestScaleFile(string baseName)
    {
        double density = 1.0;
        try { density = Microsoft.Maui.Devices.DeviceDisplay.Current.MainDisplayInfo.Density; }
        catch { }

        string[] preferred = density >= 3.5 ? new[] { "400" }
            : density >= 1.75 ? new[] { "200" }
            : density >= 1.4 ? new[] { "150" }
            : density >= 1.15 ? new[] { "125" }
            : new[] { "100" };

        foreach (var suffix in preferred.Concat(new[] { "200", "150", "125", "100" }).Distinct())
        {
            var p = Path.Combine(AppContext.BaseDirectory, $"{baseName}.scale-{suffix}.png");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>优先选最高分辨率 scale PNG（400→200→150→125→100）：小尺寸显示时缩小渲染，锐利无锯齿。</summary>
    private static string? FindHighResScaleFile(string baseName)
    {
        foreach (var suffix in new[] { "400", "200", "150", "125", "100" })
        {
            var p = Path.Combine(AppContext.BaseDirectory, $"{baseName}.scale-{suffix}.png");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>
    /// 高分辨率版（Windows）：加载最高 scale 的 PNG（如 96×96）缩小显示，小尺寸图标锐利无锯齿。
    /// ⚠️ 不用 SVG 直接渲染：MAUI Windows 的 ImageSource.FromFile 走 BitmapImage，不支持 .svg（显示空白），
    /// 因此用"超采样 PNG"等效实现矢量观感。非 Windows 回退 <see cref="FromName"/>。
    /// </summary>
    public static ImageSource? FromNameVector(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (OperatingSystem.IsWindows())
        {
            var hiRes = FindHighResScaleFile(name);
            if (hiRes != null) return ImageSource.FromFile(hiRes);
        }
        return FromName(name);
    }

    /// <summary>
    /// 播放模式图标的主题感知高分辨率版：浅色模式优先用主题色预生成变体
    /// <c>{name}_{hex}_active</c>（存在时），深色模式用原版；加载最高 scale PNG 缩小显示。
    /// </summary>
    public static ImageSource? FromNameVectorThemed(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (OperatingSystem.IsWindows())
        {
            var dark = Application.Current?.RequestedTheme == Microsoft.Maui.ApplicationModel.AppTheme.Dark;
            if (!dark)
            {
                var hex = GetPrimaryTintHex();
                if (!string.IsNullOrEmpty(hex))
                {
                    var themed = FindHighResScaleFile($"{name}_{hex}_active");
                    if (themed != null) return ImageSource.FromFile(themed);
                }
            }
            var hiRes = FindHighResScaleFile(name);
            if (hiRes != null) return ImageSource.FromFile(hiRes);
        }
        return FromName(name);
    }

    /// <summary>
    /// 主题感知版本：浅色模式下对白名单图标自动使用 _light 变体。
    /// 用于 ViewModel 中绑定的图标源，在主题切换时需调用方刷新。
    /// </summary>
    public static ImageSource? FromNameThemed(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (Application.Current?.RequestedTheme == Microsoft.Maui.ApplicationModel.AppTheme.Light && _themedIcons.Contains(name))
        {
            return FromName(name + "_light");
        }
        return FromName(name);
    }

    /// <summary>
    /// 始终返回原始版本（深色主题填充色），用于深色/主题色背景上需要白色图标的场景。
    /// </summary>
    public static ImageSource? FromNameOriginal(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return FromName(name);
    }

    /// <summary>
    /// 播放控制条图标着色：深色模式用 <paramref name="whiteName"/>（白色原版），
    /// 浅色模式用当前主题色预生成变体 <c>{name}_{hex}_active</c>（构建期由 MauiImage 转为 PNG）。
    /// <para>
    /// 不依赖平台 Image.TintColor（MAUI 的 Image 并无该属性，仅 ImageButton 有且 Windows 上行为不稳），
    /// 复用项目既有的「按主题色预生成 SVG 变体 + 运行时切 Source」模式（同 TabBar 的 ic_*_active 图标）。
    /// </para>
    /// </summary>
    public static ImageSource? FromNamePlayerCtrl(string name, string whiteName, bool? isDark = null)
    {
        if (string.IsNullOrEmpty(name)) return null;

        var dark = isDark ?? Application.Current?.RequestedTheme == Microsoft.Maui.ApplicationModel.AppTheme.Dark;
        if (dark) return FromName(whiteName);

        var hex = GetPrimaryTintHex();
        if (string.IsNullOrEmpty(hex)) return FromName(whiteName);
        // 主题色预生成变体缺失时回退白色原版（避免浅色模式下图标空白）
        return FromName($"{name}_{hex}_active") ?? FromName(whiteName);
    }

    /// <summary>读取当前主题色 PrimaryColor 的 #rrggbb（小写），取不到返回 null</summary>
    private static string? GetPrimaryTintHex()
    {
        try
        {
            if (Application.Current?.Resources.TryGetValue("PrimaryColor", out var value) == true &&
                value is Color c)
            {
                var r = (byte)Math.Round(c.Red * 255);
                var g = (byte)Math.Round(c.Green * 255);
                var b = (byte)Math.Round(c.Blue * 255);
                return $"{r:x2}{g:x2}{b:x2}";
            }
        }
        catch { }
        return null;
    }
}
