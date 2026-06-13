using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.Activity;
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
using ALog = Android.Util.Log;

namespace CatClawMusic.UI;

/// <summary>
/// 应用主界面 Activity，管理 ViewPager2 + BottomNavigation 的 Tab 切换、
/// 迷你播放器、侧面板（设置）、Fragment 导航栈和系统栏沉浸式适配
/// </summary>
[Activity(Theme = "@style/CatClaw.Splash",
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode,
    ScreenOrientation = Android.Content.PM.ScreenOrientation.Portrait)]
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
    public void SetViewPagerSwipeEnabled(bool enabled) { if (_viewPager != null) _viewPager.UserInputEnabled = enabled; }
    public TabPagerAdapter? GetTabAdapter() => _tabAdapter;

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
    public static int StatusBarHeight { get; private set; }
    public static int NavBarHeight { get; private set; }

    public NavigationService NavigationService =>
        (NavigationService)MainApplication.Services.GetRequiredService<INavigationService>();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        LockFontScale();
        ApplySavedTheme();
        base.OnCreate(savedInstanceState);
        Instance = this;

        WindowCompat.SetDecorFitsSystemWindows(Window!, false);

        Window!.AddFlags(WindowManagerFlags.DrawsSystemBarBackgrounds);

        ApplySystemBarImmersive(_currentTab is 0 or 1);

        var insetsController = WindowCompat.GetInsetsController(Window!, Window!.DecorView);
        var isDark = (Resources?.Configuration?.UiMode & UiMode.NightMask) == UiMode.NightYes;
        insetsController.AppearanceLightStatusBars = _currentStatusBarLight;
        insetsController.AppearanceLightNavigationBars = !isDark;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            Window!.InsetsController!.SystemBarsBehavior =
                (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
        }
        else
        {
            insetsController.SystemBarsBehavior =
                WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
        }

        var db = MainApplication.Services.GetRequiredService<MusicDatabase>();

        SetContentView(Resource.Layout.activity_main);

        Window!.DecorView.ImportantForAutofill = Android.Views.ImportantForAutofill.NoExcludeDescendants;

        new Helpers.WaveformView(this!);

        DesktopLyricService.Instance.Initialize(this);

        var player = MainApplication.Services.GetRequiredService<IAudioPlayerService>();
        var queue = MainApplication.Services.GetRequiredService<PlayQueue>();
        var npVm = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();

        _miniProgressTimer = new System.Timers.Timer(500);
        _miniProgressTimer.Elapsed += (_, _) => RunOnUiThread(() =>
        {
            if (_miniProgress == null) return;
            if (!player.IsPlaying || player.Duration.TotalSeconds <= 0)
            {
                if (_miniProgress.LayoutParameters is FrameLayout.LayoutParams lp0)
                { lp0.Width = _miniPlayer?.Width ?? 0; lp0.Gravity = GravityFlags.Bottom; _miniProgress.LayoutParameters = lp0; }
                else
                    _miniProgress.LayoutParameters = new FrameLayout.LayoutParams(_miniPlayer?.Width ?? 0, 2, GravityFlags.Bottom);
                return;
            }
            var dur = player.Duration.TotalSeconds;
            var pos = player.CurrentPosition.TotalSeconds;
            var newWidth = (int)(_miniPlayer!.Width * (pos / dur));
            if (_miniProgress.LayoutParameters is FrameLayout.LayoutParams lp)
            { lp.Width = newWidth; lp.Gravity = GravityFlags.Bottom; _miniProgress.LayoutParameters = lp; }
            else
                _miniProgress.LayoutParameters = new FrameLayout.LayoutParams(newWidth, 2, GravityFlags.Bottom);
        });
        _miniProgressTimer.Start();
        var networkMusic = MainApplication.Services.GetService<INetworkMusicService>();
        var subsonic = MainApplication.Services.GetService<ISubsonicService>();
        /* 后台恢复播放状态，不阻塞 UI 线程；异常已内部处理不会导致闪退 */
        var restoreTask = Task.Run(() => PlaybackStateManager.RestoreAsync(player, db, queue, npVm, networkMusic, subsonic));

        _toolbar = FindViewById<View>(Resource.Id.toolbar)!;
        _btnMenu = FindViewById<ImageButton>(Resource.Id.btn_menu)!;
        _viewPager = FindViewById<ViewPager2>(Resource.Id.view_pager)!;
        _bottomNav = FindViewById<BottomNavigationView>(Resource.Id.bottom_navigation)!;

        _tabAdapter = new TabPagerAdapter(this);
        _viewPager.Adapter = _tabAdapter;
        _viewPager.OffscreenPageLimit = 4;
        _viewPager.UserInputEnabled = true;
        // Tab 切换：ViewPager2 页面滑动回调，仅响应用户手势滑动
        _viewPager.RegisterOnPageChangeCallback(new PageChangeCallback(index =>
        {
            if (!_isUserSwipe) return;
            _currentTab = index;
            UpdateNavSelection(index);
            UpdateTabUI(index);
            // 切换到全词页时自动滚动到当前歌词位置
            if (index == 0)
                _tabAdapter.FullLyricsFragment?.ScrollToCurrentPosition();
        }));

        // Tab 切换：底部导航栏点击回调
        _bottomNav.SetOnItemSelectedListener(new NavListener(index =>
        {
            if (_suppressNavListener) return;
            _isUserSwipe = false;                       // 临时禁用滑动检测，避免与程序切换冲突
            _viewPager.SetCurrentItem(index, true);
            _isUserSwipe = true;
            _currentTab = index;
            UpdateTabUI(index);
        }));

        NavigationService.Initialize(SupportFragmentManager, Resource.Id.overlay_container, _bottomNav);

        // Android 16 (Target 36) 适配：已在OnCreate中注册
        OnBackPressedDispatcher.AddCallback(this, new MainBackCallback(this));

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
        insetsController.AppearanceLightStatusBars = _currentStatusBarLight;
        insetsController.AppearanceLightNavigationBars = !isDark;

        ApplySystemBarImmersive(_currentTab is 0 or 1);

        var rootLayout = FindViewById<FrameLayout>(Resource.Id.root_layout);
        if (rootLayout != null)
            rootLayout.SetBackgroundColor(new Android.Graphics.Color(ResolveColor(Resource.Attribute.catClawPageBackground)));

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

    /// <summary>兼容 MIUI 等不走 OnBackPressedDispatcher 的场景</summary>
    [System.Obsolete]
    public override void OnBackPressed()
    {
        ALog.Debug("CatClaw.Nav", $"OnBackPressed: backStack={SupportFragmentManager.BackStackEntryCount}, panelOpen={_panelOpen}");
        if (_panelOpen) { CloseSidePanel(); return; }
        if (SupportFragmentManager.BackStackEntryCount > 0)
        {
            var topEntry = SupportFragmentManager.GetBackStackEntryAt(
                SupportFragmentManager.BackStackEntryCount - 1);
            var isLandscape = topEntry.Name == "LandscapeNowPlaying";
            SupportFragmentManager.PopBackStackImmediate();
            if (SupportFragmentManager.BackStackEntryCount == 0)
            {
                ALog.Debug("CatClaw.Nav", "OnBackPressed: restoring bottom nav + toolbar");
                SetOverlayOpen(false);
                var overlay = FindViewById<View>(Resource.Id.overlay_container);
                if (overlay != null) overlay.Visibility = ViewStates.Gone;
                if (isLandscape)
                {
                    UpdateTabUI(_currentTab);
                }
                else
                {
                    SetToolbarVisible(true);
                    SetBottomNavVisible(true);
                    SetMiniPlayerVisible(true);
                }
            }
        }
        else
        {
            base.OnBackPressed();
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

    /// <summary>同步底部导航栏选中项与当前 ViewPager 页面</summary>
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

    private LinearLayout? _mainLayout;
    private bool _lastImmersiveState;

    /// <summary>
    /// 更新 Tab 切换后的 UI 状态：沉浸式模式下隐藏工具栏和底部导航，非沉浸式恢复显示
    /// <para>Tab 0（全词页）和 Tab 1（播放页）为沉浸式，其余 Tab 显示工具栏</para>
    /// </summary>
    private void UpdateTabUI(int index)
    {
        bool immersive = index is 0 or 1;
        if (immersive == _lastImmersiveState && _mainLayout != null) return;
        _lastImmersiveState = immersive;
        _toolbar.Visibility = immersive ? ViewStates.Gone : ViewStates.Visible;
        _bottomNav.Visibility = immersive ? ViewStates.Gone : ViewStates.Visible;
        SetMiniPlayerVisible(!immersive);

        _mainLayout ??= FindViewById<LinearLayout>(Resource.Id.main_layout);
        if (_mainLayout != null)
            _mainLayout.SetPadding(0, immersive ? 0 : StatusBarHeight, 0, 0);

        ApplySystemBarImmersive(immersive);
    }

    public void SetBottomNavVisible(bool visible)
    {
        _bottomNav.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;
        ALog.Debug("CatClaw.Nav", $"SetBottomNavVisible({visible}): actual={_bottomNav.Visibility}");
    }

    public void UpdateTabUIForCurrentTab() => UpdateTabUI(_currentTab);

    /// <summary>同步底部导航栏选中项到当前 Tab（供 NavigationService 调用）</summary>
    public void UpdateNavSelectionForCurrentTab() => UpdateNavSelection(_currentTab);

    public void SetToolbarVisible(bool visible)
        => _toolbar.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;

    public void SetMiniPlayerVisible(bool visible)
    {
        if (_overlayOpen) { _miniPlayerWrapper.Visibility = ViewStates.Gone; return; }
        var vm = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();
        _miniPlayerWrapper.Visibility = visible && vm.CurrentSong != null && !_panelOpen
            ? ViewStates.Visible : ViewStates.Gone;
    }

    /// <summary>
    /// Android 16 (Target 36) 预测性返回手势回调。
    /// 优先关闭侧面板 → 弹出 Fragment 栈 → 系统默认返回行为。
    /// </summary>
    private class MainBackCallback : OnBackPressedCallback
    {
        private readonly WeakReference<MainActivity> _ref;
        public MainBackCallback(MainActivity activity) : base(true)
        {
            _ref = new WeakReference<MainActivity>(activity);
        }

        public override void HandleOnBackPressed()
        {
            if (!_ref.TryGetTarget(out var activity)) return;

            ALog.Debug("CatClaw.Nav", $"MainBackCallback: panelOpen={activity._panelOpen}, backStack={activity.SupportFragmentManager.BackStackEntryCount}");

            if (activity._panelOpen) { activity.CloseSidePanel(); return; }
            if (activity.SupportFragmentManager.BackStackEntryCount > 0)
            {
                var topEntry = activity.SupportFragmentManager.GetBackStackEntryAt(
                    activity.SupportFragmentManager.BackStackEntryCount - 1);
                var isLandscape = topEntry.Name == "LandscapeNowPlaying";
                activity.SupportFragmentManager.PopBackStackImmediate();
                ALog.Debug("CatClaw.Nav", $"MainBackCallback after pop: backStack={activity.SupportFragmentManager.BackStackEntryCount}");
                if (activity.SupportFragmentManager.BackStackEntryCount == 0)
                {
                    ALog.Debug("CatClaw.Nav", "MainBackCallback: restoring bottom nav + toolbar");
                    activity.SetOverlayOpen(false);
                    var overlay = activity.FindViewById<View>(Resource.Id.overlay_container);
                    if (overlay != null) overlay.Visibility = ViewStates.Gone;
                    if (isLandscape)
                    {
                        activity.UpdateTabUI(activity._currentTab);
                    }
                    else
                    {
                        activity.SetToolbarVisible(true);
                        activity.SetBottomNavVisible(true);
                        activity.SetMiniPlayerVisible(true);
                    }
                }
            }
            else
            {
                // 默认返回行为（退出 Activity）
                Enabled = false;
                activity.OnBackPressedDispatcher.OnBackPressed();
            }
        }
    }

    /// <summary>程序化切换到指定 Tab 页（供 NavigationService 等外部调用）</summary>
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

        // 清除侧面板中推入的所有 Fragment，防止关闭后残留的 BackStack 条目
        // 影响后续主区 Fragment 的返回逻辑（导致底部导航栏无法恢复）
        if (SupportFragmentManager.BackStackEntryCount > 0)
            SupportFragmentManager.PopBackStackImmediate(null, AndroidX.Fragment.App.FragmentManager.PopBackStackInclusive);

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

    private int _cachedNavBarColor;
    private int _currentNavBarColor;
    private bool _systemUiFlagsApplied;
    /// <summary>当前状态栏文字颜色模式：true=浅色(白字深底)，false=深色(黑字浅底)</summary>
    private bool _currentStatusBarLight = true;

    private void ApplySystemBarImmersive(bool immersive)
    {
        if (Window == null) return;

        // Android 16 (Target 36): 使用 WindowInsetsController 替代废弃的 SystemUiVisibility
        // SetDecorFitsSystemWindows(false) 已在 OnCreate 中设置，等效于旧的 Layout* 标志
        if (!_systemUiFlagsApplied)
        {
            var insetsCtrl = WindowCompat.GetInsetsController(Window!, Window!.DecorView);
            insetsCtrl.SystemBarsBehavior =
                WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
            _systemUiFlagsApplied = true;
        }

        Window.SetStatusBarColor(Android.Graphics.Color.Transparent);

        var insetsController = WindowCompat.GetInsetsController(Window!, Window!.DecorView);
        insetsController.AppearanceLightStatusBars = _currentStatusBarLight;

        var targetNavBarColor = immersive
            ? Android.Graphics.Color.Transparent
            : (_cachedNavBarColor == 0
                ? _cachedNavBarColor = ResolveColor(Resource.Attribute.catClawNavBarBackground)
                : _cachedNavBarColor);

        if (_currentNavBarColor != targetNavBarColor)
        {
            _currentNavBarColor = targetNavBarColor;
            Window.SetNavigationBarColor(new Android.Graphics.Color(targetNavBarColor));
        }
    }

    /// <summary>由 NowPlayingFragment 调用，根据封面提取的背景色亮度动态设置状态栏文字颜色</summary>
    /// <param name="useLight">true=浅色状态栏(白字深底)，false=深色状态栏(黑字浅底)</param>
    public void UpdateStatusBarAppearance(bool useLight)
    {
        if (_currentStatusBarLight == useLight) return;
        _currentStatusBarLight = useLight;
        try
        {
            var insetsController = WindowCompat.GetInsetsController(Window!, Window!.DecorView);
            insetsController.AppearanceLightStatusBars = useLight;
        }
        catch { }
    }

    private void FitSystemBars()
    {
        var root = FindViewById<View>(Android.Resource.Id.Content)!;
        ViewCompat.SetFitsSystemWindows(root, false);
        root.SetPadding(0, 0, 0, 0);

        ViewCompat.SetOnApplyWindowInsetsListener(root, new WindowInsetsListener((v, insets) =>
        {
            var systemBars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars()
                | WindowInsetsCompat.Type.DisplayCutout());
            
            var ime = insets.GetInsets(WindowInsetsCompat.Type.Ime());

            StatusBarHeight = systemBars.Top;
            NavBarHeight = systemBars.Bottom;

            v.SetPadding(0, 0, 0, 0);

            var mainLayout = FindViewById<LinearLayout>(Resource.Id.main_layout);
            if (mainLayout != null)
            {
                mainLayout.SetPadding(0, _currentTab is 0 or 1 ? 0 : systemBars.Top, 0, 0);
            }

            _bottomNav.SetPadding(
                _bottomNav.PaddingLeft,
                _bottomNav.PaddingTop,
                _bottomNav.PaddingRight,
                systemBars.Bottom);

            var overlay = FindViewById<View>(Resource.Id.overlay_container);
            if (overlay != null)
                overlay.SetPadding(0, 0, 0, Math.Max(systemBars.Bottom, ime.Bottom));

            if (_sidePanelContent != null)
                _sidePanelContent.SetPadding(0, systemBars.Top, 0, 0);

            return insets;
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

    private void ApplySavedTheme()
    {
        try
        {
            var prefs = GetSharedPreferences("catclaw_prefs", Android.Content.FileCreationMode.Private);
            int themeValue = prefs.GetInt("selected_theme", 0);
            int darkModeValue = prefs.GetInt("dark_mode_setting", 0);
            var theme = (AppTheme)themeValue;
            var darkModeSetting = (DarkModeSetting)darkModeValue;

            var themeService = MainApplication.Services.GetRequiredService<IThemeService>();
            bool isDark = darkModeSetting == DarkModeSetting.Dark ||
                (darkModeSetting == DarkModeSetting.FollowSystem && themeService.IsSystemDarkMode());

            var config = new Configuration(Resources!.Configuration!);
            config.UiMode = (config.UiMode & ~UiMode.NightMask)
                | (isDark ? UiMode.NightYes : UiMode.NightNo);
            Resources.UpdateConfiguration(config, Resources.DisplayMetrics);

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
        {
            _skipNextRecreate = true;
            // 更新 AppCompatDelegate 设置以跟随系统
            AppCompatDelegate.DefaultNightMode = AppCompatDelegate.ModeNightFollowSystem;
        }
        else
        {
            // 如果不是跟随系统模式，保持当前的主题设置
            bool isDark = themeService.IsEffectivelyDark();
            AppCompatDelegate.DefaultNightMode = isDark 
                ? AppCompatDelegate.ModeNightYes 
                : AppCompatDelegate.ModeNightNo;
        }

        base.OnConfigurationChanged(newConfig);
        LockFontScale();

        if (isFollowSystem)
        {
            RefreshThemeViews();
            _skipNextRecreate = false;
        }
    }
}
