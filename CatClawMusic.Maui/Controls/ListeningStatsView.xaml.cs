using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Maui.Services;
using CatClawMusic.Maui.ViewModels;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Controls;

public partial class ListeningStatsView : ContentView
{
    private readonly PlayQueue _queue;
    private readonly IAudioPlayerService _audioPlayer;
    private ListeningStatsViewModel? _vm;
    private bool _isLoaded;

    // 颜色常量（集中管理，避免散落硬编码）
    private static readonly Color TrendGradientStart = Color.FromArgb("#55D6FF");
    private static readonly Color TrendGradientEnd = Color.FromArgb("#8C7BFF");
    private static readonly Color TrendGlowStart = Color.FromArgb("#6655D6FF");
    private static readonly Color TrendGlowEnd = Color.FromArgb("#338C7BFF");
    private static readonly Color LabelColor = Color.FromArgb("#8D93B7");

    private const int BarTrackHeight = 100;
    private const int BarTopPadding = 20;

    // 趋势图柱子尺寸批量去抖状态：首次布局时每个 trackGrid 的 SizeChanged 都会触发，
    // 若直接改 WidthRequest/HeightRequest 会反过来触发父级重新测量，30 根柱子并发重测形成
    // 级联布局风暴（专清 30 天切换时卡顿/ANR）。改为统一在一个布局周期里批量应用一次尺寸。
    private bool _trendLayoutFlushPending;
    private readonly List<(Border bar, Border glow, double barHeight, double trackWidth, double barWidth, double glowWidth)> _trendPendingSizes = new();

