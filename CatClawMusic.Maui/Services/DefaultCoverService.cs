using CatClawMusic.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Graphics;
using CoreAppTheme = CatClawMusic.Core.Interfaces.AppTheme;
#if ANDROID
using Microsoft.Maui.Graphics.Platform;
#endif

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 默认封面（唱片碟占位图）生成服务：用代码绘制替代内置大尺寸 PNG（cover_default.png 已移除）。
/// 按当前主题色 + 深浅模式绘制一张小尺寸唱片碟图并缓存到本地，
/// 后续经 CachingFileImageSourceService 的降采样文件管线加载，彻底避免超大位图崩溃。
/// 绘图逻辑为跨平台 <see cref="IDrawable"/>，渲染（建画布 + 存盘）分平台实现。
/// </summary>
public static class DefaultCoverService
{
    /// <summary>渲染边长（像素）。播放页封面约 220dp，3x 设备约 660px，720 足够清晰。</summary>
    private const int RenderSize = 720;

    private static readonly string _cacheDir = Path.Combine(FileSystem.CacheDirectory, "defaultcovers");

    /// <summary>各主题主色，与 ThemeService.ThemeMap 的 Primary 保持一致。</summary>
    private static readonly Dictionary<CoreAppTheme, string> ThemePrimary = new()
    {
        [CoreAppTheme.Purple] = "#9B7ED8",
        [CoreAppTheme.Pink] = "#EC407A",
        [CoreAppTheme.Blue] = "#42A5F5",
        [CoreAppTheme.Orange] = "#FF7043",
        [CoreAppTheme.Teal] = "#26A69A",
    };

    /// <summary>
    /// 获取当前主题 + 深浅模式对应的默认封面文件路径（不存在时即时绘制并缓存）。
    /// 返回的路径可直接用于 <c>ImageSource.FromFile</c>。
    /// </summary>
    public static string GetDefaultCoverPath()
    {
        var theme = CoreAppTheme.Purple;
        var isDark = true;
        try
        {
            if (MauiProgram.Services?.GetService<IThemeService>() is { } ts)
            {
                theme = ts.CurrentTheme;
                isDark = ts.IsEffectivelyDark();
            }
        }
        catch { /* 主题读取失败时使用默认值 */ }

        if (!ThemePrimary.ContainsKey(theme)) theme = CoreAppTheme.Purple;

        Directory.CreateDirectory(_cacheDir);
        var path = Path.Combine(_cacheDir, $"vinyl_{theme}_{(isDark ? "dark" : "light")}.png");
        if (!File.Exists(path))
        {
            try { RenderToFile(new VinylDrawable(ThemePrimary[theme], isDark), path, RenderSize); }
            catch (Exception ex) { Log.Debug("DefaultCoverService", $"[DefaultCoverService] Render failed: {ex.Message}"); }
        }
        return path;
    }

    /// <summary>将 IDrawable 渲染为 PNG 文件（分平台建画布 + 存盘）。</summary>
    private static void RenderToFile(IDrawable drawable, string path, int size)
    {
#if ANDROID
        using var context = new PlatformBitmapExportContext(size, size);
        drawable.Draw(context.Canvas, new RectF(0, 0, size, size));
        using var fs = File.Create(path);
        context.WriteToStream(fs);
#elif WINDOWS
        var device = Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();
        var renderTarget = new Microsoft.Graphics.Canvas.CanvasRenderTarget(device, size, size, 96);
        using (var session = renderTarget.CreateDrawingSession())
        {
            var canvas = new Microsoft.Maui.Graphics.Platform.PlatformCanvas
            {
                Session = session,
                CanvasSize = new global::Windows.Foundation.Size(size, size)
            };
            drawable.Draw(canvas, new RectF(0, 0, size, size));
        }
        renderTarget.SaveAsync(path, Microsoft.Graphics.Canvas.CanvasBitmapFileFormat.Png)
            .AsTask().GetAwaiter().GetResult();
#endif
    }

    /// <summary>
    /// 唱片碟占位图绘制：圆角渐变底 + 碟体 + 纹路 + 碟心标签（猫爪音乐 / CATCLAW MUSIC）+ 中心孔。
    /// 渐变使用相对坐标 (0-1)，圆形用椭圆包围盒绘制。
    /// </summary>
    private sealed class VinylDrawable : IDrawable
    {
        private readonly Color _primary;
        private readonly bool _isDark;

