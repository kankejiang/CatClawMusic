using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.Core.View;
using AndroidX.ViewPager2.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.UI.Fragments;
using CatClawMusic.UI.Platforms.Android;
using CatClawMusic.UI.Services;
using CatClawMusic.UI.ViewModels;
using Google.Android.Material.BottomNavigation;
using Google.Android.Material.Card;
using Google.Android.Material.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI;

[Activity(Theme = "@style/CatClaw.Splash", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation)]
public class MainActivity : AppCompatActivity
{
    private ViewPager2 _viewPager = null!;
    private BottomNavigationView _bottomNav = null!;
    private View _toolbar = null!;
    private ImageButton _btnMenu = null!;
    private int _currentTab;
    private bool _isUserSwipe; // 区分代码切换和用户滑动
    private bool _suppressNavListener; // 防止 UpdateNavSelection 触发 NavListener 循环

    // 迷你播放器
    private View _miniPlayerWrapper = null!;
    private MaterialCardView _miniPlayer = null!;
    private ImageView _miniCover = null!;
    private TextView _miniTitle = null!, _miniArtist = null!;
    private ImageButton _miniPlayPause = null!, _miniPrev = null!, _miniNext = null!;
    private View _miniProgress = null!;

    // 侧滑面板
    private View _sidePanelOverlay = null!;
    private View _sidePanelMask = null!;
    private View _sidePanelContent = null!;
    private SettingsFragment? _settingsFragment;
    private bool _panelOpen;
    private float _panelSwipeStartX, _panelStartTx;
    private const float PanelSwipeThreshold = 100f;

    public static MainActivity Instance { get; private set; } = null!;

    public NavigationService NavigationService =>
        (NavigationService)MainApplication.Services.GetRequiredService<INavigationService>();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        SetTheme(Resource.Style.CatClawTheme);
        base.OnCreate(savedInstanceState);
        Instance = this;

        var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
        _ = db.EnsureInitializedAsync();

        SetContentView(Resource.Layout.activity_main);

        // 恢复上次播放进度（不自动播放）
        var player = MainApplication.Services.GetRequiredService<IAudioPlayerService>();
        var queue = MainApplication.Services.GetRequiredService<PlayQueue>();
        var npVm = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();
        // 立即从缓存恢复播放模式和进度位置（同步，无需等待数据库查询）
        PlaybackStateManager.RestorePrefsToViewModel(queue, npVm);

        // 迷你进度条定时更新
        var miniProgressTimer = new System.Timers.Timer(500);
        miniProgressTimer.Elapsed += (_, _) => RunOnUiThread(() =>
        {
            if (_miniProgress == null) return;
            // 仅在确认播放中才读取 Duration/Position，避免在 Preparing/Error/Idle 状态触发原生 MediaPlayer 错误
            if (!player.IsPlaying || player.Duration.TotalSeconds <= 0)
            {
                _miniProgress.LayoutParameters = new FrameLayout.LayoutParams(
                    _miniPlayer?.Width ?? 0, 2, GravityFlags.Bottom);
                return;
            }
            var dur = player.Duration.TotalSeconds;
            var pos = player.CurrentPosition.TotalSeconds;
            _miniProgress.LayoutParameters = new FrameLayout.LayoutParams(
                (int)(_miniPlayer!.Width * (pos / dur)), 2,
                GravityFlags.Bottom);
        });
        miniProgressTimer.Start();
        // 后台异步：查找歌曲、恢复播放队列、播放并 seek 到上次位置（无延迟、不阻塞 UI）
        _ = Task.Run(() => PlaybackStateManager.RestoreAsync(player, db, queue, npVm));

        _toolbar = FindViewById<View>(Resource.Id.toolbar)!;
        _btnMenu = FindViewById<ImageButton>(Resource.Id.btn_menu)!;
        _viewPager = FindViewById<ViewPager2>(Resource.Id.view_pager)!;
        _bottomNav = FindViewById<BottomNavigationView>(Resource.Id.bottom_navigation)!;

        // 适配状态栏：工具栏向下偏移状态栏高度，防止被遮挡（如 iQOO 等设备）
        FitSystemBars();

