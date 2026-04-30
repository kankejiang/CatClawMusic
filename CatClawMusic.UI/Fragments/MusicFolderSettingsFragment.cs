using Android.App;
using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.UI.Platforms.Android;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class MusicFolderSettingsFragment : SettingsSubPageFragment
{
    private SettingsViewModel _viewModel = null!;
    private LinearLayout _folderListContainer = null!;
    private ProgressBar _scanProgress = null!;
    private TextView _scanStatus = null!;

    protected override string GetTitle() => "音乐文件夹";

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_music_folders, container, false)!;

    protected override void OnSubViewCreated(View view, Bundle? state)
    {
        _viewModel = MainApplication.Services.GetRequiredService<SettingsViewModel>();

        _folderListContainer = view.FindViewById<LinearLayout>(Resource.Id.folder_list)!;
        _scanProgress = view.FindViewById<ProgressBar>(Resource.Id.scan_progress)!;
        _scanStatus = view.FindViewById<TextView>(Resource.Id.scan_status)!;

        var btnAdd = view.FindViewById<Button>(Resource.Id.btn_add_folder)!;
        var btnScan = view.FindViewById<Button>(Resource.Id.btn_scan)!;

        btnAdd.Click += async (s, e) =>
        {
            await _viewModel.AddMusicFolderCommand.ExecuteAsync(null);
            RefreshList();
        };

        btnScan.Click += async (s, e) => await StartScanAsync();

        RefreshList();
    }

    private async Task StartScanAsync()
    {
        var libVm = MainApplication.Services.GetRequiredService<LibraryViewModel>();

        _scanProgress.Visibility = ViewStates.Visible;
        _scanProgress.Progress = 0;
        _scanStatus.Visibility = ViewStates.Visible;
        _scanStatus.Text = "准备扫描...";

        // 绑定 ViewModel 真实进度
        libVm.PropertyChanged += OnScanProgressChanged;

        try
        {
            await libVm.LoadLocalAsync(forceReload: true);
            while (libVm.IsScanning) await Task.Delay(200);
        }
        catch { }

        libVm.PropertyChanged -= OnScanProgressChanged;

        _scanProgress.Progress = 100;
        _scanStatus.Text = "扫描完成";

        await Task.Delay(800);
        _scanProgress.Visibility = ViewStates.Gone;
        _scanStatus.Visibility = ViewStates.Gone;
    }

    private void OnScanProgressChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(LibraryViewModel.ScanProgress) or nameof(LibraryViewModel.ScanStatus))) return;
        var libVm = MainApplication.Services.GetRequiredService<LibraryViewModel>();
        if (Activity == null) return;
        Activity.RunOnUiThread(() =>
        {
            _scanProgress.Progress = libVm.ScanProgress;
            _scanStatus.Text = libVm.ScanStatus;
        });
    }

    private void RefreshList()
    {
        _folderListContainer.RemoveAllViews();

        foreach (var folder in _viewModel.MusicFolders)
        {
            var row = new LinearLayout(Context!)
            {
                Orientation = Android.Widget.Orientation.Horizontal,
            };
            // 圆角长条背景（替代扁椭圆）
            var rowBg = new Android.Graphics.Drawables.GradientDrawable();
            rowBg.SetColor(Android.Graphics.Color.ParseColor("#0F000000"));
            rowBg.SetCornerRadius(24);
            row.Background = rowBg;
            row.SetPadding(16, 8, 12, 8);

            var text = new TextView(Context!)
            {
                Text = $"📁 {folder}",
                TextSize = 13,
            };
            text.SetTextColor(Android.Graphics.Color.ParseColor("#2D2438"));
            var textLp = new LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WrapContent, 1) { Gravity = GravityFlags.CenterVertical };
            text.LayoutParameters = textLp;
            row.AddView(text);

            var delBtn = new Android.Widget.Button(Context!) { Text = "删除", TextSize = 12 };
            delBtn.SetTextColor(Android.Graphics.Color.ParseColor("#E04040"));
            var btnBg = new Android.Graphics.Drawables.GradientDrawable();
            btnBg.SetColor(Android.Graphics.Color.ParseColor("#1AE04040"));
            btnBg.SetCornerRadius(32);
            delBtn.Background = btnBg;
            delBtn.SetPadding(20, 8, 20, 8);
            var btnLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
            { Gravity = GravityFlags.CenterVertical };
            delBtn.LayoutParameters = btnLp;
            var capturedFolder = folder;
            delBtn.Click += (s, e) =>
            {
                _viewModel.MusicFolders.Remove(capturedFolder);
                FolderPicker.RemoveSavedFolder(capturedFolder);
                RefreshList();
            };
            row.AddView(delBtn);

            var rowLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            rowLp.BottomMargin = 8;
            row.LayoutParameters = rowLp;
            _folderListContainer.AddView(row);
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
