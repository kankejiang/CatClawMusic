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
/// NativeTabPager 的 Android 实现：用原生 AndroidX ViewPager2 承载各 MAUI 页。
/// 水平分页位移由 Android 渲染管线 GPU 合成，不依赖 MAUI 的 TranslationX，
/// 从而在 net10 上彻底消除左右滑动切换 tab 的掉帧/抽搐。
/// </summary>
public class NativeTabPagerHandler : ViewHandler<NativeTabPager, ViewPager2>
{
    private MauiPagerAdapter? _adapter;
    private PageChangeCallback? _callback;

    public NativeTabPagerHandler() : base(PropertyMapper)
    {
    }

    /// <summary>属性映射：本控件无需在属性变化时同步平台视图（分页逻辑走命令式 API）。</summary>
    public static readonly IPropertyMapper<NativeTabPager, NativeTabPagerHandler> PropertyMapper =
        new PropertyMapper<NativeTabPager, NativeTabPagerHandler>(ViewHandler.ViewMapper);

    protected override ViewPager2 CreatePlatformView()
    {
        var vp = new ViewPager2(Context!)
        {
            LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent)
        };
        return vp;
    }

    protected override void ConnectHandler(ViewPager2 platformView)
    {
        base.ConnectHandler(platformView);

        _adapter = new MauiPagerAdapter(VirtualView.Pages, MauiContext!);
        platformView.Adapter = _adapter;

        // 离屏预加载默认仅相邻页常驻（1），远页由 ViewPager2 回收，降低启动图片解码/GC 压力；
        // 跨多页跳转时由 NativeTabPager.GoToItem 临时扩大、Idle 回收（懒加载兜底）。
        VirtualView.PlatformSetOffscreen = limit => platformView.OffscreenPageLimit = limit;
        VirtualView.SetOffscreenLimit(1);

        // 选中/滑动状态回调 -> 回写控件并向上抛出事件
        _callback = new PageChangeCallback(VirtualView);
        platformView.RegisterOnPageChangeCallback(_callback);

        // 命令式切换 API 注入
        VirtualView.PlatformSetCurrentItem = (index, smooth) =>
            platformView.SetCurrentItem(index, smooth);

        // 同步初始定位（MainPage 创建时已把 CurrentItem 设为启动页索引）
        platformView.SetCurrentItem(VirtualView.CurrentItem, false);
    }

    protected override void DisconnectHandler(ViewPager2 platformView)
    {
        if (_callback != null)
            platformView.UnregisterOnPageChangeCallback(_callback);
        _callback = null;
        _adapter = null;
        VirtualView.PlatformSetCurrentItem = null;
        VirtualView.PlatformSetOffscreen = null;
        platformView.Adapter = null;
        base.DisconnectHandler(platformView);
    }

    /// <summary>
    /// 承载 MAUI 页的 RecyclerView.Adapter。每个分页位置用独立 ViewHolder（GetItemViewType=position
    /// + HasStableIds），使 RecyclerView 不会跨位置回收，规避 MAUI 原生视图重绑定的状态风险。
    /// </summary>
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

        // 每页独立 view type：禁止 RecyclerView 跨位置复用 ViewHolder（避免 MAUI 原生视图错配）
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

            // 将完整 MAUI 页渲染为原生视图（Page 的 ContentViewGroup 自带 measure，
            // 因此即使其父不再是 MAUI 容器，也能被 ViewPager2 正确测量布局）。
            var native = page.ToPlatform(_mauiContext);

            // 若此前已挂到别的父容器（如被回收后重绑），先解除再重新加入
            if (native.Parent is ViewGroup oldParent)
                oldParent.RemoveView(native);

            frame.AddView(native, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));
        }

        public override void OnViewRecycled(Java.Lang.Object holderObj)
        {
            // 理论上不会触发（OffscreenPageLimit=全部 + 独立 view type），保留以防极端情况
            if (holderObj is PageViewHolder vh && vh.ItemView is ViewGroup g)
                g.RemoveAllViews();
        }

        private sealed class PageViewHolder : RecyclerView.ViewHolder
        {
            public PageViewHolder(AView itemView) : base(itemView)
            {
            }
        }
    }

    /// <summary>将 ViewPager2 的选中/滑动状态变化转发给 NativeTabPager 控件。</summary>
    private sealed class PageChangeCallback : ViewPager2.OnPageChangeCallback
    {
        private readonly NativeTabPager _pager;

        public PageChangeCallback(NativeTabPager pager) => _pager = pager;

        public override void OnPageSelected(int position) =>
            _pager.NotifyPageSelected(position);

        public override void OnPageScrollStateChanged(int state)
        {
            var s = state switch
            {
                1 => NativeTabPager.ScrollState.Dragging,
                2 => NativeTabPager.ScrollState.Settling,
                _ => NativeTabPager.ScrollState.Idle,
            };
            _pager.NotifyScrollStateChanged(s);
        }
    }
}
