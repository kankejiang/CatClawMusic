using System;
using CatClawMusic.Data;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Pages;

/// <summary>横屏桌面模式音乐库页：包装 LibraryPage，摘出其 Content 并按屏幕宽度自适应重组布局。
/// 生命周期通过公共方法 <see cref="LibraryPage.TriggerOnAppearing"/> 转发；卡片样式压缩通过
/// <see cref="LibraryPage.ApplyCompactHeroStyle"/> / <see cref="LibraryPage.ResetHeroStyle"/> 等公共 API 完成，
/// 不再使用反射或硬编码视觉树操作。
/// 响应式策略：
/// - 窄屏（手机横屏，内容宽 &lt; 900dp）：双列网格 + 极致压缩 padding/margin/spacing，最大化信息密度
/// - 宽屏（平板/车机，内容宽 ≥ 900dp）：双列网格布局，保留原始宽松样式
/// 通过 SizeChanged 监听旋转/尺寸变化，动态切换布局模式。</summary>
public class DesktopLibraryPage : ContentPage
{
    private readonly LibraryPage _inner;
    private readonly LibraryViewModel _vm;
    private VerticalStackLayout? _rootStack;

    // 原始卡片引用，用于在布局模式切换时复用（避免重复创建）
    private Border? _heroCard;
    private Border? _libraryCard;
    private Border? _dataInsightCard;
    private Border? _storageCard;
    private Border? _recentCard;
    private Controls.AppPopup? _popup;

    // 当前布局模式，避免相同模式重复重建。使用 nullable 确保首次进入时一定强制构建一次布局。
    private bool? _currentLayoutWide;
    private bool _layoutInitialized;

    // 切换阈值：内容区宽度 ≥ 900dp 视为宽屏（平板/车机）
    private const double WideThreshold = 900;

    public DesktopLibraryPage(MusicDatabase db, PlayQueue queue, LibraryViewModel vm, IServiceProvider sp)
    {
        _vm = vm;
        _inner = new LibraryPage(db, queue, vm, sp);

        // 摘出内部页面的 Content（Grid > ScrollView > VerticalStackLayout）
        // 通过 LibraryPage 公共访问器拿到 RootStack 与各卡片，避免视觉树硬编码索引。
        // 注意：必须在清空 _inner.Content 之前取出 RootStack/ScrollView，否则访问器会返回 null。
        _rootStack = _inner.RootStack;
        var scrollView = _inner.RootScrollView;

        if (_rootStack != null && scrollView != null)
        {
            // 横屏无底部 TabBar，把 132dp 底部留白压到 18dp；顶部安全区由 DesktopMainPage RootGrid 统一处理
            _rootStack.Padding = new Thickness(18, 0, 18, 18);

            // 通过公共访问器拿到 5 张卡片 + 弹窗
            _heroCard = _inner.HeroCardView;
            _libraryCard = _inner.LibraryListCardView;
            _dataInsightCard = _inner.DataInsightCardView;
            _storageCard = _inner.StorageCardView;
            _recentCard = _inner.RecentCardView;
            _popup = _inner.DiscoverSourcePopupView;
            _layoutInitialized = _heroCard != null && _libraryCard != null
                && _dataInsightCard != null && _storageCard != null && _recentCard != null;

            // 防止 inner Content 被双重挂载：本页使用其内部的 ScrollView 作为 Content
            _inner.Content = null;

            // 监听尺寸变化，按宽度选择布局模式
            SizeChanged += OnPageSizeChanged;
            // 首次布局立即执行：Width 可能尚未就绪（为 -1），传入 0 会默认使用紧凑布局，
            // 确保手机横屏下首帧即呈现双列网格，而不是保持 LibraryPage 原始单列堆叠。
            ApplyLayoutForWidth(Math.Max(Width, 0));

            // 直接使用内部的 ScrollView 作为页面 Content，
            // 避免 DesktopMainPage.CreatePageContent 再包一层 ScrollView 导致双重滚动嵌套
            Content = scrollView;
            BindingContext = _inner.BindingContext;
            return;
        }

        Content = _inner.Content;
        BindingContext = _inner.BindingContext;
    }

