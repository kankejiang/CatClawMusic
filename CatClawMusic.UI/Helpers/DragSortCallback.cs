using AndroidX.RecyclerView.Widget;

namespace CatClawMusic.UI.Helpers;

public class DragSortCallback : ItemTouchHelper.SimpleCallback
{
    private readonly RecyclerView.Adapter _adapter;
    private readonly Action<int, int> _onMove;
    private int _dragFrom = -1;
    private int _dragTo = -1;

    public DragSortCallback(RecyclerView.Adapter adapter, Action<int, int> onMove)
        : base(ItemTouchHelper.Up | ItemTouchHelper.Down, 0)
    {
        _adapter = adapter;
        _onMove = onMove;
    }

    public override bool OnMove(RecyclerView recyclerView, RecyclerView.ViewHolder viewHolder, RecyclerView.ViewHolder target)
    {
        int from = viewHolder.AdapterPosition;
        int to = target.AdapterPosition;
        if (from == -1 || to == -1 || from == to) return false;

        if (_dragFrom == -1)
        {
            _dragFrom = from;
        }
        _dragTo = to;

        _adapter.NotifyItemMoved(from, to);
        return true;
    }

    public override void OnSwiped(RecyclerView.ViewHolder viewHolder, int direction)
    {
    }

    public override bool IsLongPressDragEnabled => true;

    public override bool IsItemViewSwipeEnabled => false;

    public override void OnSelectedChanged(RecyclerView.ViewHolder? viewHolder, int actionState)
    {
        base.OnSelectedChanged(viewHolder, actionState);

        if (actionState == ItemTouchHelper.ActionStateDrag && viewHolder != null)
        {
            ApplyDragVisual(viewHolder, true);
        }
        else if (actionState == ItemTouchHelper.ActionStateIdle)
        {
            if (viewHolder != null)
            {
                ApplyDragVisual(viewHolder, false);
            }
        }
    }

    public override void ClearView(RecyclerView recyclerView, RecyclerView.ViewHolder viewHolder)
    {
        base.ClearView(recyclerView, viewHolder);
        ApplyDragVisual(viewHolder, false);

        if (_dragFrom != -1 && _dragTo != -1 && _dragFrom != _dragTo)
        {
            _onMove(_dragFrom, _dragTo);
        }

        _dragFrom = -1;
        _dragTo = -1;
    }

    private void ApplyDragVisual(RecyclerView.ViewHolder viewHolder, bool isDragging)
    {
        viewHolder.ItemView.ScaleX = isDragging ? 1.2f : 1f;
        viewHolder.ItemView.ScaleY = isDragging ? 1.2f : 1f;
        viewHolder.ItemView.Alpha = isDragging ? 0.8f : 1f;
        viewHolder.ItemView.Elevation = isDragging ? 16f : 4f;
    }
}
