using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Data;
using CatClawMusic.UI.Platforms.Android;
using System.Collections.Concurrent;

namespace CatClawMusic.UI.Adapters;

/// <summary>专辑列表适配器（水平滚动）</summary>
public class AlbumAdapter : RecyclerView.Adapter
{
    private List<AlbumWithCount> _albums = new();

    /// <summary>封面内存缓存</summary>
    private static readonly ConcurrentDictionary<string, Android.Graphics.Bitmap?> _coverCache = new();

    public event EventHandler<AlbumWithCount>? OnAlbumClick;

    public void UpdateAlbums(List<AlbumWithCount> albums)
    {
        _albums = albums;
        NotifyDataSetChanged();
    }

    public override int ItemCount => _albums.Count;

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var view = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_album, parent, false)!;
        return new AlbumViewHolder(view);
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is AlbumViewHolder vh)
        {
            var album = _albums[position];
            vh.Bind(album, _coverCache);
            vh.ItemView.Click -= vh.OnClick;
            vh.ItemView.Click += vh.OnClick;
            vh.SetAlbum(album, OnAlbumClick);
        }
    }

    public override void OnViewRecycled(Java.Lang.Object holder)
    {
        if (holder is AlbumViewHolder vh)
            vh.CancelLoad();
        base.OnViewRecycled(holder);
    }
}

public class AlbumViewHolder : RecyclerView.ViewHolder
{
    private readonly ImageView _cover;
    private readonly TextView _title;
    private readonly TextView _artist;
    private AlbumWithCount? _currentAlbum;
    private EventHandler<AlbumWithCount>? _clickHandler;
    private CancellationTokenSource? _cts;
    private string? _loadingAlbumTitle;

    public AlbumViewHolder(View view) : base(view)
    {
        _cover = view.FindViewById<ImageView>(Resource.Id.album_cover)!;
        _title = view.FindViewById<TextView>(Resource.Id.album_title)!;
        _artist = view.FindViewById<TextView>(Resource.Id.album_artist)!;
    }

    public void SetAlbum(AlbumWithCount album, EventHandler<AlbumWithCount>? clickHandler)
    {
        _currentAlbum = album;
        _clickHandler = clickHandler;
    }

    public void OnClick(object? sender, EventArgs e)
    {
        if (_currentAlbum != null && _clickHandler != null)
            _clickHandler(this, _currentAlbum);
    }

    public void Bind(AlbumWithCount album, ConcurrentDictionary<string, Android.Graphics.Bitmap?> coverCache)
    {
        _title.Text = album.Title;
        _artist.Text = album.ArtistName;

        // 内存缓存命中
        var cacheKey = album.Title + "|" + album.ArtistName;
        if (coverCache.TryGetValue(cacheKey, out var cached) && cached != null)
        {
            try { _cover.SetImageBitmap(cached); } catch { }
            return;
        }

        _cover.SetImageResource(Resource.Drawable.ic_album);
        _ = LoadCoverAsync(album, coverCache);
    }

    private async Task LoadCoverAsync(AlbumWithCount album, ConcurrentDictionary<string, Android.Graphics.Bitmap?> coverCache)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _loadingAlbumTitle = album.Title;

        var cacheKey = album.Title + "|" + album.ArtistName;
        Android.Graphics.Bitmap? bitmap = null;

        try
        {
            bitmap = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                // 1. 专辑的 CoverArtPath
                try
                {
                    if (!string.IsNullOrEmpty(album.CoverArtPath) && System.IO.File.Exists(album.CoverArtPath))
                    {
                        var b = Android.Graphics.BitmapFactory.DecodeFile(album.CoverArtPath);
                        if (b != null) return b;
                    }
                }
                catch { }

                ct.ThrowIfCancellationRequested();

                // 2. 专辑的 Cover 字段
                try
                {
                    if (!string.IsNullOrEmpty(album.Cover) && System.IO.File.Exists(album.Cover))
                    {
                        var b = Android.Graphics.BitmapFactory.DecodeFile(album.Cover);
                        if (b != null) return b;
                    }
                }
                catch { }

                ct.ThrowIfCancellationRequested();

                // 3. 从歌曲的 CoverArtPath 加载
                try
                {
                    if (!string.IsNullOrEmpty(album.SampleCoverPath) && System.IO.File.Exists(album.SampleCoverPath))
                    {
                        var b = Android.Graphics.BitmapFactory.DecodeFile(album.SampleCoverPath);
                        if (b != null) return b;
                    }
                }
                catch { }

                ct.ThrowIfCancellationRequested();

                // 4. 通过 MediaStore 加载
                try
                {
                    if (album.SampleMediaStoreId > 0)
                    {
                        var b = MediaStoreCoverHelper.LoadCoverFromMediaStore(album.SampleMediaStoreId, 200);
                        if (b != null) return b;
                    }
                }
                catch { }

                return null;
            }, ct);
        }
        catch (OperationCanceledException) { return; }
        catch { }

        if (ct.IsCancellationRequested || _loadingAlbumTitle != album.Title) return;

        if (bitmap != null)
        {
            coverCache.TryAdd(cacheKey, bitmap);
            try { _cover.SetImageBitmap(bitmap); } catch { }
        }
    }

    public void CancelLoad()
    {
        _cts?.Cancel();
        _loadingAlbumTitle = null;
    }
}
