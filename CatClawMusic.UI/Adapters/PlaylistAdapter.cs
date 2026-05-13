using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Models;

namespace CatClawMusic.UI.Adapters;

public class PlaylistAdapter : RecyclerView.Adapter
{
    private List<Playlist> _playlists = new();
    public event EventHandler<Playlist>? PlaylistClicked;
    public event EventHandler<Playlist>? PlaylistLongClicked;
    public event EventHandler? NewPlaylistClicked;

    private static BitmapDrawable? _addIconDrawable;

    private static BitmapDrawable GetAddIconDrawable(Android.Content.Context ctx)
    {
        if (_addIconDrawable != null) return _addIconDrawable;
        var density = ctx.Resources!.DisplayMetrics!.Density;
        var size = (int)(48 * density);
        var bitmap = Bitmap.CreateBitmap(size, size, Bitmap.Config.Argb8888!);
        var canvas = new Canvas(bitmap);

        var bgPaint = new Paint { Color = Android.Graphics.Color.Argb(0x33, 0x9B, 0x7E, 0xD8), AntiAlias = true };
        canvas.DrawRoundRect(0, 0, size, size, 8 * density, 8 * density, bgPaint);

        var textPaint = new Paint
        {
            Color = Android.Graphics.Color.Argb(0xFF, 0x9B, 0x7E, 0xD8),
            TextSize = 28 * density,
            AntiAlias = true,
            TextAlign = Paint.Align.Center,
        };
        var metrics = textPaint.GetFontMetrics()!;
        var y = size / 2f - (metrics.Ascent + metrics.Descent) / 2f;
        canvas.DrawText("+", size / 2f, y, textPaint);

        _addIconDrawable = new BitmapDrawable(ctx.Resources, bitmap);
        return _addIconDrawable;
    }

    public void UpdatePlaylists(IEnumerable<Playlist> playlists)
    {
        _playlists = playlists.ToList();
        NotifyDataSetChanged();
    }

    public override int ItemCount => _playlists.Count + 1;

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var view = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_playlist, parent, false)!;
        return new VH(view, p => OnItemClick(p), p => OnItemLongClick(p));
    }

    private void OnItemClick(int position)
    {
        if (position < _playlists.Count)
            PlaylistClicked?.Invoke(this, _playlists[position]);
        else
            NewPlaylistClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnItemLongClick(int position)
    {
        if (position < _playlists.Count)
            PlaylistLongClicked?.Invoke(this, _playlists[position]);
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        var vh = (VH)holder;
        if (position < _playlists.Count)
        {
            var p = _playlists[position];
            vh.Name.Text = p.Name;
            vh.Count.Text = $"{p.SongCount} 首";

            if (p.CoverSongId > 0)
            {
                var coverPath = System.IO.Path.Combine(
                    global::Android.App.Application.Context.CacheDir!.AbsolutePath,
                    "covers", $"cover_{p.CoverSongId}.jpg");
                if (System.IO.File.Exists(coverPath))
                    vh.Cover.SetImageURI(Android.Net.Uri.Parse(coverPath));
                else
                    vh.Cover.SetImageResource(Resource.Drawable.cover_default);
            }
            else
            {
                vh.Cover.SetImageResource(Resource.Drawable.cover_default);
            }
        }
        else
        {
            vh.Name.Text = "新建歌单";
            vh.Count.Text = "";
            vh.Cover.SetImageDrawable(GetAddIconDrawable(vh.ItemView.Context!));
        }
    }

    private class VH : RecyclerView.ViewHolder
    {
        public TextView Name, Count;
        public ImageView Cover;

        public VH(View view, Action<int> onClick, Action<int>? onLongClick) : base(view)
        {
            Name = view.FindViewById<TextView>(Resource.Id.playlist_name)!;
            Count = view.FindViewById<TextView>(Resource.Id.playlist_count)!;
            Cover = view.FindViewById<ImageView>(Resource.Id.playlist_cover)!;
            view.Click += (s, e) => onClick(BindingAdapterPosition);
            view.LongClick += (s, e) => { onLongClick?.Invoke(BindingAdapterPosition); };
        }
    }
}
