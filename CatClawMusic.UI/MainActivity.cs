using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using CatClawMusic.Core.Interfaces;
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

[Activity(Theme = "@style/CatClaw.Splash", MainLauncher = true, ConfigurationChanges = Android.Content.PM.ConfigChanges.ScreenSize | Android.Content.PM.ConfigChanges.Orientation)]
public class MainActivity : AppCompatActivity, NavigationBarView.IOnItemSelectedListener
{
    private BottomNavigationView _bottomNav = null!;
    private View _toolbar = null!;
    private ImageButton _btnMenu = null!;
    private Fragment[] _tabFragments = null!;
    private int _currentTab = 0;

    // 迷你播放器
    private MaterialCardView _miniPlayer = null!;
    private ImageView _miniCover = null!;
    private TextView _miniTitle = null!, _miniArtist = null!;
    private ImageButton _miniPlayPause = null!, _miniPrev = null!, _miniNext = null!;

    // 侧滑面板
    private View _sidePanelOverlay = null!;
    private View _sidePanelMask = null!;
    private View _sidePanelContent = null!;
    private SettingsFragment? _settingsFragment;
    private bool _panelOpen;
    private float _panelSwipeStartX, _panelStartTx;
    private const float PanelSwipeThreshold = 100f;

    // 滑动切换
    private float _swipeStartX;
    private const float SwipeThreshold = 80f;

    public static MainActivity Instance { get; private set; } = null!;

    public NavigationService NavigationService =>
        (NavigationService)MainApplication.Services.GetRequiredService<Core.Interfaces.INavigationService>();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        SetTheme(Resource.Style.CatClawTheme);
        base.OnCreate(savedInstanceState);

        Instance = this;

        var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
        _ = db.EnsureInitializedAsync();

        SetContentView(Resource.Layout.activity_main);

        _toolbar = FindViewById<View>(Resource.Id.toolbar)!;
        _btnMenu = FindViewById<ImageButton>(Resource.Id.btn_menu)!;
        _bottomNav = FindViewById<BottomNavigationView>(Resource.Id.bottom_navigation)!;
        _bottomNav.SetOnItemSelectedListener(this);

        // ≡ 汉堡 → 设置页
        // ≡ 汉堡 → 弹出左侧设置面板
        _sidePanelOverlay = FindViewById<View>(Resource.Id.side_panel_overlay)!;
        _sidePanelMask = FindViewById<View>(Resource.Id.side_panel_mask)!;
        _sidePanelContent = FindViewById<View>(Resource.Id.side_panel_content)!;
        _btnMenu.Click += (s, e) => ToggleSidePanel();
        _sidePanelMask.Click += (s, e) => CloseSidePanel();

        // 设置面板内右滑收起
        _sidePanelContent.Touch += OnSidePanelTouch;
        _sidePanelMask.Touch += OnSidePanelTouch;

        _tabFragments = new Fragment[]
        {
            MainApplication.Services.GetRequiredService<NowPlayingFragment>(),   // 0: 播放页面
            MainApplication.Services.GetRequiredService<PlaylistFragment>(),    // 1: 播放列表
            MainApplication.Services.GetRequiredService<SearchFragment>(),      // 2: 搜索
            MainApplication.Services.GetRequiredService<LibraryFragment>()      // 3: 音乐库
        };

        NavigationService.Initialize(SupportFragmentManager, Resource.Id.fragment_container, _bottomNav);

        BindMiniPlayer();
        EnableSwipeNavigation();

