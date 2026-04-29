using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class SettingsFragment : Fragment
{
    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
    {
        var view = inflater.Inflate(Resource.Layout.fragment_settings, container, false)!;
        var nav = MainApplication.Services.GetRequiredService<INavigationService>();

        // WebDAV 设置入口
        var btnWebDav = view.FindViewById<View>(Resource.Id.btn_webdav);
        if (btnWebDav != null)
            btnWebDav.SetOnClickListener(new ClickListener(() => nav.PushFragment("WebDavSettings")));

        // 音乐文件夹管理 → 二级页面
        var btnFolders = view.FindViewById<View>(Resource.Id.btn_music_folders);
        if (btnFolders != null)
            btnFolders.SetOnClickListener(new ClickListener(() => nav.PushFragment("MusicFolderSettings")));

        // Navidrome 设置入口
        var btnNavidrome = view.FindViewById<View>(Resource.Id.btn_navidrome);
        if (btnNavidrome != null)
            btnNavidrome.SetOnClickListener(new ClickListener(() => nav.PushFragment("NavidromeSettings")));

        return view;
    }

    private class ClickListener : Java.Lang.Object, View.IOnClickListener
    {
        private readonly Action _action;
        public ClickListener(Action action) => _action = action;
        public void OnClick(View? v) => _action();
    }
}
