using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Models;

namespace CatClawMusic.UI.Adapters;

public class SongAdapter : RecyclerView.Adapter
{
    private List<Song> _songs = new();
    public event EventHandler<Song>? SongClicked;

    public void UpdateSongs(IEnumerable<Song> songs)
    {
        _songs = songs.ToList();
        NotifyDataSetChanged();
    }

    public override int ItemCount => _songs.Count;

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var view = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_song, parent, false)!;
        return new SongViewHolder(view, OnSongClick);
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        ((SongViewHolder)holder).Bind(_songs[position]);
    }

    private void OnSongClick(int position) => SongClicked?.Invoke(this, _songs[position]);

    private class SongViewHolder : RecyclerView.ViewHolder
    {
        private readonly TextView _title, _artist, _album;
        private readonly ImageView _cover;

        public SongViewHolder(View view, Action<int> onClick) : base(view)
        {
            _title = view.FindViewById<TextView>(Resource.Id.song_title)!;
            _artist = view.FindViewById<TextView>(Resource.Id.song_artist)!;
            _album = view.FindViewById<TextView>(Resource.Id.song_album)!;
            _cover = view.FindViewById<ImageView>(Resource.Id.song_cover)!;
            view.Click += (s, e) => onClick(BindingAdapterPosition);
        }

        public void Bind(Song song)
        {
            _title.Text = song.Title ?? "未知歌曲";
            _artist.Text = song.Artist ?? "未知艺术家";
            _album.Text = song.Album ?? "";
        }
    }
}
