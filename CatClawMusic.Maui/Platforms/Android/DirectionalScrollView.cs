using Android.Content;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Microsoft.Maui.Platform;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>
/// 修复 MAUI Android 横向滚动嵌在纵向 ScrollView 内时无法左右滑动的问题
/// （如 DiscoveryPage 的 Hero 卡横向 ScrollView / 横向 CollectionView）。
///
/// 根因：外层纵向 ScrollView 在手势仲裁中抢先拦截了横向滑动，内层横向容器收不到
/// 事件。本类采用"外部拦截法"——纵向 ScrollView 仅当手势主导方向为纵向时才拦截
/// （交给自己纵向滚动），横向手势一律放行，交给内层 HeroScroll / 横向 CollectionView 处理。
///
/// 说明：
/// - 与 HorizontalSwipeHelper 的 RequestDisallowIntercept 互补（那个让横向列表按下即禁止祖先拦截），
///   本类挡的是页面级纵向 ScrollView，方向判断为标准外拦截法，对普通纵向滚动无副作用。
/// - <see cref="IsHorizontal"/> 为 true（该 ScrollView 自身是横向的）时不改变默认行为，
///   保证纯横向 ScrollView（如 HeroScroll）的滚动不被这套逻辑破坏。
/// </summary>
public class DirectionalScrollView : MauiScrollView
{
    private readonly int _touchSlop;
    private float _downX;
    private float _downY;

    /// <summary>该 ScrollView 是否为横向取向。true 时按原生行为处理，不做方向放行。</summary>
    public bool IsHorizontal { get; set; }

    public DirectionalScrollView() : this(global::Android.App.Application.Context)
    {
    }

    public DirectionalScrollView(Context context) : base(context)
    {
        _touchSlop = ViewConfiguration.Get(context).ScaledTouchSlop;
    }

    public DirectionalScrollView(Context context, IAttributeSet attrs) : base(context, attrs)
    {
        _touchSlop = ViewConfiguration.Get(context).ScaledTouchSlop;
    }

    protected DirectionalScrollView(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
        _touchSlop = ViewConfiguration.Get(global::Android.App.Application.Context).ScaledTouchSlop;
    }

    public override bool OnInterceptTouchEvent(MotionEvent? ev)
    {
        if (ev == null) return base.OnInterceptTouchEvent(ev);

        switch (ev.ActionMasked)
        {
            case MotionEventActions.Down:
                _downX = ev.RawX;
                _downY = ev.RawY;
                return base.OnInterceptTouchEvent(ev);

            case MotionEventActions.Move:
            {
                float dx = Math.Abs(ev.RawX - _downX);
                float dy = Math.Abs(ev.RawY - _downY);
                // 仅纵向 ScrollView 做方向仲裁；横向主导 → 放行给内层横向容器
                // （HeroScroll / 横向 CollectionView）。横向 ScrollView 自身保持原生行为。
                if (!IsHorizontal && dx > dy && dx > _touchSlop)
                    return false;
                break;
            }
        }

        return base.OnInterceptTouchEvent(ev);
    }
}

/// <summary>横向在纵向内无法滑动的 ScrollView handler（Android 专用）。</summary>
public class DirectionalScrollViewHandler : Microsoft.Maui.Handlers.ScrollViewHandler
{
    protected override MauiScrollView CreatePlatformView() => new DirectionalScrollView(MauiContext!.Context);

    // 方向在 XAML 布局阶段即已固定，视图连接时同步一次即可（原生 ScrollView 方向不会在运行期切换）
    protected override void ConnectHandler(MauiScrollView platformView)
    {
        base.ConnectHandler(platformView);
        if (platformView is DirectionalScrollView dsv)
            dsv.IsHorizontal = VirtualView.Orientation == ScrollOrientation.Horizontal;
    }
}