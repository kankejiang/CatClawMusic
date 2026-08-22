using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Pages.Base;
using CatClawMusic.Maui.Services;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// PC（Windows 桌面端）/横屏发现页：宽屏多列布局，复用 <see cref="SearchViewModel"/> 数据层。
/// 共享事件处理逻辑见 <see cref="DiscoverPageBase"/>；本类保留横屏专属的 Hero 多卡轮播、
/// 每日推荐/推荐艺人横滑箭头、PC 鼠标滚轮修复等差异部分。
/// </summary>
public partial class DesktopDiscoverPage : DiscoverPageBase
{
    private readonly SearchViewModel _vm;
    private readonly PlayQueue _queue;
    private readonly MusicDatabase _db;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly IServiceProvider _services;
    private readonly IThemeService? _themeService;
    private readonly ListeningStatsView _statsView;
    private readonly Services.IInteractionStateService? _interactionState;

    private IDispatcherTimer? _heroTimer;
    private int _heroIndex;
    private double _heroCardWidth;
    private const double HeroSpacing = 18;
    /// <summary>上次响应式布局应用的尺寸，避免重复刷新</summary>
    private Size _lastResponsiveSize = new(-1, -1);
    /// <summary>卡片尺寸预设：按可见高度切换 标准 / 紧凑 / 超紧凑</summary>
    private LayoutPreset _currentPreset = LayoutPreset.Regular;
    private enum LayoutPreset { Regular, Compact, SuperCompact }

    /// <summary>英雄卡在 HeroTrack 中的起始下标（0=AI 卡，其后为插件快捷入口卡，英雄卡从 _heroStartIndex 开始）</summary>
    private int _heroStartIndex = 1;

    /// <summary>初始化 <see cref="DesktopDiscoverPage"/> 并注入所需服务与视图模型。</summary>
    public DesktopDiscoverPage(MusicDatabase db, PlayQueue queue, SearchViewModel vm,
        IAudioPlayerService audioPlayer, IServiceProvider services, IThemeService? themeService,
        ListeningStatsView statsView)
    {
        InitializeComponent();
        _db = db;
        _queue = queue;
        _vm = vm;
        _audioPlayer = audioPlayer;
        _services = services;
        _themeService = themeService;
        _statsView = statsView;
        BindingContext = _vm;

        // 将听歌统计视图添加到"统计"面板
        PanelStats.Children.Add(_statsView);

        // 用 ImageSourceHelper 在代码后台设图标源（WinUI 上 XAML 字面量 Source="ic_xxx" 不渲染）
        HeroPrev.Source = Helpers.ImageSourceHelper.FromNameOriginal("ic_arrow_left");
        HeroNext.Source = Helpers.ImageSourceHelper.FromNameOriginal("ic_arrow_right");
        DailyPrev.Source = Helpers.ImageSourceHelper.FromNameOriginal("ic_arrow_left");
        DailyNext.Source = Helpers.ImageSourceHelper.FromNameOriginal("ic_arrow_right");
        ArtistPrev.Source = Helpers.ImageSourceHelper.FromNameOriginal("ic_arrow_left");
        ArtistNext.Source = Helpers.ImageSourceHelper.FromNameOriginal("ic_arrow_right");

        UpdateTabVisualState(0);
        UpdateThemeIcon();
        SetupHeroTimer();

        // 聊天对话框推理力度 chips：进入页面时按全局设置同步高亮
        Loaded += (_, _) => UpdateReasoningEffortChips();

        // 用户交互（滚动列表等）期间暂停英雄卡自动轮播，与移动端发现页行为一致
        _interactionState = _services.GetService(typeof(Services.IInteractionStateService)) as Services.IInteractionStateService;
        if (_interactionState != null)
            _interactionState.InteractionStateChanged += OnInteractionStateChangedForHero;

        _vm.PropertyChanged += OnViewModelPropertyChanged;
        _vm.ChatHistoryLoaded += OnChatHistoryLoaded;
        _vm.ScrollToLatestMessageRequested += OnScrollToLatestMessageRequested;
        // 注意：本页被 DesktopMainPage 提取 Content 后，ContentPage 自身脱离可视化树，
        // 因此监听 HeroScroll（仍留在树中）的尺寸变化来重排 Hero 卡片宽度。
        HeroScroll.SizeChanged += OnHeroSizeChanged;
        // 根容器尺寸变化 → 横屏（高度偏小时）自适应卡片尺寸，缓解 AI 歌单卡过大 + 布局拥挤。
        RootStack.SizeChanged += OnRootStackSizeChanged;

#if WINDOWS
        // 聊天列表 Handler 建立后挂接 WinUI 滚轮反转 + 隐藏滚动条（幂等，见 FixChatMouseWheelDirection）
        ChatMessagesList.HandlerChanged += OnChatMessagesListHandlerChanged;
#endif

        // 开放所有接口：渲染插件贡献的发现子 tab（鸭子类型 IDiscoverTabPlugin）与整页入口（IViewContributorPlugin）
        InitializePluginUi();
    }

    // === 基类抽象属性实现 ===

    protected override SearchViewModel Vm => _vm;
    protected override PlayQueue Queue => _queue;
    protected override IAudioPlayerService AudioPlayer => _audioPlayer;
    protected override ListeningStatsView StatsView => _statsView;
    protected override CollectionView ChatMessagesListControl => ChatMessagesList;
    protected override Entry SearchBoxControl => SearchBox;
    protected override (Border, Label)[] TabControls => new[]
    {
        (TabRec, TabRecLabel),
        (TabRank, TabRankLabel),
        (TabArtist, TabArtistLabel),
        (TabAlbum, TabAlbumLabel),
        (TabStats, TabStatsLabel)
    }.Concat(_pluginTabControls).ToArray();

    protected override IServiceProvider Services => _services;
    protected override Grid CategoryTabBarControl => CategoryTabBar;
    protected override Layout PluginEntriesRootControl => PluginEntriesBar;

    // === Hero carousel（横屏专属：ScrollView + BindableLayout 多卡同屏） ===

    private void SetupHeroTimer()
    {
        _heroTimer = Dispatcher.CreateTimer();
        _heroTimer.Interval = TimeSpan.FromSeconds(5);
        _heroTimer.Tick += OnHeroTimerTick;
    }

    private void OnHeroTimerTick(object? sender, EventArgs e)
    {
        if (_vm.HeroCards.Count == 0) return;
        if (!IsVisible || !PanelRecommend.IsVisible) return;
        _ = ScrollHeroTo(_heroIndex + 1);
    }

