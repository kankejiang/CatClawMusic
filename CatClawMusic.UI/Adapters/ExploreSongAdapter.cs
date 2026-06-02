using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.UI.Platforms.Android;

namespace CatClawMusic.UI.Adapters;

/// <summary>探索页歌曲列表适配器（每日推荐/最多播放/最新音乐）</summary>
public class ExploreSongAdapter : RecyclerView.Adapter
{
    private List<Song> _songs = new();

    /// <summary>是否显示播放次数</summary>
    public bool ShowPlayCount { get; set; }

    /// <summary>是否显示全部播放按钮（HeaderView）</summary>
    public bool ShowPlayAllButton { get; set; }

    public event EventHandler<Song>? OnSongClick;

    /// <summary>全部播放按钮点击事件</summary>
    public event EventHandler? OnPlayAllClick;

    public void UpdateSongs(List<Song> songs)
    {
        _songs = songs;
        NotifyDataSetChanged();
    }

    private const int ViewTypeHeader = 0;
    private const int ViewTypeSong = 1;

    public override int ItemCount => _songs.Count + (ShowPlayAllButton ? 1 : 0);

    public override int GetItemViewType(int position)
        => ShowPlayAllButton && position == 0 ? ViewTypeHeader : ViewTypeSong;

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        if (viewType == ViewTypeHeader)
        {
            var btn = new TextView(parent.Context!)
            {
                Text = "▶ 全部播放",
                TextSize = 14,
                Gravity = Android.Views.GravityFlags.CenterVertical
            };
            btn.SetPadding(32, 24, 32, 24);
            btn.SetTextColor(Android.Graphics.Color.ParseColor("#8B5CF6"));
            btn.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
            btn.Clickable = true;
            btn.Focusable = true;
            try
            {
                var outValue = new Android.Util.TypedValue();
                parent.Context!.Theme.ResolveAttribute(Android.Resource.Attribute.SelectableItemBackground, outValue, true);
                btn.SetBackgroundResource(outValue.ResourceId);
            }
            catch { }
            return new HeaderViewHolder(btn);
        }

        var view = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_explore_song, parent, false)!;
        return new ExploreSongViewHolder(view);
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is HeaderViewHolder hvh)
        {
            hvh.ItemView.Click -= (_, _) => OnPlayAllClick?.Invoke(this, EventArgs.Empty);
            hvh.ItemView.Click += (_, _) => OnPlayAllClick?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (holder is ExploreSongViewHolder vh)
        {
            var songIndex = ShowPlayAllButton ? position - 1 : position;
            var song = _songs[songIndex];
            vh.Bind(song, ShowPlayCount);
            vh.ItemView.Click -= vh.OnClick;
            vh.ItemView.Click += vh.OnClick;
            vh.SetSong(song, OnSongClick);
        }
    }
}

public class HeaderViewHolder : RecyclerView.ViewHolder
{
    public HeaderViewHolder(View view) : base(view) { }
}

public class ExploreSongViewHolder : RecyclerView.ViewHolder
{
    private readonly ImageView _cover;
    private readonly TextView _title;
    private readonly TextView _artist;
    private readonly TextView _playCount;
    private Song? _currentSong;
    private EventHandler<Song>? _clickHandler;
    private int _loadingSongId; // 当前正在加载封面的歌曲ID，防止复用错乱

    public ExploreSongViewHolder(View view) : base(view)
    {
        _cover = view.FindViewById<ImageView>(Resource.Id.song_cover)!;
        _title = view.FindViewById<TextView>(Resource.Id.song_title)!;
        _artist = view.FindViewById<TextView>(Resource.Id.song_artist)!;
        _playCount = view.FindViewById<TextView>(Resource.Id.tv_play_count)!;
    }

    public void SetSong(Song song, EventHandler<Song>? clickHandler)
    {
        _currentSong = song;
        _clickHandler = clickHandler;
    }

    public void OnClick(object? sender, EventArgs e)
    {
        if (_currentSong != null && _clickHandler != null)
            _clickHandler(this, _currentSong);
    }

    public void Bind(Song song, bool showPlayCount)
    {
        _title.Text = song.Title ?? "未知标题";
        _artist.Text = song.Artist ?? "未知艺术家";

        if (showPlayCount && song.PlayCount > 0)
        {
            _playCount.Text = $"{song.PlayCount}次";
            _playCount.Visibility = ViewStates.Visible;
        }
        else
        {
            _playCount.Visibility = ViewStates.Gone;
        }

        LoadCoverAsync(song);
    }

    private async void LoadCoverAsync(Song song)
    {
        _loadingSongId = song.Id;
        _cover.SetImageResource(Resource.Drawable.ic_music_note);

        // 先快速检查磁盘缓存（轻量操作）
        try
        {
            var coverPath = GetCoverCachedPath(song.Id);
            if (System.IO.File.Exists(coverPath))
            {
                _cover.SetImageBitmap(Android.Graphics.BitmapFactory.DecodeFile(coverPath));
                return;
            }
        }
        catch { }

        try
        {
            if (!string.IsNullOrEmpty(song.CoverArtPath) && System.IO.File.Exists(song.CoverArtPath))
            {
                _cover.SetImageBitmap(Android.Graphics.BitmapFactory.DecodeFile(song.CoverArtPath));
                return;
            }
        }
        catch { }

        // 重操作放到后台线程
        var songId = song.Id;
        var bitmap = await Task.Run(() => LoadCoverBackground(song));

        // 检查是否已被复用
        if (_loadingSongId != songId) return;

        if (bitmap != null)
        {
            try { _cover.SetImageBitmap(bitmap); } catch { }
        }
    }

    private static Android.Graphics.Bitmap? LoadCoverBackground(Song song)
    {
        // 1. 通过 MediaStore 加载
        try
        {
            if (song.MediaStoreId > 0)
            {
                var bitmap = MediaStoreCoverHelper.LoadCoverFromMediaStore(song.MediaStoreId, 120);
                if (bitmap != null) return bitmap;
            }
        }
        catch { }

        // 2. 从文件提取嵌入封面
        try
        {
            if (!string.IsNullOrEmpty(song.FilePath) && song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                var retriever = new Android.Media.MediaMetadataRetriever();
                try
                {
                    retriever.SetDataSource(Android.App.Application.Context,
                        Android.Net.Uri.Parse(song.FilePath));
                    var embedded = retriever.GetEmbeddedPicture();
                    if (embedded != null && embedded.Length > 0)
                    {
                        return Android.Graphics.BitmapFactory.DecodeByteArray(embedded, 0, embedded.Length);
                    }
                }
                finally { retriever.Release(); }
            }
            else if (!string.IsNullOrEmpty(song.FilePath) && System.IO.File.Exists(song.FilePath))
            {
                var coverBytes = TagReader.ExtractCoverArt(song.FilePath);
                if (coverBytes != null && coverBytes.Length > 0)
                {
                    return Android.Graphics.BitmapFactory.DecodeByteArray(coverBytes, 0, coverBytes.Length);
                }
            }
        }
        catch { }

        return null;
    }

    private static string GetCoverCachedPath(int songId)
    {
        var cacheDir = System.IO.Path.Combine(
            Android.App.Application.Context.CacheDir!.AbsolutePath, "covers");
        return System.IO.Path.Combine(cacheDir, $"cover_{songId}.jpg");
    }
}
