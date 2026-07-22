using System.Collections.Generic;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 跨平台分页容器抽象：Android 端由 <see cref="Platforms.Android.NativeTabPagerHandler"/>
/// 用原生 AndroidX ViewPager2 承载各 MAUI 页（水平分页位移走 GPU 合成，根治 net10 抽搐）；
/// Windows 端不使用本控件，保留 MainPage 内手动 TranslationX 实现兜底。
/// 控件本身不含任何平台特定类型，仅持有一组待承载的 MAUI 页与选中/滚动状态事件。
/// </summary>
public class NativeTabPager : View
{
    /// <summary>滑动/选中状态（与 AndroidX ViewPager2 的 SCROLL_STATE_* 对应）。</summary>
    public enum ScrollState
    {
        /// <summary>空闲（无滑动，页面已稳定）。</summary>
        Idle = 0,
        /// <summary>用户正在拖拽。</summary>
        Dragging = 1,
        /// <summary>正在自动归位/吸附（松手后的动画阶段）。</summary>
        Settling = 2,
    }

    /// <summary>默认离屏预加载页数：仅相邻页常驻，远页回收，降低启动图片解码/GC 压力。</summary>
    private const int DefaultOffscreen = 1;
    private int _offscreenLimit = DefaultOffscreen;

    /// <summary>待承载的 MAUI 页（顺序即分页顺序）。</summary>
    public IList<ContentPage> Pages { get; set; } = new List<ContentPage>();

    /// <summary>当前选中页索引（由 handler 在 PageSelected 时回写）。</summary>
    public int CurrentItem { get; internal set; }

    /// <summary>选中页变化事件（用户滑动或程序化 SetCurrentItem 都会触发）。</summary>
    public event EventHandler<int>? PageSelected;

    /// <summary>滑动状态变化事件（用于暂停/恢复 FrostedBackground 动画）。</summary>
    public event EventHandler<ScrollState>? ScrollStateChanged;

    /// <summary>
    /// handler 在连接时注入：请求平台 ViewPager 切换到指定页。
    /// 参数为 (目标索引, 是否平滑滚动)。控件本身不直接操作原生视图。
    /// </summary>
    internal Action<int, bool>? PlatformSetCurrentItem { get; set; }

    /// <summary>handler 在连接时注入：设置离屏预加载页数（OffscreenPageLimit）。</summary>
    internal Action<int>? PlatformSetOffscreen { get; set; }

    /// <summary>
    /// 静态事件：请求启用/禁用用户滑动翻页。
    /// 横屏模式通过 RequestedOrientation 强制，不触发 DeviceDisplay.MainDisplayInfoChanged，
    /// 因此由触发横屏的代码直接调用 <see cref="SetSwipeEnabled"/> 通知 handler。
    /// </summary>
    public static event Action<bool>? RequestUserInputEnabled;

    /// <summary>请求启用/禁用用户左右滑动切换 tab（横屏进入时 false，退出时 true）。</summary>
    public static void SetSwipeEnabled(bool enabled) => RequestUserInputEnabled?.Invoke(enabled);

    /// <summary>切换到指定页（程序化调用，如点击 TabBar、返回键、启动页）。</summary>
    /// <param name="index">目标页索引（0 基）。</param>
    /// <param name="smooth">是否平滑滚动（false 用于初始定位）。</param>
    public void SetCurrentItem(int index, bool smooth)
    {
        if (index < 0 || index >= Pages.Count) return;
        CurrentItem = index;
        PlatformSetCurrentItem?.Invoke(index, smooth);
    }

    /// <summary>
    /// 增强切换：跨多页跳转时（|目标-当前|&gt;1）临时扩大 OffscreenPageLimit 以覆盖目标页，
    /// 保证切页动画途经页已布局、不闪白；动画结束（Idle）后由 NotifyScrollStateChanged 回收为默认值。
    /// 相邻切页（span≤1）直接切换，相邻页本就常驻，保持 net10 平滑。
    /// </summary>
    public void GoToItem(int index, bool smooth)
    {
        if (index < 0 || index >= Pages.Count) return;
        var from = CurrentItem;
        var span = Math.Abs(index - from);
        if (span > 1)
            SetOffscreenLimit(span + 1); // 临时扩大，覆盖目标页
        CurrentItem = index;
        PlatformSetCurrentItem?.Invoke(index, smooth);
    }

    /// <summary>设置离屏预加载页数（handler 连接后调用）。</summary>
    public void SetOffscreenLimit(int limit)
    {
        if (limit < 1) limit = 1;
        _offscreenLimit = limit;
        PlatformSetOffscreen?.Invoke(limit);
    }

    /// <summary>由 handler 在平台 PageSelected 回调时调用，回写当前索引并向上抛出事件。</summary>
    internal void NotifyPageSelected(int index)
    {
        CurrentItem = index;
        PageSelected?.Invoke(this, index);
    }

    /// <summary>由 handler 在平台滑动状态变化时调用，向上抛出事件。</summary>
    internal void NotifyScrollStateChanged(ScrollState state)
    {
        // 切页动画结束：若此前因跨页跳转临时扩大了离屏范围，回收为默认值，
        // 让远页被 ViewPager2 回收，降低常驻图片解码/GC 压力。
        if (state == ScrollState.Idle && _offscreenLimit > DefaultOffscreen)
            SetOffscreenLimit(DefaultOffscreen);
        ScrollStateChanged?.Invoke(this, state);
    }
}
