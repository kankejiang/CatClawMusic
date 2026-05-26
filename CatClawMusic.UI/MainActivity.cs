using Android.Content.PM;
using Android.Content.Res;
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
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode)]
public class MainActivity : AppCompatActivity
{
    private ViewPager2 _viewPager = null!;
    private BottomNavigationView _bottomNav = null!;
    private View _toolbar = null!;
    private ImageButton _btnMenu = null!;
    private int _currentTab;
    private bool _isUserSwipe;
    private TabPagerAdapter _tabAdapter = null!;
    private bool _suppressNavListener;
    private bool _overlayOpen;
    private bool _skipNextRecreate;

    public void SetOverlayOpen(bool open) => _overlayOpen = open;

    private View _miniPlayerWrapper = null!;
    private MaterialCardView _miniPlayer = null!;
    private ImageView _miniCover = null!;
    private TextView _miniTitle = null!, _miniArtist = null!;
    private ImageButton _miniPlayPause = null!, _miniPrev = null!, _miniNext = null!;
    private View _miniProgress = null!;

    private View _sidePanelOverlay = null!;
    private View _sidePanelMask = null!;
    private View _sidePanelContent = null!;
    private SettingsFragment? _settingsFragment;
    private bool _panelOpen;
    private float _panelSwipeStartX, _panelStartTx;
    private const float PanelSwipeThreshold = 100f;

    private System.Timers.Timer? _miniProgressTimer;

    private LibraryViewModel? _libVm;
    private NowPlayingViewModel? _miniVm;

    public static MainActivity Instance { get; private set; } = null!;

    public NavigationService NavigationService =>
        (NavigationService)MainApplication.Services.GetRequiredService<INavigationService>();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        LockFontScale();
        ApplySavedTheme();
        base.OnCreate(savedInstanceState);
        Instance = this;

        WindowCompat.SetDecorFitsSystemWindows(Window!, false);

        var insetsController = WindowCompat.GetInsetsController(Window!, Window!.DecorView);
        var isDark = (Resources?.Configuration?.UiMode & UiMode.NightMask) == UiMode.NightYes;
        insetsController.AppearanceLightStatusBars = !isDark;
        insetsController.AppearanceLightNavigationBars = !isDark;

        var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
        /* 异步初始化数据库，RestoreAsync 内部也会调用 EnsureInitializedAsync 并等待完成 */
        _ = db.EnsureInitializedAsync();

        SetContentView(Resource.Layout.activity_main);