        public VinylDrawable(string primaryHex, bool isDark)
        {
            _primary = Color.FromArgb(primaryHex);
            _isDark = isDark;
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            const float S = RenderSize;
            var black = new Color(0f, 0f, 0f);
            var white = new Color(1f, 1f, 1f);

            // 背景圆角方块（主题色微调的深/浅渐变）
            var bgTop = _isDark ? Mix(_primary, black, 0.80f) : Mix(_primary, white, 0.88f);
            var bgBottom = _isDark ? Mix(_primary, black, 0.90f) : Mix(_primary, white, 0.78f);
            var bgPaint = new LinearGradientPaint(new Point(0, 0), new Point(1, 1)) { StartColor = bgTop, EndColor = bgBottom };
            canvas.SetFillPaint(bgPaint, new RectF(0, 0, S, S));
            canvas.FillRoundedRectangle(0, 0, S, S, S * 0.12f);

            float cx = S / 2f, cy = S / 2f;
            float discR = S * 0.33f; // 碟体直径约为边长 66%

            // 碟体（径向渐变模拟碟面深浅）
            var discCenter = _isDark ? Mix(_primary, black, 0.68f) : Mix(_primary, white, 0.52f);
            var discEdge = _isDark ? Mix(_primary, black, 0.84f) : Mix(_primary, white, 0.66f);
            var discPaint = new RadialGradientPaint(new Point(0.5, 0.5), 0.5) { StartColor = discCenter, EndColor = discEdge };
            canvas.SetFillPaint(discPaint, new RectF(cx - discR, cy - discR, discR * 2, discR * 2));
            canvas.FillEllipse(cx - discR, cy - discR, discR * 2, discR * 2);

            // 唱片纹路（同心圆）
            canvas.StrokeColor = _isDark ? white.WithAlpha(0.10f) : black.WithAlpha(0.08f);
            canvas.StrokeSize = Math.Max(1f, S * 0.002f);
            canvas.DrawEllipse(cx - discR * 0.86f, cy - discR * 0.86f, discR * 1.72f, discR * 1.72f);
            canvas.DrawEllipse(cx - discR * 0.70f, cy - discR * 0.70f, discR * 1.40f, discR * 1.40f);

            // 碟心标签（主题色渐变圆 + 描边）
            float labelR = discR * 0.62f;
            var labelTop = _isDark ? Mix(_primary, white, 0.18f) : Mix(_primary, white, 0.28f);
            var labelBottom = _isDark ? Mix(_primary, black, 0.28f) : Mix(_primary, black, 0.08f);
            var labelPaint = new LinearGradientPaint(new Point(0.5, 0), new Point(0.5, 1)) { StartColor = labelTop, EndColor = labelBottom };
            canvas.SetFillPaint(labelPaint, new RectF(cx - labelR, cy - labelR, labelR * 2, labelR * 2));
            canvas.FillEllipse(cx - labelR, cy - labelR, labelR * 2, labelR * 2);
            canvas.StrokeColor = white.WithAlpha(0.25f);
            canvas.StrokeSize = Math.Max(1f, S * 0.003f);
            canvas.DrawEllipse(cx - labelR, cy - labelR, labelR * 2, labelR * 2);

            // 标签文字：中文在上半部，英文小字在下半部
            canvas.FontSize = S * 0.052f;
            canvas.FontColor = white.WithAlpha(0.95f);
            canvas.DrawString("猫爪音乐", cx - labelR, cy - labelR * 0.78f, labelR * 2, labelR * 0.62f, HorizontalAlignment.Center, VerticalAlignment.Center);
            canvas.FontSize = S * 0.023f;
            canvas.FontColor = white.WithAlpha(0.70f);
            canvas.DrawString("CATCLAW MUSIC", cx - labelR, cy + labelR * 0.16f, labelR * 2, labelR * 0.62f, HorizontalAlignment.Center, VerticalAlignment.Center);

            // 中心孔
            float holeR = S * 0.012f;
            canvas.FillColor = bgBottom;
            canvas.FillEllipse(cx - holeR, cy - holeR, holeR * 2, holeR * 2);
            canvas.StrokeColor = white.WithAlpha(0.30f);
            canvas.StrokeSize = Math.Max(1f, S * 0.002f);
            canvas.DrawEllipse(cx - holeR, cy - holeR, holeR * 2, holeR * 2);
        }

        /// <summary>颜色线性混合（t=0 返回 a，t=1 返回 b）。</summary>
        private static Color Mix(Color a, Color b, float t) =>
            new(a.Red + (b.Red - a.Red) * t,
                a.Green + (b.Green - a.Green) * t,
                a.Blue + (b.Blue - a.Blue) * t,
                1f);
    }
}
