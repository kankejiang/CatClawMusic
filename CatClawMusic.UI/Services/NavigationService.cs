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
    private int _overlayContainerId; // 主区子页面容器
    private BottomNavigationView? _bottomNav;
    private int? _sidePanelContainerId;

    public void Initialize(FragmentManager fm, int overlayContainerId, BottomNavigationView bottomNav)
    {
        _fm = fm;
        _overlayContainerId = overlayContainerId;
        _bottomNav = bottomNav;
    }

    /// <summary>启用侧面板导航（推入的 Fragment 放进面板）</summary>
    public void EnterSidePanelMode(int sidePanelContainerId)
    {
        _sidePanelContainerId = sidePanelContainerId;
    }

    /// <summary>退出侧面板导航</summary>
    public void ExitSidePanelMode()
    {
        _sidePanelContainerId = null;
    }

    public void PushFragment(string route, Dictionary<string, object>? parameters = null)
    {
        if (_fm == null) return;

        Fragment fragment = route switch
        {
            "PlaylistDetail" => CreatePlaylistDetail(parameters),
            "WebDavSettings" => MainApplication.Services.GetRequiredService<WebDavSettingsFragment>(),
            "NavidromeSettings" => MainApplication.Services.GetRequiredService<NavidromeSettingsFragment>(),
            "MusicFolderSettings" => MainApplication.Services.GetRequiredService<MusicFolderSettingsFragment>(),
            "Settings" => MainApplication.Services.GetRequiredService<SettingsFragment>(),
            _ => throw new ArgumentException($"Unknown route: {route}")
        };

        int containerId = _sidePanelContainerId ?? _overlayContainerId;

        // 主区推入：隐藏底部导航 + 显示 overlay
        if (_sidePanelContainerId == null)
        {
            _bottomNav?.Visibility = ViewStates.Gone;
            MainActivity.Instance?.SetMiniPlayerVisible(false);
            MainActivity.Instance?.RunOnUiThread(() =>
            {
                var overlay = MainActivity.Instance?.FindViewById<View>(_overlayContainerId);
                if (overlay != null) overlay.Visibility = ViewStates.Visible;
            });
        }

        _fm.BeginTransaction()
            .Replace(containerId, fragment, route)
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
        if (_fm == null) return;
        _fm.PopBackStack();

        if (_fm.BackStackEntryCount == 0 && _sidePanelContainerId == null)
        {
            MainActivity.Instance?.RunOnUiThread(() =>
            {
                var overlay = MainActivity.Instance?.FindViewById<View>(_overlayContainerId);
                if (overlay != null) overlay.Visibility = ViewStates.Gone;
                _bottomNav!.Visibility = ViewStates.Visible;
                MainActivity.Instance?.SetMiniPlayerVisible(true);
            });
        }
    }

    public void SwitchTab(int tabIndex)
    {
        MainActivity.Instance?.SwitchTab(tabIndex);
    }
}
