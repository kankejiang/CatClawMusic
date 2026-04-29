using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Models;

namespace CatClawMusic.UI.Adapters;

public class PlaylistAdapter : RecyclerView.Adapter
{
    private List<Playlist> _playlists = new();
    public event EventHandler<Playlist>? PlaylistClicked;

    public void UpdatePlaylists(IEnumerable<Playlist> playlists)
    {
        _playlists = playlists.ToList();
        NotifyDataSetChanged();
    }

    public override int ItemCount => _playlists.Count;

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var view = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_playlist, parent, false)!;
        return new VH(view, p => PlaylistClicked?.Invoke(this, _playlists[p]));
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        var vh = (VH)holder;
        var p = _playlists[position];
        vh.Name.Text = p.Name;
        vh.Count.Text = $"{p.SongCount} 首";
    }

    private class VH : RecyclerView.ViewHolder
    {
        public TextView Name, Count;
        public VH(View view, Action<int> onClick) : base(view)
        {
            Name = view.FindViewById<TextView>(Resource.Id.playlist_name)!;
            Count = view.FindViewById<TextView>(Resource.Id.playlist_count)!;
            view.Click += (s, e) => onClick(BindingAdapterPosition);
        }
    }
}
