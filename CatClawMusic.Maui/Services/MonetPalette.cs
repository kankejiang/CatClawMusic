using System.Security.Cryptography;
using System.Text;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 莫奈取色源（Material You）：从系统壁纸提取主色/次色，供主题背景动态生成。
/// - Android 8+：WallpaperManager.GetWallpaperColors(FlagSystem)，minSdk 31 恒可用
/// - Windows：系统强调色（UISettings.Accent，PC 上"壁纸"的最近对应物）
/// - 取不到色（无壁纸/异常）时 Current 为 null，调用方回退标准渐变设计。
/// 颜色数学（RGB↔HSL）跨平台共用一份实现。
/// </summary>
public static class MonetPalette
{
    public sealed record PaletteColors(string Primary, string Secondary);

    /// <summary>当前壁纸调色板；取不到色时为 null（回退标准背景）</summary>
    public static PaletteColors? Current { get; private set; }

    /// <summary>调色板指纹（8 hex，用于背景缓存文件名：壁纸变化 → 指纹变化 → 缓存失效重画）</summary>
    public static string? Fingerprint { get; private set; }

    private static bool _watched;

    /// <summary>刷新壁纸取色（同步、轻量：系统缓存壁纸色，无解码开销）</summary>
    public static void Refresh()
    {
        try
        {
#if ANDROID
            var wm = Android.App.WallpaperManager.GetInstance(Android.App.Application.Context);
            var wc = wm?.GetWallpaperColors((int)Android.App.WallpaperManagerFlags.System);
            var p = wc?.PrimaryColor;
            if (p == null)
            {
                Current = null;
                Fingerprint = null;
                return;
            }
            // 次色缺失时用主色 hue 旋转 60° 合成（保持双光晕层次）
            var s = wc?.SecondaryColor;
            var primary = FromAndroid(p);
            var secondary = s != null ? FromAndroid(s) : RotateHue(primary, 60);
            Current = new PaletteColors(ToHex(primary), ToHex(secondary));
            Fingerprint = FingerprintOf(primary, secondary);
#elif WINDOWS
            var ui = new Windows.UI.ViewManagement.UISettings();
            var accent = ui.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            var primary = FromWin(accent);
            var light = ui.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentLight1);
            var secondary = light != accent ? FromWin(light) : RotateHue(primary, 60);
            Current = new PaletteColors(ToHex(primary), ToHex(secondary));
            Fingerprint = FingerprintOf(primary, secondary);
#else
            Current = null;
            Fingerprint = null;
#endif
        }
        catch
        {
            Current = null;
            Fingerprint = null;
        }
    }

    /// <summary>监听壁纸颜色变化（Android）：变化后回调（重取色由回调方执行）。
    /// 幂等：多次调用只注册一次。其他平台无操作。</summary>
    public static void Watch(Action onChanged)
    {
#if ANDROID
        if (_watched) return;
        _watched = true;
        try
        {
            var wm = Android.App.WallpaperManager.GetInstance(Android.App.Application.Context);
            var handler = new Android.OS.Handler(Android.OS.Looper.MainLooper);
            wm?.AddOnColorsChangedListener(new ColorsChangedListener(onChanged), handler);
        }
        catch { _watched = false; }
#else
        _ = onChanged;
#endif
    }

#if ANDROID
    private sealed class ColorsChangedListener : Java.Lang.Object, Android.App.WallpaperManager.IOnColorsChangedListener
    {
        private readonly Action _onChanged;
        public ColorsChangedListener(Action onChanged) => _onChanged = onChanged;
        public void OnColorsChanged(Android.App.WallpaperColors? colors, int which) => _onChanged();
    }

    private static (byte R, byte G, byte B) FromAndroid(Android.Graphics.ColorObject c)
    {
        var argb = c.ToArgb();
        return ((byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));
    }
#endif

#if WINDOWS
    private static (byte R, byte G, byte B) FromWin(Windows.UI.Color c)
        => (c.R, c.G, c.B);
#endif

    private static string FingerprintOf((byte R, byte G, byte B) a, (byte R, byte G, byte B) b)
    {
        var raw = $"{a.R},{a.G},{a.B}|{b.R},{b.G},{b.B}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..8];
    }

    private static string ToHex((byte R, byte G, byte B) c)
        => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    // ═══ RGB ↔ HSL 数学（跨平台共用） ═══

    public static (double H, double S, double L) ToHsl((byte R, byte G, byte B) c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2.0;
        if (Math.Abs(max - min) < 1e-9) return (0, 0, l);

        double d = max - min;
        double s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        double h;
        if (Math.Abs(max - r) < 1e-9) h = (g - b) / d + (g < b ? 6 : 0);
        else if (Math.Abs(max - g) < 1e-9) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        return (h * 60.0, s, l);
    }

    public static (byte R, byte G, byte B) FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = l - c / 2;
        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return ((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    public static (byte R, byte G, byte B) RotateHue((byte R, byte G, byte B) c, double degrees)
    {
        var (h, s, l) = ToHsl(c);
        return FromHsl(h + degrees, s, l);
    }
}