        // ViewPager2
        _viewPager.Adapter = new TabPagerAdapter(this);
        _viewPager.UserInputEnabled = true;
        _viewPager.RegisterOnPageChangeCallback(new PageChangeCallback(index =>
        {
            if (!_isUserSwipe) return;
            _currentTab = index;
            UpdateNavSelection(index);
            UpdateTabUI(index);
        }));

        // BottomNav ↔ ViewPager 双向绑定
        _bottomNav.SetOnItemSelectedListener(new NavListener(index =>
        {
            if (_suppressNavListener) return; // UpdateNavSelection 程序化设置时跳过
            _isUserSwipe = false;
            _viewPager.SetCurrentItem(index, true);
            _isUserSwipe = true;
            _currentTab = index;
            UpdateTabUI(index);
        }));

        NavigationService.Initialize(SupportFragmentManager, Resource.Id.overlay_container, _bottomNav);

        // ≡ 汉堡 → 左侧设置面板
        _sidePanelOverlay = FindViewById<View>(Resource.Id.side_panel_overlay)!;
        _sidePanelMask = FindViewById<View>(Resource.Id.side_panel_mask)!;
        _sidePanelContent = FindViewById<View>(Resource.Id.side_panel_content)!;
        _btnMenu.Click += (s, e) => ToggleSidePanel();
        _sidePanelOverlay.SetOnClickListener(new ClickListener(() => CloseSidePanel()));
        _sidePanelMask.SetOnClickListener(new ClickListener(() => CloseSidePanel()));
        _sidePanelOverlay.Touch += OnSidePanelTouch;

        BindMiniPlayer();

