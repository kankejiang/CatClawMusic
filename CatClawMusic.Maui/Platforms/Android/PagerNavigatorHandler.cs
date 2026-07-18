using System;
using System.Collections.Generic;
using Android.Content;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using AndroidX.ViewPager2.Widget;
using Microsoft.Maui.Controls;
using CatClawMusic.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.Maui;

// 类型别名：消解 Android.Views.View 与 MAUI Microsoft.Maui.Controls.View 的歧义
using AView = Android.Views.View;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>
/// PagerNavigator 的 Android 实现：用原生 AndroidX ViewPager2 承载压入的 MAUI 页，
/// 进出转场走 ViewPager2 的原生 GPU 合成水平滑动（与 tab 同一套机制），根治 net10 上
/// Shell 默认 push/pop 动画的抽动。栈式语义：Pages[0] 为透明占位（底层 hub），
/// push 追加真实二级页并平滑滑入，pop 平滑滑出后移除。
/// </summary>
public class PagerNavigatorHandler : ViewHandler<PagerNavigator, ViewPager2>
{
    private MauiPagerAdapter? _adapter;
    private PageChangeCallback? _callback;

    public PagerNavigatorHandler() : base(PropertyMapper)
    {
    }

    public static readonly IPropertyMapper<PagerNavigator, PagerNavigatorHandler> PropertyMapper =
        new PropertyMapper<PagerNavigator, PagerNavigatorHandler>(ViewHandler.ViewMapper)
        {
            // 覆盖默认 IsVisible 映射：false 必须映射为 native Gone（而非可能退化为 Invisible）。
            // Invisible 仍占据布局并吞掉底层 hub 的滚动/手势，会让设置页/音乐库页“无法上滑”
            // 或长内容下卡片被透明覆盖层挡住（内容不显示）。Gone 才彻底退出布局与命中测试。
            [nameof(Microsoft.Maui.Controls.View.IsVisible)] = MapIsVisible
        };

    private static void MapIsVisible(PagerNavigatorHandler handler, PagerNavigator pager)
    {
        if (handler.PlatformView is ViewPager2 vp)
            vp.Visibility = pager.IsVisible ? ViewStates.Visible : ViewStates.Gone;
    }

