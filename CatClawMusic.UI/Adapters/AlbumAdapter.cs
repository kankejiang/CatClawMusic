using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Services;
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
    private static readonly Android.OS.Handler _mainHandler = new(Android.OS.Looper.MainLooper!);

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
            vh.Bind(album, _coverCache, _mainHandler);
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
    private Android.OS.Handler? _mainHandler;

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

    public void Bind(AlbumWithCount album, ConcurrentDictionary<string, Android.Graphics.Bitmap?> coverCache, Android.OS.Handler mainHandler)
    {
        _mainHandler = mainHandler;
        _title.Text = album.Title;
        _artist.Text = album.ArtistName;

        var cacheKey = album.Title + "|" + album.ArtistName;
        if (coverCache.TryGetValue(cacheKey, out var cached) && cached != null)
        {
            _mainHandler.Post(() => { try { _cover.SetImageBitmap(cached); } catch { } });
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

                try
                {
                    if (!string.IsNullOrEmpty(album.CoverArtPath) && System.IO.File.Exists(album.CoverArtPath))
                    {
                        var b = DecodeSampledBitmap(album.CoverArtPath, 200);
                        if (b != null) return b;
                    }
                }
                catch { }

                ct.ThrowIfCancellationRequested();

                try
                {
                    if (!string.IsNullOrEmpty(album.Cover) && System.IO.File.Exists(album.Cover))
                    {
                        var b = DecodeSampledBitmap(album.Cover, 200);
                        if (b != null) return b;
                    }
                }
                catch { }

                ct.ThrowIfCancellationRequested();

                try
                {
                    if (!string.IsNullOrEmpty(album.SampleCoverPath) && System.IO.File.Exists(album.SampleCoverPath))
                    {
                        var b = DecodeSampledBitmap(album.SampleCoverPath, 200);
                        if (b != null) return b;
                    }
                }
                catch { }

                ct.ThrowIfCancellationRequested();

                try
                {
                    if (!string.IsNullOrEmpty(album.SampleFilePath))
                    {
                        var (b, _) = MediaStoreCoverHelper.LoadCoverByFilePath(album.SampleFilePath, 200);
                        if (b != null) return b;
                    }
                }
                catch { }

                ct.ThrowIfCancellationRequested();

                try
                {
                    if (album.SampleMediaStoreId > 0)
                    {
                        var b = MediaStoreCoverHelper.LoadCoverFromMediaStore(album.SampleMediaStoreId, 200);
                        if (b != null) return b;
                    }
                }
                catch { }

                ct.ThrowIfCancellationRequested();

                // 从音频文件嵌入标签提取封面
                try
                {
                    if (!string.IsNullOrEmpty(album.SampleFilePath) && !album.SampleFilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(album.SampleFilePath))
                    {
                        var coverBytes = TagReader.ExtractCoverArt(album.SampleFilePath);
                        if (coverBytes != null && coverBytes.Length > 0)
                        {
                            var b = Android.Graphics.BitmapFactory.DecodeByteArray(coverBytes, 0, coverBytes.Length);
                            if (b != null) return b;
                        }
                    }
                }
                catch { }

                return null;
            }, ct);
        }
        catch (OperationCanceledException) { return; }
        catch { }

        if (ct.IsCancellationRequested || _loadingAlbumTitle != album.Title) return;

        var handler = _mainHandler;
        if (handler == null) return;

        handler.Post(() =>
        {
            if (_currentAlbum?.Title != album.Title) return;

            if (bitmap != null)
            {
                coverCache.TryAdd(cacheKey, bitmap);
                try { _cover.SetImageBitmap(bitmap); } catch { }
            }
        });
    }

    public void CancelLoad()
    {
        _cts?.Cancel();
        _loadingAlbumTitle = null;
    }

    private static Android.Graphics.Bitmap? DecodeSampledBitmap(string path, int targetSize)
    {
        try
        {
            var options = new Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
            Android.Graphics.BitmapFactory.DecodeFile(path, options);
            if (options.OutWidth <= 0 || options.OutHeight <= 0) return null;
            options.InSampleSize = CalculateInSampleSize(options.OutWidth, options.OutHeight, targetSize);
            options.InJustDecodeBounds = false;
            options.InPreferredConfig = Android.Graphics.Bitmap.Config.Rgb565;
            return Android.Graphics.BitmapFactory.DecodeFile(path, options);
        }
        catch { return null; }
    }

    private static int CalculateInSampleSize(int width, int height, int targetSize)
    {
        int inSampleSize = 1;
        if (width > targetSize || height > targetSize)
        {
            var halfWidth = width / 2;
            var halfHeight = height / 2;
            while ((halfWidth / inSampleSize) >= targetSize && (halfHeight / inSampleSize) >= targetSize)
                inSampleSize *= 2;
        }
        return inSampleSize;
    }
}
