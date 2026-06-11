using System.Linq;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Models;
using Google.Android.Material.Card;

namespace CatClawMusic.UI.Adapters;

/// <summary>
/// 歌单列表适配器，支持分组显示（系统歌单 / 我的歌单）及末尾"新建歌单"入口
/// </summary>
public class PlaylistAdapter : RecyclerView.Adapter
{
    private const int ViewTypeSectionHeader = 0;
    private const int ViewTypePlaylist = 1;
    private const int ViewTypeNewPlaylist = 2;

    private List<Playlist> _playlists = new();
    private List<Playlist> _systemPlaylists = new();
    private List<Playlist> _userPlaylists = new();

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
        var size = (int)(44 * density);
        var bitmap = Bitmap.CreateBitmap(size, size, Bitmap.Config.Argb8888!);
        var canvas = new Canvas(bitmap);

        // Dashed border style
        var borderPaint = new Paint
        {
            AntiAlias = true,
            StrokeWidth = 1.5f * density,
        };
        borderPaint.SetStyle(Paint.Style.Stroke);
        borderPaint.Color = Android.Graphics.Color.Argb(0x66, 0x8E, 0x8E, 0x93);
        borderPaint.SetPathEffect(new DashPathEffect(new float[] { 6 * density, 4 * density }, 0));
        canvas.DrawRoundRect(1 * density, 1 * density, size - 1 * density, size - 1 * density,
            10 * density, 10 * density, borderPaint);

        // Larger "+" sign
        var textPaint = new Paint
        {
            Color = Android.Graphics.Color.Argb(0x99, 0x8E, 0x8E, 0x93),
            TextSize = 24 * density,
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
    /// 更新歌单列表数据，自动分离系统歌单与用户歌单
    /// </summary>
    public void UpdatePlaylists(IEnumerable<Playlist> playlists)
    {
        var all = playlists.ToList();
        _systemPlaylists = all.Where(p => p.IsSystem).ToList();
        _userPlaylists = all.Where(p => !p.IsSystem).ToList();
        _playlists = all;
        NotifyDataSetChanged();
    }

    /// <summary>
    /// 列表项总数：2个分组头 + 系统歌单 + 用户歌单 + 新建歌单按钮
    /// </summary>
    public override int ItemCount => 1 + _systemPlaylists.Count + 1 + _userPlaylists.Count + 1;

    /// <summary>
    /// 根据位置返回视图类型
    /// </summary>
    public override int GetItemViewType(int position)
    {
        // Layout: [Header:系统歌单] [N system playlists] [Header:我的歌单] [M user playlists] [New playlist button]
        int offset = 0;
        // Section header "系统歌单"
        if (position == offset) return ViewTypeSectionHeader;
        offset++;
        // System playlists
        if (position < offset + _systemPlaylists.Count) return ViewTypePlaylist;
        offset += _systemPlaylists.Count;
        // Section header "我的歌单"
        if (position == offset) return ViewTypeSectionHeader;
        offset++;
        // User playlists
        if (position < offset + _userPlaylists.Count) return ViewTypePlaylist;
        offset += _userPlaylists.Count;
        // New playlist
        return ViewTypeNewPlaylist;
    }

    /// <summary>
    /// 根据视图类型创建对应的ViewHolder
    /// </summary>
    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var ctx = parent.Context!;
        if (viewType == ViewTypeSectionHeader)
        {
            // Create section header view programmatically
            var density = ctx.Resources!.DisplayMetrics!.Density;
            var tv = new TextView(ctx)
            {
                TextSize = 12f,
            };
            tv.SetTextColor(Android.Graphics.Color.ParseColor("#8E8E93"));
            tv.SetPadding((int)(20 * density),
                          (int)(12 * density),
                          (int)(20 * density),
                          (int)(8 * density));
            tv.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
            tv.LetterSpacing = 0.04f;
            var lp = new RecyclerView.LayoutParams(
                RecyclerView.LayoutParams.MatchParent,
                RecyclerView.LayoutParams.WrapContent);
            tv.LayoutParameters = lp;
            return new SectionHeaderViewHolder(tv);
        }
        else if (viewType == ViewTypeNewPlaylist)
        {
            // Create "new playlist" card view with subtle purple fill
            var density = ctx.Resources!.DisplayMetrics!.Density;
            var card = new Google.Android.Material.Card.MaterialCardView(ctx);
            card.Radius = 16 * density;
            card.CardElevation = 0;
            card.StrokeWidth = (int)(1.5f * density);
            card.SetCardBackgroundColor(Android.Graphics.Color.ParseColor("#0D9B7ED8")); // 5% opacity purple fill
            card.StrokeColor = Android.Graphics.Color.ParseColor("#339B7ED8"); // 20% opacity purple stroke

            var container = new LinearLayout(ctx)
            {
                Orientation = Android.Widget.Orientation.Horizontal,
            };
            container.SetGravity(GravityFlags.Center);
            container.SetPadding((int)(16 * density), (int)(18 * density), (int)(16 * density), (int)(18 * density));

            var plusText = new TextView(ctx) { Text = "+", TextSize = 22 };
            plusText.SetTextColor(Android.Graphics.Color.ParseColor("#9B7ED8"));
            plusText.SetPadding(0, 0, (int)(8 * density), 0);
            container.AddView(plusText);

            var label = new TextView(ctx) { Text = "创建新歌单", TextSize = 15 };
            label.SetTextColor(Android.Graphics.Color.ParseColor("#9B7ED8"));
            container.AddView(label);

            card.AddView(container);
            var lp = new RecyclerView.LayoutParams(
                RecyclerView.LayoutParams.MatchParent,
                RecyclerView.LayoutParams.WrapContent);
            lp.SetMargins((int)(16 * density), (int)(8 * density), (int)(16 * density), (int)(4 * density));
            card.LayoutParameters = lp;
            return new NewPlaylistViewHolder(card);
        }
        else
        {
            // Existing playlist item - inflate item_playlist
            var view = LayoutInflater.From(ctx)!.Inflate(Resource.Layout.item_playlist, parent, false)!;
            return new VH(view, OnPlaylistClick, OnPlaylistLongClick);
        }
    }