    /// <summary>用户交互期间暂停英雄卡自动轮播，交互结束后恢复</summary>
    private void OnInteractionStateChangedForHero(object? sender, bool interacting)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (interacting)
            {
                _heroTimer?.Stop();
            }
            else if (IsVisible && PanelRecommend.IsVisible && _vm.HeroCards.Count > 0)
            {
                _heroTimer?.Start();
            }
        });
    }

    private void OnHeroSizeChanged(object? sender, EventArgs e)
    {
        LayoutHeroCards();
    }

    /// <summary>收到 ViewModel 的"滚动到最新消息"请求时，转发给基类实现。</summary>
    private void OnScrollToLatestMessageRequested(object? sender, EventArgs e) => ScrollToLatestMessage();

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_vm.HeroCards))
        {
            Dispatcher.Dispatch(() =>
            {
                RebuildHeroTrack();
                LayoutHeroCards();
                _ = ScrollHeroTo(0);
            });
        }
        else if (e.PropertyName == nameof(_vm.IsChatMode) && _vm.IsChatMode)
        {
#if WINDOWS
            // 聊天模式开启后 ChatOverlay 可见，ChatMessagesList 的 WinUI Handler 此时才建立；
            // 延迟到渲染完成后挂接滚轮反转 + 隐藏滚动条（幂等，_chatWheelFixed 防重复）
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), () =>
            {
                if (_vm.IsChatMode) FixChatMouseWheelDirection();
            });
