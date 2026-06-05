using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.UI.Platforms.Android;
using System.Collections.Concurrent;

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

    /// <summary>封面内存缓存，避免重复解码 Bitmap</summary>
    private static readonly ConcurrentDictionary<int, Android.Graphics.Bitmap?> _coverCache = new();

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
            vh.Bind(song, ShowPlayCount, _coverCache);
            vh.ItemView.Click -= vh.OnClick;
            vh.ItemView.Click += vh.OnClick;
            vh.SetSong(song, OnSongClick);
        }
    }

    public override void OnViewRecycled(Java.Lang.Object holder)
    {
        if (holder is ExploreSongViewHolder vh)
            vh.CancelLoad();
        base.OnViewRecycled(holder);
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
    private int _loadingSongId;
    private CancellationTokenSource? _cts;

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

    public void Bind(Song song, bool showPlayCount, ConcurrentDictionary<int, Android.Graphics.Bitmap?> coverCache)
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

        LoadCoverAsync(song, coverCache);
    }

    private async void LoadCoverAsync(Song song, ConcurrentDictionary<int, Android.Graphics.Bitmap?> coverCache)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        _loadingSongId = song.Id;

        if (coverCache.TryGetValue(song.Id, out var cached) && cached != null)
        {
            try { _cover.SetImageBitmap(cached); } catch { }
            return;
        }

        _cover.SetImageResource(Resource.Drawable.ic_music_note);

        var songId = song.Id;
        Android.Graphics.Bitmap? bitmap = null;

        try
        {
            bitmap = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var coverPath = GetCoverCachedPath(song.Id);
                    if (System.IO.File.Exists(coverPath))
                    {
                        var b = DecodeFileWithSize(coverPath, 200);
                        if (b != null) return b;
                    }
                }
                catch { }

                ct.ThrowIfCancellationRequested();

                try
                {
                    if (!string.IsNullOrEmpty(song.CoverArtPath) && System.IO.File.Exists(song.CoverArtPath))
                    {
                        var b = DecodeFileWithSize(song.CoverArtPath, 200);
                        if (b != null) return b;
                    }
                }
                catch { }

                ct.ThrowIfCancellationRequested();

                try
                {
                    if (song.MediaStoreId > 0)
                    {
                        var b = MediaStoreCoverHelper.LoadCoverFromMediaStore(song.MediaStoreId, 120);
                        if (b != null) return b;
                    }
                }
                catch { }

                ct.ThrowIfCancellationRequested();

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
                                return DecodeBytesWithSize(embedded, 200);
                        }
                        finally { retriever.Release(); }
                    }
                    else if (!string.IsNullOrEmpty(song.FilePath) && System.IO.File.Exists(song.FilePath))
                    {
                        var coverBytes = TagReader.ExtractCoverArt(song.FilePath);
                        if (coverBytes != null && coverBytes.Length > 0)
                            return DecodeBytesWithSize(coverBytes, 200);
                    }
                }
                catch { }

                return null;
            }, ct);
        }
        catch (OperationCanceledException) { return; }
        catch { }

        if (ct.IsCancellationRequested || _loadingSongId != songId) return;

        if (bitmap != null)
        {
            coverCache.TryAdd(songId, bitmap);
            try { _cover.SetImageBitmap(bitmap); } catch { }
        }
    }

    public void CancelLoad()
    {
        _cts?.Cancel();
        _loadingSongId = -1;
    }

    private static Android.Graphics.Bitmap? DecodeFileWithSize(string path, int targetSize)
    {
        try
        {
            var options = new Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
            Android.Graphics.BitmapFactory.DecodeFile(path, options);

            options.InSampleSize = CalculateInSampleSize(options.OutWidth, options.OutHeight, targetSize);
            options.InJustDecodeBounds = false;
            options.InPreferredConfig = Android.Graphics.Bitmap.Config.Rgb565;
            return Android.Graphics.BitmapFactory.DecodeFile(path, options);
        }
        catch { return null; }
    }

    private static Android.Graphics.Bitmap? DecodeBytesWithSize(byte[] data, int targetSize)
    {
        try
        {
            var options = new Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
            Android.Graphics.BitmapFactory.DecodeByteArray(data, 0, data.Length, options);

            options.InSampleSize = CalculateInSampleSize(options.OutWidth, options.OutHeight, targetSize);
            options.InJustDecodeBounds = false;
            options.InPreferredConfig = Android.Graphics.Bitmap.Config.Rgb565;
            return Android.Graphics.BitmapFactory.DecodeByteArray(data, 0, data.Length, options);
        }
        catch { return null; }
    }

    private static int CalculateInSampleSize(int width, int height, int targetSize)
    {
        int inSampleSize = 1;
        if (height > targetSize || width > targetSize)
        {
            var halfH = height / 2;
            var halfW = width / 2;
            while ((halfH / inSampleSize) >= targetSize && (halfW / inSampleSize) >= targetSize)
                inSampleSize *= 2;
        }
        return inSampleSize;
    }

    private static string GetCoverCachedPath(int songId)
    {
        var cacheDir = System.IO.Path.Combine(
            Android.App.Application.Context.CacheDir!.AbsolutePath, "covers");
        return System.IO.Path.Combine(cacheDir, $"cover_{songId}.jpg");
    }
}
