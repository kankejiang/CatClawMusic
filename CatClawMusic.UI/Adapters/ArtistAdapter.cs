using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.UI.Services;

namespace CatClawMusic.UI.Adapters;

/// <summary>艺术家列表适配器</summary>
public class ArtistAdapter : RecyclerView.Adapter
{
    private List<ArtistWithCount> _artists = new();
    private NetEaseMusicScraper? _scraper;

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
            vh.Bind(artist, _scraper);
            vh.ItemView.Click -= vh.OnClick;
            vh.ItemView.Click += vh.OnClick;
            vh.SetArtist(artist, OnArtistClick);
        }
    }
}

public class ArtistViewHolder : RecyclerView.ViewHolder
{
    private readonly ImageView _cover;
    private readonly TextView _name;
    private readonly TextView _songCount;
    private ArtistWithCount? _currentArtist;
    private EventHandler<ArtistWithCount>? _clickHandler;

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

    public void Bind(ArtistWithCount artist, NetEaseMusicScraper? scraper)
    {
        _name.Text = artist.Name;
        _songCount.Text = $"{artist.SongCount}首";

        // 1. 尝试数据库中的 Cover 字段
        try
        {
            if (!string.IsNullOrEmpty(artist.Cover) && System.IO.File.Exists(artist.Cover))
            {
                var bitmap = Android.Graphics.BitmapFactory.DecodeFile(artist.Cover);
                if (bitmap != null) { _cover.SetImageBitmap(bitmap); return; }
            }
        }
        catch { }

        // 2. 尝试网易云缓存封面
        if (scraper != null)
        {
            var cachedPath = scraper.GetCachedCoverPath(artist.Name);
            if (cachedPath != null)
            {
                try
                {
                    var bitmap = Android.Graphics.BitmapFactory.DecodeFile(cachedPath);
                    if (bitmap != null) { _cover.SetImageBitmap(bitmap); return; }
                }
                catch { }
            }

            // 异步刮削封面
            _ = ScrapeArtistCoverAsync(artist.Name, scraper);
        }

        _cover.SetImageResource(Resource.Drawable.ic_person);
    }

    private async Task ScrapeArtistCoverAsync(string artistName, NetEaseMusicScraper scraper)
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
                        var bitmap = Android.Graphics.BitmapFactory.DecodeFile(coverPath);
                        if (bitmap != null && _currentArtist?.Name == artistName)
                            _cover.SetImageBitmap(bitmap);
                    }
                    catch { }
                });
            }
        }
        catch { }
    }
}
