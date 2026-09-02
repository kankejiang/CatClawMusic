using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Helpers;

/// <summary>
/// 桌面布局内容区切换过渡（Android 横屏 DesktopMainPage.ContentArea / Windows DesktopBlankPage.MainArea 共用）。
/// 统一动画语言：
/// - 详情/沉浸页推入 = Material shared-axis X：新页从进入侧滑入 + 淡入，旧页向反方向 30% 距离视差滑出 + 淡出；
///   关闭时旧页滑回进入侧（幕布揭开）。方向约定：歌词页在左、播放页在右（与竖屏 ViewPager tab 顺序一致），
///   其余详情页一律从右推入（push 语义）。
/// - 顶级 tab 切换 = Material fade-through：旧内容快速淡出揭幕，新内容淡入 + 轻微上浮。
/// 所有动画只操作 TranslationX/Opacity/TranslationY 渲染变换（合成器层，不触发布局）；
/// 快速连点幂等（先 CancelAnimations 再设初值，收尾延迟移除对已移除内容为 no-op）。
/// </summary>
public static class DesktopTransitions
{
    /// <summary>推入/退场过渡时长（ms）。</summary>
    public const int PushMs = 300;

    /// <summary>tab fade-through 时长（ms）。</summary>
    public const int FadeThroughMs = 200;

    /// <summary>推入过渡（打开详情/沉浸页）：incoming 从 <paramref name="fromLeft"/> 侧滑入 + 淡入，
    /// outgoing 向反方向 30% 距离视差滑出 + 淡出。调用前 incoming/outgoing 都已在 container 中
    /// （incoming 在顶层），收尾自动移除 outgoing 并复位其变换。</summary>
    public static void PushSwap(Grid container, View incoming, View outgoing, bool fromLeft)
    {
        double w = container.Width;
        if (w <= 10) return;
        int sign = fromLeft ? -1 : 1;

        incoming.CancelAnimations();
        outgoing.CancelAnimations();
        incoming.TranslationX = sign * w;
        incoming.Opacity = 0;

        _ = incoming.TranslateTo(0, 0, PushMs, Easing.CubicInOut);
        _ = incoming.FadeTo(1, PushMs, Easing.CubicIn);
        _ = outgoing.TranslateTo(-sign * 0.3 * w, 0, PushMs, Easing.CubicOut);
        _ = outgoing.FadeTo(0, PushMs, Easing.CubicOut);

        RemoveAfterAsync(container, outgoing, PushMs);
    }

    /// <summary>推入页退场（关闭揭幕）：outgoing 滑回进入侧 + 淡出，露出容器底层已就位的内容；
    /// 收尾自动移除 outgoing 并复位其变换。</summary>
    public static void PushExit(Grid container, View outgoing, bool exitLeft)
    {
        double w = container.Width;
        if (w <= 10) return;
        int sign = exitLeft ? -1 : 1;

        outgoing.CancelAnimations();
        _ = outgoing.TranslateTo(sign * w, 0, PushMs, Easing.CubicIn);
        _ = outgoing.FadeTo(0, PushMs, Easing.CubicIn);

        RemoveAfterAsync(container, outgoing, PushMs);
    }

    /// <summary>顶级 tab fade-through：outgoing（可为 null，首次进入）快速淡出揭幕，
    /// incoming 淡入 + 12px 轻微上浮。outgoing 是被缓存复用的 tab 内容，收尾只复位变换不移除。</summary>
    public static void FadeThrough(Grid container, View incoming, View? outgoing)
    {
        if (container.Width <= 10) return;

        incoming.CancelAnimations();
        incoming.Opacity = 0;
        incoming.TranslationY = 12;
        _ = incoming.FadeTo(1, FadeThroughMs, Easing.CubicOut);
        _ = incoming.TranslateTo(0, 0, FadeThroughMs, Easing.CubicOut);

        if (outgoing == null) return;
        outgoing.CancelAnimations();
        _ = outgoing.FadeTo(0, FadeThroughMs / 2, Easing.CubicOut);
        _ = Task.Delay(FadeThroughMs / 2 + 40).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (container.Children.Contains(outgoing))
                container.Children.Remove(outgoing);
            outgoing.Opacity = 1;
            outgoing.TranslationY = 0;
        }));
    }

    /// <summary>过渡收尾：延迟移除已滑出的内容并复位变换，供下次复用（缓存的 tab 内容 / 复用页内容）。
    /// 快速连点时目标可能已被 Clear/重新加入——Contains 检查 + 复位为 no-op，均安全。</summary>
    private static void RemoveAfterAsync(Grid container, View outgoing, int ms)
    {
        _ = Task.Delay(ms + 60).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (container.Children.Contains(outgoing))
                container.Children.Remove(outgoing);
            outgoing.TranslationX = 0;
            outgoing.Opacity = 1;
        }));
    }
}
