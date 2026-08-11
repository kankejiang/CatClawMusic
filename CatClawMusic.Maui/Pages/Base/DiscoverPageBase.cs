using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls.Shapes;

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

    /// <summary>宿主服务提供者（子类构造函数注入），插件创建视图/入口时用于解析服务。</summary>
    protected abstract IServiceProvider Services { get; }

    /// <summary>发现页顶部分类 Tab 栏（Grid），插件子 tab 动态插入"推荐"右侧。</summary>
    protected abstract Grid CategoryTabBarControl { get; }

    /// <summary>顶栏插件入口按钮容器（HorizontalStackLayout），IViewContributorPlugin 入口动态加入。</summary>
    protected abstract Layout PluginEntriesRootControl { get; }

    // === 插件子 tab / 整页入口状态（开放所有接口） ===

    /// <summary>动态插件子 tab 控件（追加在固定 5 tab 之后，逻辑索引从 5 起）。</summary>
    protected readonly List<(Border border, Label label)> _pluginTabControls = new();

    /// <summary>插件面板（逻辑索引 → 容器），显隐随 CurrentCategory 控制。</summary>
    private readonly List<(int logicalIndex, VerticalStackLayout panel)> _pluginPanels = new();

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

    // === 插件子 tab / 整页入口（开放所有接口，横竖屏发现页共用） ===

    /// <summary>
    /// 初始化插件 UI：渲染发现子 tab（鸭子类型探测 IDiscoverTabPlugin）与整页入口（IViewContributorPlugin）。
    /// 由子类构造函数 InitializeComponent 之后调用；任何异常不阻断页面。
    /// </summary>
    protected void InitializePluginUi()
    {
        try
        {
            InitializePluginTabs();
            InitializePluginEntries();
            // 统一监听 CurrentCategory 控制插件面板显隐（固定面板走 XAML IntToBoolConverter 绑定，互不干扰）
            Vm.PropertyChanged += OnPluginVmPropertyChanged;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PluginUi] 初始化失败: {ex}");
        }
    }

    private void OnPluginVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Vm.CurrentCategory))
            UpdatePluginPanelVisibility(Vm.CurrentCategory);
    }

    /// <summary>
    /// 发现页插件子 tab：鸭子类型探测插件实例的 TabTitle/TabIcon/TabOrder/CreateTabView 成员，
    /// 存在即视为发现子 tab 贡献者（对应插件侧 IDiscoverTabPlugin，宿主零 Core 改动）。
    /// 插件 tab 逻辑索引从 5 起（固定 5 tab 占 0-4），视觉列插入"推荐"(0)右侧。
    /// </summary>
    private void InitializePluginTabs()
    {
        if (Services.GetService(typeof(IPluginManager)) is not IPluginManager pluginManager) return;

        // 收集所有发现子 tab 贡献者（鸭子类型探测）
        var tabs = new List<(object instance, string title, string icon, int order, System.Reflection.MethodInfo create)>();
        foreach (var info in pluginManager.GetAllPlugins())
        {
            if (!info.IsEnabled) continue;
            var type = info.Plugin.GetType();
            var titleProp = type.GetProperty("TabTitle");
            var createMethod = type.GetMethod("CreateTabView");
            if (titleProp == null || createMethod == null) continue;
            tabs.Add((
                info.Plugin,
                (string?)titleProp.GetValue(info.Plugin) ?? "插件",
                (string?)type.GetProperty("TabIcon")?.GetValue(info.Plugin) ?? "🧩",
                (int?)type.GetProperty("TabOrder")?.GetValue(info.Plugin) ?? 100,
                createMethod));
        }
        if (tabs.Count == 0) return;

        // 按 TabOrder 升序、同序按标题排序
        tabs = tabs.OrderBy(t => t.order).ThenBy(t => t.title).ToList();

        // 视觉列：推荐(0) 右侧依次插插件 tab(1..N)，固定 tab 排行/歌手/专辑/报告右移 N 列
        int pluginCount = tabs.Count;
        for (int i = 0; i < pluginCount; i++)
            CategoryTabBarControl.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        Grid.SetColumn(TabControls[1].border, 1 + pluginCount);
        Grid.SetColumn(TabControls[2].border, 2 + pluginCount);
        Grid.SetColumn(TabControls[3].border, 3 + pluginCount);
        Grid.SetColumn(TabControls[4].border, 4 + pluginCount);

        // 面板父容器 = CategoryTabBar 的父容器（Desktop 为 RootStack，Search 为 CollectionView.Header）
        if (CategoryTabBarControl.Parent is not Layout root) return;
        int tabBarIndex = root.Children.IndexOf(CategoryTabBarControl);

        for (int i = 0; i < pluginCount; i++)
        {
            var tab = tabs[i];
            int logicalIndex = 5 + i;   // 固定 5 tab 占 0-4，插件 tab 从 5 起
            int visualColumn = 1 + i;   // 推荐(0) 右侧

            // 插件 tab Border（样式对齐固定 tab，背景色由 UpdateTabVisualState 统一管理）
            var tabLabel = new Label
            {
                Text = $"{tab.icon} {tab.title}",
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.Center
            };
            tabLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
            var tabBorder = new Border
            {
                Style = (Style)Application.Current!.Resources["GlassCardStyle"],
                Padding = new Thickness(12, 10),
                Content = tabLabel
            };
            tabBorder.SetDynamicResource(Border.StrokeProperty, "GlassStrokeColor");
            int capturedLogical = logicalIndex;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                Vm.CurrentCategory = capturedLogical;
                UpdateTabVisualState(capturedLogical);
            };
            tabBorder.GestureRecognizers.Add(tap);
            Grid.SetColumn(tabBorder, visualColumn);
            CategoryTabBarControl.Children.Add(tabBorder);
            _pluginTabControls.Add((tabBorder, tabLabel));

            // 插件面板：挂插件 CreateTabView 返回的 View，显隐由 UpdatePluginPanelVisibility 统一控制
            var panel = new VerticalStackLayout { Spacing = 0, IsVisible = false };
            try
            {
                if (tab.create.Invoke(tab.instance, new object[] { Services }) is View pluginView)
                    panel.Children.Add(pluginView);
            }
            catch (Exception ex)
            {
                panel.Children.Add(new Label { Text = $"插件内容加载失败：{ex.Message}", FontSize = 12 });
            }
            root.Children.Insert(tabBarIndex + 1 + i, panel);
            _pluginPanels.Add((logicalIndex, panel));
        }

        // 刷新一次视觉，纳入新增插件 tab
        UpdateTabVisualState(Vm.CurrentCategory);
    }

    /// <summary>插件面板显隐：随 CurrentCategory 变化，仅显示匹配逻辑索引的插件面板。</summary>
    private void UpdatePluginPanelVisibility(int currentCategory)
    {
        foreach (var (logicalIndex, panel) in _pluginPanels)
            panel.IsVisible = currentCategory == logicalIndex;
    }

    /// <summary>
    /// 渲染 IViewContributorPlugin 整页入口：在顶栏按钮区为每个已启用的视图贡献者插件
    /// 动态添加入口按钮，点击后调用 CreateEntryPage 并 Push 到导航栈。
    /// </summary>
    private void InitializePluginEntries()
    {
        if (Services.GetService(typeof(IPluginManager)) is not IPluginManager pluginManager) return;
        var contributors = pluginManager.GetEnabledPlugins<IViewContributorPlugin>().ToList();
        foreach (var contributor in contributors)
        {
            var entryButton = new Border
            {
                WidthRequest = 32,
                HeightRequest = 32,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) },
                StrokeThickness = 1,
                VerticalOptions = LayoutOptions.Center,
                Content = CreateEntryIcon(contributor.EntryIcon)
            };
            entryButton.SetDynamicResource(Border.StrokeProperty, "GlassStrokeColor");
            entryButton.SetDynamicResource(Border.BackgroundColorProperty, "CardBackgroundColor");
            ToolTipProperties.SetText(entryButton, contributor.EntryTitle);
            var captured = contributor;
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await OpenPluginEntryAsync(captured);
            entryButton.GestureRecognizers.Add(tap);
            PluginEntriesRootControl.Children.Insert(0, entryButton);
        }
    }

    /// <summary>
    /// 构造入口按钮的图标 View：EntryIcon 含路径分隔符 / http(s) / 图片扩展名时用 <see cref="Image"/> 渲染，
    /// 否则保持 emoji/字符兼容（<see cref="Label"/>）。让插件端自由指定图标源（本地 png/http），宿主零硬编码。
    /// </summary>
    private static View CreateEntryIcon(string entryIcon)
    {
        // res:// 嵌入式资源：png 打包进插件 DLL（.ccp），在已加载程序集里按资源名后缀模糊匹配，
        // 用 ImageSource.FromResource 加载。找不到时回落 emoji/文本展示，避免空白。
        if (!string.IsNullOrEmpty(entryIcon) && entryIcon.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
        {
            var resKey = entryIcon["res://".Length..].Trim();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                string[] names;
                try { names = asm.GetManifestResourceNames(); } catch { continue; }
                var match = names.FirstOrDefault(n => n.EndsWith(resKey, StringComparison.OrdinalIgnoreCase));
                if (match == null) continue;
                return new Image
                {
                    Source = ImageSource.FromResource(match, asm),
                    Aspect = Aspect.AspectFit,
                    WidthRequest = 16,
                    HeightRequest = 16,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                };
            }
            return new Label
            {
                Text = entryIcon,
                FontSize = 14,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
        }

        bool isImageIcon = !string.IsNullOrEmpty(entryIcon) && (
            entryIcon.Contains('/') || entryIcon.Contains('\\') ||
            entryIcon.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
            entryIcon.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            entryIcon.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            entryIcon.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            entryIcon.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
            entryIcon.EndsWith(".gif", StringComparison.OrdinalIgnoreCase));

        if (isImageIcon)
        {
            return new Image
            {
                Source = entryIcon,  // ImageSourceConverter 自动解析 File/Uri/Resource
                Aspect = Aspect.AspectFit,
                WidthRequest = 22,
                HeightRequest = 22,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
        }
        return new Label
        {
            Text = entryIcon,
            FontSize = 14,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
    }

    /// <summary>打开 IViewContributorPlugin 贡献的整页（CreateEntryPage → Push / 桌面嵌入）。</summary>
    private async Task OpenPluginEntryAsync(IViewContributorPlugin contributor)
    {
        try
        {
            if (contributor.CreateEntryPage(Services) is not Page page) return;
            // Shell.Current 在桌面无 Shell 窗口会抛异常，必须用 TryGetShell 探测
            if (DesktopNavigation.TryGetShell() is { } shell)
                await shell.Navigation.PushAsync(page);
            else if (page is ContentPage contentPage)
                // 桌面无 Shell：嵌入 DesktopBlankPage.MainArea（保留插件页返回按钮）
                DesktopNavigation.OpenEmbedded(contentPage, hideBack: false);
            else
                await Navigation.PushModalAsync(page);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PluginEntry] 打开失败: {ex.Message}");
            try { await DisplayAlert("打开插件页面失败", ex.Message, "确定"); } catch { }
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
        var name = artist.Name ?? string.Empty;
        if (DesktopNavigation.TryGoToShell($"artistdetail?artistName={Uri.EscapeDataString(name)}")) return;
        DesktopNavigation.OpenArtistDetail(name);
    }

    protected async void OnSearchAlbumSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SearchAlbumItem album) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        SearchBoxControl.Text = "";
        Vm.ClearSearchDropdown();
        var title = album.Title ?? string.Empty;
        if (DesktopNavigation.TryGoToShell($"albumdetail?title={Uri.EscapeDataString(title)}")) return;
        DesktopNavigation.OpenAlbumDetail(title);
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
                {
                    var songId = song.Id.ToString();
                    if (DesktopNavigation.TryGoToShell($"songdetail?songId={songId}")) break;
                    DesktopNavigation.OpenSongDetail(songId);
                    break;
                }
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

        var name = artist.Name ?? string.Empty;
        // Android/Shell 环境走 GoToAsync；Windows 桌面（无 Shell）走嵌入式详情页。
        if (DesktopNavigation.TryGoToShell($"artistdetail?artistName={Uri.EscapeDataString(name)}")) return;
        DesktopNavigation.OpenArtistDetail(name);
    }

    protected async void OnAlbumSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SearchAlbumItem album) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;

        var title = album.Title ?? string.Empty;
        if (DesktopNavigation.TryGoToShell($"albumdetail?title={Uri.EscapeDataString(title)}")) return;
        DesktopNavigation.OpenAlbumDetail(title);
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
