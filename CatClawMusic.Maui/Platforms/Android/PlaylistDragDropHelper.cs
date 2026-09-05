using Android.Graphics;
using Android.Views;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Models;
using Microsoft.Maui.Controls;
using AView = Android.Views.View;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>
/// 歌单列表长按手势增强（Android）：
/// - 长按（约 400ms 未移动）→ 触觉反馈并进入 <see cref="ItemTouchHelper"/> 拖拽排序，
///   上下拖动实时让位，松手按最终顺序回调页面持久化；
/// - 长按后未拖动直接松手 → 无动作（重命名/删除菜单由行尾 ⋮ 按钮触发，长按不承担菜单职责）；
/// - 普通点击与滚动不受影响（监听器永不消费触摸事件）。
///
/// 实现要点：
/// - 不能给行挂原生 LongClick：行变 long-clickable 后会在 ACTION_DOWN 消费触摸，
///   导致 CollectionView(MauiRecyclerView) 的点击/滚动失效（与 SongContextMenuBehavior 同因）；
///   因此用「不拦截的 OnItemTouchListener + 按下定时器」自行判定长按；
/// - 拖拽确认后对目标 ViewHolder 调 StartDrag：ItemTouchHelper 会拦截后续触摸流，
///   行收不到 UP 也就不会误触发列表选中导航；
/// - 行位置 ↔ 数据映射用「适配器位置 → ItemsSource 索引」（本列表无 header/footer/EmptyView，
///   位置一一对应），避免从原生视图反查 MAUI BindingContext 的脆弱链路；
/// - 让位实现为就地改动 ObservableCollection（RemoveAt + Insert），MAUI 会同步通知适配器，
///   这是 MAUI CollectionView 拖拽重排的标准做法。
/// </summary>
public static class PlaylistDragDropHelper
{
    // 已挂接的 RecyclerView 注册表（幂等防重复：C# 绑定无 GetOnTouchListener 可查询）
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RecyclerView, PlaylistTouchTracker> Attached =
        new();

    /// <summary>
    /// 给歌单 CollectionView 挂长按拖拽排序（需 Handler 就绪后调用，如 HandlerChanged；幂等）。
    /// </summary>
    /// <param name="cv">歌单列表（ItemsSource 为 <paramref name="items"/>）</param>
    /// <param name="items">与 ItemsSource 相同引用的 ObservableCollection&lt;Playlist&gt;（就地重排）</param>
    /// <param name="onLongPressArmed">长按确认（即将进入拖拽）：页面借此抑制随后的误触选中</param>
    /// <param name="onOrderChanged">拖拽结束且发生位移：页面按最终顺序持久化</param>
    public static void Attach(CollectionView cv,
        System.Collections.IList items,
        Action? onLongPressArmed,
        Action onOrderChanged)
    {
        if (cv?.Handler?.PlatformView is not RecyclerView rv) return;
        if (Attached.TryGetValue(rv, out _)) return; // 防重复挂接

        var tracker = new PlaylistTouchTracker(rv, items, onLongPressArmed, onOrderChanged);
        rv.AddOnItemTouchListener(tracker);

        var callback = new PlaylistDragCallback(tracker);
        tracker.DragHelper = new ItemTouchHelper(callback);
        tracker.DragHelper.AttachToRecyclerView(rv);
        Attached.Add(rv, tracker);
    }

    // ═══════════ 长按判定 ═══════════

    /// <summary>
    /// 不拦截的 OnItemTouchListener：RecyclerView 会在每个事件派发前先喂给本监听器
    /// （含被子视图消费的触摸，与 ItemTouchHelper 同款机制），因此可观察 DOWN→MOVE→UP 全流
    /// 而不消费任何事件（点击/滚动照常）。超时未移动判定为长按并启动拖拽。
    /// 注意：不能用 SetOnTouchListener——它只收到未被行消费的触摸，行内按下永远检测不到。
    /// </summary>
    private sealed class PlaylistTouchTracker : Java.Lang.Object, RecyclerView.IOnItemTouchListener
    {
        private readonly RecyclerView _rv;
        private readonly System.Collections.IList _items;
        private readonly Action? _onLongPressArmed;
        private readonly Action _onOrderChanged;
        private readonly int _touchSlop;
        private readonly int _longPressTimeout;