    /// <summary>
    /// 绑定数据到ViewHolder，根据类型分别处理
    /// </summary>
    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is SectionHeaderViewHolder sectionHolder)
        {
            int offset = 0;
            if (position == offset)
            {
                sectionHolder.TextView.Text = "系统歌单";
            }
            else
            {
                sectionHolder.TextView.Text = "我的歌单";
            }
        }
        else if (holder is VH playlistHolder)
        {
            // Determine which playlist this is
            Playlist? playlist = GetPlaylistAtPosition(position);
            if (playlist != null)
            {
                playlistHolder.Bind(playlist);
            }
        }
        else if (holder is NewPlaylistViewHolder newHolder)
        {
            newHolder.ItemView.Click += (s, e) => NewPlaylistClicked?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnPlaylistClick(int position)
    {
        var playlist = GetPlaylistAtPosition(position);
        if (playlist != null)
            PlaylistClicked?.Invoke(this, playlist);
    }

    private void OnPlaylistLongClick(int position)
    {
        var playlist = GetPlaylistAtPosition(position);
        if (playlist != null)
            PlaylistLongClicked?.Invoke(this, playlist);
    }

    /// <summary>
    /// 根据适配器位置获取对应的歌单对象
    /// </summary>
    private Playlist? GetPlaylistAtPosition(int position)
    {
        int offset = 1; // skip first header
        if (position >= offset && position < offset + _systemPlaylists.Count)
            return _systemPlaylists[position - offset];
        offset += _systemPlaylists.Count + 1; // skip system + second header
        if (position >= offset && position < offset + _userPlaylists.Count)
            return _userPlaylists[position - offset];
        return null;
    }

    /// <summary>
    /// 分组标题ViewHolder（如"系统歌单"、"我的歌单"）
    /// </summary>
    public class SectionHeaderViewHolder : RecyclerView.ViewHolder
    {
        public TextView TextView { get; }
        public SectionHeaderViewHolder(View view) : base(view)
        {
            TextView = (TextView)view;
        }
    }

    /// <summary>
    /// "创建新歌单"按钮ViewHolder
    /// </summary>
    public class NewPlaylistViewHolder : RecyclerView.ViewHolder
    {
        public NewPlaylistViewHolder(View view) : base(view) { }
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

        /// <summary>
        /// 绑定歌单数据到视图
        /// </summary>
        public void Bind(Playlist p)
        {
            Name.Text = p.Name;
            Count.Text = $"{p.SongCount} 首";

            if (p.CoverSongId > 0)
            {
                var coverPath = System.IO.Path.Combine(
                    global::Android.App.Application.Context.CacheDir!.AbsolutePath,
                    "covers", $"cover_{p.CoverSongId}.jpg");
                if (System.IO.File.Exists(coverPath))
                    Cover.SetImageURI(Android.Net.Uri.Parse(coverPath));
                else
                    Cover.SetImageResource(Resource.Drawable.cover_default);
            }
            else
            {
                Cover.SetImageResource(Resource.Drawable.cover_default);
            }
        }
    }
}