#endif
        }
    }

    // === 英雄卡区（AI 助手卡 + 英雄卡，手动构建） ===

    /// <summary>
    /// 重建英雄卡横滑队列：最左侧第一张固定为 AI 助手卡（进入聊天），其后为插件快捷入口卡
    /// （IQuickEntryPlugin 注册，如"私人漫游"），最后为英雄卡。
    /// 不能混用 BindableLayout——其 ItemsSource 变化时内部会 Clear 整个布局，
    /// 手动添加的 AI 卡会被删除导致聊天入口消失。
    /// </summary>
    private void RebuildHeroTrack()
    {
        try
        {
            HeroTrack.Children.Clear();
            HeroTrack.Children.Add(CreateAiCardView());

            // 插件快捷入口卡（通用机制：任何 IQuickEntryPlugin 都可注册，老插件无则自动不显示）。
            // 排序：SortOrder 升序，并列按插件注册顺序（先注册在前）——确定性排序，多插件不冲突。
            int quickEntryCount = 0;
            if (Services.GetService(typeof(IPluginManager)) is IPluginManager pluginManager)
            {
                var quickEntries = new List<(int PluginOrder, IQuickEntryPlugin Plugin, QuickEntryInfo Entry)>();
                var plugins = pluginManager.GetEnabledPlugins<IQuickEntryPlugin>();
                for (int pi = 0; pi < plugins.Count; pi++)
                {
                    foreach (var entry in plugins[pi].QuickEntries)
                        quickEntries.Add((pi, plugins[pi], entry));
                }
                foreach (var (_, plugin, entry) in quickEntries
                    .OrderBy(q => q.Entry.SortOrder).ThenBy(q => q.PluginOrder))
                {
                    try
                    {
                        HeroTrack.Children.Add(CreateQuickEntryCardView(plugin, entry));
                        quickEntryCount++;
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("DesktopDiscoverPage.xaml", $"[HeroTrack] 快捷入口卡构建失败(跳过): {ex.Message}");
                    }
                }
            }
            // 英雄卡从第 (1 + quickEntryCount) 位开始，滚动/圆点定位用它做偏移
            _heroStartIndex = 1 + quickEntryCount;

            foreach (var hero in _vm.HeroCards)
            {
                try
                {
                    if (hero != null)
                        HeroTrack.Children.Add(CreateHeroCardView(hero));
                }
                catch (Exception ex)
                {
                    Log.Debug("DesktopDiscoverPage.xaml", $"[HeroTrack] 英雄卡构建失败(跳过): {ex.Message}");
                }
            }
            LayoutHeroCards();
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopDiscoverPage.xaml", $"[HeroTrack] 重建失败: {ex.Message}");
        }
    }

    /// <summary>构建插件快捷入口卡（方形渐变，结构同 AI 卡；点击 → ExecuteQuickEntry + 打开插件入口页）。</summary>
    private View CreateQuickEntryCardView(IQuickEntryPlugin plugin, QuickEntryInfo entry)
    {
        var card = new Border
        {
            HeightRequest = 150,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(entry.Color1), 0),
                    new GradientStop(Color.FromArgb(entry.Color2), 1),
                },
                new Point(0, 0), new Point(1, 1)),
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => OnQuickEntryTapped(plugin, entry);
        card.GestureRecognizers.Add(tap);

        var grid = new Grid
        {
            Padding = new Thickness(18),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star },
                new RowDefinition { Height = GridLength.Auto },
            },
        };

        var tagBorder = new Border
        {
            Padding = new Thickness(8, 3),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 999 },
            BackgroundColor = Color.FromArgb("#30FFFFFF"),
            HorizontalOptions = LayoutOptions.Start,
            Content = new Label { Text = string.IsNullOrEmpty(entry.Icon) ? entry.Title : entry.Icon, FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
        };
        grid.Children.Add(tagBorder);

        var content = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Spacing = 4,
        };
        content.Children.Add(new Label
        {
            Text = entry.Title,
            FontSize = 17,
            FontFamily = "OpenSansSemibold",
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            MaxLines = 2,
            Margin = new Thickness(0, 2, 16, 0),
        });
        content.Children.Add(new Label
        {
            Text = entry.Subtitle,
            FontSize = 11,
            TextColor = Color.FromArgb("#CCFFFFFF"),
            MaxLines = 2,
            Margin = new Thickness(0, 0, 16, 0),
        });
        Grid.SetRow(content, 1);
        grid.Children.Add(content);

        var arrow = new Border
        {
            WidthRequest = 42,
            HeightRequest = 42,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 21 },
            BackgroundColor = Color.FromArgb("#50FFFFFF"),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.End,
            Content = new Label { Text = "›", FontSize = 24, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center },
        };
        Grid.SetRow(arrow, 2);
        grid.Children.Add(arrow);

        card.Content = grid;
        return card;
    }

    /// <summary>点击快捷入口卡：把执行权完全交给插件（传入宿主服务），是否开页/直接播放由插件决定。</summary>
    private void OnQuickEntryTapped(IQuickEntryPlugin plugin, QuickEntryInfo entry)
    {
        try
        {
            plugin.ExecuteQuickEntry(entry.Id, Services);
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopDiscoverPage.xaml", $"[QuickEntry] 执行快捷入口失败: {ex.Message}");
        }
    }

    /// <summary>构建 AI 助手卡（方形，点击进入 AI 聊天模式）。</summary>
    private View CreateAiCardView()
    {
        var card = new Border
        {
            HeightRequest = 150,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb("#667eea"), 0),
                    new GradientStop(Color.FromArgb("#764ba2"), 1),
                },
                new Point(0, 0), new Point(1, 1)),
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += OnAiEntryTapped;
        card.GestureRecognizers.Add(tap);

        var grid = new Grid
        {
            Padding = new Thickness(18),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star },
                new RowDefinition { Height = GridLength.Auto },
            },
        };

        var tagBorder = new Border
        {
            Padding = new Thickness(8, 3),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 999 },
            BackgroundColor = Color.FromArgb("#30FFFFFF"),
            HorizontalOptions = LayoutOptions.Start,
            Content = new Label { Text = "AI 助手", FontSize = 9.5, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
        };
        grid.Children.Add(tagBorder);

        var content = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Spacing = 8,
        };
        var avatar = new Border
        {
            WidthRequest = 46,
            HeightRequest = 46,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 23 },
            Padding = new Thickness(0),
            HorizontalOptions = LayoutOptions.Start,
            Content = new Image { Source = "avatar_yuki.png", Aspect = Aspect.AspectFill, WidthRequest = 46, HeightRequest = 46 },
        };
        content.Children.Add(avatar);
        content.Children.Add(new Label
        {
            Text = "🐾 和 Yuki 聊聊",
            FontSize = 17,
            FontFamily = "OpenSansSemibold",
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            MaxLines = 2,
            Margin = new Thickness(0, 2, 16, 0),
        });
        content.Children.Add(new Label
        {
            Text = "找歌、推荐、聊天都可以",
            FontSize = 11,
            TextColor = Color.FromArgb("#CCFFFFFF"),
            MaxLines = 2,
            Margin = new Thickness(0, 0, 16, 0),
        });
        Grid.SetRow(content, 1);
        grid.Children.Add(content);

        var arrow = new Border
        {
            WidthRequest = 42,
            HeightRequest = 42,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 21 },
            BackgroundColor = Color.FromArgb("#50FFFFFF"),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.End,
            Content = new Label { Text = "›", FontSize = 24, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center },
        };
        Grid.SetRow(arrow, 2);
        grid.Children.Add(arrow);

        card.Content = grid;
        return card;
    }

    /// <summary>构建英雄卡（方形渐变 + 标签/标题/描述 + 播放按钮）。</summary>
    private View CreateHeroCardView(HeroCardItem item)
    {
        var card = new Border
        {
            HeightRequest = 150,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            BindingContext = item,
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(item.GradientStart, 0),
                    new GradientStop(item.GradientEnd, 1),
                },
                new Point(0, 0), new Point(1, 1)),
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += OnHeroCardTapped;
        card.GestureRecognizers.Add(tap);

        var grid = new Grid { Padding = new Thickness(18) };

        var textStack = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Spacing = 6,
        };
        textStack.Children.Add(new Border
        {
            Padding = new Thickness(8, 3),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 999 },
            BackgroundColor = Color.FromArgb("#30FFFFFF"),
            HorizontalOptions = LayoutOptions.Start,
            Content = new Label { Text = item.Tag, FontSize = 9.5, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
        });
        textStack.Children.Add(new Label
        {
            Text = item.Title,
            FontSize = 19,
            FontFamily = "OpenSansSemibold",
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            MaxLines = 2,
            Margin = new Thickness(0, 4, 56, 0),
        });
        textStack.Children.Add(new Label
        {
            Text = item.Description,
            FontSize = 11.5,
            TextColor = Color.FromArgb("#CCFFFFFF"),
            MaxLines = 3,
            Margin = new Thickness(0, 2, 56, 0),
        });
        grid.Children.Add(textStack);

        var playBtn = new Border
        {
            WidthRequest = 42,
            HeightRequest = 42,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 21 },
            BackgroundColor = Color.FromArgb("#50FFFFFF"),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.End,
            Content = new ImageButton
            {
                Source = item.PlayIcon,
                WidthRequest = 20,
                HeightRequest = 20,
                BackgroundColor = Colors.Transparent,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Aspect = Aspect.AspectFit,
            },
        };
        var playTap = new TapGestureRecognizer();
        playTap.Tapped += OnHeroPlayTapped;
        playBtn.GestureRecognizers.Add(playTap);
        grid.Children.Add(playBtn);

        card.Content = grid;
        return card;
    }

    /// <summary>根据可见宽度计算每张 Hero 卡尺寸（方形：宽=高），目标一屏约 4 张，
    /// 并限制单卡尺寸（方形卡片随窗口过宽会变得巨大）；同时刷新圆点数量。
    /// AI 助手卡（最左侧第一张）与英雄卡同尺寸。
    /// 横屏（高度小）时进一步收窄单卡上限，避免 AI 入口卡过高挤占其他模块。</summary>
    private void LayoutHeroCards(double? overrideCap = null)
    {
        HeroDots.Count = _vm.HeroCards.Count;
        if (HeroScroll.Width <= 0) return;
        // 一屏约 4 张；方形卡上限按布局预设下降（外部 overrideCap 优先，用于 ApplyResponsiveMetrics 统一档位）
        var cardW = (HeroScroll.Width - HeroSpacing * 4) / 4;
        double cap = overrideCap ?? (_currentPreset switch
        {
            LayoutPreset.SuperCompact => 136,
            LayoutPreset.Compact => 176,
            _ => 210
        });
        double minW = _currentPreset == LayoutPreset.SuperCompact ? 96 : 120;
        cardW = Math.Clamp(cardW, minW, cap);
        _heroCardWidth = cardW;

        foreach (View child in HeroTrack.Children)
        {
            // 方形卡片：宽高一致（覆盖模板中的固定高度）
            child.WidthRequest = cardW;
            child.HeightRequest = cardW;
        }
    }

    /// <summary>根容器尺寸变化：首次或切横竖屏时触发响应式卡片尺寸重算。</summary>
    private void OnRootStackSizeChanged(object? sender, EventArgs e)
    {
        // RootStack 在 ScrollView 内，其 Height 会被内容撑开至无限大，不能用作可视高度判断依据。
        // 改用 HeroScroll.Height（横向 ScrollView 位于可视树内，高度受窗口约束，能真实反映可用空间）。
        double w = HeroScroll.Width > 0 ? HeroScroll.Width : (sender is VisualElement el ? el.Width : 0);
        double h = HeroScroll.Height > 0 ? HeroScroll.Height : 0;
        // 若 HeroScroll 高度仍不可用，回退取本页所在窗口（父页面 DesktopMainPage）的 Height
        if (h <= 0 && this.Parent is VisualElement p) h = p.Height;
        if (h <= 0 && Window != null) h = Window.Height;
        ApplyResponsiveMetrics(w, h);
    }

    /// <summary>横屏自适应卡片：依据【可视高度】（HeroScroll / 父窗口高度）分档
    /// 下调 AI 歌单 / 每日推荐 / 推荐艺人 等卡片尺寸与间距，
    /// 避免 AI 歌单单卡占据可视高度的 60%+ 导致布局过度拥挤。
    /// 关键修复：不再使用 ScrollView 内被内容撑高的 RootStack.Height 做阈值。</summary>
    private void ApplyResponsiveMetrics(double width, double height)
    {
        // width 允许为 0（Hero 卡按宽度计算，但高度维度判断仍可进行）
        if (height <= 0) return;
        // 仅以高度做档位依据；宽度变化单独驱动 Hero 卡重排
        int hRounded = (int)Math.Round(height);
        if (hRounded == (int)_lastResponsiveSize.Height)
        {
            LayoutHeroCards();
            return;
        }
        _lastResponsiveSize = new Size(width, hRounded);

        // 阈值按【可视高度】划分（参考手机横屏：
        //   PC 大屏窗口 ≥ 860，平板 / 大折叠屏 ≥ 680，普通手机横屏 520~680，窄边高刷屏 / 安全区压缩 < 520）
        LayoutPreset preset;
        if (height >= 860) preset = LayoutPreset.Regular;
        else if (height >= 620) preset = LayoutPreset.Regular; // 中等高度以上仍可常规
        else if (height >= 460) preset = LayoutPreset.Compact;
        else preset = LayoutPreset.SuperCompact;

        bool presetChanged = preset != _currentPreset;
        _currentPreset = preset;

        // 预设切换 → 按档重设各横滑行/网格的固定尺寸。整体再缩小一档，确保横屏首屏至少可见 Hero+AI+每日。
        // 元组：(AI 歌单行高, AI 卡宽, 每日行高, 每日卡宽, 艺人行高, 艺人卡宽, Hero 卡宽上限)
        (double aiRowH, double aiCardW, double dailyRowH, double dailyCardW, double artistRowH, double artistCardW, double heroCap) = preset switch
        {
            LayoutPreset.Compact => (236, 184, 142, 104, 100, 66, 176),
            LayoutPreset.SuperCompact => (186, 144, 114, 84, 82, 54, 136),
            _ => (310, 240, 184, 136, 128, 82, 210)
        };

        if (presetChanged)
        {
            // ── AI 歌单横滑行 ──
            AiPlaylistRow.HeightRequest = aiRowH;
            AiPlaylistRow.ItemTemplate = BuildAiPlaylistCardTemplate(aiCardW);
            ForceReloadItems(AiPlaylistRow);

            // ── 每日推荐横滑行 ──
            DailyList.HeightRequest = dailyRowH;
            DailyList.ItemTemplate = BuildDailyCardTemplate(dailyCardW);
            ForceReloadItems(DailyList);

            // ── 推荐艺人横滑行 ──
            ArtistsRow.HeightRequest = artistRowH;
            ArtistsRow.ItemTemplate = BuildArtistCardTemplate(artistCardW);
            ForceReloadItems(ArtistsRow);

            // ── Hero 区 / 各分区间距同步收紧 ──
            HeroWrap.Margin = preset == LayoutPreset.SuperCompact
                ? new Thickness(0, 0, 0, 2)
                : new Thickness(0, 0, 0, preset == LayoutPreset.Compact ? 4 : 6);
            HeroDots.Margin = preset == LayoutPreset.SuperCompact
                ? new Thickness(0, 0, 0, 6)
                : new Thickness(0, 2, 0, preset == LayoutPreset.Compact ? 10 : 18);

            // 同步调整分段 Tab 与页面头部的内边距（只在紧凑档收紧）
            CategoryTabBar.Margin = preset == LayoutPreset.SuperCompact
                ? new Thickness(0, 8, 0, 10)
                : preset == LayoutPreset.Compact
                    ? new Thickness(0, 10, 0, 12)
                    : new Thickness(0, 16, 0, 16);
            RootStack.Padding = preset == LayoutPreset.SuperCompact
                ? new Thickness(20, 2, 20, 18)
                : preset == LayoutPreset.Compact
                    ? new Thickness(22, 3, 22, 22)
                    : new Thickness(26, 4, 26, 26);
        }

        // Hero 卡按新上限重排（无论 preset 是否变化都需要，因为宽度可能改变）
        LayoutHeroCards(heroCap);
    }

    private DataTemplate BuildAiPlaylistCardTemplate(double cardW)
    {
        return new DataTemplate(() =>
        {
            var cover = new Border
            {
                WidthRequest = cardW,
                HeightRequest = cardW,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16, 16, 0, 0) },
                StrokeThickness = 0,
            };
            cover.SetDynamicResource(BackgroundColorProperty, "SurfaceColor");
            var coverImg = new Image { Aspect = Aspect.AspectFill };
            coverImg.SetBinding(Image.SourceProperty, new Binding("CoverPath") { TargetNullValue = "ic_music_note" });
            cover.Content = coverImg;

            var name = new Label
            {
                FontFamily = "OpenSansSemibold",
                FontSize = cardW >= 200 ? 14 : 12.5,
                MaxLines = 1
            };
            name.SetBinding(Label.TextProperty, "Name");
            name.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");

            var reason = new Label
            {
                FontSize = cardW >= 200 ? 11 : 10,
                MaxLines = cardW >= 200 ? 2 : 1,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            reason.SetBinding(Label.TextProperty, "Reason");
            reason.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");

            var subtitle = new Label
            {
                FontSize = cardW >= 200 ? 10.5 : 9.5,
                MaxLines = 1,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            subtitle.SetBinding(Label.TextProperty, "Subtitle");
            subtitle.SetDynamicResource(Label.TextColorProperty, "PrimaryColor");

            var body = new VerticalStackLayout
            {
                Padding = new Thickness(cardW >= 200 ? 12 : 9, cardW >= 200 ? 10 : 7, cardW >= 200 ? 12 : 9, cardW >= 200 ? 12 : 9),
                Spacing = Math.Max(2, (cardW >= 200 ? 4 : 2)),
                Children = { name, reason, subtitle }
            };

            var root = new VerticalStackLayout { Spacing = 0 };
            root.Children.Add(cover);
            root.Children.Add(body);

            var card = new Border
            {
                WidthRequest = cardW,
                StrokeShape = new RoundRectangle { CornerRadius = 16 },
                StrokeThickness = 1,
                Content = root
            };
            card.SetDynamicResource(Border.StrokeProperty, "GlassStrokeColor");
            card.SetDynamicResource(Border.BackgroundColorProperty, "CardBackgroundStrongColor");
            var tap = new TapGestureRecognizer();
            tap.Tapped += OnAiPlaylistTapped;
            card.GestureRecognizers.Add(tap);
            return card;
        });
    }

    private DataTemplate BuildDailyCardTemplate(double cardW)
    {
        return new DataTemplate(() =>
        {
            var cover = new Border
            {
                WidthRequest = cardW,
                HeightRequest = cardW,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18, 18, 0, 0) },
                StrokeThickness = 0
            };
            cover.SetDynamicResource(BackgroundColorProperty, "SurfaceColor");
            var coverImg = new Image { WidthRequest = cardW, HeightRequest = cardW, Aspect = Aspect.AspectFill };
            coverImg.SetBinding(Image.SourceProperty, new Binding("CoverArtPath") { TargetNullValue = "ic_music_note" });
            cover.Content = coverImg;

            var title = new Label
            {
                FontFamily = "OpenSansSemibold",
                FontSize = cardW >= 130 ? 12.5 : 11,
                MaxLines = 1
            };
            title.SetBinding(Label.TextProperty, "Title");
            title.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");

            var artist = new Label
            {
                FontSize = cardW >= 130 ? 10.5 : 9.5,
                MaxLines = 1,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            artist.SetBinding(Label.TextProperty, "Artist");
            artist.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");

            var body = new VerticalStackLayout
            {
                Padding = new Thickness(8, 6, 8, 8),
                Spacing = 2,
                Children = { title, artist }
            };
            var root = new VerticalStackLayout { Spacing = 0 };
            root.Children.Add(cover);
            root.Children.Add(body);
            var card = new Border
            {
                WidthRequest = cardW,
                StrokeShape = new RoundRectangle { CornerRadius = 18 },
                StrokeThickness = 1,
                Content = root
            };
            card.SetDynamicResource(Border.StrokeProperty, "GlassStrokeColor");
            card.SetDynamicResource(Border.BackgroundColorProperty, "CardBackgroundStrongColor");
            return card;
        });
    }

    private DataTemplate BuildArtistCardTemplate(double cardW)
    {
        return new DataTemplate(() =>
        {
            var avatarBorder = new Border
            {
                WidthRequest = cardW,
                HeightRequest = cardW,
                StrokeShape = new RoundRectangle { CornerRadius = cardW / 2.0 },
                StrokeThickness = 0,
                HorizontalOptions = LayoutOptions.Center
            };
            avatarBorder.SetDynamicResource(BackgroundColorProperty, "SurfaceColor");
            var img = new Image { WidthRequest = cardW, HeightRequest = cardW, Aspect = Aspect.AspectFill };
            img.SetBinding(Image.SourceProperty, "CoverSource");
            avatarBorder.Content = img;

            var name = new Label
            {
                FontFamily = "OpenSansSemibold",
                FontSize = cardW >= 80 ? 13 : 11.5,
                HorizontalOptions = LayoutOptions.Center,
                MaxLines = 1
            };
            name.SetBinding(Label.TextProperty, "Name");

            var subtitle = new Label
            {
                FontSize = cardW >= 80 ? 11 : 9.5,
                HorizontalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 1
            };
            subtitle.SetBinding(Label.TextProperty, "Subtitle");
            subtitle.SetDynamicResource(Label.TextColorProperty, "TextHintColor");

            var vsl = new VerticalStackLayout
            {
                WidthRequest = cardW + 8,
                Spacing = 6,
                Children = { avatarBorder, name, subtitle }
            };
            return vsl;
        });
    }

    /// <summary>强制让 CollectionView 按新 ItemTemplate 重新渲染可视项：
    /// 卸载 ItemsSource 再重新赋值，MAUI 会按新 DataTemplate 新建可视项；MVVM 源不变。</summary>
    private static void ForceReloadItems(CollectionView cv)
    {
        try
        {
            if (cv == null) return;
            var source = cv.ItemsSource;
            if (source == null) return;
            cv.ItemsSource = null;
            cv.ItemsSource = source;
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopDiscoverPage.xaml", $"[Responsive] ForceReloadItems 失败(忽略): {ex.Message}");
        }
    }

    private async Task ScrollHeroTo(int index)
    {
        if (_vm.HeroCards.Count == 0) return;
        var count = _vm.HeroCards.Count;
        index = ((index % count) + count) % count;
        _heroIndex = index;

        // 轨道前部是 AI 卡 + 插件快捷入口卡，英雄卡从 _heroStartIndex 开始：滚动位置 = (index + _heroStartIndex) 张卡的偏移
        var step = _heroCardWidth > 0 ? _heroCardWidth + HeroSpacing : HeroScroll.Width / 2;
        await HeroScroll.ScrollToAsync((index + _heroStartIndex) * step, 0, true);
        HeroDots.Position = index;
    }

    private void OnHeroScrolled(object? sender, ScrolledEventArgs e)
    {
        if (_heroCardWidth <= 0) return;
        var count = _vm.HeroCards.Count;
        if (count == 0) return;
        var idx = (int)Math.Round(e.ScrollX / (_heroCardWidth + HeroSpacing)) - _heroStartIndex;
        idx = Math.Clamp(idx, 0, count - 1);
        if (idx != _heroIndex)
        {
            _heroIndex = idx;
            HeroDots.Position = idx;
        }
    }

    private void OnHeroPrevClicked(object? sender, EventArgs e) => _ = ScrollHeroTo(_heroIndex - 1);
    private void OnHeroNextClicked(object? sender, EventArgs e) => _ = ScrollHeroTo(_heroIndex + 1);

    // === 每日推荐左右箭头 ===

    /// <summary>每次翻页的卡片数（根据可见宽度动态计算）。</summary>
    private int DailyPageSize => DailyList.Width > 0 ? Math.Max(1, (int)((DailyList.Width + 12) / (150 + 12))) : 4;

    private void OnDailyPrevClicked(object? sender, EventArgs e)
    {
        var items = _vm.DailyRecommendSongs;
        if (items.Count == 0) return;
        var page = DailyPageSize;
        var targetIdx = Math.Max(0, _dailyVisibleStart - page);
        ScrollDailyTo(targetIdx);
    }

    private void OnDailyNextClicked(object? sender, EventArgs e)
    {
        var items = _vm.DailyRecommendSongs;
        if (items.Count == 0) return;
        var page = DailyPageSize;
        var targetIdx = Math.Min(items.Count - 1, _dailyVisibleStart + page);
        ScrollDailyTo(targetIdx);
    }

    private int _dailyVisibleStart;

    private void ScrollDailyTo(int index)
    {
        var items = _vm.DailyRecommendSongs;
        if (items.Count == 0 || index < 0 || index >= items.Count) return;
        _dailyVisibleStart = index;
        DailyList.ScrollTo(items[index], null, ScrollToPosition.Start);
        UpdateDailyArrowVisibility();
    }

    /// <summary>根据滚动位置更新每日推荐箭头的显示/隐藏。</summary>
    private void UpdateDailyArrowVisibility()
    {
        var items = _vm.DailyRecommendSongs;
        if (items.Count == 0) return;
        var page = DailyPageSize;
        DailyPrev.IsVisible = _dailyVisibleStart > 0;
        DailyNext.IsVisible = (_dailyVisibleStart + page) < items.Count;
    }

    // === 推荐艺人左右箭头 ===

    private int ArtistPageSize => ArtistsRow.Width > 0 ? Math.Max(1, (int)((ArtistsRow.Width + 14) / (92 + 14))) : 5;

    private void OnArtistPrevClicked(object? sender, EventArgs e)
    {
        var items = _vm.Artists;
        if (items.Count == 0) return;
        var page = ArtistPageSize;
        var targetIdx = Math.Max(0, _artistVisibleStart - page);
        ScrollArtistTo(targetIdx);
    }

    private void OnArtistNextClicked(object? sender, EventArgs e)
    {
        var items = _vm.Artists;
        if (items.Count == 0) return;
        var page = ArtistPageSize;
        var targetIdx = Math.Min(items.Count - 1, _artistVisibleStart + page);
        ScrollArtistTo(targetIdx);
    }

    private int _artistVisibleStart;

    private void ScrollArtistTo(int index)
    {
        var items = _vm.Artists;
        if (items.Count == 0 || index < 0 || index >= items.Count) return;
        _artistVisibleStart = index;
        ArtistsRow.ScrollTo(items[index], null, ScrollToPosition.Start);
        UpdateArtistArrowVisibility();
    }

    private void UpdateArtistArrowVisibility()
    {
        var items = _vm.Artists;
        if (items.Count == 0) return;
        var page = ArtistPageSize;
        ArtistPrev.IsVisible = _artistVisibleStart > 0;
        ArtistNext.IsVisible = (_artistVisibleStart + page) < items.Count;
    }

    // === Lifecycle ===

    /// <summary>订阅页面所需事件（幂等：先解绑再订阅，避免重复订阅）。
    /// 构造函数与每次 OnAppearing 均调用，确保事件始终处于订阅状态。</summary>
    private void SubscribeEvents()
    {
        if (_interactionState != null)
        {
            _interactionState.InteractionStateChanged -= OnInteractionStateChangedForHero;
            _interactionState.InteractionStateChanged += OnInteractionStateChangedForHero;
        }
        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        _vm.ChatHistoryLoaded -= OnChatHistoryLoaded;
        _vm.ChatHistoryLoaded += OnChatHistoryLoaded;
        _vm.ScrollToLatestMessageRequested -= OnScrollToLatestMessageRequested;
        _vm.ScrollToLatestMessageRequested += OnScrollToLatestMessageRequested;
        HeroScroll.SizeChanged -= OnHeroSizeChanged;
        HeroScroll.SizeChanged += OnHeroSizeChanged;
        RootStack.SizeChanged -= OnRootStackSizeChanged;
        RootStack.SizeChanged += OnRootStackSizeChanged;
    }

    /// <summary>解绑页面事件（在 OnDisappearing 中调用，避免页面不可见时仍处理回调导致内存泄漏或无效刷新）</summary>
    private void UnsubscribeEvents()
    {
        if (_interactionState != null)
            _interactionState.InteractionStateChanged -= OnInteractionStateChangedForHero;
        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm.ChatHistoryLoaded -= OnChatHistoryLoaded;
        _vm.ScrollToLatestMessageRequested -= OnScrollToLatestMessageRequested;
        HeroScroll.SizeChanged -= OnHeroSizeChanged;
        RootStack.SizeChanged -= OnRootStackSizeChanged;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _heroTimer?.Stop();
        UnsubscribeEvents();
        // 离开聊天页：注销浏览器预览宿主（Agent 浏览器请求由协调器超时兜底）
        CatClawMusic.Maui.Services.AgentBrowser.AgentBrowserCoordinator.Instance.UnregisterHost(AgentBrowserPreview);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // 注册为 Agent 浏览器宿主（browser_open 工具在聊天页顶部弹出预览）
        CatClawMusic.Maui.Services.AgentBrowser.AgentBrowserCoordinator.Instance.RegisterHost(AgentBrowserPreview);

        SubscribeEvents();
        _vm.GreetingText = CalculateGreeting();

        // 重建英雄卡区（AI 助手卡 + 英雄卡），数据未就绪时也保留 AI 入口
        try
        {
            RebuildHeroTrack();
            if (_vm.HeroCards.Count > 0)
            {
                _heroTimer?.Start();
            }
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopDiscoverPage.xaml", $"[OnAppearing] HeroTrack 初始化失败(继续): {ex.Message}");
        }

        // 准备当天 AI 歌单（幂等：内部有会话级 loaded 标记；未开启/未配置模型时自动跳过）
        _ = _vm.EnsureDailyAiPlaylistsAsync();

#if WINDOWS

        // PC 端：将横向 CollectionView 的纵向滚轮事件转发给父级 ScrollView，
        // 解决"鼠标滚轮被横向内容截获、无法翻页"的问题
        FixHorizontalMouseWheelCapture();


        // PC 端：聊天消息列表（Rotation=180 翻转）滚轮方向反转，恢复自然滚动
        FixChatMouseWheelDirection();

#endif

        if (LocalScanService.NeedsReload)
        {
            LocalScanService.NeedsReload = false;
            try { await _vm.ReloadAfterScanAsync(); }
            catch (Exception ex) { Log.Debug("DesktopDiscoverPage.xaml", $"DesktopDiscover reload: {ex.Message}"); }
            RebuildHeroTrack();
            if (_vm.HeroCards.Count > 0)
            {
                _heroIndex = 0;
                _heroTimer?.Start();
            }
            RefreshArrowVisibility();
            return;
        }

        if (_vm.DailyRecommendSongs.Count > 0 || _vm.TopPlayedSongs.Count > 0)
        {
            // 缓存命中早退：数据已在内存池（_allTopPlayedSongs 等）中，直接准备 AI 歌单

            _ = _vm.EnsureDailyAiPlaylistsAsync();
            RefreshArrowVisibility();
            return;
        }

        try
        {
            await _vm.LoadExploreDataAsync();

        }
        catch (Exception ex)
        {
            Log.Debug("DesktopDiscoverPage.xaml", $"DesktopDiscover OnAppearing: {ex.Message}");

        }

        // 数据加载完成后准备当天 AI 歌单（候选池已就绪）
        _ = _vm.EnsureDailyAiPlaylistsAsync();

        RebuildHeroTrack();
        if (_vm.HeroCards.Count > 0)
        {
            _heroIndex = 0;
            _heroTimer?.Start();
        }
        RefreshArrowVisibility();
    }

    // === Arrow visibility helpers ===

    private void RefreshArrowVisibility()
    {
        UpdateDailyArrowVisibility();
        UpdateArtistArrowVisibility();
    }

