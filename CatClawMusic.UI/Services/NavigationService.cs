using Android.Views;
using AndroidX.Fragment.App;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.UI.Fragments;
using Google.Android.Material.BottomNavigation;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Services;

public class NavigationService : INavigationService
{
    private FragmentManager? _fm;
    private int _containerId;
    private BottomNavigationView? _bottomNav;

    public void Initialize(FragmentManager fm, int containerId, BottomNavigationView bottomNav)
    {
        _fm = fm;
        _containerId = containerId;
        _bottomNav = bottomNav;
    }

    public void PushFragment(string route, Dictionary<string, object>? parameters = null)
    {
        if (_fm == null) return;

        Fragment fragment = route switch
        {
            "NowPlaying" => MainApplication.Services.GetRequiredService<NowPlayingFragment>(),
            "PlaylistDetail" => CreatePlaylistDetail(parameters),
            "WebDavSettings" => MainApplication.Services.GetRequiredService<WebDavSettingsFragment>(),
            "NavidromeSettings" => MainApplication.Services.GetRequiredService<NavidromeSettingsFragment>(),
            _ => throw new ArgumentException($"Unknown route: {route}")
        };

        _bottomNav?.Visibility = ViewStates.Gone;

        _fm.BeginTransaction()
            .Replace(_containerId, fragment, route)
            .AddToBackStack(route)
            .Commit();
    }

    private Fragment CreatePlaylistDetail(Dictionary<string, object>? parameters)
    {
        var fragment = MainApplication.Services.GetRequiredService<PlaylistDetailFragment>();
        if (parameters != null)
        {
            var args = new Android.OS.Bundle();
            if (parameters.TryGetValue("playlistId", out var id))
                args.PutInt("playlistId", Convert.ToInt32(id));
            if (parameters.TryGetValue("playlistName", out var name))
                args.PutString("playlistName", name?.ToString());
            fragment.Arguments = args;
        }
        return fragment;
    }

    public void GoBack()
    {
        _fm?.PopBackStack();
    }

    public void SwitchTab(int tabIndex)
    {
        MainActivity.Instance?.SwitchTab(tabIndex);
    }
}
