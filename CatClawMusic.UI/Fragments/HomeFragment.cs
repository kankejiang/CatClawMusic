using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class HomeFragment : Fragment
{
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

    private class ClickListener : Java.Lang.Object, View.IOnClickListener
    {
        private readonly Action _action;
        public ClickListener(Action action) => _action = action;
        public void OnClick(View? v) => _action();
    }
}
