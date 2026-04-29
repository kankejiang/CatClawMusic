using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.UI.Platforms.Android;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class MusicFolderSettingsFragment : Fragment
{
    private SettingsViewModel _viewModel = null!;
    private LinearLayout _folderListContainer = null!;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_music_folders, container, false)!;

    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);
        _viewModel = MainApplication.Services.GetRequiredService<SettingsViewModel>();

        _folderListContainer = view.FindViewById<LinearLayout>(Resource.Id.folder_list)!;
        var btnAdd = view.FindViewById<Button>(Resource.Id.btn_add_folder)!;
        var btnClear = view.FindViewById<Button>(Resource.Id.btn_clear)!;

        btnAdd.Click += async (s, e) =>
        {
            await _viewModel.AddMusicFolderCommand.ExecuteAsync(null);
            RefreshList();
        };

        btnClear.Click += (s, e) =>
        {
            _viewModel.ClearMusicFoldersCommand.Execute(null);
            RefreshList();
        };

        RefreshList();
    }

    private void RefreshList()
    {
        _folderListContainer.RemoveAllViews();

        foreach (var folder in _viewModel.MusicFolders)
        {
            var item = new TextView(Context!)
            {
                Text = $"📁 {folder}",
                TextSize = 14,
            };
            item.SetTextColor(Android.Graphics.Color.ParseColor("#2D2438"));
            item.SetPadding(16, 12, 16, 12);
            item.SetBackgroundResource(Resource.Drawable.bg_rounded_glass);
            var lp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent);
            lp.BottomMargin = 8;
            item.LayoutParameters = lp;

            _folderListContainer.AddView(item);
        }

        if (_viewModel.MusicFolders.Count == 0)
        {
            var empty = new TextView(Context!)
            {
                Text = "尚未添加音乐文件夹\n\n点击下方按钮选择手机上的音乐目录",
                TextSize = 14,
                Gravity = Android.Views.GravityFlags.Center,
            };
            empty.SetTextColor(Android.Graphics.Color.ParseColor("#B0A8BA"));
            empty.SetPadding(32, 48, 32, 48);
            _folderListContainer.AddView(empty);
        }
    }
}
