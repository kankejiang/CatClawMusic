#if ANDROID
using Android.App;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using AndroidX.Core.View;
using Microsoft.Maui.ApplicationModel;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 统一全局背景类：把主题内置渐变图 / 用户自定义背景图只绘制一份，挂到 Android Window(DayDecorView)
/// 层，位于所有页面与系统栏之下。这样「背景」对所有页面（含歌单、设置等推入页）全局生效，且只存在一个图层，
/// 从根源上解决了此前各个页面各自渲染、推入页用不透明笔刷盖掉图片、底部导航栏与页面背景不在同一图层的问题。
///
/// 使用方式：MainActivity.OnCreate 调用 <see cref="Init"/>；每次主题/背景切换时
/// ThemeService 通过 <see cref="ThemeService.StaticApplied"/> 通知本类 <see cref="Apply"/> 重新应用。
///
/// 生效前提：页面根布局需为半透明（ThemeService 已把 PageBackgroundBrush / WindowBackgroundColor
/// 改为半透明），才能透出这层全局背景。
/// </summary>
public static class GlobalBackgroundService
{
    private static bool _subscribed;
    private static bool _applied;
    // 启动首帧延迟重刷专用标记：只在首次启动时触发一次，不做周期性重刷。
    private static bool _startupSettleScheduled;

    /// <summary>订阅主题变更事件，确保主题/背景切换立即全局刷新（无需重启）。</summary>
    public static void Init()
    {
        if (_subscribed) return;
        _subscribed = true;
        ThemeService.StaticApplied += OnThemeApplied;
    }

    private static void OnThemeApplied()
    {
        // 主题已变化：清除「已应用」标记，触发重新解码并应用到窗口
        _applied = false;
        Apply();
    }

    /// <summary>把当前背景应用到平台窗口（DecorView）。无可用 Activity/背景时退化为 WindowBackgroundColor 纯色。</summary>
    public static void Apply()
    {
#if ANDROID
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                if (_applied) return; // 已应用且主题未变化：避免每次布局/恢复重复解码背景图
                if (Platform.CurrentActivity is not Activity activity) return;
                Android.Views.View? decor = activity.Window?.DecorView;
                if (decor == null) return;

                int screenW = decor.Width, screenH = decor.Height;
                if (screenW <= 0 || screenH <= 0)
                {
                    var dm = activity.Resources?.DisplayMetrics;
                    screenW = dm?.WidthPixels ?? 1080;
                    screenH = dm?.HeightPixels ?? 1920;
                }

                var bg = CreateBackgroundDrawable(activity, screenW, screenH);
                // 背景图缺失时退回纯色兜底（按明暗模式），保证系统栏与页面底色一致
                var fallback = Android.Graphics.Color.ParseColor(ThemeService.CurrentIsDark ? "#12102B" : "#F8F7FF");
                decor.Background = bg ?? new ColorDrawable(fallback);
                UpdateSystemBars(activity, decor);
                _applied = true;

                // 启动首帧窗口仍处于入场过渡/尺寸未定阶段，硬件层可能按低分辨率缓存，
                // 导致后续页面背景「启动发糊、切换页面后变清晰」。窗口稳定后重刷一次：
                // 重新解码并以全屏物理分辨率重建 DecorView 背景，等效于把「导航后变清晰」
                // 这一步提前到启动完成时自动完成。仅启动时触发一次，不做周期性重刷。
                ScheduleStartupSettleReapply();
            }
            catch { /* 背景应用失败不阻塞启动 */ }
        });
#else
        // 非 Android 平台：保持原有纯色背景策略，不额外处理
#endif
    }

    /// <summary>首次启动时预约一次延迟重刷：窗口完全铺开后按最终尺寸重新解码应用背景，
    /// 消除启动阶段的低清缓存帧。任何一次成功的 Apply 后仅调度一次，避免无限循环。</summary>
    private static void ScheduleStartupSettleReapply()
    {
        if (_startupSettleScheduled) return;
        _startupSettleScheduled = true;

        System.Threading.Tasks.Task.Delay(500).ContinueWith(_ =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    _applied = false;
                    Apply();
                }
                catch { }
            });
        });
    }

    /// <summary>按优先级生成背景 Drawable：用户自定义图 → 主题内置渐变图 → null（退化为纯色）。</summary>
    private static Drawable? CreateBackgroundDrawable(Activity activity, int screenW, int screenH)
    {
        if (!ThemeService.CurrentBackgroundEnabled) return null;

        Bitmap? src = null;
        try
        {
            var custom = ThemeService.CurrentCustomBackgroundPath;
            if (!string.IsNullOrEmpty(custom) && File.Exists(custom))
                src = DecodeDownsampled(custom, Math.Max(screenW, screenH));

            if (src == null && ThemeService.CurrentThemeBackgroundPng is { Length: > 0 } png)
                src = BitmapFactory.DecodeByteArray(png, 0, png.Length);
        }
        catch { }

        if (src == null) return null;

        try
        {
            // 居中裁剪到屏幕比例（等效 AspectFill），再铺满 DecorView 不会拉伸变形
            var cropped = CenterCropToScreen(src, screenW, screenH);
            return new BitmapDrawable(activity.Resources, cropped);
        }
        catch
        {
            return new BitmapDrawable(activity.Resources, src);
        }
    }

    /// <summary>把源图按屏幕比例居中裁剪（AspectFill 语义）。</summary>
    private static Bitmap CenterCropToScreen(Bitmap src, int screenW, int screenH)
    {
        int sw = src.Width, sh = src.Height;
        if (sw <= 0 || sh <= 0) return src;
        float scale = Math.Max((float)screenW / sw, (float)screenH / sh);
        int cropW = Math.Min((int)(screenW / scale), sw);
        int cropH = Math.Min((int)(screenH / scale), sh);
        int x = (sw - cropW) / 2;
        int y = (sh - cropH) / 2;
        return Bitmap.CreateBitmap(src, x, y, cropW, cropH);
    }

    /// <summary>降采样解码本地图片，长边限定为目标像素，避免整张大图分配内存导致 OOM。</summary>
    private static Bitmap? DecodeDownsampled(string path, int targetPx)
    {
        try
        {
            var opts = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeFile(path, opts);
            if (opts.OutWidth <= 0 || opts.OutHeight <= 0) return null;

            int maxDim = Math.Max(opts.OutWidth, opts.OutHeight);
            int sample = 1;
            while (maxDim / sample > targetPx) sample *= 2;

            opts.InJustDecodeBounds = false;
            opts.InSampleSize = sample;
            opts.InPreferredConfig = Bitmap.Config.Argb8888;
            return BitmapFactory.DecodeFile(path, opts);
        }
        catch (Exception ex)
        {
            Log.Debug("GlobalBg", $"[GlobalBackground] decode failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>根据当前明暗模式自动切换状态栏/导航栏图标颜色（浅色深图标，深色白图标）。
    /// 不依赖背景色亮度，避免完全透明的 WindowBackgroundColor 导致误判。</summary>
    private static void UpdateSystemBars(Activity activity, Android.Views.View decor)
    {
        try
        {
            bool isLight = !ThemeService.CurrentIsDark; // 浅色 → 深色图标
            var controller = WindowCompat.GetInsetsController(activity.Window, decor);
            if (controller != null)
            {
                controller.AppearanceLightStatusBars = isLight;
                controller.AppearanceLightNavigationBars = isLight;
            }
        }
        catch { }
    }
}
#endif