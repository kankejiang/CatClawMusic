using Android.Views;
using Microsoft.Maui.Controls;
using AndroidX.RecyclerView.Widget;
using AView = Android.Views.View;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>
/// 横滑手势最基本的仲裁：让横向 CollectionView 在按下时禁止所有祖先拦截本次触摸，
/// 其余交给系统原生嵌套滚动自己协调。不替换容器类型、不禁用嵌套滚动、不做边界判断。
///
/// 适用场景：横向列表嵌套在纵向滚动容器（ScrollView / CollectionView / ViewPager2）内，
/// 祖先的 onInterceptTouchEvent 会在横向拖动时抢先拦截。按下即 requestDisallowInterceptTouchEvent(true)
/// 会沿 ViewParent 链上抛，令纵向外层与 ViewPager2 都不再拦截横向拖动；松手后释放。
/// 纵向外层继续只在纵向拦截并按原生嵌套滚动逐级协调，互不影响。
/// </summary>
public static class HorizontalSwipeHelper
{
    /// <summary>给横向 CollectionView 挂上最基本的横滑仲裁（需 Handler 就绪后调用，如 HandlerChanged/Loaded）。</summary>
    public static void Attach(CollectionView cv)
    {
        if (cv?.Handler?.PlatformView is not RecyclerView rv) return;
        if (!IsHorizontal(cv.ItemsLayout)) return;

        rv.SetOnTouchListener(new HorizontalSwipeTouchListener());
    }

    private static bool IsHorizontal(IItemsLayout layout) => layout switch
    {
        LinearItemsLayout l => l.Orientation == ItemsLayoutOrientation.Horizontal,
        GridItemsLayout g => g.Orientation == ItemsLayoutOrientation.Horizontal,
        _ => false
    };

    /// <summary>仅做 Disallow 仲裁，不消费事件：RecyclerView 继续原生处理滚动。</summary>
    private sealed class HorizontalSwipeTouchListener : Java.Lang.Object, AView.IOnTouchListener
    {
        private bool _disallowed;

        public bool OnTouch(AView? v, MotionEvent? e)
        {
            if (e == null) return false;

            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                    _disallowed = true;
                    v?.Parent?.RequestDisallowInterceptTouchEvent(true);
                    break;

                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    if (_disallowed)
                        v?.Parent?.RequestDisallowInterceptTouchEvent(false);
                    _disallowed = false;
                    break;
            }

            return false; // 不消费，横滑由 RecyclerView 原生滚动驱动
        }
    }
}