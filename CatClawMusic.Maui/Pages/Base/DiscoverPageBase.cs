using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages.Base;

/// <summary>
/// 发现页（Search / DesktopDiscover）共享的事件处理基类。
/// 横竖屏 Hero 轮播、设置抽屉等差异部分由子类自行实现；本基类只包含两者完全相同或可参数化的逻辑。
/// </summary>
public abstract class DiscoverPageBase : ContentPage
{
    // === 子类提供的依赖 ===

    protected abstract SearchViewModel Vm { get; }
    protected abstract PlayQueue Queue { get; }
    protected abstract IAudioPlayerService AudioPlayer { get; }
    protected abstract ListeningStatsView StatsView { get; }

    // === 子类提供的控件引用 ===

    /// <summary>聊天消息列表 CollectionView，用于滚动到最新消息和加载历史。</summary>
    protected abstract CollectionView ChatMessagesListControl { get; }

    /// <summary>搜索框 Entry，用于 Focus/Unfocus/清空。</summary>
    protected abstract Entry SearchBoxControl { get; }

    /// <summary>分类标签页控件数组（Border, Label），按 [推荐, 排行, 歌手, 专辑, 报告] 顺序。</summary>
    protected abstract (Border border, Label label)[] TabControls { get; }

    // === Tab 切换 ===

