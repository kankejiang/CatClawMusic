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
    private Fragment[] _tabFragments = null!;
    private int _currentTab = 0;

    // 迷你播放器
    private MaterialCardView _miniPlayer = null!;
    private ImageView _miniCover = null!;
    private TextView _miniTitle = null!, _miniArtist = null!;
    private ImageButton _miniPlayPause = null!, _miniPrev = null!, _miniNext = null!;

    public static MainActivity Instance { get; private set; } = null!;

    public NavigationService NavigationService =>
        (NavigationService)MainApplication.Services.GetRequiredService<Core.Interfaces.INavigationService>();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // 从启动画面主题切换到 Material3 主主题，否则 BottomNavigationView 等 Material 组件无法加载
        SetTheme(Resource.Style.CatClawTheme);
        base.OnCreate(savedInstanceState);

        Instance = this;

        var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
        _ = db.EnsureInitializedAsync();

        SetContentView(Resource.Layout.activity_main);

        _bottomNav = FindViewById<BottomNavigationView>(Resource.Id.bottom_navigation)!;
        _bottomNav.SetOnItemSelectedListener(this);

        _tabFragments = new Fragment[]
        {
            MainApplication.Services.GetRequiredService<LibraryFragment>(),
            MainApplication.Services.GetRequiredService<PlaylistFragment>(),
            MainApplication.Services.GetRequiredService<SearchFragment>(),
            MainApplication.Services.GetRequiredService<SettingsFragment>()
        };

        NavigationService.Initialize(SupportFragmentManager, Resource.Id.fragment_container, _bottomNav);

        // 迷你播放器绑定
        BindMiniPlayer();

        if (savedInstanceState == null)
        {
            SupportFragmentManager.BeginTransaction()
                .Add(Resource.Id.fragment_container, _tabFragments[0], "tab_0")
                .Commit();
        }
    }

    public bool OnNavigationItemSelected(IMenuItem item)
    {
        int index = item.ItemId switch
        {
            Resource.Id.nav_library => 0,
            Resource.Id.nav_playlist => 1,
            Resource.Id.nav_search => 2,
            Resource.Id.nav_settings => 3,
            _ => 0
        };

        if (index == _currentTab) return true;

        var ft = SupportFragmentManager.BeginTransaction();
        if (_currentTab >= 0 && _currentTab < _tabFragments.Length)
            ft.Hide(_tabFragments[_currentTab]);

        string tag = $"tab_{index}";
        var fragment = SupportFragmentManager.FindFragmentByTag(tag);
        if (fragment == null)
            ft.Add(Resource.Id.fragment_container, _tabFragments[index], tag);
        else
            ft.Show(fragment);

        _currentTab = index;
        ft.Commit();
        return true;
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
            // PopBackStack() 是异步的，必须在此调用前判断是否为最后一个条目
            bool isLastEntry = SupportFragmentManager.BackStackEntryCount == 1;
            SupportFragmentManager.PopBackStack();
            if (isLastEntry)
            {
                SetBottomNavVisible(true);
                SetMiniPlayerVisible(true);
                _bottomNav.SelectedItemId = _currentTab switch
                {
                    0 => Resource.Id.nav_library, 1 => Resource.Id.nav_playlist,
                    2 => Resource.Id.nav_search, 3 => Resource.Id.nav_settings,
                    _ => Resource.Id.nav_library
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
                0 => Resource.Id.nav_library, 1 => Resource.Id.nav_playlist,
                2 => Resource.Id.nav_search, 3 => Resource.Id.nav_settings,
                _ => Resource.Id.nav_library
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

        // 点击迷你播放器→打开 NowPlaying
        _miniPlayer.Click += (s, e) =>
        {
            if (vm.CurrentSong != null)
                NavigationService.PushFragment("NowPlaying");
        };

        // 控制按钮
        _miniPlayPause.Click += (s, e) => vm.PlayPauseCommand.Execute(null);
        _miniPrev.Click += (s, e) => vm.PreviousCommand.Execute(null);
        _miniNext.Click += (s, e) => vm.NextCommand.Execute(null);

        // 监听 ViewModel 更新 UI（仅主页时显示）
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

            // 封面
            if (!string.IsNullOrEmpty(vm.CoverSource))
                _miniCover.SetImageDrawable(Android.Graphics.Drawables.Drawable.CreateFromPath(vm.CoverSource));
        });
    }
}
