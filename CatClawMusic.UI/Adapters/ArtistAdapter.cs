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
            vh.Bind(artist, _scraper, _coverCache);
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

    public void Bind(ArtistWithCount artist, NetEaseMusicScraper? scraper, ConcurrentDictionary<string, Android.Graphics.Bitmap?> coverCache)
    {
        _name.Text = artist.Name;
        _songCount.Text = $"{artist.SongCount}首";

        // 内存缓存命中
        if (coverCache.TryGetValue(artist.Name, out var cached) && cached != null)
        {
            try { _cover.SetImageBitmap(cached); } catch { }
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

                // 1. 数据库中的 Cover 字段
                try
                {
                    if (!string.IsNullOrEmpty(artist.Cover) && System.IO.File.Exists(artist.Cover))
                    {
                        var b = Android.Graphics.BitmapFactory.DecodeFile(artist.Cover);
                        if (b != null) return b;
                    }
                }
                catch { }

                ct.ThrowIfCancellationRequested();

                // 2. 从歌曲的 CoverArtPath 加载
                try
                {
                    if (!string.IsNullOrEmpty(artist.SampleCoverPath) && System.IO.File.Exists(artist.SampleCoverPath))
                    {
                        var b = Android.Graphics.BitmapFactory.DecodeFile(artist.SampleCoverPath);
                        if (b != null) return b;
                    }
                }
                catch { }

                ct.ThrowIfCancellationRequested();

                // 3. 通过 MediaStore 加载
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

                // 4. 网易云缓存封面
                if (scraper != null)
                {
                    var cachedPath = scraper.GetCachedCoverPath(artistName);
                    if (cachedPath != null)
                    {
                        try
                        {
                            var b = Android.Graphics.BitmapFactory.DecodeFile(cachedPath);
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

        if (bitmap != null)
        {
            coverCache.TryAdd(artistName, bitmap);
            try { _cover.SetImageBitmap(bitmap); } catch { }
        }
        else if (scraper != null)
        {
            // 异步刮削封面（下次加载时可用）
            _ = ScrapeArtistCoverAsync(artistName, scraper, coverCache);
        }
    }

    private async Task ScrapeArtistCoverAsync(string artistName, NetEaseMusicScraper scraper, ConcurrentDictionary<string, Android.Graphics.Bitmap?> coverCache)
    {
        try
        {
            var coverPath = await scraper.GetArtistCoverAsync(artistName);
            if (coverPath != null)
            {
                ItemView.Post(() =>
                {
                    try
                    {
                        if (_currentArtist?.Name != artistName) return;
                        var bitmap = Android.Graphics.BitmapFactory.DecodeFile(coverPath);
                        if (bitmap != null)
                        {
                            coverCache.TryAdd(artistName, bitmap);
                            _cover.SetImageBitmap(bitmap);
                        }
                    }
                    catch { }
                });
            }
        }
        catch { }
    }

    public void CancelLoad()
    {
        _cts?.Cancel();
        _loadingArtistName = null;
    }
}
