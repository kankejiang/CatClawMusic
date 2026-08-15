using Android.Views;
using Microsoft.Maui.Controls;
using AView = Android.Views.View;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>
/// ViewPager2 与内层横向列表的手势仲裁。
/// 竖屏 5 个 tab 由 ViewPager2 承载（NativeTabPagerHandler），发现页内部有多个横向
/// CollectionView（Hero 卡 / AI 歌单 / 推荐专辑 2 行 / 每日推荐 / 推荐艺人）——横向滑动
/// 卡片时 ViewPager2 会抢手势切 tab。标准解法：列表真正水平滚动时
/// requestDisallowInterceptTouchEvent(true) 让外层分页让路；未横向滚动或列表
/// 不可滚时（CanScrollHorizontally=false）不抢，空白处滑动仍可正常切 tab。
/// </summary>
public static class ViewPagerGestureHelper
{
    /// <summary>给横向 CollectionView 挂载手势仲裁（需在控件 Handler 就绪后调用，如 HandlerChanged）</summary>
    public static void Attach(CollectionView cv)
    {
        if (cv?.Handler?.PlatformView is not AndroidX.RecyclerView.Widget.RecyclerView rv) return;

        // 仅横向布局需要仲裁：纵向列表滑动方向与外层 ViewPager2 正交，无冲突
        bool horizontal = cv.ItemsLayout switch
        {
            LinearItemsLayout l => l.Orientation == ItemsLayoutOrientation.Horizontal,
            GridItemsLayout g => g.Orientation == ItemsLayoutOrientation.Horizontal,
            _ => false
        };
        if (!horizontal) return;

        rv.SetOnTouchListener(new PagerTouchListener(rv));
        Log.Debug("ViewPagerGesture", $"[PagerGuard] Attached to {cv.GetType().Name} (rv={rv.GetHashCode()})");
    }

    private sealed class PagerTouchListener : Java.Lang.Object, AView.IOnTouchListener
    {
        private readonly AndroidX.RecyclerView.Widget.RecyclerView _rv;
        private readonly int _touchSlop;
        private float _downX;
        private float _downY;
        private bool _disallowed;

        public PagerTouchListener(AndroidX.RecyclerView.Widget.RecyclerView rv)
        {
            _rv = rv;
            _touchSlop = ViewConfiguration.Get(rv.Context!).ScaledTouchSlop;
        }

        public bool OnTouch(AView? v, MotionEvent? e)
        {
            if (e == null) return false;
            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                    _downX = e.RawX;
                    _downY = e.RawY;
                    _disallowed = true;
                    v?.Parent?.RequestDisallowInterceptTouchEvent(true);
                    Log.Debug("ViewPagerGesture", $"[PagerGuard] DOWN (rv={_rv.GetHashCode()}, canH={_rv.CanScrollHorizontally(0)}) disallow=TRUE");
                    break;

                case MotionEventActions.Move:
                    if (!_disallowed) break;
                    float dx = Math.Abs(e.RawX - _downX);
                    float dy = Math.Abs(e.RawY - _downY);
                    int dir = (int)(_downX - e.RawX);
                    bool canScroll = _rv.CanScrollHorizontally(dir);
                    // 水平趋势且列表在该方向已无法继续滚动（滑到最左/最右）→ 放行给
                    // ViewPager2 切 tab；未到边界则保持卡片滚动。
                    if (dx > dy && dx > _touchSlop && !canScroll)
                    {
                        v?.Parent?.RequestDisallowInterceptTouchEvent(false);
                        _disallowed = false;
                        Log.Debug("ViewPagerGesture", $"[PagerGuard] MOVE 放行: dx={dx:F0} dy={dy:F0} dir={dir} canScroll={canScroll}");
                    }
                    break;

                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    if (_disallowed)
                        v?.Parent?.RequestDisallowInterceptTouchEvent(false);
                    _disallowed = false;
                    break;
            }
            return false; // 不消费事件，RecyclerView 自身继续正常处理滚动
        }
    }
}