    protected void OnCategoryTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string p && int.TryParse(p, out int idx))
        {
            Vm.CurrentCategory = idx;
            UpdateTabVisualState(idx);
            // 切换到"报告"Tab 时触发统计数据加载
            if (idx == 4)
            {
                _ = StatsView.LoadAsync();
            }
        }
    }

    protected void UpdateTabVisualState(int selectedIndex)
    {
        var primary = (Color)(Application.Current?.Resources["PrimaryColor"] ?? Colors.Purple);
        var cardBg = (Color)(Application.Current?.Resources["CardBackgroundColor"] ?? Colors.Transparent);
        var textSecondary = (Color)(Application.Current?.Resources["TextSecondaryColor"] ?? Colors.Gray);

        for (int i = 0; i < TabControls.Length; i++)
        {
            TabControls[i].border.BackgroundColor = selectedIndex == i ? primary : cardBg;
            TabControls[i].label.TextColor = selectedIndex == i ? Colors.White : textSecondary;
        }
    }

    // === 搜索框 ===

    protected void OnSearchToggleClicked(object? sender, EventArgs e)
    {
        Vm.IsSearchOpen = !Vm.IsSearchOpen;
        if (Vm.IsSearchOpen)
        {
            SearchBoxControl.Focus();
        }
        else
        {
            SearchBoxControl.Unfocus();
            SearchBoxControl.Text = "";
            Vm.ClearSearchDropdown();
        }
    }

    protected void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        Vm.SearchQuery = e.NewTextValue ?? "";
    }

    protected void OnSearchCompleted(object? sender, EventArgs e)
    {
        var entry = sender as Entry;
        Vm.SearchQuery = entry?.Text?.Trim() ?? "";
        entry?.Unfocus();
    }

    protected void OnClearSearchClicked(object? sender, EventArgs e)
    {
        SearchBoxControl.Text = "";
        Vm.ClearSearchDropdown();
    }

    // === 搜索结果导航 ===

    protected async void OnSearchArtistSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SearchArtistItem artist) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        SearchBoxControl.Text = "";
        Vm.ClearSearchDropdown();
        await Shell.Current.GoToAsync($"artistdetail?artistName={Uri.EscapeDataString(artist.Name)}");
    }

    protected async void OnSearchAlbumSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SearchAlbumItem album) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        SearchBoxControl.Text = "";
        Vm.ClearSearchDropdown();
        await Shell.Current.GoToAsync($"albumdetail?title={Uri.EscapeDataString(album.Title)}");
    }

    // === 歌曲更多操作 ===

    protected async void OnMoreTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not Song song) return;
        var action = await DisplayActionSheet(song.Title, "取消", null,
            "添加到播放队列", "添加到歌单", "查看歌曲详情", "分享");
        await HandleSongAction(action, song);
    }

    protected async Task HandleSongAction(string? action, Song song)
    {
        switch (action)
        {
            case "查看歌曲详情":
                await Shell.Current.GoToAsync($"songdetail?songId={song.Id}");
                break;
            case "添加到播放队列":
                // TODO: 添加到播放队列
                break;
            case "添加到歌单":
                // TODO: 添加到歌单
                break;
        }
    }

    // === 歌曲选择 ===

    protected async void OnDailySongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Song song) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        await PlaySongAsync(song, Vm.DailyRecommendSongs.ToList());
    }

    protected async void OnTopPlayedSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Song song) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        await PlaySongAsync(song, Vm.TopPlayedSongs.ToList());
    }

    protected async void OnFavoriteSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Song song) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        await PlaySongAsync(song, Vm.FavoriteSongs.ToList());
    }

    // === 排行榜列表（最多播放 / 我的最爱） ===

    protected async void OnRankItemTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border && border.BindingContext is Song song)
        {
            await PlaySongAsync(song, Vm.TopPlayedSongs.ToList());
        }
    }

    protected async void OnRankPlayTapped(object? sender, EventArgs e)
    {
        if (sender is ImageButton btn && btn.BindingContext is Song song)
        {
            await PlaySongAsync(song, Vm.TopPlayedSongs.ToList());
        }
    }

    protected async void OnFavItemTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border && border.BindingContext is Song song)
        {
            await PlaySongAsync(song, Vm.FavoriteSongs.ToList());
        }
    }

    protected async void OnFavPlayTapped(object? sender, EventArgs e)
    {
        if (sender is ImageButton btn && btn.BindingContext is Song song)
        {
            await PlaySongAsync(song, Vm.FavoriteSongs.ToList());
        }
    }

    /// <summary>排名标签着色：前 3 名用主色，其余用次要色。</summary>
    protected void OnRankItemBindingContextChanged(object? sender, EventArgs e)
    {
        if (sender is not Border border) return;
        if (border.BindingContext is not Song song) return;

        var list = Vm.TopPlayedSongs;
        var index = list.IndexOf(song);
        if (index < 0) return;

        var rank = index + 1;
        if (border.Content is Grid grid && grid.Children.Count > 0 && grid.Children[0] is Label rankLabel)
        {
            rankLabel.Text = rank.ToString();
            rankLabel.TextColor = rank <= 3
                ? (Color)Application.Current?.Resources["PrimaryColor"]!
                : (Color)Application.Current?.Resources["TextHintColor"]!;
        }
    }

    // === 艺术家 / 专辑导航 ===

    protected async void OnArtistSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SearchArtistItem artist) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        await Shell.Current.GoToAsync($"artistdetail?artistName={Uri.EscapeDataString(artist.Name)}");
    }

    protected async void OnAlbumSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SearchAlbumItem album) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        await Shell.Current.GoToAsync($"albumdetail?title={Uri.EscapeDataString(album.Title)}");
    }

    // === AI 入口 ===

    protected void OnAiEntryTapped(object? sender, TappedEventArgs e)
    {
        Vm.IsSearchOpen = false;
        SearchBoxControl.Unfocus();
        SearchBoxControl.Text = "";
        Vm.ClearSearchDropdown();
        Vm.EnterChatModeCommand.Execute(null);
    }

    // === 聊天 ===

    protected void OnChatBackClicked(object? sender, EventArgs e)
    {
        Vm.ExitChatModeCommand.Execute(null);
    }

    protected void OnChatInputCompleted(object? sender, EventArgs e)
    {
        _ = Vm.SendMessageCommand.ExecuteAsync(null);
    }

    protected void OnSendClicked(object? sender, EventArgs e)
    {
        _ = Vm.SendMessageCommand.ExecuteAsync(null);
    }

    protected void OnChatHistoryLoaded(object? sender, ChatHistoryLoadedEventArgs e)
    {
        // 倒序模式下：首次加载滚到 index 0（最新消息，翻转后视觉在底部）
        // 加载更多历史时无需处理（末尾追加不改变已有项位置）
        if (e is { IsInitialLoad: true, ScrollToEnd: true } && Vm.ChatMessages.Count > 0)
        {
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), () =>
            {
                ChatMessagesListControl.ScrollTo(0, position: ScrollToPosition.Start, animate: false);
            });
        }
    }

    protected void ScrollToLatestMessage()
    {
        // 倒序模式：最新消息在 index 0，翻转后视觉在底部
        if (Vm.ChatMessages.Count > 0)
        {
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), () =>
            {
                ChatMessagesListControl.ScrollTo(0, position: ScrollToPosition.Start, animate: true);
            });
        }
    }

    /// <summary>聊天消息列表滚动时检测是否需要加载更多历史记录
    /// 倒序+翻转模式：视觉底部 = 数据源末尾 = 最旧消息，滚到底部时加载更旧的历史</summary>
    protected async void OnChatMessagesScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        // 翻转后 VerticalOffset 语义反转：滚到视觉底部时 offset 接近 0
        if (e.VerticalOffset < 30 && Vm.HasMoreChatHistory)
        {
            await Vm.LoadMoreChatHistoryAsync();
        }
    }

    // === 播放（虚方法，子类可覆盖） ===

    /// <summary>播放歌曲：基类提供基础实现。子类可覆盖以加入额外逻辑（如 SearchPage 的"确保歌曲在队列"）。</summary>
    protected virtual async Task PlaySongAsync(Song song, IReadOnlyList<Song> songs)
    {
        try
        {
            if (songs.Count > 0) Queue.SetSongs(songs);
            Queue.SelectSong(song.Id);
            if (!string.IsNullOrWhiteSpace(song.FilePath))
            {
                await AudioPlayer.PlayAsync(song.FilePath);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("播放失败", ex.Message, "确定");
        }
    }

    // === 辅助 ===

    protected static string CalculateGreeting()
    {
        var hour = DateTime.Now.Hour;
        return hour switch
        {
            >= 0 and < 6 => "凌晨好，为你精选深夜好歌",
            >= 6 and < 12 => "早上好，为你精选晨间好歌",
            >= 12 and < 18 => "下午好，为你精选午后好歌",
            _ => "晚上好，为你精选今日好歌"
        };
    }
}
