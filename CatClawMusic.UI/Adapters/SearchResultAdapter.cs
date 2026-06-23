using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Adapters;

public class SearchResultAdapter : RecyclerView.Adapter
{
    private readonly List<Song> _songs = new();
    public event EventHandler<Song>? OnSongPlay;

    public IReadOnlyList<Song> Songs => _songs.AsReadOnly();

    public void SetSongs(List<Song> songs)
    {
        _songs.Clear();
        _songs.AddRange(songs);
        NotifyDataSetChanged();
    }

    public void Clear()
    {
        _songs.Clear();
        NotifyDataSetChanged();
    }

    public override int ItemCount => _songs.Count;

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is SearchResultViewHolder vh)
        {
            if (position >= _songs.Count) return;
            vh.Bind(_songs[position]);
        }
    }

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var view = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_search_result, parent, false)!;
        return new SearchResultViewHolder(view, song => OnSongPlay?.Invoke(this, song));
    }
}

public class SearchResultViewHolder : RecyclerView.ViewHolder
{
    private readonly ImageView _imgCover;
    private readonly TextView _tvTitle;
    private readonly TextView _tvArtist;
    private readonly ImageButton _btnPlay;
    private Song? _currentSong;

    public SearchResultViewHolder(View view, Action<Song> onPlay) : base(view)
    {
        _imgCover = view.FindViewById<ImageView>(Resource.Id.img_cover)!;
        _tvTitle = view.FindViewById<TextView>(Resource.Id.tv_title)!;
        _tvArtist = view.FindViewById<TextView>(Resource.Id.tv_artist)!;
        _btnPlay = view.FindViewById<ImageButton>(Resource.Id.btn_play)!;

        _btnPlay.Click += (s, e) =>
        {
            if (_currentSong != null)
                onPlay(_currentSong);
        };

        view.Click += (s, e) =>
        {
            if (_currentSong != null)
                onPlay(_currentSong);
        };
    }

    public void Bind(Song song)
    {
        _currentSong = song;
        _tvTitle.Text = song.Title ?? "未知标题";
        _tvArtist.Text = song.Artist ?? "未知艺术家";
        // 这里可以添加加载封面的逻辑
    }
}
