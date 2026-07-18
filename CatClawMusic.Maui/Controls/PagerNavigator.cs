using System.Collections.Generic;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 栈式原生分页导航容器：给 Shell 子页的「进出」也套上原生 ViewPager2 的丝滑水平滑动转场，
/// 替代 net10 上抽动的 Shell 默认 push/pop 动画。典型用法是作为某 hub 页（设置/音乐库）之上的
/// 覆盖层（overlay）：底层 hub 始终可见，二级页通过 <see cref="PushAsync"/> 从右侧原生滑入、
/// 返回键 <see cref="PopAsync"/> 原生滑出。容器本身不含任何平台类型，Windows 端也能编译
/// （无原生 handler 时退回简单显隐，转场动画由后续补充）。
/// <para>结构：Pages[0] 永远是一个透明占位页（代表底层 hub），真实二级页从索引 1 起入栈，
/// 因此首个二级页也能呈现「从 hub 滑出」的效果，而无需把 hub 抽成独立页。</para>
/// </summary>
public class PagerNavigator : View
{
    /// <summary>透明占位页（代表底层 hub），永远位于栈底（索引 0）。</summary>
    private readonly ContentPage _placeholder = new() { BackgroundColor = Colors.Transparent };

    /// <summary>已压入的页面栈（索引 0 = 透明占位；1.. 为真实二级页，末尾为当前可见页）。</summary>
    public IList<ContentPage> Pages { get; set; } = new List<ContentPage>();

    /// <summary>当前可见页索引（栈顶）。空栈时为 0（占位）。</summary>
    public int CurrentIndex { get; internal set; }

    /// <summary>栈内是否还有可弹出的真实二级页（Pages.Count &gt; 1 表示处于某二级页内）。</summary>
    public bool CanPop => Pages.Count > 1;

    /// <summary>页面被压入（滑入完成）后触发。</summary>
    public event EventHandler<ContentPage>? PagePushed;

    /// <summary>页面被弹出（滑出完成）后触发。</summary>
    public event EventHandler<ContentPage>? PagePopped;

    /// <summary>栈深度变化（用于 BackButton 全局拦截判断）。</summary>
    public event EventHandler? DepthChanged;

    // ── 由平台 handler 在连接时注入的命令式 API ──
    internal Action<ContentPage, bool>? PlatformPush { get; set; }
    internal Action<bool>? PlatformPop { get; set; }
    internal Action<int>? PlatformNotifyRemoved { get; set; }
    internal Action? PlatformClear { get; set; }

    private static PagerNavigator? _active;

    /// <summary>当前处于激活态（可见且有页面）的导航器，供全局 BackButton 拦截时使用。</summary>
    public static PagerNavigator? Active => _active;

    internal static void SetActive(PagerNavigator? nav) => _active = nav;

    /// <summary>若当前有激活的 overlay 导航器且仍可弹出真实二级页，则平滑滑出栈顶并返回 true；
    /// 否则返回 false（调用方应退回 Shell 默认返回）。供各二级页的自定义返回按钮统一拦截。</summary>
    public static bool TryPopOverlay()
    {
        if (Active is { CanPop: true } nav)
        {
            nav.PopAsync();
            return true;
        }
        return false;
    }

    public PagerNavigator()
    {
        // 预置透明占位页（栈底）
        Pages.Add(_placeholder);
        CurrentIndex = 0;
        // 初始隐藏：overlay 不拦截底层 hub（设置/音乐库）的 ScrollView 滚动与手势。
        // 走跨平台 IsVisible 让 MAUI 把 native 可见性映射为 Gone，避免手动设 Gone 被
        // ViewHandler 的 Visibility 属性映射覆盖回 Visible 而导致覆盖层吃掉滑动手势。
        IsVisible = false;
    }

    /// <summary>压入一个页面并以原生平滑滑动滑入（从当前页滑到新页）。</summary>
    /// <param name="page">目标二级页（需已通过 DI 构造好）。</param>
    /// <param name="animate">是否播放平滑滑动转场。</param>
    public void PushAsync(ContentPage page, bool animate = true)
    {
        Pages.Add(page);
        CurrentIndex = Pages.Count - 1;
        IsVisible = true;
        SetActive(this);
        DepthChanged?.Invoke(this, System.EventArgs.Empty);
        PlatformPush?.Invoke(page, animate);
    }

    /// <summary>弹出栈顶页面并以原生平滑滑动滑出。</summary>
    /// <param name="animate">是否播放平滑滑动转场。</param>
    public void PopAsync(bool animate = true)
    {
        if (Pages.Count <= 1) return; // 仅剩占位，无真实页可弹
        var removedIndex = Pages.Count - 1;
        PlatformPop?.Invoke(animate);
        // 实际移除与事件抛出由 handler 在滑出动画结束后回调 CompletePop 执行，
        // 以保证动画期间页面仍可见。
        _ = removedIndex;
    }

    /// <summary>由平台 handler 在滑入动画结束时调用：触发子页 OnAppearing 并激活自身。</summary>
    internal void NotifyPushed(ContentPage page)
    {
        // 手动触发子页生命周期（子页未被 Shell 导航，OnAppearing 不会自动触发）
        try { page.SendAppearing(); } catch { }
        SetActive(this);
        PagePushed?.Invoke(this, page);
    }

    /// <summary>由平台 handler 在滑出动画结束时调用：触发子页 OnDisappearing、移除栈顶；
    /// 若弹回后只剩占位页，则隐藏自身并清空激活态。</summary>
    internal void CompletePop()
    {
        if (Pages.Count <= 1) return;
        var removedIndex = Pages.Count - 1;
        var top = Pages[removedIndex];
        try { top.SendDisappearing(); } catch { }
        Pages.RemoveAt(removedIndex);
        CurrentIndex = Pages.Count - 1;
        PlatformNotifyRemoved?.Invoke(removedIndex);
        if (Pages.Count <= 1)
        {
            IsVisible = false;
            if (_active == this) SetActive(null);
        }
        DepthChanged?.Invoke(this, System.EventArgs.Empty);
        PagePopped?.Invoke(this, top);
    }
}
