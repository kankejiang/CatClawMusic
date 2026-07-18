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

    private static readonly Color BarTopColor = Color.FromArgb("#55D6FF");
    private static readonly Color BarBottomColor = Color.FromArgb("#8C7BFF");
    private const int BarTrackHeight = 100;
    private const int BarTopPadding = 20;

    // 缓存趋势图刷子，避免每次 RebuildTrendChart 重新分配 GradientStopCollection 等对象
    private static readonly LinearGradientBrush BarBrush = new()
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(0, 1),
        GradientStops = new GradientStopCollection
        {
            new GradientStop(Color.FromArgb("#55D6FF"), 0),
            new GradientStop(Color.FromArgb("#8C7BFF"), 1),
        }
    };
    private static readonly LinearGradientBrush GlowBrush = new()
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(0, 1),
        GradientStops = new GradientStopCollection
        {
            new GradientStop(Color.FromArgb("#6655D6FF"), 0),
            new GradientStop(Color.FromArgb("#338C7BFF"), 1),
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

    private void OnTrendBarsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_vm != null)
        {
            MainThread.BeginInvokeOnMainThread(() => RebuildTrendChart(_vm.TrendBars));
        }
    }

    private void RebuildTrendChart(ObservableCollection<TrendBar> bars)
    {
        var grid = TrendChartGrid;
        if (grid == null) return;

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
                TextColor = BarTopColor,
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
                barBorder.WidthRequest = bw;
                glowBorder.WidthRequest = gw;
                glowBorder.HeightRequest = barHeight + 10;
            };

            stack.Children.Add(trackGrid);

            var label = new Label
            {
                Text = bar.Label,
                FontSize = labelFontSize,
                TextColor = Color.FromArgb("#8D93B7"),
                HorizontalOptions = LayoutOptions.Center,
            };
            stack.Children.Add(label);

            Grid.SetColumn(stack, i);
            grid.Children.Add(stack);
        }
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
