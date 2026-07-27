using System;
using System.Linq;
using System.Reflection;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Data;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Pages;

/// <summary>横屏桌面模式音乐库页：包装 LibraryPage，摘出其 Content 并按屏幕宽度自适应重组布局。
/// 生命周期通过反射委托给内部 LibraryPage 实例（复用全部逻辑，零重复）。
/// 响应式策略：
/// - 窄屏（手机横屏，内容宽 &lt; 900dp）：单列堆叠 + 极致压缩 padding/margin/spacing，最大化信息密度
/// - 宽屏（平板/车机，内容宽 ≥ 900dp）：双列网格布局，充分利用横向空间
/// 通过 SizeChanged 监听旋转/尺寸变化，动态切换布局模式。</summary>
public class DesktopLibraryPage : ContentPage
{
    private readonly LibraryPage _inner;
    private readonly LibraryViewModel _vm;
    private ScrollView? _scrollView;
    private VerticalStackLayout? _rootStack;

    // 原始卡片引用，用于在布局模式切换时复用（避免重复创建）
    private Border? _heroCard;
    private Border? _libraryCard;
    private Border? _dataInsightCard;
    private Border? _storageCard;
    private Border? _recentCard;
    private Controls.AppPopup? _popup;

    // 当前布局模式，避免相同模式重复重建
    private bool _isWideLayout;
    private bool _layoutInitialized;

    // 切换阈值：内容区宽度 ≥ 900dp 视为宽屏（平板/车机）
    private const double WideThreshold = 900;

    public DesktopLibraryPage(MusicDatabase db, PlayQueue queue, LibraryViewModel vm, IServiceProvider sp)
    {
        _vm = vm;
        _inner = new LibraryPage(db, queue, vm, sp);

        // 摘出内部页面的 Content（Grid > ScrollView > VerticalStackLayout）
        var content = _inner.Content;
        _inner.Content = null;

        if (content is Grid grid
            && grid.Children.Count > 0
            && grid.Children[0] is ScrollView sv
            && sv.Content is VerticalStackLayout vsl)
        {
            _scrollView = sv;
            _rootStack = vsl;

            // 横屏无底部 TabBar，把 132dp 底部留白压到 18dp
            vsl.Padding = new Thickness(18, 8, 18, 18);

            // 取出各卡片：Hero / 资料库 / 数据洞察 / 存储占用 / 最近添加
            // AppPopup 不是 Border，单独保留
            var cards = vsl.Children.OfType<Border>().ToList();
            _popup = vsl.Children.OfType<Controls.AppPopup>().FirstOrDefault();
            if (cards.Count >= 5)
            {
                _heroCard = cards[0];
                _libraryCard = cards[1];
                _dataInsightCard = cards[2];
                _storageCard = cards[3];
                _recentCard = cards[4];
                _layoutInitialized = true;
            }

            // 监听尺寸变化，按宽度选择布局模式
            SizeChanged += OnPageSizeChanged;
            // 首次布局（延迟到下一帧，确保 Width 已就绪）
            Dispatcher.Dispatch(() => ApplyLayoutForWidth(Width));

            // 直接使用内部的 ScrollView 作为页面 Content，
            // 避免 DesktopMainPage.CreatePageContent 再包一层 ScrollView 导致双重滚动嵌套
            Content = sv;
            BindingContext = _inner.BindingContext;
            return;
        }

        Content = content;
        BindingContext = _inner.BindingContext;
    }

    /// <summary>页面尺寸变化时按内容宽度选择布局模式（窄屏单列 / 宽屏双列）。</summary>
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

        if (_isWideLayout == shouldBeWide) return;
        _isWideLayout = shouldBeWide;

