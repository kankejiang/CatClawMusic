using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.UI.Platforms.Android;
using CatClawMusic.UI.Services;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class LocalMusicSettingsFragment : SettingsSubPageFragment
{
    private Switch? _switchMediaStore;
    private Switch? _switchFilterShort;
    private LinearLayout? _folderListContainer;
    private ProgressBar? _scanProgress;
    private TextView? _scanStatus;
    private TextView? _tvPermissionStatus;
    private SettingsViewModel? _viewModel;

    protected override string GetTitle() => "本地音乐";

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_local_music_settings, container, false)!;

    protected override void OnSubViewCreated(View view, Bundle? state)
    {
        _viewModel = MainApplication.Services.GetRequiredService<SettingsViewModel>();

        _switchMediaStore = view.FindViewById<Switch>(Resource.Id.switch_use_media_store);
        _switchFilterShort = view.FindViewById<Switch>(Resource.Id.switch_filter_short);
        _folderListContainer = view.FindViewById<LinearLayout>(Resource.Id.folder_list);
        _scanProgress = view.FindViewById<ProgressBar>(Resource.Id.scan_progress);
        _scanStatus = view.FindViewById<TextView>(Resource.Id.scan_status);
        _tvPermissionStatus = view.FindViewById<TextView>(Resource.Id.tv_permission_status);

        _switchMediaStore!.Checked = ScanSettings.UseMediaStore;
        _switchFilterShort!.Checked = ScanSettings.FilterShortAudio;

        _switchMediaStore.CheckedChange += (s, e) => ScanSettings.UseMediaStore = e.IsChecked;
        _switchFilterShort.CheckedChange += (s, e) => ScanSettings.FilterShortAudio = e.IsChecked;

        var btnAdd = view.FindViewById<Button>(Resource.Id.btn_add_folder);
        btnAdd!.Click += async (s, e) =>
        {
            await _viewModel.AddMusicFolderCommand.ExecuteAsync(null);
            RefreshFolderList();
        };

        var btnScan = view.FindViewById<Button>(Resource.Id.btn_start_scan);
        btnScan!.Click += async (s, e) => await StartScanAsync();

        var cardPermission = view.FindViewById<View>(Resource.Id.card_storage_permission);
        cardPermission!.Click += (s, e) =>
        {
            try
            {
                Intent? intent = null;
                if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
                {
                    intent = new Intent(global::Android.Provider.Settings.ActionManageAppAllFilesAccessPermission);
                    intent.SetData(global::Android.Net.Uri.Parse("package:com.catclaw.music"));
                    if (Activity?.PackageManager?.ResolveActivity(intent, 0) == null)
                        intent = null;
                }
                if (intent == null)
                {
                    intent = new Intent(global::Android.Provider.Settings.ActionApplicationDetailsSettings);
                    intent.SetData(global::Android.Net.Uri.Parse("package:com.catclaw.music"));
                }
                StartActivity(intent);
            }
            catch
            {
                try
                {
                    var intent = new Intent(global::Android.Provider.Settings.ActionSettings);
                    StartActivity(intent);
                }
                catch { }
            }
        };

        RefreshFolderList();
        UpdatePermissionStatus();
    }

    public override void OnResume()
    {
        base.OnResume();
        UpdatePermissionStatus();
    }

    private void UpdatePermissionStatus()
    {
        if (_tvPermissionStatus == null) return;
        bool hasPermission = Build.VERSION.SdkInt >= BuildVersionCodes.R
            && global::Android.OS.Environment.IsExternalStorageManager;
        _tvPermissionStatus.Text = hasPermission ? "已授权" : "未授权";
        _tvPermissionStatus.SetTextColor(hasPermission
            ? Android.Graphics.Color.ParseColor("#4CAF50")
            : Android.Graphics.Color.ParseColor("#E04040"));
    }

    private async Task StartScanAsync()
    {
        var libVm = MainApplication.Services.GetRequiredService<LibraryViewModel>();

        if (_scanProgress != null) _scanProgress.Visibility = ViewStates.Visible;
        if (_scanStatus != null) { _scanStatus.Visibility = ViewStates.Visible; _scanStatus.Text = "准备扫描..."; }

        libVm.PropertyChanged += OnScanProgressChanged;

        try
        {
            await libVm.LoadLocalAsync(forceReload: true);
            while (libVm.IsScanning) await Task.Delay(200);
        }
        catch { }

        libVm.PropertyChanged -= OnScanProgressChanged;

        if (_scanProgress != null) _scanProgress.Progress = 100;
        if (_scanStatus != null) _scanStatus.Text = "扫描完成";

        await Task.Delay(800);
        if (_scanProgress != null) _scanProgress.Visibility = ViewStates.Gone;
        if (_scanStatus != null) _scanStatus.Visibility = ViewStates.Gone;
    }

    private void OnScanProgressChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(LibraryViewModel.ScanProgress) or nameof(LibraryViewModel.ScanStatus))) return;
        var libVm = MainApplication.Services.GetRequiredService<LibraryViewModel>();
        if (Activity == null) return;
        Activity.RunOnUiThread(() =>
        {
            if (_scanProgress != null) _scanProgress.Progress = libVm.ScanProgress;
            if (_scanStatus != null) _scanStatus.Text = libVm.ScanStatus;
        });
    }

    private void RefreshFolderList()
    {
        if (_folderListContainer == null || _viewModel == null) return;
        _folderListContainer.RemoveAllViews();

        var folderIndex = 0;
        foreach (var folder in _viewModel.MusicFolders)
        {
            var currentIndex = folderIndex;
            var row = new LinearLayout(Context!) { Orientation = Android.Widget.Orientation.Horizontal };
            var rowBg = new Android.Graphics.Drawables.GradientDrawable();
            rowBg.SetColor(Android.Graphics.Color.ParseColor("#0F000000"));
            rowBg.SetCornerRadius(24);
            row.Background = rowBg;
            row.SetPadding(16, 8, 12, 8);

            var text = new TextView(Context!) { Text = $"📁 {folder}", TextSize = 13 };
            text.SetTextColor(Android.Graphics.Color.ParseColor("#2D2438"));
            var textLp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1)
            { Gravity = GravityFlags.CenterVertical };
            text.LayoutParameters = textLp;
            row.AddView(text);

            var delBtn = new Android.Widget.Button(Context!) { Text = "删除", TextSize = 12 };
            delBtn.SetTextColor(Android.Graphics.Color.ParseColor("#E04040"));
            var btnBg = new Android.Graphics.Drawables.GradientDrawable();
            btnBg.SetColor(Android.Graphics.Color.ParseColor("#1AE04040"));
            btnBg.SetCornerRadius(32);
            delBtn.Background = btnBg;
            delBtn.SetPadding(20, 8, 20, 8);
            var btnLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
            { Gravity = GravityFlags.CenterVertical };
            delBtn.LayoutParameters = btnLp;
            delBtn.Click += (s, e) => { _viewModel.RemoveMusicFolder(currentIndex); RefreshFolderList(); };
            row.AddView(delBtn);

            var rowLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            rowLp.BottomMargin = 8;
            row.LayoutParameters = rowLp;
            _folderListContainer.AddView(row);
            folderIndex++;
        }

        if (_viewModel.MusicFolders.Count == 0)
        {
            var empty = new TextView(Context!)
            {
                Text = "尚未添加自定义文件夹",
                TextSize = 13,
                Gravity = Android.Views.GravityFlags.Center,
            };
            empty.SetTextColor(Android.Graphics.Color.ParseColor("#B0A8BA"));
            empty.SetPadding(16, 24, 16, 24);
            _folderListContainer.AddView(empty);
        }
    }
}
