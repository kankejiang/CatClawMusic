using Android.Views;
using AndroidX.Fragment.App;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.UI.Fragments;
using Google.Android.Material.BottomNavigation;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Services;

/// <summary>页面导航服务，管理 Fragment 推入/弹出与底部导航栏联动</summary>
public class NavigationService : INavigationService
{
    private FragmentManager? _fm;
    private int _overlayContainerId; // 主区子页面容器
    private BottomNavigationView? _bottomNav;
    private int? _sidePanelContainerId;

    /// <summary>初始化导航服务，绑定 FragmentManager、主页面容器和底部导航</summary>
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

    /// <summary>根据路由名称推入对应的 Fragment 页面，主区推入时隐藏底部导航栏和工具栏</summary>
    public void PushFragment(string route, Dictionary<string, object>? parameters = null)
    {
        if (_fm == null) return;

        Fragment fragment = route switch
        {
            "PlaylistDetail" => CreatePlaylistDetail(parameters),
            "RemoteMusic" => MainApplication.Services.GetRequiredService<RemoteMusicFragment>(),
            "WebDavSettings" => MainApplication.Services.GetRequiredService<WebDavSettingsFragment>(),
            "NavidromeSettings" => MainApplication.Services.GetRequiredService<NavidromeSettingsFragment>(),
            "SmbSettings" => MainApplication.Services.GetRequiredService<SmbSettingsFragment>(),
            "MusicFolderSettings" => MainApplication.Services.GetRequiredService<MusicFolderSettingsFragment>(),
            "LocalMusicSettings" => MainApplication.Services.GetRequiredService<LocalMusicSettingsFragment>(),
            "Settings" => MainApplication.Services.GetRequiredService<SettingsFragment>(),
            "GeneralSettings" => MainApplication.Services.GetRequiredService<GeneralSettingsFragment>(),
            "DesktopLyric" => MainApplication.Services.GetRequiredService<DesktopLyricFragment>(),
            "PluginManagement" => MainApplication.Services.GetRequiredService<PluginManagementFragment>(),
            "AiSettings" => MainApplication.Services.GetRequiredService<AiSettingsFragment>(),
            _ => throw new ArgumentException($"Unknown route: {route}")
        };

        int containerId = _sidePanelContainerId ?? _overlayContainerId;

        // 主区推入：隐藏底部导航 + 工具栏 + 迷你播放器，显示 overlay
        if (_sidePanelContainerId == null)
        {
            _bottomNav?.Visibility = ViewStates.Gone;
            MainActivity.Instance?.SetToolbarVisible(false);
            MainActivity.Instance?.SetMiniPlayerVisible(false);
            MainActivity.Instance?.SetOverlayOpen(true);
            MainActivity.Instance?.RunOnUiThread(() =>
            {
                var overlay = MainActivity.Instance?.FindViewById<View>(_overlayContainerId);
                if (overlay != null) overlay.Visibility = ViewStates.Visible;
            });
        }

        _fm.BeginTransaction()
            .SetCustomAnimations(Resource.Animation.slide_in_right, Resource.Animation.slide_out_left,
                                  Resource.Animation.slide_in_left, Resource.Animation.slide_out_right)
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

    /// <summary>弹出当前页面，返回上一级。当返回栈清空时恢复底部导航栏和工具栏</summary>
    public void GoBack()
    {
        if (_fm == null) return;
        _fm.PopBackStack();

        if (_fm.BackStackEntryCount <= 1 && _sidePanelContainerId == null)
        {
            var overlay = MainActivity.Instance?.FindViewById<View>(_overlayContainerId);
            if (overlay != null) overlay.Visibility = ViewStates.Gone;
            if (_bottomNav != null) _bottomNav.Visibility = ViewStates.Visible;
            MainActivity.Instance?.SetOverlayOpen(false);
            MainActivity.Instance?.SetToolbarVisible(true);
            MainActivity.Instance?.SetMiniPlayerVisible(true);
        }
    }

    /// <summary>切换到指定 Tab 页</summary>
    public void SwitchTab(int tabIndex)
    {
        MainActivity.Instance?.SwitchTab(tabIndex);
    }
}