        private CancellationTokenSource? _cts;
        private bool _isDown;
        private bool _dragging;       // 长按确认后进入待拖拽/拖拽态
        private bool _dragEngaged;    // ItemTouchHelper 真正接管（OnSelectedChanged 回调确认）
        private bool _hadMove;
        private float _downRawX, _downRawY;     // 屏幕坐标（跨视图稳定）
        private float _downLocalX, _downLocalY; // RecyclerView 本地坐标（FindChildViewUnder 用）
        private Playlist? _draggedPlaylist;

        internal ItemTouchHelper? DragHelper { get; set; }

        internal System.Collections.IList Items => _items;

        internal PlaylistTouchTracker(RecyclerView rv,
            System.Collections.IList items,
            Action? onLongPressArmed,
            Action onOrderChanged)
        {
            _rv = rv;
            _items = items;
            _onLongPressArmed = onLongPressArmed;
            _onOrderChanged = onOrderChanged;
            _touchSlop = ViewConfiguration.Get(rv.Context)?.ScaledTouchSlop ?? 24;
            _longPressTimeout = ViewConfiguration.LongPressTimeout;
        }

        public bool OnInterceptTouchEvent(RecyclerView rv, MotionEvent e)
        {
            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                    {
                        _isDown = true;
                        _downRawX = e.RawX;
                        _downRawY = e.RawY;
                        var loc = new int[2];
                        rv.GetLocationOnScreen(loc);
                        _downLocalX = e.RawX - loc[0];
                        _downLocalY = e.RawY - loc[1];
                        StartTimer();
                        break;
                    }

                case MotionEventActions.Move:
                    // 未到长按时限就大幅移动 → 是滚动，取消判定（拖拽态由 ItemTouchHelper 接管，此处不管）
                    if (_isDown &&
                        (Math.Abs(e.RawX - _downRawX) > _touchSlop * 2 ||
                         Math.Abs(e.RawY - _downRawY) > _touchSlop * 2))
                    {
                        CancelTimer();
                        _isDown = false;
                    }
                    break;

                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    CancelTimer();
                    _isDown = false;
                    // 长按成立但 ItemTouchHelper 未接管（StartDrag 静默失败）：复位状态即可。
                    // 无菜单兜底（菜单职责归 ⋮ 按钮）；本次手势仍按普通点击处理，由选中抑制窗防误导航
                    if (_dragging)
                    {
                        _dragging = false;
                        _dragEngaged = false;
                        _draggedPlaylist = null;
                    }
                    break;
            }

            return false; // 永不拦截：点击、滚动全部照常
        }

        public void OnTouchEvent(RecyclerView rv, MotionEvent e)
        {
            // 拖拽被 ItemTouchHelper 接管后的事件流走这里，长按判定已结束，无需处理
        }

        public void OnRequestDisallowInterceptTouchEvent(bool disallowIntercept)
        {
            // 行内控件（如有）的 disallow 请求不涉及本监听器的状态
        }

