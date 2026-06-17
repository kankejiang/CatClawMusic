using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.UI.Helpers;
using CatClawMusic.UI.Platforms.Android;
using CatClawMusic.UI.Services;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 本地音乐设置页面 Fragment。
/// <para>提供本地音乐扫描相关的配置项，包括：</para>
/// <list type="bullet">
///   <item>是否使用 MediaStore 作为扫描数据源</item>
///   <item>是否过滤短音频文件</item>
///   <item>是否使用 SAF 文件夹选择器</item>
///   <item>自定义音乐文件夹的添加与删除</item>
///   <item>手动触发本地音乐扫描</item>
/// </list>
/// <para>继承自 <see cref="SettingsSubPageFragment"/>，作为设置页的子页面呈现。</para>
/// </summary>
public class LocalMusicSettingsFragment : SettingsSubPageFragment
{
    private Switch? _switchMediaStore;
    private Switch? _switchFilterShort;
    private Switch? _switchUseSaf;
    private LinearLayout? _folderListContainer;
    private ProgressBar? _scanProgress;
    private TextView? _scanStatus;
    private SettingsViewModel? _viewModel;

    protected override string GetTitle() => "本地音乐";

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_local_music_settings, container, false)!;

    protected override void OnSubViewCreated(View view, Bundle? state)
    {
        _viewModel = MainApplication.Services.GetRequiredService<SettingsViewModel>();

        _switchMediaStore = view.FindViewById<Switch>(Resource.Id.switch_use_media_store);
        _switchFilterShort = view.FindViewById<Switch>(Resource.Id.switch_filter_short);
        _switchUseSaf = view.FindViewById<Switch>(Resource.Id.switch_use_saf);
        _folderListContainer = view.FindViewById<LinearLayout>(Resource.Id.folder_list);
        _scanProgress = view.FindViewById<ProgressBar>(Resource.Id.scan_progress);
        _scanStatus = view.FindViewById<TextView>(Resource.Id.scan_status);

        _switchMediaStore!.Checked = ScanSettings.UseMediaStore;
        _switchFilterShort!.Checked = ScanSettings.FilterShortAudio;
        _switchUseSaf!.Checked = ScanSettings.UseSafScanner;

        _switchMediaStore.CheckedChange += (s, e) => ScanSettings.UseMediaStore = e.IsChecked;
        _switchFilterShort.CheckedChange += (s, e) => ScanSettings.FilterShortAudio = e.IsChecked;
        _switchUseSaf.CheckedChange += (s, e) => ScanSettings.UseSafScanner = e.IsChecked;

        var btnAdd = view.FindViewById<Button>(Resource.Id.btn_add_folder);
        btnAdd!.Click += async (s, e) =>
        {
            if (ScanSettings.UseSafScanner)
            {
                // SAF 模式：使用系统文件选择器
                await _viewModel.AddMusicFolderCommand.ExecuteAsync(null);
            }
            else
            {
                // 自建浏览器模式：先检查权限，再打开文件夹浏览器
                await AddFolderWithBrowserAsync();
            }
            RefreshFolderList();
        };

        var btnScan = view.FindViewById<Button>(Resource.Id.btn_start_scan);
        btnScan!.Click += async (s, e) => await StartScanAsync();

        RefreshFolderList();
    }

    /// <summary>使用自建文件浏览器添加文件夹，先检查/请求权限</summary>
    private async Task AddFolderWithBrowserAsync()
    {
        var permission = MainApplication.Services.GetRequiredService<IPermissionService>();

        // Android 11+ 需要"所有文件访问"权限
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            var hasPermission = await permission.CheckManageStoragePermissionAsync();
            if (!hasPermission)
            {
                // 提示用户授权，并跳转到系统设置页
                if (Context != null)
                {
                    Toast.MakeText(Context, "需要授予「所有文件访问」权限才能浏览文件夹，授权后请返回重试", ToastLength.Long)?.Show();
                }
                await permission.RequestManageStoragePermissionAsync();
                // RequestManageStoragePermissionAsync 打开系统设置后立即返回，
                // 用户授权后需返回 App 再次点击添加，此处不再尝试打开浏览器
                return;
            }
        }

        // 权限已获取，打开文件夹浏览器
        var nav = MainApplication.Services.GetRequiredService<INavigationService>();
        nav.PushFragment("FolderBrowser");
    }

    public override void OnResume()
    {
        base.OnResume();
        // 从文件夹浏览器或权限设置页返回后刷新列表
        RefreshFolderList();
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

    /// <summary>
    /// 刷新自定义音乐文件夹列表的 UI 显示。
    /// <para>同时显示 SAF 文件夹和本地文件夹路径</para>
    /// </summary>
    private void RefreshFolderList()
    {
        if (_folderListContainer == null || _viewModel == null) return;
        _folderListContainer.RemoveAllViews();

        var allFolders = new List<(string display, string path, bool isSaf)>();

        // SAF 文件夹
        foreach (var folder in _viewModel.MusicFolders)
            allFolders.Add((folder, folder, true));

        // 本地文件夹（真实路径）
        foreach (var path in ScanSettings.GetLocalFolderPaths())
            allFolders.Add((path, path, false));

        var folderIndex = 0;
        foreach (var (display, path, isSaf) in allFolders)
        {
            var currentIndex = folderIndex;
            var currentIsSaf = isSaf;
            var currentPath = path;

            var row = new LinearLayout(Context!) { Orientation = Android.Widget.Orientation.Horizontal };
            var rowBg = new Android.Graphics.Drawables.GradientDrawable();
            rowBg.SetColor(Android.Graphics.Color.ParseColor("#0F000000"));
            rowBg.SetCornerRadius(24);
            row.Background = rowBg;
            row.SetPadding(16, 8, 12, 8);

            var icon = currentIsSaf ? "📁" : "📂";
            var text = new TextView(Context!) { Text = $"{icon} {display}", TextSize = 13 };
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
            delBtn.Click += (s, e) =>
            {
                if (currentIsSaf)
                {
                    _viewModel.RemoveMusicFolder(currentIndex);
                }
                else
                {
                    ScanSettings.RemoveLocalFolderPath(currentPath);
                }
                RefreshFolderList();
            };
            row.AddView(delBtn);

            var rowLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            rowLp.BottomMargin = 8;
            row.LayoutParameters = rowLp;
            _folderListContainer.AddView(row);
            folderIndex++;
        }

        if (allFolders.Count == 0)
        {
            var empty = new TextView(Context!)
            {
                Text = "尚未添加自定义文件夹",
                TextSize = 13,
                Gravity = Android.Views.GravityFlags.Center,
            };
            empty.SetTextColor(new Android.Graphics.Color(UiHelper.ResolveThemeColor(Context!, Resource.Attribute.catClawTextHint, Android.Graphics.Color.ParseColor("#B0A8BA").ToArgb())));
            empty.SetPadding(16, 24, 16, 24);
            _folderListContainer.AddView(empty);
        }
    }
}
