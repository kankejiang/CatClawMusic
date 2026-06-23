using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Models;

namespace CatClawMusic.UI.Adapters;

/// <summary>
/// 即将播放歌曲列表适配器，显示后续歌曲的标题和艺术家
/// </summary>
public class UpcomingSongAdapter : RecyclerView.Adapter
{
    private List<Song> _songs = new();

    /// <summary>
    /// 更新歌曲列表数据
    /// </summary>
    public void UpdateSongs(IEnumerable<Song> songs)
    {
        _songs = songs.ToList();
        NotifyDataSetChanged();
    }

    /// <summary>
    /// 歌曲总数
    /// </summary>
    public override int ItemCount => _songs.Count;

    /// <summary>
    /// 创建歌曲项ViewHolder实例
    /// </summary>
    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var view = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_upcoming_song, parent, false)!;
        return new VH(view);
    }

    /// <summary>
    /// 绑定歌曲数据到ViewHolder
    /// </summary>
    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        var vh = (VH)holder;
        if (position < 0 || position >= _songs.Count) return;
        var s = _songs[position];
        vh.Title.Text = s.Title;
        vh.Artist.Text = s.Artist;
    }

    /// <summary>
    /// 歌曲项ViewHolder，持有标题和艺术家文本视图
    /// </summary>
    private class VH : RecyclerView.ViewHolder
    {
        /// <summary>
        /// 歌曲标题文本
        /// </summary>
        public TextView Title, Artist;
        /// <summary>
        /// 初始化ViewHolder，查找子视图引用
        /// </summary>
        public VH(View view) : base(view)
        {
            Title = view.FindViewById<TextView>(Resource.Id.upcoming_title)!;
            Artist = view.FindViewById<TextView>(Resource.Id.upcoming_artist)!;
        }
    }
}