    protected override ViewPager2 CreatePlatformView()
    {
        var vp = new ViewPager2(Context!)
        {
            LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent),
            // 进出转场为水平滑动（与 tab 一致）
            Orientation = ViewPager2.OrientationHorizontal,
        };
        return vp;
    }

    protected override void ConnectHandler(ViewPager2 platformView)
    {
        base.ConnectHandler(platformView);

        _adapter = new MauiPagerAdapter(VirtualView.Pages, MauiContext!);
        platformView.Adapter = _adapter;

        // 全部常驻，规避 RecyclerView 回收 MAUI 原生视图带来的重绑定风险
        platformView.OffscreenPageLimit = Math.Max(1, VirtualView.Pages.Count);

        _callback = new PageChangeCallback(VirtualView);
        platformView.RegisterOnPageChangeCallback(_callback);

        VirtualView.PlatformPush = (page, animate) => PushPlatform(page, animate);
        VirtualView.PlatformPop = animate => PopPlatform(animate);
        VirtualView.PlatformNotifyRemoved = index => _adapter?.NotifyItemRemoved(index);
        VirtualView.PlatformClear = () => { VirtualView.Pages.Clear(); _adapter?.NotifyDataSetChanged(); };

        // 初始只有透明占位页，且跨平台 IsVisible 已为 false（见 PagerNavigator 构造）。
        // 兜底：订阅 IsVisible 变化并强制映射为 native Gone/Visible，避免任何运行时映射歧义
        // 导致覆盖层在 idle 时仍占据布局/拦截手势，从而遮挡底层 hub 内容。
        platformView.Visibility = ViewStates.Gone;
        VirtualView.PropertyChanged += OnVirtualPropertyChanged;
    }

    private void OnVirtualPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Microsoft.Maui.Controls.View.IsVisible) && PlatformView is { } pv)
            pv.Visibility = VirtualView.IsVisible ? ViewStates.Visible : ViewStates.Gone;
    }

    protected override void DisconnectHandler(ViewPager2 platformView)
    {
        if (PagerNavigator.Active == VirtualView)
            PagerNavigator.SetActive(null);
        if (_callback != null)
            platformView.UnregisterOnPageChangeCallback(_callback);
        _callback = null;
        VirtualView.PropertyChanged -= OnVirtualPropertyChanged;
        _adapter = null;
        VirtualView.PlatformPush = null;
        VirtualView.PlatformPop = null;
        VirtualView.PlatformNotifyRemoved = null;
        VirtualView.PlatformClear = null;
        platformView.Adapter = null;
        base.DisconnectHandler(platformView);
    }

    private void PushPlatform(ContentPage page, bool animate)
    {
        var position = VirtualView.Pages.Count - 1; // 末尾即新页（占位在 0，真实页从 1 起）
        _adapter!.NotifyItemInserted(position);
        PlatformView.OffscreenPageLimit = Math.Max(1, VirtualView.Pages.Count);
        // 平滑滑入到新页（从当前页滑到新页）
        PlatformView.SetCurrentItem(position, animate);
    }

    private void PopPlatform(bool animate)
    {
        if (VirtualView.Pages.Count <= 1)
        {
            VirtualView.CompletePop();
            return;
        }
        // 滑回上一页（末尾将在此次滑动结束后由回调移除）
        PlatformView.SetCurrentItem(VirtualView.Pages.Count - 2, animate);
    }

    /// <summary>承载 MAUI 页的 RecyclerView.Adapter（与 NativeTabPager 同款稳定 id 策略）。</summary>
    private sealed class MauiPagerAdapter : RecyclerView.Adapter
    {
        private readonly IList<ContentPage> _pages;
        private readonly IMauiContext _mauiContext;

        public MauiPagerAdapter(IList<ContentPage> pages, IMauiContext mauiContext)
        {
            _pages = pages;
            _mauiContext = mauiContext;
            HasStableIds = true;
        }

        public override int ItemCount => _pages.Count;
        public override long GetItemId(int position) => position;
        public override int GetItemViewType(int position) => position;

        public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            var frame = new FrameLayout(parent.Context!)
            {
                LayoutParameters = new ViewGroup.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.MatchParent)
            };
            return new PageViewHolder(frame);
        }

        public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
        {
            var frame = (FrameLayout)holder.ItemView;
            var page = _pages[position];
            var native = page.ToPlatform(_mauiContext);
            if (native.Parent is ViewGroup oldParent)
                oldParent.RemoveView(native);
            frame.AddView(native, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));
        }

        private sealed class PageViewHolder : RecyclerView.ViewHolder
        {
            public PageViewHolder(AView itemView) : base(itemView) { }
        }
    }

    /// <summary>
    /// 监听 ViewPager2 滑动：滑入到新页（位置==栈顶）时回调 NotifyPushed；滑回到上一页
    /// （位置==栈顶-1，且仍有真实页）时回调 CompletePop 移除栈顶。
    /// </summary>
    private sealed class PageChangeCallback : ViewPager2.OnPageChangeCallback
    {
        private readonly PagerNavigator _pager;
        private int _lastSelected = -1;

        public PageChangeCallback(PagerNavigator pager)
        {
            _pager = pager;
        }

        public override void OnPageSelected(int position)
        {
            if (position == _pager.Pages.Count - 1 && position != _lastSelected)
            {
                // push 完成：新页成为当前页
                var page = _pager.Pages[position];
                _pager.NotifyPushed(page);
            }
            else if (position == _pager.Pages.Count - 2 && _pager.Pages.Count >= 2)
            {
                // pop 完成：已滑回上一页，移除栈顶真实页。
                // 注：CompletePop 内部已通过 PlatformNotifyRemoved 通知适配器，此处勿重复 NotifyItemRemoved。
                _pager.CompletePop();
            }
            _lastSelected = position;
        }
    }
}
