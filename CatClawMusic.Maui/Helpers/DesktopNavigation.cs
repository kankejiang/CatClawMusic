using CatClawMusic.Core.Interfaces;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Pages;

namespace CatClawMusic.Maui.Helpers;

/// <summary>
/// Shell / 桌面双通道导航统一入口。
/// Windows 桌面主窗口是 Window(DesktopBlankPage)（无 Shell），直接调 Shell.Current 会抛
/// "Unable to determine the current Shell instance"；Android 竖屏/横屏都在 Shell 内。
/// 所有跨页面跳转都应优先走 <see cref="TryGoToShell"/>（Shell 环境），失败后走
/// <see cref="OpenEmbedded"/>（桌面无 Shell：嵌入 DesktopBlankPage.MainArea 保留左侧导航栏）。
/// </summary>
public static class DesktopNavigation
{
    /// <summary>获取当前窗口的 Shell；无 Shell（桌面 Window(Page) 模式）返回 null（不抛异常）。</summary>
    public static Shell? TryGetShell()
    {
        try
        {
            // Shell.Current 在无 Shell 的窗口会抛 InvalidOperationException，必须 try 捕获。
            return Shell.Current;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>有 Shell 则 GoToAsync 并返回 true；无 Shell（Windows 桌面）返回 false。</summary>
    public static bool TryGoToShell(string route)
    {
        var shell = TryGetShell();
        if (shell == null) return false;
        _ = shell.GoToAsync(route);
        return true;
    }

    /// <summary>桌面无 Shell 环境：把页面 Content 摘出嵌入 DesktopBlankPage.MainArea。
    /// hideBack=true 时隐藏页面返回按钮（专辑/艺术家等终点详情页）；false 保留（设置子页等，返回=关闭嵌入）。</summary>
    public static void OpenEmbedded(ContentPage page, bool hideBack = true)
    {
        try
        {
            if (page == null) return;

            // 嵌入模式下隐藏左上角返回按钮（左侧导航栏提供全局返回）。
            // 返回按钮可能是根 Grid 直接子元素（BackButton）或 HeroCard 内嵌（专辑/艺术家详情页）。
            if (hideBack && page.Content is Grid root)
            {
                foreach (var back in root.Children.OfType<BackButton>())
                    back.IsVisible = false;
                if (root.Children.OfType<HeroCard>().FirstOrDefault() is HeroCard hero)
                    hero.ShowBackButton = false;
            }

            DesktopBlankPage.Instance?.OpenEmbeddedPage(page);
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopNavigation", $"[DesktopNav] OpenEmbedded failed: {ex.Message}");
        }
    }

    /// <summary>设置子页等二级页统一入口：有 Shell 走 GoToAsync；桌面（无 Shell）解析页面并嵌入，
    /// 保留返回按钮（BackButton 桌面感知：点击关闭嵌入恢复原 tab）。</summary>
    public static void GoOrEmbed(string route, Type pageType, bool hideBack = false)
    {
        if (TryGoToShell(route)) return;
        try
        {
            var page = (ContentPage)MauiProgram.Services.GetRequiredService(pageType);
            OpenEmbedded(page, hideBack);
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopNavigation", $"[DesktopNav] GoOrEmbed failed for {pageType.Name}: {ex.Message}");
        }
    }

    /// <summary>返回上一页：Shell 环境 GoToAsync("..")；桌面无 Shell 关闭嵌入恢复原 tab。</summary>
    public static void GoBack()
    {
        if (TryGoToShell("..")) return;
        CloseEmbedded();
    }

    /// <summary>PushAsync 的替代：Shell 环境压入导航栈；桌面（无 Shell）嵌入主区域并保留返回按钮。</summary>
    public static void PushEmbed(Page page)
    {
        var shell = TryGetShell();
        if (shell != null)
        {
            _ = shell.Navigation.PushAsync(page);
            return;
        }
        if (page is ContentPage contentPage)
            OpenEmbedded(contentPage, hideBack: false);
    }

    /// <summary>PopAsync 的替代：Shell 有导航栈则弹栈；桌面关闭嵌入恢复原 tab。</summary>
    public static void PopOrClose()
    {
        var shell = TryGetShell();
        if (shell != null && shell.Navigation.NavigationStack.Count > 1)
        {
            _ = shell.Navigation.PopAsync();
            return;
        }
        CloseEmbedded();
    }

    /// <summary>桌面播放页覆盖层专用：关闭 PlayerOverlay 覆盖层（Shell 环境无操作）。</summary>
    public static void ClosePlayerOverlay() => DesktopBlankPage.Instance?.ClosePlayerOverlay();

    /// <summary>桌面无 Shell 环境：关闭嵌入的子页面，恢复原 tab 内容。</summary>
    public static void CloseEmbedded() => DesktopBlankPage.Instance?.CloseEmbeddedPage();

    /// <summary>打开专辑详情（Android 走 Shell 路由；桌面嵌入主区域）。</summary>
    public static void OpenAlbumDetail(string title)
    {
        try
        {
            var page = MauiProgram.Services.GetRequiredService<AlbumDetailPage>();
            page.AlbumTitle = title; // setter 触发 LoadAsync
            OpenEmbedded(page);
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopNavigation", $"[DesktopNav] OpenAlbumDetail failed: {ex.Message}");
        }
    }

    /// <summary>打开艺术家详情（Android 走 Shell 路由；桌面嵌入主区域）。</summary>
    public static void OpenArtistDetail(string name)
    {
        try
        {
            var page = MauiProgram.Services.GetRequiredService<ArtistDetailPage>();
            page.ArtistName = name; // setter 触发 LoadArtistCommand
            OpenEmbedded(page);
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopNavigation", $"[DesktopNav] OpenArtistDetail failed: {ex.Message}");
        }
    }

    /// <summary>打开歌曲详情（Android 走 Shell 路由；桌面嵌入主区域）。</summary>
    public static void OpenSongDetail(string songId)
    {
        try
        {
            var page = MauiProgram.Services.GetRequiredService<SongDetailPage>();
            page.SongId = songId; // [QueryProperty] setter 触发加载
            OpenEmbedded(page);
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopNavigation", $"[DesktopNav] OpenSongDetail failed: {ex.Message}");
        }
    }

    /// <summary>打开歌单详情（Android 走 Shell 路由；桌面嵌入主区域）。</summary>
    public static void OpenPlaylistDetail(int playlistId, string name)
    {
        try
        {
            var page = MauiProgram.Services.GetRequiredService<PlaylistDetailPage>();
            page.PlaylistId = playlistId;
            page.PlaylistName = name;
            OpenEmbedded(page);
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopNavigation", $"[DesktopNav] OpenPlaylistDetail failed: {ex.Message}");
        }
    }
}