        if (savedInstanceState == null)
        {
            // 启动时直接进入播放页面
            _currentTab = 0;
            _bottomNav.SelectedItemId = Resource.Id.nav_playing;
            SupportFragmentManager.BeginTransaction()
                .Add(Resource.Id.fragment_container, _tabFragments[0], "tab_0")
                .Commit();
            _toolbar.Visibility = ViewStates.Gone;
            _bottomNav.Visibility = ViewStates.Gone; // 播放页沉浸
        }
    }

    // ═══════════ 滑动切换 ═══════════

    private void EnableSwipeNavigation()
    {
        var container = FindViewById<View>(Resource.Id.fragment_container)!;
        container.Touch += OnContainerTouch;
    }

    private void OnContainerTouch(object? sender, View.TouchEventArgs e)
    {
        if (e?.Event == null) return;
        switch (e.Event.Action)
        {
            case MotionEventActions.Down:
                _swipeStartX = e.Event.GetX();
                break;
            case MotionEventActions.Up:
                float deltaX = e.Event.GetX() - _swipeStartX;
                if (Math.Abs(deltaX) > SwipeThreshold)
                {
                    if (deltaX < 0) SwitchTabAnimated(_currentTab + 1);  // 左滑→下一个
                    else SwitchTabAnimated(_currentTab - 1);             // 右滑→上一个
                }
                break;
        }
    }

    private void SwitchTabAnimated(int index)
    {
        index = Math.Clamp(index, 0, _tabFragments.Length - 1);
        if (index == _currentTab) return;

        int oldIndex = _currentTab;
        _currentTab = index;

        // 更新底部导航高亮
        _bottomNav.SelectedItemId = index switch
        {
            0 => Resource.Id.nav_playing, 1 => Resource.Id.nav_playlist,
            2 => Resource.Id.nav_search, 3 => Resource.Id.nav_library,
            _ => Resource.Id.nav_playing
        };

        // 切换 Fragment
        var ft = SupportFragmentManager.BeginTransaction();
        ft.SetTransition((int)(index > oldIndex
            ? FragmentTransit.FragmentOpen
            : FragmentTransit.FragmentClose));

        ft.Hide(_tabFragments[oldIndex]);

        string tag = $"tab_{index}";
        var fragment = SupportFragmentManager.FindFragmentByTag(tag);
        if (fragment == null)
            ft.Add(Resource.Id.fragment_container, _tabFragments[index], tag);
        else
            ft.Show(fragment);

        ft.Commit();

        _toolbar.Visibility = index == 0 ? ViewStates.Gone : ViewStates.Visible;
        _bottomNav.Visibility = index == 0 ? ViewStates.Gone : ViewStates.Visible;
    }

    // ═══════════ 导航回调 ═══════════

    public bool OnNavigationItemSelected(IMenuItem item)
    {
        int index = item.ItemId switch
        {
            Resource.Id.nav_playing => 0,
            Resource.Id.nav_playlist => 1,
            Resource.Id.nav_search => 2,
            Resource.Id.nav_library => 3,
            _ => 0
        };

        if (index == _currentTab) return true;

        int old = _currentTab;
        _currentTab = index;

        var ft = SupportFragmentManager.BeginTransaction();
        ft.Hide(_tabFragments[old]);

        string tag = $"tab_{index}";
        var fragment = SupportFragmentManager.FindFragmentByTag(tag);
        if (fragment == null)
            ft.Add(Resource.Id.fragment_container, _tabFragments[index], tag);
        else
            ft.Show(fragment);

        ft.Commit();

        _toolbar.Visibility = index == 0 ? ViewStates.Gone : ViewStates.Visible;
        _bottomNav.Visibility = index == 0 ? ViewStates.Gone : ViewStates.Visible;
        _miniPlayer.Visibility = index == 0 ? ViewStates.Gone : _miniPlayer.Visibility;
        return true;
    }

    // ═══════════ 左侧设置面板 ═══════════

    private void ToggleSidePanel()
    {
        if (_panelOpen) CloseSidePanel();
        else OpenSidePanel();
    }

    private void OpenSidePanel()
    {
        _panelOpen = true;
        _sidePanelOverlay.Visibility = ViewStates.Visible;

        // 面板从左滑入
        int screenW = Resources?.DisplayMetrics?.WidthPixels ?? 1080;
        _sidePanelContent.TranslationX = -screenW * 0.8f;
        _sidePanelContent.Animate().TranslationX(0).SetDuration(250).Start();

        // 遮罩淡入
        _sidePanelMask.Alpha = 0f;
        _sidePanelMask.Animate().Alpha(1f).SetDuration(250).Start();

        // 加载设置 Fragment
        if (_settingsFragment == null)
        {
            _settingsFragment = MainApplication.Services.GetRequiredService<SettingsFragment>();
            SupportFragmentManager.BeginTransaction()
                .Replace(Resource.Id.side_panel_content, _settingsFragment)
                .Commit();
        }
    }

    private void CloseSidePanel()
    {
        if (!_panelOpen) return;
        _panelOpen = false;

        int screenW = Resources?.DisplayMetrics?.WidthPixels ?? 1080;
        _sidePanelContent.Animate().TranslationX(-screenW * 0.8f).SetDuration(200)
            .WithEndAction(new Java.Lang.Runnable(() =>
                RunOnUiThread(() => _sidePanelOverlay.Visibility = ViewStates.Gone)))
            .Start();
        _sidePanelMask.Animate().Alpha(0f).SetDuration(200).Start();
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
                if (dx > 0) // 只允许向右拖（收起方向）
                    _sidePanelContent.TranslationX = _panelStartTx + dx;
                break;
            case MotionEventActions.Up:
                if (e.Event.GetX() - _panelSwipeStartX > PanelSwipeThreshold)
                    CloseSidePanel();
                else
                    _sidePanelContent.Animate().TranslationX(0).SetDuration(150).Start();
                break;
        }
    }

    public void SetBottomNavVisible(bool visible)
    {
        _bottomNav.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;
    }

    public void SetMiniPlayerVisible(bool visible)
    {
        var vm = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();
        _miniPlayer.Visibility = visible && vm.CurrentSong != null
            ? ViewStates.Visible : ViewStates.Gone;
    }

