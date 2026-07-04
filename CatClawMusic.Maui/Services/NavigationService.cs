using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using System.Threading.Tasks;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 导航服务实现 - 封装 Shell 导航逻辑
/// </summary>
public class NavigationService : INavigationService
{
    /// <summary>异步导航到指定路由页面</summary>
    /// <param name="route">目标路由地址</param>
    /// <param name="parameters">传递给目标页面的参数字典；可为空</param>
    public async Task NavigateToAsync(string route, Dictionary<string, object>? parameters = null)
    {
        try
        {
            if (parameters != null)
            {
                await Shell.Current.GoToAsync(route, parameters);
            }
            else
            {
                await Shell.Current.GoToAsync(route);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Navigation error: {ex.Message}");
        }
    }

    /// <summary>异步返回上一页</summary>
    public async Task GoBackAsync()
    {
        try
        {
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Navigation back error: {ex.Message}");
        }
    }

    /// <summary>切换底部 TabBar 的当前页签</summary>
    /// <param name="tabIndex">目标页签索引（从 0 开始）</param>
    public void SwitchTab(int tabIndex)
    {
        try
        {
            var shell = Shell.Current;
            if (shell?.CurrentItem is TabBar tabBar && tabBar.Items.Count > tabIndex)
            {
                shell.CurrentItem = tabBar.Items[tabIndex];
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SwitchTab error: {ex.Message}");
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
