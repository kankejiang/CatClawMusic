using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.AppCompat.App;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Data;
using CatClawMusic.UI.Fragments;
using CatClawMusic.UI.Platforms.Android;
using CatClawMusic.UI.Services;
using Google.Android.Material.BottomNavigation;
using Google.Android.Material.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI;

[Activity(Theme = "@style/CatClaw.Splash", MainLauncher = true, ConfigurationChanges = Android.Content.PM.ConfigChanges.ScreenSize | Android.Content.PM.ConfigChanges.Orientation)]
public class MainActivity : AppCompatActivity, NavigationBarView.IOnItemSelectedListener
{
    private BottomNavigationView _bottomNav = null!;
    private Fragment[] _tabFragments = null!;
    private int _currentTab = 0;

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

#pragma warning disable CA1422
    public override void OnBackPressed()
#pragma warning restore CA1422
    {
        if (SupportFragmentManager.BackStackEntryCount > 0)
        {
            SupportFragmentManager.PopBackStack();
            if (SupportFragmentManager.BackStackEntryCount == 0)
            {
                SetBottomNavVisible(true);
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
}
