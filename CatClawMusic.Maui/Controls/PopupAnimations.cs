using System.Threading.Tasks;

namespace CatClawMusic.Maui.Controls;

/// <summary>Action → Java.Lang.IRunnable 适配（ViewPropertyAnimator.WithEndAction 需要）</summary>
internal sealed class EndActionRunnable : Java.Lang.Object, Java.Lang.IRunnable
{
    private readonly Action _action;
    public EndActionRunnable(Action action) { _action = action; }
    public void Run() => _action();
}

/// <summary>
/// 弹窗系统动画封装：Android 用框架级 ViewPropertyAnimator + 系统 Interpolator——
/// 位移/缩放由 RenderThread 合成驱动，不占 UI 线程（MAUI Ticker 逐帧方案在主线程繁忙时掉帧）；
/// 其他平台回退 MAUI ViewExtensions 动画，Windows 端行为不变。
/// </summary>
/// <remarks>
/// 可选系统插值器：
/// - DecelerateInterpolator(tension)：减速收尾，抽屉滑入/滑出的标准手感；
/// - OvershootInterpolator(tension)：带弹性过冲，居中卡片弹入的"系统级"质感（0.9 ≈ 轻微过冲）；
/// - FastOutSlowInInterpolator 需 androidx.interpolator（当前未直接引用，如需 Material 标准曲线再加）；
/// - 物理弹簧 SpringAnimation 需 Xamarin.AndroidX.DynamicAnimation 包（当前未引用）。
/// </remarks>
internal static class PopupAnimations
{
    /// <summary>系统减速插值器张力：越大前段越快、收尾越柔；1.3f ≈ Material decelerate 手感</summary>
    private const float DecelerateTension = 1.3f;

    /// <summary>居中卡片弹入的过冲张力：&gt;0 产生轻微越过目标再回弹的系统弹性</summary>
    private const float OvershootTension = 0.9f;

    /// <summary>底部抽屉/卡片从 fromDp（dp 位移）滑入到位（系统减速曲线，无回弹）。</summary>
    public static Task SlideUpAsync(Microsoft.Maui.Controls.VisualElement v, double fromDp, uint durationMs = 280)
    {
#if ANDROID
        if (v.Handler?.PlatformView is global::Android.Views.View native)
        {
            var d = native.Resources!.DisplayMetrics!.Density;
            native.TranslationY = (float)(fromDp * d);
            var tcs = new TaskCompletionSource();
            native.Animate().TranslationY(0f).SetDuration(durationMs)
                .SetInterpolator(new global::Android.Views.Animations.DecelerateInterpolator(DecelerateTension))
                .WithEndAction(new EndActionRunnable(() => tcs.TrySetResult()))
                .Start();
            return tcs.Task;
        }
#endif
        return v.TranslateTo(0, 0, durationMs, Easing.CubicOut);
    }

    /// <summary>滑出屏幕下方（关闭动画；toDp 为滑出目标位移）。</summary>
    public static Task SlideDownAwayAsync(Microsoft.Maui.Controls.VisualElement v, double toDp, uint durationMs = 220)
    {
#if ANDROID
        if (v.Handler?.PlatformView is global::Android.Views.View native)
        {
            var d = native.Resources!.DisplayMetrics!.Density;
            var tcs = new TaskCompletionSource();
            native.Animate().TranslationY((float)(toDp * d)).SetDuration(durationMs)
                .SetInterpolator(new global::Android.Views.Animations.DecelerateInterpolator(DecelerateTension))
                .WithEndAction(new EndActionRunnable(() => tcs.TrySetResult()))
                .Start();
            return tcs.Task;
        }
#endif
        return v.TranslateTo(0, toDp, durationMs, Easing.CubicIn);
    }

    /// <summary>居中卡片弹入：缩放 0.92→1 + 淡入，OvershootInterpolator 带一丝系统弹性过冲。</summary>
    public static Task PopInAsync(Microsoft.Maui.Controls.VisualElement v, uint durationMs = 240)
    {
#if ANDROID
        if (v.Handler?.PlatformView is global::Android.Views.View native)
        {
            native.ScaleX = 0.92f;
            native.ScaleY = 0.92f;
            native.Alpha = 0f;
            var tcs = new TaskCompletionSource();
            native.Animate().ScaleX(1f).ScaleY(1f).Alpha(1f).SetDuration(durationMs)
                .SetInterpolator(new global::Android.Views.Animations.OvershootInterpolator(OvershootTension))
                .WithEndAction(new EndActionRunnable(() => tcs.TrySetResult()))
                .Start();
            return tcs.Task;
        }
#endif
        return Task.WhenAll(
            v.ScaleTo(1, durationMs, Easing.CubicOut),
            v.FadeTo(1, durationMs, Easing.CubicOut));
    }

    /// <summary>居中卡片弹出：缩放 0.94 + 淡出。</summary>
    public static Task PopOutAsync(Microsoft.Maui.Controls.VisualElement v, uint durationMs = 160)
    {
#if ANDROID
        if (v.Handler?.PlatformView is global::Android.Views.View native)
        {
            var tcs = new TaskCompletionSource();
            native.Animate().ScaleX(0.94f).ScaleY(0.94f).Alpha(0f).SetDuration(durationMs)
                .SetInterpolator(new global::Android.Views.Animations.DecelerateInterpolator(1f))
                .WithEndAction(new EndActionRunnable(() => tcs.TrySetResult()))
                .Start();
            return tcs.Task;
        }
#endif
        return Task.WhenAll(
            v.ScaleTo(0.94, durationMs, Easing.CubicIn),
            v.FadeTo(0, durationMs, Easing.CubicIn));
    }
}