    // 缓存趋势图刷子，避免每次 RebuildTrendChart 重新分配 GradientStopCollection 等对象
    private static readonly LinearGradientBrush BarBrush = new()
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(0, 1),
        GradientStops = new GradientStopCollection
        {
            new GradientStop(TrendGradientStart, 0),
            new GradientStop(TrendGradientEnd, 1),
        }
    };
    private static readonly LinearGradientBrush GlowBrush = new()
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(0, 1),
        GradientStops = new GradientStopCollection
        {
            new GradientStop(TrendGlowStart, 0),
            new GradientStop(TrendGlowEnd, 1),
        }
    };

    public ListeningStatsView(ListeningStatsViewModel vm, PlayQueue queue, IAudioPlayerService audioPlayer)
    {
        InitializeComponent();
        _queue = queue;
        _audioPlayer = audioPlayer;
        BindingContext = _vm = vm;
    }

    public async Task LoadAsync(bool force = false)
    {
        if (_isLoaded && !force) return;
        _isLoaded = true;
        if (_vm != null) await _vm.LoadAsync();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (_vm != null)
        {
            _vm.TrendBars.CollectionChanged -= OnTrendBarsChanged;
        }

        _vm = BindingContext as ListeningStatsViewModel;
        if (_vm != null)
        {
            _vm.TrendBars.CollectionChanged += OnTrendBarsChanged;
            MainThread.BeginInvokeOnMainThread(() => RebuildTrendChart(_vm.TrendBars));
        }
    }

    /// <summary>趋势图重建去抖标记：一批 CollectionChanged 只触发一次重建。
    /// 填充 30 根柱子会连续触发 30 次事件，若每次都整体重建图表（清空+重建约 200 个视图），
    /// 会在主线程形成 O(n²) 的视图创建风暴，是统计页加载卡顿的主因之一。</summary>
    private bool _trendRebuildPending;

    private void OnTrendBarsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_vm == null || _trendRebuildPending) return;
        _trendRebuildPending = true;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _trendRebuildPending = false;
            if (_vm != null)
                RebuildTrendChart(_vm.TrendBars);
        });
    }

    private void RebuildTrendChart(ObservableCollection<TrendBar> bars)
    {
        var grid = TrendChartGrid;
        if (grid == null) return;

        // 丢弃上一轮遗留的待应用尺寸，避免应用到已销毁的栅格/柱子
        _trendPendingSizes.Clear();
        _trendLayoutFlushPending = false;

        grid.ColumnDefinitions.Clear();
        grid.Children.Clear();

        if (bars.Count == 0) return;

        int count = bars.Count;
        double columnSpacing = count <= 7 ? 4 : 2;

        for (int i = 0; i < count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        }
        grid.ColumnSpacing = columnSpacing;

        double barWidthRatio = count <= 7 ? 0.6 : count <= 12 ? 0.55 : 0.5;
        double labelFontSize = count <= 7 ? 10 : 8;
        double valueFontSize = count <= 7 ? 10 : 8;
        double cornerRadius = count <= 7 ? 7 : 5;
        double cornerBottomRadius = count <= 7 ? 3 : 2;

        // 使用静态缓存的刷子，避免每次重建分配新的 GradientStopCollection

        for (int i = 0; i < count; i++)
        {
            var bar = bars[i];
            double barHeight = Math.Max(bar.HeightValue, bar.HasValue ? 8 : 2);

            var stack = new VerticalStackLayout
            {
                Spacing = 3,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.End,
                Padding = new Thickness(0, BarTopPadding, 0, 0),
            };

            var valueLabel = new Label
            {
                Text = bar.ValueText,
                FontSize = valueFontSize,
                FontAttributes = FontAttributes.Bold,
                FontFamily = "OpenSansSemibold",
                TextColor = TrendGradientStart,
                HorizontalOptions = LayoutOptions.Center,
                IsVisible = bar.HasValue,
            };
            stack.Children.Add(valueLabel);

            var trackGrid = new Grid
            {
                HeightRequest = BarTrackHeight,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.End,
            };

            var glowBorder = new Border
            {
                VerticalOptions = LayoutOptions.End,
                HorizontalOptions = LayoutOptions.Center,
                StrokeThickness = 0,
                Background = GlowBrush,
                Opacity = 0.3,
            };
            trackGrid.Children.Add(glowBorder);

            var barBorder = new Border
            {
                HeightRequest = barHeight,
                VerticalOptions = LayoutOptions.End,
                HorizontalOptions = LayoutOptions.Center,
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                {
                    CornerRadius = new CornerRadius(cornerRadius, cornerRadius, cornerBottomRadius, cornerBottomRadius),
                },
                Background = BarBrush,
            };
            trackGrid.Children.Add(barBorder);

            trackGrid.SizeChanged += (s, e) =>
            {
                double colWidth = trackGrid.Width;
                if (colWidth <= 0) return;
                double bw = Math.Max(count > 20 ? 5 : 6, colWidth * barWidthRatio);
                double gw = bw + 10;

                // 收集本次所需尺寸，统一本轮布局周期批量应用，避免逐根柱子 set 反复触发重测
                _trendPendingSizes.Add((barBorder, glowBorder, barHeight, colWidth, bw, gw));
                if (_trendLayoutFlushPending) return;
                _trendLayoutFlushPending = true;
                MainThread.BeginInvokeOnMainThread(FlushTrendPendingSizes);
            };

            stack.Children.Add(trackGrid);

            var label = new Label
            {
                Text = bar.Label,
                FontSize = labelFontSize,
                TextColor = LabelColor,
                HorizontalOptions = LayoutOptions.Center,
            };
            stack.Children.Add(label);

            Grid.SetColumn(stack, i);
            grid.Children.Add(stack);
        }
    }

    /// <summary>批量应用趋势图柱子尺寸：一次布局周期内把收集到的所有柱子宽/高/辉光统一设好，
    /// 避免逐根 set WidthRequest/HeightRequest 触发父级重新测量造成级联布局风暴。</summary>
    private void FlushTrendPendingSizes()
    {
        _trendLayoutFlushPending = false;
        if (_trendPendingSizes.Count == 0) return;
        foreach (var (bar, glow, barHeight, _, barWidth, glowWidth) in _trendPendingSizes)
        {
            if (bar.Parent == null || glow.Parent == null) continue; // 栅格/柱子已被重建则跳过
            bar.WidthRequest = barWidth;
            glow.WidthRequest = glowWidth;
            glow.HeightRequest = barHeight + 10;
        }
        _trendPendingSizes.Clear();
    }

    private async void OnTopSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not TopSongItem item) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        await PlaySongAsync(item.Song);
    }

    private async void OnRecentSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Song song) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        await PlaySongAsync(song);
    }

    private async Task PlaySongAsync(Song song)
    {
        try
        {
            _queue.SelectSong(song.Id);
            if (!string.IsNullOrWhiteSpace(song.FilePath))
            {
                await _audioPlayer.PlayAsync(song.FilePath);
            }
        }
        catch (Exception ex)
        {
            Log.Debug("ListeningStatsView.xaml", $"[ListeningStatsView] PlaySongAsync failed: {ex.Message}");
        }
    }
}
