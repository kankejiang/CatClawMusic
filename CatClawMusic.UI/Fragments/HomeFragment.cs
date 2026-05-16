using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 首页Fragment，显示歌曲数量和快速入口
/// </summary>
public class HomeFragment : Fragment
{
    /// <summary>
    /// 创建首页视图，初始化歌曲数量显示和跳转按钮
    /// </summary>
    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
    {
        var view = inflater.Inflate(Resource.Layout.fragment_home, container, false)!;
        var nav = MainApplication.Services.GetRequiredService<INavigationService>();

        // 歌曲数量
        var songCount = view.FindViewById<TextView>(Resource.Id.home_song_count)!;
        var libSv = MainApplication.Services.GetRequiredService<IMusicLibraryService>();
        _ = UpdateSongCountAsync(songCount, libSv);

        // 跳转音乐库
        var btnLibrary = view.FindViewById<View>(Resource.Id.btn_goto_library);
        if (btnLibrary != null)
            btnLibrary.SetOnClickListener(new ClickListener(() => nav.SwitchTab(3)));

        return view;
    }

    /// <summary>
    /// 异步更新首页歌曲数量显示
    /// </summary>
    private async Task UpdateSongCountAsync(TextView tv, IMusicLibraryService lib)
    {
        try
        {
            var songs = await lib.GetAllSongsAsync();
            if (Context != null)
                tv.Text = $"{songs.Count} 首";
        }
        catch { }
    }

    /// <summary>
    /// 通用点击监听器，封装 Action 回调
    /// </summary>
    private class ClickListener : Java.Lang.Object, View.IOnClickListener
    {
        private readonly Action _action;
        /// <summary>
        /// 使用指定的操作初始化点击监听器
        /// </summary>
        public ClickListener(Action action) => _action = action;
        /// <summary>
        /// 触发点击回调
        /// </summary>
        public void OnClick(View? v) => _action();
    }
}