#pragma warning disable CA1422
    public override void OnBackPressed()
#pragma warning restore CA1422
    {
        if (SupportFragmentManager.BackStackEntryCount > 0)
        {
            bool isLastEntry = SupportFragmentManager.BackStackEntryCount == 1;
            SupportFragmentManager.PopBackStack();
            if (isLastEntry)
            {
                SetBottomNavVisible(true);
                SetMiniPlayerVisible(true);
                _bottomNav.SelectedItemId = _currentTab switch
                {
                    0 => Resource.Id.nav_playing, 1 => Resource.Id.nav_playlist,
                    2 => Resource.Id.nav_search, 3 => Resource.Id.nav_library,
                    _ => Resource.Id.nav_playing
                };
            }
        }
        else base.OnBackPressed();
    }

    public void SwitchTab(int index)
    {
        if (index >= 0 && index < _tabFragments.Length)
        {
            _bottomNav.SelectedItemId = index switch
            {
                0 => Resource.Id.nav_playing, 1 => Resource.Id.nav_playlist,
                2 => Resource.Id.nav_search, 3 => Resource.Id.nav_library,
                _ => Resource.Id.nav_playing
            };
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

    // ═══════════ 迷你播放器 ═══════════

    private void BindMiniPlayer()
    {
        _miniPlayer = FindViewById<MaterialCardView>(Resource.Id.mini_player)!;
        _miniCover = FindViewById<ImageView>(Resource.Id.mini_cover)!;
        _miniTitle = FindViewById<TextView>(Resource.Id.mini_title)!;
        _miniArtist = FindViewById<TextView>(Resource.Id.mini_artist)!;
        _miniPlayPause = FindViewById<ImageButton>(Resource.Id.mini_play_pause)!;
        _miniPrev = FindViewById<ImageButton>(Resource.Id.mini_prev)!;
        _miniNext = FindViewById<ImageButton>(Resource.Id.mini_next)!;

        var vm = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();
        var player = MainApplication.Services.GetRequiredService<IAudioPlayerService>();

        _miniPlayer.Click += (s, e) =>
        {
            if (vm.CurrentSong != null)
                SwitchTab(0);
        };
        _miniPlayPause.Click += (s, e) => vm.PlayPauseCommand.Execute(null);
        _miniPrev.Click += (s, e) => vm.PreviousCommand.Execute(null);
        _miniNext.Click += (s, e) => vm.NextCommand.Execute(null);

        vm.PropertyChanged += (s, e) => RunOnUiThread(() =>
        {
            var song = vm.CurrentSong;
            bool hasSong = song != null;
            bool onMainPage = SupportFragmentManager.BackStackEntryCount == 0;

            _miniPlayer.Visibility = hasSong && onMainPage
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
}
