using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.UI.Services;

namespace CatClawMusic.UI.Adapters;

/// <summary>专辑列表适配器（水平滚动）</summary>
public class AlbumAdapter : RecyclerView.Adapter
{
    private List<AlbumWithCount> _albums = new();

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
            vh.Bind(album);
            vh.ItemView.Click -= vh.OnClick;
            vh.ItemView.Click += vh.OnClick;
            vh.SetAlbum(album, OnAlbumClick);
        }
    }
}

public class AlbumViewHolder : RecyclerView.ViewHolder
{
    private readonly ImageView _cover;
    private readonly TextView _title;
    private readonly TextView _artist;
    private AlbumWithCount? _currentAlbum;
    private EventHandler<AlbumWithCount>? _clickHandler;

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

    public void Bind(AlbumWithCount album)
    {
        _title.Text = album.Title;
        _artist.Text = album.ArtistName;

        try
        {
            if (!string.IsNullOrEmpty(album.CoverArtPath) && System.IO.File.Exists(album.CoverArtPath))
            {
                var bitmap = Android.Graphics.BitmapFactory.DecodeFile(album.CoverArtPath);
                if (bitmap != null) { _cover.SetImageBitmap(bitmap); return; }
            }
        }
        catch { }

        try
        {
            if (!string.IsNullOrEmpty(album.Cover) && System.IO.File.Exists(album.Cover))
            {
                var bitmap = Android.Graphics.BitmapFactory.DecodeFile(album.Cover);
                if (bitmap != null) { _cover.SetImageBitmap(bitmap); return; }
            }
        }
        catch { }

        _cover.SetImageResource(Resource.Drawable.ic_album);
    }
}