#if WINDOWS
    // ─── PC 鼠标滚轮修复：横向区域不再截获纵向滚动 ───

    /// <summary>
    /// 在 WinUI 层找到每日推荐/推荐艺人的 CollectionView 内部 ScrollViewer，
    /// 将纵向鼠标滚轮事件转发给父级 ScrollView，让用户可以正常上下翻页。
    /// </summary>
    private void FixHorizontalMouseWheelCapture()
    {
        if (this.Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement pageEl) return;

        var horizontalTargets = new[] { DailyList, ArtistsRow };
        foreach (var cv in horizontalTargets)
        {
            if (cv?.Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement cvEl) continue;
            var sv = FindWinUIChild<Microsoft.UI.Xaml.Controls.ScrollViewer>(cvEl);
            if (sv == null) continue;
            sv.PointerWheelChanged += OnHorizontalAreaWheelChanged;
        }
    }

    private void OnHorizontalAreaWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(sender as Microsoft.UI.Xaml.UIElement);
        var delta = point.Properties.MouseWheelDelta;
        if (Math.Abs(delta) > 0.1)
        {
            e.Handled = true;
            if (this.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement pageEl)
            {
                var parentSv = FindWinUIParent<Microsoft.UI.Xaml.Controls.ScrollViewer>(pageEl);
                if (parentSv != null)
                {
                    var offset = parentSv.VerticalOffset - delta;
                    parentSv.ChangeView(null, Math.Max(0, offset), null);
                }
            }
        }
    }

    private static T? FindWinUIChild<T>(Microsoft.UI.Xaml.DependencyObject parent) where T : Microsoft.UI.Xaml.DependencyObject
    {
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            var result = FindWinUIChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    private static T? FindWinUIParent<T>(Microsoft.UI.Xaml.DependencyObject child) where T : Microsoft.UI.Xaml.DependencyObject
    {
        var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T found) return found;
            parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    // ─── 聊天列表（Rotation=180）滚轮方向反转 + 隐藏滚动条 ───

    private bool _chatWheelFixed;

    private void OnChatMessagesListHandlerChanged(object? sender, EventArgs e)
    {
        FixChatMouseWheelDirection();
    }

    /// <summary>
    /// 聊天消息列表用 Rotation=180 翻转展示（倒序数据），WinUI 下鼠标滚轮方向随之反转：
    /// 拦截滚轮事件并按反方向滚动，恢复"向上滚看更旧消息"的自然手感；同时隐藏滚动条。
    /// 关键：**禁用内部滚动容器的滚轮响应（VerticalScrollMode=Disabled，编程 ScrollTo 不受影响）**，
    /// 否则内部先按默认方向滚一段、我们再反向滚 = 来回冲突 = 抽搐。
    /// 幂等：ChatOverlay 在非聊天模式下 IsVisible=false，列表 Handler 可能尚未建立，
    /// 由 HandlerChanged / IsChatMode 变更 / OnAppearing 三处触发，找到滚动容器才置位。
    /// </summary>
    private void FixChatMouseWheelDirection()
    {
        if (_chatWheelFixed) return;
        if (ChatMessagesList?.Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement cvEl) return;

        // 新结构：ItemsView → ScrollView/ScrollPresenter（WinAppSDK 1.4+，MAUI CollectionView 实际使用）
        var scrollView = FindWinUIChild<Microsoft.UI.Xaml.Controls.ScrollView>(cvEl);
        if (scrollView != null)
        {
            scrollView.VerticalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollingScrollBarVisibility.Hidden;
            scrollView.VerticalScrollMode = Microsoft.UI.Xaml.Controls.ScrollingScrollMode.Disabled;
            scrollView.AddHandler(Microsoft.UI.Xaml.UIElement.PointerWheelChangedEvent,
                new Microsoft.UI.Xaml.Input.PointerEventHandler(OnChatWheelChangedScrollView), true);
            _chatWheelFixed = true;
            return;
        }

        // 旧结构：ScrollViewer（MAUI 旧版 Windows CollectionView 内部）
        var scrollViewer = FindWinUIChild<Microsoft.UI.Xaml.Controls.ScrollViewer>(cvEl);
        if (scrollViewer != null)
        {
            scrollViewer.VerticalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Hidden;
            scrollViewer.VerticalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Disabled;
            scrollViewer.AddHandler(Microsoft.UI.Xaml.UIElement.PointerWheelChangedEvent,
                new Microsoft.UI.Xaml.Input.PointerEventHandler(OnChatWheelChangedScrollViewer), true);
            _chatWheelFixed = true;
        }
    }

    private void OnChatWheelChangedScrollViewer(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(sender as Microsoft.UI.Xaml.UIElement);
        var delta = point.Properties.MouseWheelDelta;
        if (Math.Abs(delta) < 0.1) return;
        e.Handled = true;
        if (sender is Microsoft.UI.Xaml.Controls.ScrollViewer sv)
        {
            // 旋转 180 后滚动方向视觉反转：滚轮向上（delta>0）应看更旧消息 = 视觉顶部 = offset 增大。
            // WinUI 默认滚轮向上是 offset 减小，这里必须取反（+delta）。
            sv.ChangeView(null, Math.Max(0, sv.VerticalOffset + delta), null, disableAnimation: true);
        }
    }

    private void OnChatWheelChangedScrollView(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(sender as Microsoft.UI.Xaml.UIElement);
        var delta = point.Properties.MouseWheelDelta;
        if (Math.Abs(delta) < 0.1) return;
        e.Handled = true;
        if (sender is Microsoft.UI.Xaml.Controls.ScrollView sv)
        {
            sv.ScrollTo(sv.HorizontalOffset, Math.Max(0, sv.VerticalOffset + delta));
        }
    }
#endif

    // === 主题切换 ===

    private void OnThemeToggleClicked(object? sender, EventArgs e)
    {
        if (_themeService == null) return;
        var next = _themeService.IsEffectivelyDark() ? DarkModeSetting.Light : DarkModeSetting.Dark;
        _themeService.SetDarkModeSetting(next);
        UpdateThemeIcon();
    }

    private void UpdateThemeIcon()
    {
        if (ThemeIcon == null) return;
        ThemeIcon.Text = _themeService != null && _themeService.IsEffectivelyDark()
            ? "\uD83C\uDF19"   // 🌙
            : "\u2600\uFE0F";  // ☀️
    }

    // === Shuffle：换批后回到开头并刷新箭头 ===

    private void OnShuffleDailyClicked(object? sender, EventArgs e)
    {
        _vm.ShuffleDailyCommand.Execute(null);
        // 换批后回到开头并刷新箭头
        _dailyVisibleStart = 0;
        ScrollDailyTo(0);
    }

    // === Hero 卡片点击（横屏专属：从 BindingContext 解析 HeroCardItem） ===

    /// <summary>从手势/按钮的 sender 中解析出绑定的 HeroCardItem。
    /// 注意：PC 端 Hero 用 ScrollView+BindableLayout，TapGestureRecognizer.Tapped 的 sender 是识别器本身（非 Border），
    /// 但二者都会从可视化树继承 BindingContext，故统一用 BindableObject.BindingContext 取值。</summary>
    private static HeroCardItem? ResolveHeroItem(object? sender)
        => (sender as BindableObject)?.BindingContext as HeroCardItem;

    private async void OnHeroCardTapped(object? sender, TappedEventArgs e)
    {
        var heroItem = ResolveHeroItem(sender);
        if (heroItem?.Song != null)
        {
            await PlayHeroSongAsync(heroItem.Song);
        }
    }

    private async void OnHeroPlayTapped(object? sender, EventArgs e)
    {
        var heroItem = ResolveHeroItem(sender);
        if (heroItem?.Song != null)
        {
            await PlayHeroSongAsync(heroItem.Song);
        }
    }

    /// <summary>播放 Hero 卡歌曲：以每日推荐为播放队列，但确保被点击的歌曲（如 AI 推荐歌）也在队列中。</summary>
    private async Task PlayHeroSongAsync(Song song)
    {
        var list = _vm.DailyRecommendSongs.ToList();
        if (!list.Any(s => s.Id == song.Id))
            list.Insert(0, song);
        await PlaySongAsync(song, list);
    }

    /// <summary>AI 歌单卡片点击：以歌单内歌曲为播放队列，播放第一首。</summary>
    private async void OnAiPlaylistTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not AiPlaylist playlist) return;
        if (playlist.Songs.Count == 0) return;
        var first = playlist.Songs[0];
        await PlaySongAsync(first, playlist.Songs);
    }

    /// <summary>手动重新生成 AI 歌单（清缓存后强制调用 AI）</summary>
    private async void OnAiPlaylistRegenerateTapped(object? sender, TappedEventArgs e)
    {
        await _vm.RegenerateAiPlaylistsAsync();
    }

    // === 搜索结果中的歌曲播放 ===

    private async void OnSearchSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Song song) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        SearchBox.Text = "";
        _vm.ClearSearchDropdown();
        var allSongs = _vm.DailyRecommendSongs.Concat(_vm.TopPlayedSongs).ToList();
        await PlaySongAsync(song, allSongs);
    }

    private async void OnSearchSongPlayTapped(object? sender, EventArgs e)
    {
        if (sender is ImageButton btn && btn.BindingContext is Song song)
        {
            var allSongs = _vm.DailyRecommendSongs.Concat(_vm.TopPlayedSongs).ToList();
            await PlaySongAsync(song, allSongs);
        }
    }

    // === 在线音乐 ===
    // OnOnlineMusicTapped 已移除：在线音乐入口由 IViewContributorPlugin 插件提供

    /// <summary>选中在线音乐搜索结果时触发：取播放直链 → 构造临时 Song → 接入现有播放链路。</summary>
    private async void OnOnlineSearchResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is CollectionView cv) cv.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is not OnlineSong song) return;

        string? playUrl = null;
        try
        {
            playUrl = await _services.GetRequiredService<OnlineMusicAggregator>().GetPlayUrlAsync(song);
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopDiscoverPage.xaml", $"[OnlineMusic] GetPlayUrl failed: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(playUrl))
        {
            await DisplayAlert("暂不可播放", $"{song.PlatformName} 当前未接入播放直链，可尝试其他音源", "确定");
            return;
        }

        SearchBox.Text = "";
        _vm.ClearSearchDropdown();

        // 构造临时 Song 接入现有播放链路（不落库；FilePath 为临时直链）
        var tmp = new Song
        {
            Id = -1,
            Title = song.Title,
            Artist = song.Artist,
            Album = song.Album,
            Duration = (int)(song.DurationMs / 1000),
            FilePath = playUrl,
            RemoteId = $"{song.Platform}:{song.Id}",
            Source = SongSource.Local,
            AllArtists = song.Artist
        };
        await PlaySongAsync(tmp, new List<Song> { tmp });
    }

    // === 刷新 ===

    // === 聊天对话框推理力度（上拉菜单：点击"推理"按钮弹出底部面板选择） ===

    private void OnReasoningEffortButtonTapped(object? sender, TappedEventArgs e)
    {
        UpdateReasoningEffortSheet();
        EffortSheetOverlay.IsVisible = true;
        // 从底部滑入（上拉菜单动效）：面板先下移一屏，再动画回到原位
        EffortSheet.TranslationY = EffortSheet.Height > 0 ? EffortSheet.Height : 500;
        _ = EffortSheet.TranslateTo(0, 0, 220, Easing.CubicOut);
    }

    private void OnEffortSheetBackdropTapped(object? sender, TappedEventArgs e)
    {
        _ = CloseEffortSheetAsync();
    }

    private async Task CloseEffortSheetAsync()
    {
        if (!EffortSheetOverlay.IsVisible) return;
        await EffortSheet.TranslateTo(0, EffortSheet.Height, 160, Easing.CubicIn);
        EffortSheetOverlay.IsVisible = false;
    }

    private void OnReasoningEffortOptionTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string effort)
        {
            CatClawMusic.Core.Services.AI.AgentService.SetReasoningEffort(effort);
            _ = CloseEffortSheetAsync();
            UpdateReasoningEffortChips();
        }
    }

    /// <summary>刷新推理按钮文字与上拉菜单选中标记</summary>
    private void UpdateReasoningEffortChips()
    {
        var current = CatClawMusic.Core.Services.AI.AgentService.GetReasoningEffort();
        EffortButtonLabel.Text = $"推理：{EffortDisplayName(current)}";
        UpdateReasoningEffortSheet();
    }

    private static string EffortDisplayName(string effort) => effort switch
    {
        "auto" => "自动",
        "disabled" => "关闭",
        "low" => "低",
        "high" => "高",
        "max" => "最强",
        _ => effort
    };

    private void UpdateReasoningEffortSheet()
    {
        if (EffortOptDisabled == null) return; // Loaded 前
        var current = CatClawMusic.Core.Services.AI.AgentService.GetReasoningEffort();
        EffortOptDisabledCheck.IsVisible = current == "disabled";
        EffortOptLowCheck.IsVisible = current == "low";
        EffortOptHighCheck.IsVisible = current == "high";
        EffortOptMaxCheck.IsVisible = current == "max";
    }

    private void OnRefreshClicked(object? sender, EventArgs e)
    {
        // 统一走 RefreshCommand（清探索缓存 + 重新生成 AI 歌单 + IsRefreshing 转圈反馈）；
        // 不再用 IsLoading 无感拦截（加载中点击也要有反馈）
        if (_vm.IsRefreshing) return;
        _ = _vm.RefreshCommand.ExecuteAsync(null);
    }

    // === 查看全部入口（横屏专属：导航到 DesktopArtists/DesktopAlbums） ===

    private void OnArtistsViewAllClicked(object? sender, EventArgs e)
    {
        _vm.CurrentCategory = 2;
        UpdateTabVisualState(2);
    }
}