        // 启动 → Tab 1（播放页，Tab 0 是歌词页）
        _viewPager.SetCurrentItem(1, false);
        _currentTab = 1;
        _isUserSwipe = true;
        UpdateTabUI(1);
    }

    private void UpdateNavSelection(int index)
    {
        _suppressNavListener = true;
        _bottomNav.SelectedItemId = index switch
        {
            1 => Resource.Id.nav_playing, 2 => Resource.Id.nav_playlist,
            3 => Resource.Id.nav_search, 4 => Resource.Id.nav_library,
            _ => Resource.Id.nav_playing // Tab 0 (歌词) 无导航项，默认高亮播放
        };
        _suppressNavListener = false;
    }

    private void UpdateTabUI(int index)
    {
        // 歌词页(Tab0) + 播放页(Tab1)：隐藏工具栏 + 底部导航 + 迷你播放器
        bool hideNav = index is 0 or 1;
        _toolbar.Visibility = hideNav ? ViewStates.Gone : ViewStates.Visible;
        _bottomNav.Visibility = hideNav ? ViewStates.Gone : ViewStates.Visible;
        SetMiniPlayerVisible(!hideNav);
    }

    public void SetBottomNavVisible(bool visible)
        => _bottomNav.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;

    public void SetMiniPlayerVisible(bool visible)
    {
        var vm = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();
        _miniPlayerWrapper.Visibility = visible && vm.CurrentSong != null && !_panelOpen
            ? ViewStates.Visible : ViewStates.Gone;
    }

    public override void OnBackPressed()
    {
        if (_panelOpen) { CloseSidePanel(); return; }
        if (SupportFragmentManager.BackStackEntryCount > 0)
        {
            SupportFragmentManager.PopBackStack();
            if (SupportFragmentManager.BackStackEntryCount == 0)
            {
                var overlay = FindViewById<View>(Resource.Id.overlay_container);
                if (overlay != null) overlay.Visibility = ViewStates.Gone;
                SetBottomNavVisible(true);
                SetMiniPlayerVisible(true);
                UpdateNavSelection(_currentTab);
            }
        }
        else base.OnBackPressed();
    }

    public void SwitchTab(int index)
    {
        if (index >= 0 && index < 5) // 5 tabs
        {
            _isUserSwipe = false;
            _viewPager.SetCurrentItem(index, true);
            _isUserSwipe = true;
            _currentTab = index;
            UpdateNavSelection(index);
            UpdateTabUI(index);
        }
    }

    protected override void OnDestroy()
    {
        if (Instance == this) Instance = null!;
        base.OnDestroy();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        PermissionService.HandlePermissionResult(requestCode, grantResults);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        FolderPicker.HandleResult(requestCode, resultCode, data);
    }

    // ═══════════ 侧面板 ═══════════

    private void ToggleSidePanel()
    {
        if (_panelOpen) CloseSidePanel(); else OpenSidePanel();
    }

    private void OpenSidePanel()
    {
        _panelOpen = true;
        _sidePanelOverlay.Visibility = ViewStates.Visible;
        int screenW = Resources?.DisplayMetrics?.WidthPixels ?? 1080;
        _sidePanelContent.TranslationX = -screenW * 0.8f;
        _sidePanelContent.Animate().TranslationX(0).SetDuration(250).Start();
        _sidePanelMask.Alpha = 0f;
        _sidePanelMask.Animate().Alpha(1f).SetDuration(250).Start();

        if (_settingsFragment == null)
        {
            _settingsFragment = MainApplication.Services.GetRequiredService<SettingsFragment>();
            SupportFragmentManager.BeginTransaction()
                .Replace(Resource.Id.side_panel_content, _settingsFragment).Commit();
        }
        NavigationService.EnterSidePanelMode(Resource.Id.side_panel_content);
    }

    private void CloseSidePanel()
    {
        if (!_panelOpen) return;
        _panelOpen = false;
        NavigationService.ExitSidePanelMode();
        int screenW = Resources?.DisplayMetrics?.WidthPixels ?? 1080;
        _sidePanelContent.Animate().TranslationX(-screenW * 0.8f).SetDuration(200).Start();
        _sidePanelMask.Animate().Alpha(0f).SetDuration(200)
            .WithEndAction(new Java.Lang.Runnable(() => _sidePanelOverlay.Visibility = ViewStates.Gone)).Start();
    }

    private void OnSidePanelTouch(object? sender, View.TouchEventArgs e)
    {
        if (!_panelOpen || e?.Event == null) return;
        switch (e.Event.Action)
        {
            case MotionEventActions.Down:
                _panelSwipeStartX = e.Event.GetX();
                _panelStartTx = _sidePanelContent.TranslationX;
                break;
            case MotionEventActions.Move:
                float dx = e.Event.GetX() - _panelSwipeStartX;
                if (dx > 0) _sidePanelContent.TranslationX = Math.Min(dx, 0);
                break;
            case MotionEventActions.Up:
                if (e.Event.GetX() - _panelSwipeStartX > PanelSwipeThreshold)
                    CloseSidePanel();
                else if (_panelOpen)
                    _sidePanelContent.Animate().TranslationX(0).SetDuration(150).Start();
                break;
        }
    }

    // ═══════════ 迷你播放器 ═══════════

    private void BindMiniPlayer()
    {
        _miniPlayer = FindViewById<MaterialCardView>(Resource.Id.mini_player)!;
        _miniPlayerWrapper = FindViewById<View>(Resource.Id.mini_player_wrapper)!;
        _miniCover = FindViewById<ImageView>(Resource.Id.mini_cover)!;
        _miniTitle = FindViewById<TextView>(Resource.Id.mini_title)!;
        _miniArtist = FindViewById<TextView>(Resource.Id.mini_artist)!;
        _miniPlayPause = FindViewById<ImageButton>(Resource.Id.mini_play_pause)!;
        _miniProgress = FindViewById<View>(Resource.Id.mini_progress)!;
        _miniPrev = FindViewById<ImageButton>(Resource.Id.mini_prev)!;
        _miniNext = FindViewById<ImageButton>(Resource.Id.mini_next)!;

        // 全局扫描进度条
        var _scanProgressBar = FindViewById<View>(Resource.Id.scan_progress_bar)!;
        var _scanProgress = FindViewById<ProgressBar>(Resource.Id.scan_progress)!;
        var _scanStatusText = FindViewById<TextView>(Resource.Id.scan_status_text)!;
        var libVm = MainApplication.Services.GetRequiredService<LibraryViewModel>();
        libVm.PropertyChanged += (s, e) => RunOnUiThread(() =>
        {
            if (e.PropertyName == nameof(LibraryViewModel.IsScanning))
                _scanProgressBar.Visibility = libVm.IsScanning ? ViewStates.Visible : ViewStates.Gone;
            else if (e.PropertyName == nameof(LibraryViewModel.ScanProgress))
                _scanProgress.Progress = libVm.ScanProgress;
            else if (e.PropertyName == nameof(LibraryViewModel.ScanStatus))
                _scanStatusText.Text = libVm.ScanStatus;
        });

        var vm = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();
        var player = MainApplication.Services.GetRequiredService<IAudioPlayerService>();

        _miniPlayer.Click += (s, e) => { if (vm.CurrentSong != null) SwitchTab(1); };
        _miniPlayPause.Click += (s, e) => vm.PlayPauseCommand.Execute(null);
        _miniPrev.Click += (s, e) => vm.PreviousCommand.Execute(null);
        _miniNext.Click += (s, e) => vm.NextCommand.Execute(null);

        vm.PropertyChanged += (s, e) => RunOnUiThread(() =>
        {
            var song = vm.CurrentSong;
            bool hasSong = song != null;
            bool onMainPage = !_panelOpen;
            bool isNowPlayingTab = _currentTab is 0 or 1;
            _miniPlayerWrapper.Visibility = hasSong && onMainPage && !isNowPlayingTab
                ? ViewStates.Visible : ViewStates.Gone;
            if (!hasSong) return;
            _miniTitle.Text = song.Title ?? "";
            _miniArtist.Text = song.Artist ?? "";
            _miniPlayPause.SetImageResource(
                player.IsPlaying ? Resource.Drawable.ic_pause : Resource.Drawable.ic_play);
            if (!string.IsNullOrEmpty(vm.CoverSource))
                _miniCover.SetImageDrawable(Android.Graphics.Drawables.Drawable.CreateFromPath(vm.CoverSource));
        });
    }

    // ═══════════ 系统栏适配 ═══════════

    private void FitSystemBars()
    {
        var root = FindViewById<View>(Android.Resource.Id.Content)!;
        ViewCompat.SetOnApplyWindowInsetsListener(root, new WindowInsetsListener((v, insets) =>
        {
            var bars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars());
            // 工具栏整体下移留出状态栏空间，内容区保持完整
            var toolbarLp = (LinearLayout.LayoutParams)_toolbar.LayoutParameters!;
            toolbarLp.TopMargin = bars.Top;
            _toolbar.LayoutParameters = toolbarLp;
            // 底部导航底部加 padding，避免被导航栏手势条遮挡
            _bottomNav.SetPadding(
                _bottomNav.PaddingLeft,
                _bottomNav.PaddingTop,
                _bottomNav.PaddingRight,
                bars.Bottom);
            return ViewCompat.OnApplyWindowInsets(v, insets);
        }));
    }

    // ═══════════ 内部类 ═══════════

    private class WindowInsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        private readonly Func<View, WindowInsetsCompat, WindowInsetsCompat> _callback;
        public WindowInsetsListener(Func<View, WindowInsetsCompat, WindowInsetsCompat> callback) => _callback = callback;
        public WindowInsetsCompat OnApplyWindowInsets(View v, WindowInsetsCompat insets) => _callback(v, insets);
    }

    private class PageChangeCallback : ViewPager2.OnPageChangeCallback
    {
        private readonly Action<int> _onPageSelected;
        public PageChangeCallback(Action<int> onPageSelected) => _onPageSelected = onPageSelected;
        public override void OnPageSelected(int position) => _onPageSelected(position);
    }

    private class NavListener : Java.Lang.Object, NavigationBarView.IOnItemSelectedListener
    {
        private readonly Action<int> _onSelected;
        public NavListener(Action<int> onSelected) => _onSelected = onSelected;
        public bool OnNavigationItemSelected(IMenuItem item)
        {
            int index = item.ItemId switch
            {
                Resource.Id.nav_playing => 1, Resource.Id.nav_playlist => 2,
                Resource.Id.nav_search => 3, Resource.Id.nav_library => 4,
                _ => 1
            };
            _onSelected(index);
            return true;
        }
    }

    private class ClickListener : Java.Lang.Object, View.IOnClickListener
    {
        private readonly Action _action;
        public ClickListener(Action action) => _action = action;
        public void OnClick(View? v) => _action();
    }
}
