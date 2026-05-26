using AndroidX.RecyclerView.Widget;

namespace CatClawMusic.UI.Helpers;

public class DragSortCallback : ItemTouchHelper.SimpleCallback
{
    private readonly RecyclerView.Adapter _adapter;
    private readonly Action<int, int> _onMove;

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
        _onMove(from, to);
        _adapter.NotifyItemMoved(from, to);
        return true;
    }

    public override void OnSwiped(RecyclerView.ViewHolder viewHolder, int direction)
    {
    }

    public override bool IsLongPressDragEnabled => true;

    public override bool IsItemViewSwipeEnabled => false;
}
