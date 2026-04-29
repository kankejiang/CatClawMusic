using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.UI.Platforms.Android;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class SettingsFragment : Fragment
{
    private SettingsViewModel _viewModel = null!;
    private TextView _folderPathText = null!;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
    {
        var view = inflater.Inflate(Resource.Layout.fragment_settings, container, false)!;
        _viewModel = MainApplication.Services.GetRequiredService<SettingsViewModel>();

        var nav = MainApplication.Services.GetRequiredService<Core.Interfaces.INavigationService>();

        // WebDAV 设置入口
        var btnWebDav = view.FindViewById<View>(Resource.Id.btn_test);
        if (btnWebDav != null) btnWebDav.SetOnClickListener(new ClickListener(() => nav.PushFragment("WebDavSettings")));

        // 音乐文件夹选择
        _folderPathText = view.FindViewById<TextView>(Resource.Id.folder_path_text)!;
        _folderPathText.Text = _viewModel.MusicFolderPath ?? "默认: /storage/emulated/0/Music";

        var btnPickFolder = view.FindViewById<Button>(Resource.Id.btn_pick_folder);
        if (btnPickFolder != null)
        {
            btnPickFolder.Click += async (s, e) =>
            {
                var path = await FolderPicker.PickFolderAsync();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    _viewModel.MusicFolderPath = path;
                    _folderPathText.Text = path;
                }
            };
        }

        return view;
    }

    private class ClickListener : Java.Lang.Object, View.IOnClickListener
    {
        private readonly Action _action;
        public ClickListener(Action action) => _action = action;
        public void OnClick(View? v) => _action();
    }
}
