using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.Pages;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 导航服务实现 - 封装 Shell 导航逻辑（桌面无 Shell 时走嵌入回退）
/// </summary>
public class NavigationService : INavigationService
{
    /// <summary>桌面端可嵌入打开的路由 → 页面类型映射（MAUI 无按路由取类型的公开 API，此处显式登记）。</summary>
    private static readonly Dictionary<string, Type> _embeddedRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["webviewlogin"] = typeof(WebViewLoginPage),
        // 需要桌面端嵌入打开的注册路由在此扩展
    };

    /// <summary>异步导航到指定路由页面</summary>
    /// <param name="route">目标路由地址</param>
    /// <param name="parameters">传递给目标页面的参数字典；可为空</param>
    public async Task NavigateToAsync(string route, Dictionary<string, object>? parameters = null)
    {
        try
        {
            var shell = DesktopNavigation.TryGetShell();
            if (shell == null)
            {
                // 桌面无 Shell（窗口直连不走导航栈）：解析注册路由为页面类型并嵌入主区域打开。
                // 支持带查询参数的路由（如 "webviewlogin?platform=netease"，参数注入 IQueryAttributable）。
                OpenEmbeddedByRoute(route, parameters);
                return;
            }

            if (parameters != null)
            {
                await shell.GoToAsync(route, parameters);
            }
            else
            {
                await shell.GoToAsync(route);
            }
        }
        catch (Exception ex)
        {
            Log.Debug("NavigationService", $"Navigation error: {ex.Message}");
        }
    }

    /// <summary>
    /// 桌面嵌入回退：按路由映射表解析页面类型，拆分查询参数注入 <see cref="IQueryAttributable"/>，
    /// 然后嵌入主区域打开（返回由左侧导航栏提供）。
    /// </summary>
    private static void OpenEmbeddedByRoute(string route, Dictionary<string, object>? parameters)
    {
        try
        {
            // 拆分路由与查询参数："webviewlogin?platform=netease" → "webviewlogin" + {platform: netease}
            var parts = route.Split('?', 2);
            var routeName = parts[0].TrimStart('/');
            var query = new Dictionary<string, object>();
            if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1]))
            {
                foreach (var pair in parts[1].Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = pair.Split('=', 2);
                    if (kv.Length == 2)
                        query[kv[0]] = System.Uri.UnescapeDataString(kv[1]);
                }
            }
            if (parameters != null)
            {
                foreach (var (k, v) in parameters)
                    query[k] = v;
            }

            if (!_embeddedRoutes.TryGetValue(routeName, out var pageType))
            {
                Log.Debug("NavigationService", $"OpenEmbeddedByRoute: 未登记桌面嵌入路由 {routeName}");
                return;
            }

            var page = (Microsoft.Maui.Controls.Page?)MauiProgram.Services.GetRequiredService(pageType);
            if (page == null) return;

            // 路由参数注入（与 Shell 导航事务等价：WebViewLoginViewModel 靠 IQueryAttributable 收 platform）
            if (page.BindingContext is IQueryAttributable attributable && query.Count > 0)
                attributable.ApplyQueryAttributes(query);

            if (page is Microsoft.Maui.Controls.ContentPage contentPage)
                DesktopNavigation.OpenEmbedded(contentPage);
            else
                Log.Debug("NavigationService", $"OpenEmbeddedByRoute: {routeName} 非 ContentPage，跳过");
        }
        catch (Exception ex)
        {
            Log.Debug("NavigationService", $"OpenEmbeddedByRoute error: {ex.Message}");
        }
    }

    /// <summary>异步返回上一页</summary>
    public async Task GoBackAsync()
    {
        try
        {
            DesktopNavigation.GoBack();
        }
        catch (Exception ex)
        {
            Log.Debug("NavigationService", $"Navigation back error: {ex.Message}");
        }
    }

    /// <summary>切换底部 TabBar 的当前页签</summary>
    /// <param name="tabIndex">目标页签索引（从 0 开始）</param>
    public void SwitchTab(int tabIndex)
    {
        try
        {
            var shell = DesktopNavigation.TryGetShell();
            if (shell?.CurrentItem is TabBar tabBar && tabBar.Items.Count > tabIndex)
            {
                shell.CurrentItem = tabBar.Items[tabIndex];
            }
        }
        catch (Exception ex)
        {
            Log.Debug("NavigationService", $"SwitchTab error: {ex.Message}");
        }
    }

    /// <summary>
    /// 导航到专辑详情页面（带参数）
    /// </summary>
    /// <param name="album">专辑对象</param>
    public async Task NavigateToAlbumDetailAsync(Album album)
    {
        var parameters = new Dictionary<string, object>
        {
            { "Album", album }
        };

        await NavigateToAsync("//albumdetail", parameters);
    }

    /// <summary>
    /// 导航到艺术家详情页面
    /// </summary>
    /// <param name="artist">艺术家对象</param>
    public async Task NavigateToArtistDetailAsync(Artist artist)
    {
        var parameters = new Dictionary<string, object>
        {
            { "Artist", artist }
        };

        await NavigateToAsync("//artistdetail", parameters);
    }

    /// <summary>
    /// 导航到播放列表详情页面
    /// </summary>
    /// <param name="playlist">播放列表对象</param>
    public async Task NavigateToPlaylistDetailAsync(Playlist playlist)
    {
        var parameters = new Dictionary<string, object>
        {
            { "Playlist", playlist }
        };

        await NavigateToAsync("//playlistdetail", parameters);
    }
}
