using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Data;
using CatClawMusic.UI.Platforms.Android;
using System.Collections.Concurrent;

namespace CatClawMusic.UI.Adapters;

/// <summary>艺术家列表适配器</summary>
public class ArtistAdapter : RecyclerView.Adapter
{
    private List<ArtistWithCount> _artists = new();
    private NetEaseMusicScraper? _scraper;

    /// <summary>封面内存缓存</summary>
    private static readonly ConcurrentDictionary<string, Android.Graphics.Bitmap?> _coverCache = new();
    private static readonly Android.OS.Handler _mainHandler = new(Android.OS.Looper.MainLooper!);

    public event EventHandler<ArtistWithCount>? OnArtistClick;

    public void SetScraper(NetEaseMusicScraper scraper) => _scraper = scraper;

    public void UpdateArtists(List<ArtistWithCount> artists)
    {
        _artists = artists;
        NotifyDataSetChanged();
    }

    public override int ItemCount => _artists.Count;

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var view = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_artist, parent, false)!;
        return new ArtistViewHolder(view);
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is ArtistViewHolder vh)
        {
            var artist = _artists[position];
            vh.Bind(artist, _scraper, _coverCache, _mainHandler);
            vh.ItemView.Click -= vh.OnClick;
            vh.ItemView.Click += vh.OnClick;
            vh.SetArtist(artist, OnArtistClick);
        }
    }

    public override void OnViewRecycled(Java.Lang.Object holder)
    {
        if (holder is ArtistViewHolder vh)
            vh.CancelLoad();
        base.OnViewRecycled(holder);
    }
}

public class ArtistViewHolder : RecyclerView.ViewHolder
{
    private readonly ImageView _cover;
    private readonly TextView _name;
    private readonly TextView _songCount;
    private ArtistWithCount? _currentArtist;
    private EventHandler<ArtistWithCount>? _clickHandler;
    private CancellationTokenSource? _cts;
    private string? _loadingArtistName;
    private Android.OS.Handler? _mainHandler;

    public ArtistViewHolder(View view) : base(view)
    {
        _cover = view.FindViewById<ImageView>(Resource.Id.artist_cover)!;
        _name = view.FindViewById<TextView>(Resource.Id.artist_name)!;
        _songCount = view.FindViewById<TextView>(Resource.Id.artist_song_count)!;
    }

    public void SetArtist(ArtistWithCount artist, EventHandler<ArtistWithCount>? clickHandler)
    {
        _currentArtist = artist;
        _clickHandler = clickHandler;
    }

    public void OnClick(object? sender, EventArgs e)
    {
        if (_currentArtist != null && _clickHandler != null)
            _clickHandler(this, _currentArtist);
    }

    public void Bind(ArtistWithCount artist, NetEaseMusicScraper? scraper, ConcurrentDictionary<string, Android.Graphics.Bitmap?> coverCache, Android.OS.Handler mainHandler)
    {
        _mainHandler = mainHandler;
        _name.Text = artist.Name;
        _songCount.Text = $"{artist.SongCount}首";

        if (coverCache.TryGetValue(artist.Name, out var cached) && cached != null)
        {
            _mainHandler.Post(() => { try { _cover.SetImageBitmap(cached); } catch { } });
            return;
        }

        _cover.SetImageResource(Resource.Drawable.ic_person);
        _ = LoadCoverAsync(artist, scraper, coverCache);
    }

    private async Task LoadCoverAsync(ArtistWithCount artist, NetEaseMusicScraper? scraper, ConcurrentDictionary<string, Android.Graphics.Bitmap?> coverCache)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _loadingArtistName = artist.Name;

        var artistName = artist.Name;
        Android.Graphics.Bitmap? bitmap = null;

        try
        {
            bitmap = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    if (!string.IsNullOrEmpty(artist.Cover) && System.IO.File.Exists(artist.Cover))
                    {
                        var b = DecodeSampledBitmap(artist.Cover, 200);
                        if (b != null) return b;
                    }
                }
                catch { }

                ct.ThrowIfCancellationRequested();

                try
                {
                    if (!string.IsNullOrEmpty(artist.SampleCoverPath) && System.IO.File.Exists(artist.SampleCoverPath))
                    {
                        var b = DecodeSampledBitmap(artist.SampleCoverPath, 200);
                        if (b != null) return b;
                    }
                }
                catch { }

                ct.ThrowIfCancellationRequested();

                try
                {
                    if (!string.IsNullOrEmpty(artist.SampleFilePath))
                    {
                        var (b, _) = MediaStoreCoverHelper.LoadCoverByFilePath(artist.SampleFilePath, 200);
                        if (b != null) return b;
                    }
                }
                catch { }

                ct.ThrowIfCancellationRequested();

                try
                {
                    if (artist.SampleMediaStoreId > 0)
                    {
                        var b = MediaStoreCoverHelper.LoadCoverFromMediaStore(artist.SampleMediaStoreId, 200);
                        if (b != null) return b;
                    }
                }
                catch { }

                ct.ThrowIfCancellationRequested();

                if (scraper != null)
                {
                    var cachedPath = scraper.GetCachedCoverPath(artistName);
                    if (cachedPath != null)
                    {
                        try
                        {
                            var b = DecodeSampledBitmap(cachedPath, 200);
                            if (b != null) return b;
                        }
                        catch { }
                    }
                }

                return null;
            }, ct);
        }
        catch (OperationCanceledException) { return; }
        catch { }

        if (ct.IsCancellationRequested || _loadingArtistName != artistName) return;

        var handler = _mainHandler;
        if (handler == null) return;

        handler.Post(() =>
        {
            if (_currentArtist?.Name != artistName) return;

            if (bitmap != null)
            {
                coverCache.TryAdd(artistName, bitmap);
                try { _cover.SetImageBitmap(bitmap); } catch { }
            }
            else if (scraper != null)
            {
                _ = ScrapeArtistCoverAsync(artistName, scraper, coverCache);
            }
        });
    }

    private async Task ScrapeArtistCoverAsync(string artistName, NetEaseMusicScraper scraper, ConcurrentDictionary<string, Android.Graphics.Bitmap?> coverCache)
    {
        try
        {
            var coverPath = await scraper.GetArtistCoverAsync(artistName);
            if (coverPath == null) return;

            var handler = _mainHandler;
            if (handler == null) return;

            handler.Post(() =>
            {
                try
                {
                    if (_currentArtist?.Name != artistName) return;
                    var bitmap = DecodeSampledBitmap(coverPath, 200);
                    if (bitmap != null)
                    {
                        coverCache.TryAdd(artistName, bitmap);
                        _cover.SetImageBitmap(bitmap);
                    }
                }
                catch { }
            });
        }
        catch { }
    }

    public void CancelLoad()
    {
        _cts?.Cancel();
        _loadingArtistName = null;
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