        private void StartTimer()
        {
            CancelTimer();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Delay(_longPressTimeout, token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                MainThread.BeginInvokeOnMainThread(OnLongPressed);
            }, TaskScheduler.Default);
        }

        private void CancelTimer()
        {
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            _cts = null;
        }

        /// <summary>长按确认：触觉反馈 + 对按下位置的歌单启动拖拽。菜单职责已归 ⋮ 按钮，无兜底弹窗。</summary>
        private void OnLongPressed()
        {
            if (!_isDown || _dragging) return;
            _isDown = false;

            var playlist = FindItemAt(_downLocalX, _downLocalY);
            if (playlist is not { IsSystem: false }) return; // 系统歌单不可拖（长按无动作）

            var row = _rv.FindChildViewUnder(_downLocalX, _downLocalY);

            try { _rv.PerformHapticFeedback(FeedbackConstants.LongPress); } catch { }

            // 对行容器（ItemContentView，已 clickable）执行 PerformLongClick：
            // 内部置位 mHasPerformedLongPress，抑制长按后松手误触发的列表点击导航
            // （与 SongContextMenuBehavior 同款抑制手法）
            try { row?.PerformLongClick(); } catch { }

            try
            {
                var holder = row != null ? _rv.GetChildViewHolder(row) : null;
                if (holder == null || DragHelper == null) return;

                _onLongPressArmed?.Invoke();
                _draggedPlaylist = playlist;
                _hadMove = false;
                _dragging = true;
                DragHelper.StartDrag(holder); // C# 绑定返回 void；接管失败由 UP 分支复位状态
            }
            catch
            {
                _dragging = false;
                _dragEngaged = false;
                _draggedPlaylist = null;
            }
        }

        /// <summary>按下位置对应的歌单（适配器位置 → ItemsSource 索引，列表无 header/footer，一一对应）。</summary>
        private Playlist? FindItemAt(float localX, float localY)
        {
            try
            {
                var row = _rv.FindChildViewUnder(localX, localY);
                if (row == null) return null;
                var pos = _rv.GetChildAdapterPosition(row);
                if (pos < 0 || pos >= _items.Count) return null;
                return _items[pos] as Playlist;
            }
            catch
            {
                return null;
            }
        }

        internal void MarkMoved() => _hadMove = true;

        internal void MarkEngaged() => _dragEngaged = true;

        /// <summary>拖拽结束（ItemTouchHelper 回到 Idle）：有位移则提交顺序，无位移无动作。</summary>
        internal void NotifyDragSettled()
        {
            if (!_dragging && !_dragEngaged) return;
            _dragging = false;
            _dragEngaged = false;

            _draggedPlaylist = null;

            if (_hadMove)
            {
                try { _onOrderChanged(); } catch { }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) CancelTimer();
            base.Dispose(disposing);
        }
    }

    // ═══════════ 拖拽排序 ═══════════

    /// <summary>
    /// ItemTouchHelper.Callback：上下拖动歌单行，就地重排 ObservableCollection 保持 UI 与数据同序；
    /// 拖拽结束（Idle）回调 tracker 收尾。长按启动由 <see cref="PlaylistTouchTracker"/> 接管。
    /// </summary>
    private sealed class PlaylistDragCallback : ItemTouchHelper.Callback
    {
        private readonly PlaylistTouchTracker _tracker;

        internal PlaylistDragCallback(PlaylistTouchTracker tracker) => _tracker = tracker;

        public override bool IsLongPressDragEnabled => false;

        public override bool IsItemViewSwipeEnabled => false;

        public override int GetMovementFlags(RecyclerView recyclerView, RecyclerView.ViewHolder viewHolder)
        {
            var pos = recyclerView.GetChildAdapterPosition(viewHolder.ItemView);
            if (pos < 0 || pos >= _tracker.Items.Count) return MakeMovementFlags(0, 0);
            if (_tracker.Items[pos] is Playlist { IsSystem: true }) return MakeMovementFlags(0, 0);

            return MakeMovementFlags(ItemTouchHelper.Up | ItemTouchHelper.Down, 0);
        }

        public override bool OnMove(RecyclerView recyclerView, RecyclerView.ViewHolder viewHolder, RecyclerView.ViewHolder target)
        {
            var from = viewHolder.BindingAdapterPosition;
            var to = target.BindingAdapterPosition;
            if (from < 0 || to < 0 || from == to) return false;
            if (from >= _tracker.Items.Count || to >= _tracker.Items.Count) return false;

            try
            {
                var item = _tracker.Items[from]!;
                _tracker.Items.RemoveAt(from);
                _tracker.Items.Insert(to, item);
                _tracker.MarkMoved();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public override void OnSwiped(RecyclerView.ViewHolder viewHolder, int direction) { }

        public override void OnSelectedChanged(RecyclerView.ViewHolder? viewHolder, int actionState)
        {
            base.OnSelectedChanged(viewHolder, actionState);
            if (actionState == ItemTouchHelper.ActionStateDrag)
                _tracker.MarkEngaged();
            else if (actionState == ItemTouchHelper.ActionStateIdle)
                _tracker.NotifyDragSettled();
        }

        public override void ClearView(RecyclerView recyclerView, RecyclerView.ViewHolder viewHolder)
        {
            base.ClearView(recyclerView, viewHolder);
            viewHolder.ItemView.Alpha = 1f;
            viewHolder.ItemView.ScaleX = 1f;
            viewHolder.ItemView.ScaleY = 1f;
            viewHolder.ItemView.Elevation = 0;
        }

        public override void OnChildDraw(Canvas c, RecyclerView recyclerView, RecyclerView.ViewHolder viewHolder,
            float dX, float dY, int actionState, bool isCurrentlyActive)
        {
            base.OnChildDraw(c, recyclerView, viewHolder, dX, dY, actionState, isCurrentlyActive);

            if (actionState == ItemTouchHelper.ActionStateDrag && isCurrentlyActive)
            {
                // 拖拽中的浮层反馈：轻微放大抬升
                viewHolder.ItemView.Alpha = 0.94f;
                viewHolder.ItemView.ScaleX = 1.02f;
                viewHolder.ItemView.ScaleY = 1.02f;
                viewHolder.ItemView.Elevation = 12;
            }
        }
    }
}
