using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Models;

namespace CatClawMusic.UI.Adapters;

/// <summary>
/// 歌单列表适配器，最后一项为"新建歌单"入口
/// </summary>
public class PlaylistAdapter : RecyclerView.Adapter
{
    private List<Playlist> _playlists = new();
    /// <summary>
    /// 歌单点击事件
    /// </summary>
    public event EventHandler<Playlist>? PlaylistClicked;
    /// <summary>
    /// 歌单长按事件
    /// </summary>
    public event EventHandler<Playlist>? PlaylistLongClicked;
    /// <summary>
    /// 新建歌单点击事件
    /// </summary>
    public event EventHandler? NewPlaylistClicked;

    private static BitmapDrawable? _addIconDrawable;

    /// <summary>
    /// 获取或创建"新建歌单"的加号图标
    /// </summary>
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

    /// <summary>
    /// 更新歌单列表数据
    /// </summary>
    public void UpdatePlaylists(IEnumerable<Playlist> playlists)
    {
        _playlists = playlists.ToList();
        NotifyDataSetChanged();
    }

    /// <summary>
    /// 列表项总数（含最后的"新建歌单"项）
    /// </summary>
    public override int ItemCount => _playlists.Count + 1;

    /// <summary>
    /// 创建歌单项ViewHolder实例
    /// </summary>
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

    /// <summary>
    /// 绑定歌单数据到ViewHolder，最后一项显示新建歌单入口
    /// </summary>
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

    /// <summary>
    /// 歌单项ViewHolder，持有名称、数量、封面视图引用
    /// </summary>
    private class VH : RecyclerView.ViewHolder
    {
        /// <summary>
        /// 歌单名称文本
        /// </summary>
        public TextView Name, Count;
        /// <summary>
        /// 歌单封面图片
        /// </summary>
        public ImageView Cover;

        /// <summary>
        /// 初始化ViewHolder，查找子视图并绑定点击事件
        /// </summary>
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
