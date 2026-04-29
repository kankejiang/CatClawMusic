using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Models;

namespace CatClawMusic.UI.Adapters;

public class UpcomingSongAdapter : RecyclerView.Adapter
{
    private List<Song> _songs = new();

    public void UpdateSongs(IEnumerable<Song> songs)
    {
        _songs = songs.ToList();
        NotifyDataSetChanged();
    }

    public override int ItemCount => _songs.Count;

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var view = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_upcoming_song, parent, false)!;
        return new VH(view);
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        var vh = (VH)holder;
        var s = _songs[position];
        vh.Title.Text = s.Title;
        vh.Artist.Text = s.Artist;
    }

    private class VH : RecyclerView.ViewHolder
    {
        public TextView Title, Artist;
        public VH(View view) : base(view)
        {
            Title = view.FindViewById<TextView>(Resource.Id.upcoming_title)!;
            Artist = view.FindViewById<TextView>(Resource.Id.upcoming_artist)!;
        }
    }
}