    /// <summary>页面尺寸变化时按内容宽度选择布局模式（窄屏紧凑 / 宽屏宽松）。</summary>
    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        if (!_layoutInitialized || _rootStack == null) return;
        ApplyLayoutForWidth(Width);
    }

    /// <summary>根据内容宽度应用对应布局模式。相同模式不重复重建。</summary>
    private void ApplyLayoutForWidth(double pageWidth)
    {
        if (!_layoutInitialized || _rootStack == null) return;
        if (_heroCard == null || _libraryCard == null || _dataInsightCard == null
            || _storageCard == null || _recentCard == null) return;

        // 减去侧栏宽度和页面 padding，得到内容区实际可用宽度
        // Android 横屏侧栏 168dp，页面 padding 18*2=36dp；Windows 侧栏 220dp
        double sidebarWidth = 220;
#if ANDROID
        sidebarWidth = 168;
#endif
        double contentWidth = pageWidth - sidebarWidth - 36;
        bool shouldBeWide = contentWidth >= WideThreshold;

        if (_currentLayoutWide == shouldBeWide) return;
        _currentLayoutWide = shouldBeWide;

        if (shouldBeWide)
            BuildWideLayout();
        else
            BuildCompactLayout();
    }

    /// <summary>宽屏布局（平板/车机）：Hero 全宽 → [资料库 | 数据洞察+存储占用] 并排 → 最近添加通栏。
    /// 把「数据洞察」与「存储占用」叠在同一列，填满资料库右侧的纵向空白。</summary>
    private void BuildWideLayout()
    {
        var vsl = _rootStack!;
        vsl.Children.Clear();

        // 还原 Hero 内部样式（从紧凑模式切回时）
        _inner.ResetHeroStyle();
        ResetCardStyle(_heroCard!);
        ResetCardStyle(_libraryCard!);
        ResetCardStyle(_dataInsightCard!);
        ResetCardStyle(_storageCard!);
        ResetCardStyle(_recentCard!);

        // Hero 保持全宽
        _heroCard!.Margin = new Thickness(0, 0, 0, 10);
        vsl.Children.Add(_heroCard);

        // 双列网格：资料库（左） | 数据洞察 + 存储占用（右，上下叠）
        _inner.ApplyCompactDataInsightStyle();
        _dataInsightCard!.HeightRequest = 160;
        var rightColumn = CreateVerticalStack(_dataInsightCard, _storageCard!, 14);
        var row1 = CreateTwoColumnRow(_libraryCard!, rightColumn, 14, new Thickness(0, 0, 0, 10));
        vsl.Children.Add(row1);

        // 最近添加：通栏全宽
        _recentCard!.Margin = new Thickness(0);
        vsl.Children.Add(_recentCard);

        if (_popup != null)
            vsl.Children.Add(_popup);
    }

    /// <summary>窄屏紧凑布局（手机横屏）：双列网格 + 激进压缩 padding/margin/字号。
    /// 手机横屏宽度有限，单列会让卡片拉得过长浪费纵向空间；双列网格能更紧凑地展示所有内容。
    /// 右侧为「数据洞察 + 存储占用」上下叠，最近添加通栏。</summary>
    private void BuildCompactLayout()
    {
        var vsl = _rootStack!;
        vsl.Children.Clear();

        // 压缩页面整体 padding
        vsl.Padding = new Thickness(10, 4, 10, 10);
        vsl.Spacing = 6;

        // 压缩所有卡片样式 + Hero 内部样式
        CompactCardStyle(_heroCard!);
        CompactCardStyle(_libraryCard!);
        CompactCardStyle(_dataInsightCard!);
        CompactCardStyle(_storageCard!);
        CompactCardStyle(_recentCard!);
        _inner.ApplyCompactHeroStyle();
        _inner.ApplyCompactLibraryListStyle();

        // Hero 单列全宽，激进压缩内部
        _heroCard!.Margin = new Thickness(0, 0, 0, 4);
        vsl.Children.Add(_heroCard);

        // 双列网格：资料库（左） | 数据洞察 + 存储占用（右，上下叠）
        _inner.ApplyCompactDataInsightStyle();
        _dataInsightCard!.HeightRequest = 150;
        var rightColumn = CreateVerticalStack(_dataInsightCard, _storageCard!, 6);
        var row1 = CreateTwoColumnRow(_libraryCard!, rightColumn, 6, new Thickness(0, 0, 0, 4));
        vsl.Children.Add(row1);

        // 最近添加：通栏全宽
        _recentCard!.Margin = new Thickness(0);
        vsl.Children.Add(_recentCard);

        if (_popup != null)
            vsl.Children.Add(_popup);
    }

    /// <summary>构造双列等宽网格行：左视图 | 右视图。</summary>
    private static Grid CreateTwoColumnRow(View left, View right, double columnSpacing, Thickness margin)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = new GridLength(1, GridUnitType.Star) },
                new() { Width = new GridLength(1, GridUnitType.Star) }
            },
            ColumnSpacing = columnSpacing,
            Margin = margin
        };
        left.Margin = new Thickness(0);
        right.Margin = new Thickness(0);
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        row.Children.Add(left);
        row.Children.Add(right);
        return row;
    }

    /// <summary>构造纵向叠放容器：上卡固定高度 + 下卡占满剩余空间，用于把两张卡片塞进同一列，
    /// 且整体高度撑满整列（与左侧资料库列表等高）。</summary>
    private static Grid CreateVerticalStack(Border topCard, Border bottomCard, double spacing)
    {
        topCard.Margin = new Thickness(0);
        bottomCard.Margin = new Thickness(0);
        bottomCard.VerticalOptions = LayoutOptions.FillAndExpand;
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitionCollection
            {
                new() { Height = GridLength.Auto },
                new() { Height = GridLength.Star }
            },
            RowSpacing = spacing,
            VerticalOptions = LayoutOptions.Fill
        };
        Grid.SetRow(topCard, 0);
        Grid.SetRow(bottomCard, 1);
        grid.Children.Add(topCard);
        grid.Children.Add(bottomCard);
        return grid;
    }

    /// <summary>还原卡片到原始样式（从紧凑模式切换回宽屏时调用）。</summary>
    private static void ResetCardStyle(Border card)
    {
        card.Padding = 16;
        card.Margin = new Thickness(0, 18, 0, 0);
        card.HeightRequest = -1;
    }

    /// <summary>压缩卡片样式：降低 padding。</summary>
    private static void CompactCardStyle(Border card)
    {
        card.Padding = 10;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 通过公共方法转发生命周期，不再使用反射
        _inner.TriggerOnAppearing();

        // 兜底刷新：横屏桌面页复用同一个 LibraryViewModel，新的 LibraryPage 实例可能错过之前已加载的数据通知。
        // 若检测到概览数据仍为空，强制重新加载，确保首次进入横屏音乐库时内容不为空。
        _ = RefreshIfEmptyAsync();
    }

    /// <summary>若 VM 概览数据为空，则异步刷新协议与概览数据。</summary>
    private async Task RefreshIfEmptyAsync()
    {
        try
        {
            // 给 _inner.OnAppearing 一帧时间完成首次加载
            await Task.Delay(150);
            if (_vm.TotalSongCount > 0 || (_vm.LibraryCards?.Count ?? 0) > 0)
            {
                Log.Debug("DesktopLibraryPage", "[RefreshIfEmpty] data already loaded, skip");
                return;
            }

            Log.Debug("DesktopLibraryPage", "[RefreshIfEmpty] overview data empty, force refresh");
            await _vm.RefreshProtocolsAsync();
            await _vm.LoadOverviewDataAsync();
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopLibraryPage", $"[RefreshIfEmpty] force refresh failed: {ex}");
        }
    }
}