        if (shouldBeWide)
            BuildWideLayout();
        else
            BuildCompactLayout();
    }

    /// <summary>宽屏布局（平板/车机）：Hero 全宽 → [资料库 | 数据洞察] 并排 → [存储占用 | 最近添加] 并排。</summary>
    private void BuildWideLayout()
    {
        var vsl = _rootStack!;
        vsl.Children.Clear();

        // 还原卡片原始样式（撤销紧凑模式的压缩）
        ResetCardStyle(_heroCard!);
        ResetCardStyle(_libraryCard!);
        ResetCardStyle(_dataInsightCard!);
        ResetCardStyle(_storageCard!);
        ResetCardStyle(_recentCard!);

        // Hero 保持全宽
        _heroCard!.Margin = new Thickness(0, 0, 0, 10);
        vsl.Children.Add(_heroCard);

        // 双列网格：资料库 | 数据洞察
        var row1 = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = new GridLength(1, GridUnitType.Star) },
                new() { Width = new GridLength(1, GridUnitType.Star) }
            },
            ColumnSpacing = 14,
            Margin = new Thickness(0, 0, 0, 10)
        };
        _libraryCard!.Margin = new Thickness(0);
        _dataInsightCard!.Margin = new Thickness(0);
        Grid.SetColumn(_libraryCard, 0);
        Grid.SetColumn(_dataInsightCard, 1);
        row1.Children.Add(_libraryCard);
        row1.Children.Add(_dataInsightCard);
        vsl.Children.Add(row1);

        // 双列网格：存储占用 | 最近添加
        var row2 = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = new GridLength(1, GridUnitType.Star) },
                new() { Width = new GridLength(1, GridUnitType.Star) }
            },
            ColumnSpacing = 14
        };
        _storageCard!.Margin = new Thickness(0);
        _recentCard!.Margin = new Thickness(0);
        Grid.SetColumn(_storageCard, 0);
        Grid.SetColumn(_recentCard, 1);
        row2.Children.Add(_storageCard);
        row2.Children.Add(_recentCard);
        vsl.Children.Add(row2);

        if (_popup != null)
            vsl.Children.Add(_popup);
    }

    /// <summary>窄屏紧凑布局（手机横屏）：单列堆叠，极致压缩 padding/margin/spacing 最大化信息密度。</summary>
    private void BuildCompactLayout()
    {
        var vsl = _rootStack!;
        vsl.Children.Clear();

        // 压缩页面整体 padding
        vsl.Padding = new Thickness(12, 6, 12, 12);

        // 压缩所有卡片样式
        CompactCardStyle(_heroCard!);
        CompactCardStyle(_libraryCard!);
        CompactCardStyle(_dataInsightCard!);
        CompactCardStyle(_storageCard!);
        CompactCardStyle(_recentCard!);

        // Hero 单列全宽，但压缩 margin
        _heroCard!.Margin = new Thickness(0, 0, 0, 6);
        vsl.Children.Add(_heroCard);

        _libraryCard!.Margin = new Thickness(0, 0, 0, 6);
        vsl.Children.Add(_libraryCard);

        _dataInsightCard!.Margin = new Thickness(0, 0, 0, 6);
        vsl.Children.Add(_dataInsightCard);

        _storageCard!.Margin = new Thickness(0, 0, 0, 6);
        vsl.Children.Add(_storageCard);

        _recentCard!.Margin = new Thickness(0);
        vsl.Children.Add(_recentCard);

        if (_popup != null)
            vsl.Children.Add(_popup);

        // 进一步压缩 Hero 卡片内部：遍历降低 padding
        CompactHeroInternal(_heroCard);
        // 压缩资料库列表项间距
        CompactLibraryListInternal(_libraryCard);
    }

    /// <summary>还原卡片到原始样式（从紧凑模式切换回宽屏时调用）。</summary>
    private static void ResetCardStyle(Border card)
    {
        card.Padding = 16;
        card.Margin = new Thickness(0, 18, 0, 0);
    }

    /// <summary>压缩卡片样式：降低 padding 和 margin。</summary>
    private static void CompactCardStyle(Border card)
    {
        card.Padding = 10;
    }

    /// <summary>压缩 Hero 卡片内部布局：统计卡片 padding、标题间距等。</summary>
    private static void CompactHeroInternal(Border heroCard)
    {
        try
        {
            // Hero 内部结构：Grid > [刷新按钮, VerticalStackLayout(Padding=22,20)]
            if (heroCard.Content is Grid heroGrid)
            {
                foreach (var child in heroGrid.Children)
                {
                    if (child is VerticalStackLayout vsl)
                    {
                        vsl.Padding = new Thickness(14, 10);
                        // 统计卡片网格 Margin 压缩
                        foreach (var sub in vsl.Children)
                        {
                            if (sub is Grid statGrid && statGrid.ColumnDefinitions.Count == 4)
                            {
                                statGrid.Margin = new Thickness(0, 10, 0, 0);
                                statGrid.ColumnSpacing = 6;
                                // 压缩每个统计卡片的 padding
                                foreach (var statCard in statGrid.Children.OfType<Border>())
                                {
                                    statCard.Padding = new Thickness(8, 6);
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { /* 内部结构变化时静默忽略，不影响主布局 */ }
    }

    /// <summary>压缩资料库列表内部间距。</summary>
    private static void CompactLibraryListInternal(Border libraryCard)
    {
        try
        {
            if (libraryCard.Content is VerticalStackLayout vsl)
            {
                foreach (var child in vsl.Children)
                {
                    if (child is VerticalStackLayout listContainer)
                    {
                        listContainer.Spacing = 6; // 原始 12
                    }
                }
            }
        }
        catch { }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        InvokeInner("OnAppearing");

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

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        InvokeInner("OnDisappearing");
    }

    private void InvokeInner(string methodName)
    {
        try
        {
            var method = typeof(LibraryPage).GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null)
            {
                Log.Debug("DesktopLibraryPage", $"[Lifecycle] {methodName} not found on LibraryPage");
                return;
            }
            method.Invoke(_inner, null);
            Log.Debug("DesktopLibraryPage", $"[Lifecycle] {methodName} invoked on LibraryPage (firstAppearing={GetInnerFirstAppearing()})");
        }
        catch (Exception ex)
        {
            Log.Debug("DesktopLibraryPage", $"[Lifecycle] {methodName} FAILED: {ex}");
        }
    }

    /// <summary>读取内部 LibraryPage 的 _isFirstAppearing 字段（仅用于调试日志）。</summary>
    private bool GetInnerFirstAppearing()
    {
        try
        {
            var field = typeof(LibraryPage).GetField("_isFirstAppearing",
                BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(_inner) is bool b && b;
        }
        catch { return false; }
    }
}