        Window!.DecorView.ImportantForAutofill = Android.Views.ImportantForAutofill.NoExcludeDescendants;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            Window!.DecorView.SetOnApplyWindowInsetsListener(new DecorViewInsetsListener());
        }
        else
        {
            ViewCompat.SetOnApplyWindowInsetsListener(Window!.DecorView, new DecorViewInsetsListenerCompat());
        }

        DesktopLyricService.Instance.Initialize(this);

        var player = MainApplication.Services.GetRequiredService<IAudioPlayerService>();
        var queue = MainApplication.Services.GetRequiredService<PlayQueue>();
        var npVm = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();
        PlaybackStateManager.RestorePrefsToViewModel(queue, npVm);

        _miniProgressTimer = new System.Timers.Timer(500);
        _miniProgressTimer.Elapsed += (_, _) => RunOnUiThread(() =>
        {
            if (_miniProgress == null) return;
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
        _miniProgressTimer.Start();
        var networkMusic = MainApplication.Services.GetService<INetworkMusicService>();
        var subsonic = MainApplication.Services.GetService<ISubsonicService>();
        /* 后台恢复播放状态，不阻塞 UI 线程；异常已内部处理不会导致闪退 */
        _ = Task.Run(() => PlaybackStateManager.RestoreAsync(player, db, queue, npVm, networkMusic, subsonic));

        _toolbar = FindViewById<View>(Resource.Id.toolbar)!;
        _btnMenu = FindViewById<ImageButton>(Resource.Id.btn_menu)!;
        _viewPager = FindViewById<ViewPager2>(Resource.Id.view_pager)!;
        _bottomNav = FindViewById<BottomNavigationView>(Resource.Id.bottom_navigation)!;

        _tabAdapter = new TabPagerAdapter(this);
        _viewPager.Adapter = _tabAdapter;
        _viewPager.OffscreenPageLimit = 4;
        _viewPager.UserInputEnabled = true;
        _viewPager.RegisterOnPageChangeCallback(new PageChangeCallback(index =>
        {
            if (!_isUserSwipe) return;
            _currentTab = index;
            UpdateNavSelection(index);
            UpdateTabUI(index);
            if (index == 0)
                _tabAdapter.FullLyricsFragment?.ScrollToCurrentPosition();
        }));

        _bottomNav.SetOnItemSelectedListener(new NavListener(index =>
        {
            if (_suppressNavListener) return;
            _isUserSwipe = false;
            _viewPager.SetCurrentItem(index, true);
            _isUserSwipe = true;
            _currentTab = index;
            UpdateTabUI(index);
        }));

        NavigationService.Initialize(SupportFragmentManager, Resource.Id.overlay_container, _bottomNav);

        _sidePanelOverlay = FindViewById<View>(Resource.Id.side_panel_overlay)!;
        _sidePanelMask = FindViewById<View>(Resource.Id.side_panel_mask)!;
        _sidePanelContent = FindViewById<View>(Resource.Id.side_panel_content)!;
        _btnMenu.Click += (s, e) => ToggleSidePanel();

        FitSystemBars();
        _sidePanelOverlay.SetOnClickListener(new ClickListener(() => CloseSidePanel()));
        _sidePanelMask.SetOnClickListener(new ClickListener(() => CloseSidePanel()));
        _sidePanelOverlay.Touch += OnSidePanelTouch;

        BindMiniPlayer();

        _viewPager.SetCurrentItem(1, false);
        _currentTab = 1;
        _isUserSwipe = true;
        UpdateTabUI(1);
    }

    public override void Recreate()
    {
        if (_skipNextRecreate)
        {
            _skipNextRecreate = false;
            return;
        }
        base.Recreate();
    }

    public void ApplyThemeAndRefresh()
    {
        _skipNextRecreate = true;

        var themeService = MainApplication.Services.GetRequiredService<IThemeService>() as ThemeService;
        if (themeService == null) return;

        bool isDark = themeService.IsEffectivelyDark();

        var config = new Configuration(Resources!.Configuration!);
        config.UiMode = (config.UiMode & ~UiMode.NightMask)
            | (isDark ? UiMode.NightYes : UiMode.NightNo);
        Resources.UpdateConfiguration(config, Resources.DisplayMetrics);

        switch (themeService.DarkModeSetting)
        {
            case DarkModeSetting.Light:
                AppCompatDelegate.DefaultNightMode = AppCompatDelegate.ModeNightNo;
                break;
            case DarkModeSetting.Dark:
                AppCompatDelegate.DefaultNightMode = AppCompatDelegate.ModeNightYes;
                break;
            case DarkModeSetting.FollowSystem:
                AppCompatDelegate.DefaultNightMode = AppCompatDelegate.ModeNightFollowSystem;
                break;
        }

        SetTheme(themeService.GetThemeResourceId());
        RefreshThemeViews();
        _skipNextRecreate = false;
    }

    private int ResolveColor(int attrId)
    {
        var ta = Theme!.ObtainStyledAttributes(new int[] { attrId });
        var color = ta.GetColor(0, 0);
        ta.Recycle();
        return color;
    }

    private void RefreshThemeViews()
    {
        bool isDark = (Resources?.Configuration?.UiMode & UiMode.NightMask) == UiMode.NightYes;

        var insetsController = WindowCompat.GetInsetsController(Window!, Window!.DecorView);
        insetsController.AppearanceLightStatusBars = !isDark;
        insetsController.AppearanceLightNavigationBars = !isDark;

        var mainLayout = FindViewById<LinearLayout>(Resource.Id.main_layout);
        if (mainLayout != null)
            mainLayout.SetBackgroundColor(new Android.Graphics.Color(ResolveColor(Resource.Attribute.catClawPageBackground)));

        if (_toolbar != null)
        {
            var textPrimary = new Android.Graphics.Color(ResolveColor(Resource.Attribute.catClawTextPrimary));
            var title = _toolbar.FindViewById<TextView>(Resource.Id.toolbar_title);
            if (title != null) title.SetTextColor(textPrimary);
            if (_btnMenu != null)
                _btnMenu.SetColorFilter(textPrimary, Android.Graphics.PorterDuff.Mode.SrcIn);
        }

        if (_miniPlayer != null)
        {
            _miniPlayer.CardBackgroundColor = Android.Content.Res.ColorStateList.ValueOf(
                new Android.Graphics.Color(ResolveColor(Resource.Attribute.catClawCardBackgroundSolid)));
        }
        var tp = new Android.Graphics.Color(ResolveColor(Resource.Attribute.catClawTextPrimary));
        var th = new Android.Graphics.Color(ResolveColor(Resource.Attribute.catClawTextHint));
        var pc = new Android.Graphics.Color(ResolveColor(Resource.Attribute.catClawPrimaryColor));
        _miniTitle?.SetTextColor(tp);
        _miniArtist?.SetTextColor(th);
        _miniPrev?.SetColorFilter(tp, Android.Graphics.PorterDuff.Mode.SrcIn);
        _miniNext?.SetColorFilter(tp, Android.Graphics.PorterDuff.Mode.SrcIn);
        _miniPlayPause?.SetColorFilter(pc, Android.Graphics.PorterDuff.Mode.SrcIn);
        _miniProgress?.SetBackgroundColor(pc);

        if (_bottomNav != null)
        {
            _bottomNav.SetBackgroundColor(new Android.Graphics.Color(ResolveColor(Resource.Attribute.catClawNavBarBackground)));
            var tabActive = new Android.Graphics.Color(ResolveColor(Resource.Attribute.catClawTabActive));
            var tabInactive = new Android.Graphics.Color(ResolveColor(Resource.Attribute.catClawTabInactive));
            var csl = new Android.Content.Res.ColorStateList(
                new int[][] { new int[] { Android.Resource.Attribute.StateChecked }, new int[0] },
                new int[] { tabActive.ToArgb(), tabInactive.ToArgb() });
            _bottomNav.ItemIconTintList = csl;
            _bottomNav.ItemTextColor = csl;
        }

        if (_sidePanelContent != null)
            _sidePanelContent.SetBackgroundColor(new Android.Graphics.Color(ResolveColor(Resource.Attribute.catClawPageBackground)));

        var scanBar = FindViewById<LinearLayout>(Resource.Id.scan_progress_bar);
        if (scanBar != null)
            scanBar.SetBackgroundColor(new Android.Graphics.Color(ResolveColor(Resource.Attribute.catClawPageBackground)));
        var scanProgress = FindViewById<ProgressBar>(Resource.Id.scan_progress);
        if (scanProgress != null)
            scanProgress.ProgressTintList = Android.Content.Res.ColorStateList.ValueOf(pc);
        var scanText = FindViewById<TextView>(Resource.Id.scan_status_text);
        if (scanText != null)
            scanText.SetTextColor(new Android.Graphics.Color(ResolveColor(Resource.Attribute.catClawTextHint)));

        if (SupportFragmentManager.BackStackEntryCount > 0)
        {
            SupportFragmentManager.PopBackStackImmediate(null, AndroidX.Fragment.App.FragmentManager.PopBackStackInclusive);
            SetOverlayOpen(false);
            var overlay = FindViewById<View>(Resource.Id.overlay_container);
            if (overlay != null) overlay.Visibility = ViewStates.Gone;
            SetToolbarVisible(true);
            SetBottomNavVisible(true);
            SetMiniPlayerVisible(true);
            UpdateNavSelection(_currentTab);
        }

        RefreshViewPager();

        if (_settingsFragment != null && _panelOpen)
        {
            SupportFragmentManager.BeginTransaction()
                .Detach(_settingsFragment)
                .CommitNow();
            SupportFragmentManager.BeginTransaction()
                .Attach(_settingsFragment)
                .CommitNow();
        }
    }

    private void RefreshViewPager()
    {
        var currentItem = _viewPager.CurrentItem;
        _viewPager.Adapter = null;
        _tabAdapter = new TabPagerAdapter(this);
        _viewPager.Adapter = _tabAdapter;
        _viewPager.OffscreenPageLimit = 4;
        _viewPager.SetCurrentItem(currentItem, false);
        _currentTab = currentItem;
        UpdateTabUI(currentItem);
    }

    private void UpdateNavSelection(int index)
    {
        _suppressNavListener = true;
        _bottomNav.SelectedItemId = index switch
        {
            1 => Resource.Id.nav_playing, 2 => Resource.Id.nav_playlist,
            3 => Resource.Id.nav_search, 4 => Resource.Id.nav_library,
            _ => Resource.Id.nav_playing
        };
        _suppressNavListener = false;
    }

    private void UpdateTabUI(int index)
    {
        bool hideNav = index is 0 or 1;
        _toolbar.Visibility = hideNav ? ViewStates.Gone : ViewStates.Visible;
        _bottomNav.Visibility = hideNav ? ViewStates.Gone : ViewStates.Visible;
        SetMiniPlayerVisible(!hideNav);
    }

    public void SetBottomNavVisible(bool visible)
        => _bottomNav.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;

    public void SetToolbarVisible(bool visible)
        => _toolbar.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;

    public void SetMiniPlayerVisible(bool visible)
    {
        if (_overlayOpen) { _miniPlayerWrapper.Visibility = ViewStates.Gone; return; }
        var vm = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();
        _miniPlayerWrapper.Visibility = visible && vm.CurrentSong != null && !_panelOpen
            ? ViewStates.Visible : ViewStates.Gone;
    }

    public override void OnBackPressed()
    {
        if (_panelOpen) { CloseSidePanel(); return; }
        if (SupportFragmentManager.BackStackEntryCount > 0)
        {
            SupportFragmentManager.PopBackStackImmediate();
            if (SupportFragmentManager.BackStackEntryCount == 0)
            {
                SetOverlayOpen(false);
                var overlay = FindViewById<View>(Resource.Id.overlay_container);
                if (overlay != null) overlay.Visibility = ViewStates.Gone;
                SetToolbarVisible(true);
                SetBottomNavVisible(true);
                SetMiniPlayerVisible(true);
                UpdateNavSelection(_currentTab);
            }
        }
        else base.OnBackPressed();
    }

    public void SwitchTab(int index)
    {
        if (index >= 0 && index < 5)
        {
            _isUserSwipe = false;
            _viewPager.SetCurrentItem(index, true);
            _isUserSwipe = true;
            _currentTab = index;
            UpdateNavSelection(index);
            UpdateTabUI(index);
            if (index == 0)
                _tabAdapter.FullLyricsFragment?.ScrollToCurrentPosition();
        }
    }

    protected override void OnDestroy()
    {
        var player = MainApplication.Services.GetService<IAudioPlayerService>();
        var npVm = MainApplication.Services.GetService<NowPlayingViewModel>();
        var queue = MainApplication.Services.GetService<PlayQueue>();
        if (player != null && npVm != null)
            PlaybackStateManager.Save(player, npVm.CurrentSong, queue);

        _miniProgressTimer?.Stop();
        _miniProgressTimer?.Dispose();

        if (_libVm != null)
            _libVm.PropertyChanged -= OnLibraryPropertyChanged;
        if (_miniVm != null)
            _miniVm.PropertyChanged -= OnMiniPlayerPropertyChanged;

        DesktopLyricService.Instance.Dispose();
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

        var _scanProgressBar = FindViewById<View>(Resource.Id.scan_progress_bar)!;
        var _scanProgress = FindViewById<ProgressBar>(Resource.Id.scan_progress)!;
        var _scanStatusText = FindViewById<TextView>(Resource.Id.scan_status_text)!;
        _libVm = MainApplication.Services.GetRequiredService<LibraryViewModel>();
        _libVm.PropertyChanged += OnLibraryPropertyChanged;

        _miniVm = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();
        var player = MainApplication.Services.GetRequiredService<IAudioPlayerService>();

        _miniPlayer.Click += (s, e) => { if (_miniVm.CurrentSong != null) SwitchTab(1); };
        _miniPlayPause.Click += (s, e) => _miniVm.PlayPauseCommand.Execute(null);
        _miniPrev.Click += (s, e) => _miniVm.PreviousCommand.Execute(null);
        _miniNext.Click += (s, e) => _miniVm.NextCommand.Execute(null);

        _miniVm.PropertyChanged += OnMiniPlayerPropertyChanged;
    }

    private void OnLibraryPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        RunOnUiThread(() =>
        {
            if (_libVm == null) return;
            var bar = FindViewById<View>(Resource.Id.scan_progress_bar);
            var pgr = FindViewById<ProgressBar>(Resource.Id.scan_progress);
            var txt = FindViewById<TextView>(Resource.Id.scan_status_text);
            if (bar == null || pgr == null || txt == null) return;
            if (e.PropertyName == nameof(LibraryViewModel.IsScanning))
                bar.Visibility = _libVm.IsScanning ? ViewStates.Visible : ViewStates.Gone;
            else if (e.PropertyName == nameof(LibraryViewModel.ScanProgress))
                pgr.Progress = _libVm.ScanProgress;
            else if (e.PropertyName == nameof(LibraryViewModel.ScanStatus))
                txt.Text = _libVm.ScanStatus;
        });
    }

    private void OnMiniPlayerPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        RunOnUiThread(() =>
        {
            if (_miniVm == null) return;
            var song = _miniVm.CurrentSong;
            bool hasSong = song != null;
            bool onMainPage = !_panelOpen && !_overlayOpen;
            bool isNowPlayingTab = _currentTab is 0 or 1;
            _miniPlayerWrapper.Visibility = hasSong && onMainPage && !isNowPlayingTab
                ? ViewStates.Visible : ViewStates.Gone;
            if (!hasSong) return;

            var propName = e.PropertyName;
            bool needUpdateIcon = propName == nameof(NowPlayingViewModel.PlayPauseIcon) || propName == null;
            bool needUpdateInfo = propName == nameof(NowPlayingViewModel.CurrentSong) || propName == null;
            bool needUpdateCover = propName == nameof(NowPlayingViewModel.CoverSource) || propName == null;

            if (needUpdateInfo)
            {
                _miniTitle.Text = song.Title ?? "";
                _miniArtist.Text = song.Artist ?? "";
            }

            if (needUpdateIcon)
            {
                bool isPlaying = _miniVm.PlayPauseIcon == "⏸";
                _miniPlayPause.SetImageResource(
                    isPlaying ? Resource.Drawable.ic_pause : Resource.Drawable.ic_play);
            }

            if (needUpdateCover && !string.IsNullOrEmpty(_miniVm.CoverSource))
            {
                try
                {
                    var oldMiniDrawable = _miniCover.Drawable as Android.Graphics.Drawables.BitmapDrawable;
                    if (oldMiniDrawable?.Bitmap != null && oldMiniDrawable.Bitmap.IsRecycled)
                        _miniCover.SetImageResource(Resource.Drawable.cover_default);

                    if (System.IO.File.Exists(_miniVm.CoverSource))
                        _miniCover.SetImageDrawable(Android.Graphics.Drawables.Drawable.CreateFromPath(_miniVm.CoverSource));
                    else
                        _miniCover.SetImageResource(Resource.Drawable.cover_default);
                }
                catch
                {
                    try { _miniCover.SetImageResource(Resource.Drawable.cover_default); } catch { }
                }
            }
        });
    }

    // ═══════════ 系统栏适配 ═══════════

    private void FitSystemBars()
    {
        var root = FindViewById<View>(Android.Resource.Id.Content)!;
        ViewCompat.SetOnApplyWindowInsetsListener(root, new WindowInsetsListener((v, insets) =>
        {
            var bars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars()
                | WindowInsetsCompat.Type.DisplayCutout());

            var mainLayout = FindViewById<LinearLayout>(Resource.Id.main_layout);
            if (mainLayout != null)
                mainLayout.SetPadding(0, bars.Top, 0, 0);

            _bottomNav.SetPadding(
                _bottomNav.PaddingLeft,
                _bottomNav.PaddingTop,
                _bottomNav.PaddingRight,
                bars.Bottom);

            var overlay = FindViewById<View>(Resource.Id.overlay_container);
            if (overlay != null)
                overlay.SetPadding(0, 0, 0, bars.Bottom);

            if (_sidePanelContent != null)
                _sidePanelContent.SetPadding(0, bars.Top, 0, 0);

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

    private class DecorViewInsetsListener : Java.Lang.Object, Android.Views.View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(Android.Views.View v, WindowInsets insets)
        {
            _ = insets.GetInsetsIgnoringVisibility(WindowInsets.Type.SystemBars());
            return WindowInsets.Consumed;
        }
    }

    private class DecorViewInsetsListenerCompat : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat OnApplyWindowInsets(View v, WindowInsetsCompat insets)
        {
            _ = insets.GetInsetsIgnoringVisibility(WindowInsetsCompat.Type.SystemBars());
            return WindowInsetsCompat.Consumed;
        }
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

    private void ApplySavedTheme()
    {
        try
        {
            var prefs = GetSharedPreferences("catclaw_prefs", Android.Content.FileCreationMode.Private);
            int themeValue = prefs.GetInt("selected_theme", 0);
            int darkModeValue = prefs.GetInt("dark_mode_setting", 0);
            var theme = (AppTheme)themeValue;
            var darkModeSetting = (DarkModeSetting)darkModeValue;

            switch (darkModeSetting)
            {
                case DarkModeSetting.Light:
                    AppCompatDelegate.DefaultNightMode = AppCompatDelegate.ModeNightNo;
                    break;
                case DarkModeSetting.Dark:
                    AppCompatDelegate.DefaultNightMode = AppCompatDelegate.ModeNightYes;
                    break;
                case DarkModeSetting.FollowSystem:
                    AppCompatDelegate.DefaultNightMode = AppCompatDelegate.ModeNightFollowSystem;
                    break;
            }

            SetTheme(theme switch
            {
                AppTheme.Pink => Resource.Style.CatClawTheme_Pink,
                AppTheme.Blue => Resource.Style.CatClawTheme_Blue,
                AppTheme.Green => Resource.Style.CatClawTheme_Green,
                AppTheme.Orange => Resource.Style.CatClawTheme_Orange,
                _ => Resource.Style.CatClawTheme
            });
        }
        catch
        {
            SetTheme(Resource.Style.CatClawTheme);
        }
    }

    private void LockFontScale()
    {
        var config = new Android.Content.Res.Configuration(Resources!.Configuration);
        if (Math.Abs(config.FontScale - 1.0f) > 0.001f)
        {
            config.FontScale = 1.0f;
            var metrics = Resources.DisplayMetrics;
            Resources.UpdateConfiguration(config, metrics);
        }
    }

    public override void OnConfigurationChanged(Android.Content.Res.Configuration newConfig)
    {
        var themeService = MainApplication.Services.GetRequiredService<IThemeService>();
        bool isFollowSystem = themeService.DarkModeSetting == DarkModeSetting.FollowSystem;

        if (isFollowSystem)
            _skipNextRecreate = true;

        base.OnConfigurationChanged(newConfig);
        LockFontScale();

        if (isFollowSystem)
        {
            RefreshThemeViews();
            _skipNextRecreate = false;
        }
    }
}
