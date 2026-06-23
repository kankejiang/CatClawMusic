using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;

namespace CatClawMusic.UI.Adapters;

/// <summary>
/// 搜索建议项类型
/// </summary>
public enum SuggestionType
{
    Artist,
    Song
}

/// <summary>
/// 搜索建议数据项
/// </summary>
public class SearchSuggestion
{
    public SuggestionType Type { get; set; }
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public ArtistWithCount? Artist { get; set; }
    public Song? Song { get; set; }
}

/// <summary>
/// 搜索建议下拉框适配器，分组显示艺术家和歌曲
/// </summary>
public class SearchSuggestionAdapter : RecyclerView.Adapter
{
    private readonly List<object> _items = new();

    /// <summary>点击艺术家</summary>
    public event EventHandler<ArtistWithCount>? OnArtistClick;
    /// <summary>点击歌曲</summary>
    public event EventHandler<Song>? OnSongClick;

    private const int ViewTypeHeader = 0;
    private const int ViewTypeArtist = 1;
    private const int ViewTypeSong = 2;

    public override int ItemCount => _items.Count;

    public override int GetItemViewType(int position)
    {
        if (position >= _items.Count) return ViewTypeHeader;
        return _items[position] switch
        {
            string => ViewTypeHeader,
            ArtistWithCount => ViewTypeArtist,
            Song => ViewTypeSong,
            _ => ViewTypeHeader
        };
    }

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var inflater = LayoutInflater.From(parent.Context)!;
        return viewType switch
        {
            ViewTypeHeader => new HeaderViewHolder(
                inflater.Inflate(Resource.Layout.item_search_suggestion_header, parent, false)!),
            ViewTypeArtist => new ItemViewHolder(
                inflater.Inflate(Resource.Layout.item_search_suggestion, parent, false)!),
            ViewTypeSong => new ItemViewHolder(
                inflater.Inflate(Resource.Layout.item_search_suggestion, parent, false)!),
            _ => throw new ArgumentOutOfRangeException(nameof(viewType))
        };
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (position >= _items.Count) return;
        var item = _items[position];
        switch (holder)
        {
            case HeaderViewHolder hvh:
                hvh.Header.Text = (string)item;
                break;
            case ItemViewHolder ivh:
                if (item is ArtistWithCount artist)
                {
                    ivh.Title.Text = artist.Name;
                    ivh.Subtitle.Text = $"{artist.SongCount} 首歌曲";
                    ivh.Icon.SetImageResource(Resource.Drawable.ic_person);
                    ivh.Icon.SetImageDrawable(null);
                    ivh.Icon.SetImageResource(Resource.Drawable.ic_person);
                    ivh.ItemView.Click += (_, _) => OnArtistClick?.Invoke(this, artist);
                }
                else if (item is Song song)
                {
                    ivh.Title.Text = song.Title;
                    ivh.Subtitle.Text = song.Artist;
                    ivh.Icon.SetImageResource(Resource.Drawable.ic_music_note);
                    ivh.ItemView.Click += (_, _) => OnSongClick?.Invoke(this, song);
                }
                break;
        }
    }

    /// <summary>
    /// 更新搜索建议列表
    /// </summary>
    public void UpdateSuggestions(List<ArtistWithCount> artists, List<Song> songs)
    {
        _items.Clear();
        if (artists.Count > 0)
        {
            _items.Add("艺术家");
            foreach (var a in artists) _items.Add(a);
        }
        if (songs.Count > 0)
        {
            _items.Add("歌曲");
            foreach (var s in songs) _items.Add(s);
        }
        NotifyDataSetChanged();
    }

    public void Clear()
    {
        _items.Clear();
        NotifyDataSetChanged();
    }

    private class HeaderViewHolder : RecyclerView.ViewHolder
    {
        public TextView Header { get; }
        public HeaderViewHolder(View view) : base(view)
        {
            Header = view.FindViewById<TextView>(Resource.Id.tv_header)!;
        }
    }

    private class ItemViewHolder : RecyclerView.ViewHolder
    {
        public ImageView Icon { get; }
        public TextView Title { get; }
        public TextView Subtitle { get; }
        public ItemViewHolder(View view) : base(view)
        {
            Icon = view.FindViewById<ImageView>(Resource.Id.img_icon)!;
            Title = view.FindViewById<TextView>(Resource.Id.tv_title)!;
            Subtitle = view.FindViewById<TextView>(Resource.Id.tv_subtitle)!;
        }
    }
}
