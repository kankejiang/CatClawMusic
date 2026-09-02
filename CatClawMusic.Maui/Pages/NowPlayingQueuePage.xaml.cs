using System.Collections.ObjectModel;
using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.Services;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// 独立全屏播放队列页：从播放页按钮推入（Shell push / 桌面嵌入）。
/// ⚡ 与「覆盖在播放页上的弹窗/Sheet」不同——本页是独立页面，推入后播放页整体进入后台，
/// 其动态背景/毛玻璃/歌词定时器全部停止，队列滚动不再与底层渲染抢 GPU/主线程。
/// 支持：点击行播放、滑动删除（单行移除保持滚动位置）、当前歌曲高亮、从底部滑入转场。
/// </summary>
public partial class NowPlayingQueuePage : ContentPage
{
    private readonly NowPlayingViewModel _vm;
    private ObservableCollection<Song>? _items;

    public NowPlayingQueuePage(NowPlayingViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        // ⚠ 横屏桌面舞台内嵌（MainPage.ShowDesktopStage）下本页 Content 可能被借入内容区，
        // 页面自身收不到尺寸事件——方向相关仅体现在转场距离，使用根 Grid 尺寸即可。
        RootGrid.SizeChanged += OnRootGridSizeChanged;
    }

    /// <summary>从屏幕底部滑入转场（首次布局完成后执行一次）。</summary>
    private void OnRootGridSizeChanged(object? sender, EventArgs e)
    {
        RootGrid.SizeChanged -= OnRootGridSizeChanged;

        if (RootGrid.Width <= 0 || RootGrid.Height <= 0) return;
        var h = RootGrid.Height;
        RootGrid.TranslationY = h;
        _ = RootGrid.TranslateTo(0, 0, 280, Easing.CubicOut);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        BuildQueue();
        ScrollToCurrent(false);

        // 转场兜底：若布局就绪事件已过（二次进入），强制回到原位
        if (RootGrid.TranslationY != 0)
            _ = RootGrid.TranslateTo(0, 0, 280, Easing.CubicOut);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // 结束转场动画，避免返回时残留位移（下一次推入会重新走滑入）
        this.CancelAnimations();
        RootGrid.TranslationY = 0;
    }

    /// <summary>构建队列列表（每打开重建，反映最新队列）。</summary>
    private void BuildQueue()
    {
        var songs = _vm.GetQueueSongs();
        var currentSong = _vm.CurrentSong;
        var primaryColor = (Color)Application.Current!.Resources["PrimaryColor"];
        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var textSecondary = (Color)Application.Current!.Resources["TextSecondaryColor"];

        CountLabel.Text = $"{songs.Count} 首歌曲";
        _items = new ObservableCollection<Song>(songs);

        QueueList.ItemTemplate = new DataTemplate(() =>
        {
            var row = BuildRow(primaryColor, textPrimary, textSecondary, currentSong);
            return row;
        });
        QueueList.ItemsSource = _items;

        if (songs.Count == 0)
        {
            // 空队列占位
            CountLabel.Text = "队列为空";
        }
    }

    private Grid BuildRow(Color primaryColor, Color textPrimary, Color textSecondary, Song? currentSong)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = GridLength.Auto },
                new() { Width = new GridLength(1, GridUnitType.Star) },
                new() { Width = GridLength.Auto }
            },
            HeightRequest = 56,
            Padding = new Thickness(0, 4),
            ColumnSpacing = 10
        };

        var indicator = new Image
        {
            WidthRequest = 16,
            HeightRequest = 16,
            Aspect = Aspect.AspectFit,
            Source = ImageSourceHelper.FromNameOriginal("ic_play_dark"),
            IsVisible = false,
            VerticalOptions = LayoutOptions.Center
        };
        grid.Add(indicator, 0);

        var titleLabel = new Label
        {
            FontSize = 14,
            FontFamily = "OpenSansSemibold",
            MaxLines = 1,
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalOptions = LayoutOptions.Center
        };
        titleLabel.SetBinding(Label.TextProperty, "Title");

        var artistLabel = new Label
        {
            FontSize = 12,
            TextColor = textSecondary,
            MaxLines = 1,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        artistLabel.SetBinding(Label.TextProperty, "Artist");

        var infoStack = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        infoStack.Children.Add(titleLabel);
        infoStack.Children.Add(artistLabel);
        grid.Add(infoStack, 1);

        var removeBtn = new ImageButton
        {
            WidthRequest = 32,
            HeightRequest = 32,
            CornerRadius = 16,
            Padding = 6,
            Aspect = Aspect.AspectFit,
            BackgroundColor = Colors.Transparent,
            Source = ImageSourceHelper.FromNameOriginal("ic_close"),
            VerticalOptions = LayoutOptions.Center
        };
        grid.Add(removeBtn, 2);

        // 当前歌曲高亮
        grid.BindingContextChanged += (s, _) =>
        {
            if (s is Grid g && g.BindingContext is Song song)
            {
                var isCurrent = currentSong != null && song.Id == currentSong.Id;
                titleLabel.TextColor = isCurrent ? primaryColor : textPrimary;
                indicator.IsVisible = isCurrent;
                if (isCurrent)
                    titleLabel.FontAttributes = FontAttributes.Bold;
            }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (grid.BindingContext is Song song)
            {
                _ = _vm.PlaySongFromQueueCommand.ExecuteAsync(song);
                GoBack();
            }
        };
        grid.GestureRecognizers.Add(tap);

        removeBtn.Clicked += (_, _) =>
        {
            if (grid.BindingContext is Song song)
            {
                _ = _vm.RemoveSongFromQueueCommand.ExecuteAsync(song);
                // 单行移除保持滚动位置
                _items?.Remove(song);
                if (_items != null)
                    CountLabel.Text = $"{_items.Count} 首歌曲";
            }
        };

        return grid;
    }

    private void ScrollToCurrent(bool animate)
    {
        var songs = _vm.GetQueueSongs();
        var current = _vm.CurrentSong;
        if (current != null && songs is { Count: > 0 })
        {
            var idx = 0;
            for (int i = 0; i < songs.Count; i++)
            {
                if (songs[i].Id == current.Id) { idx = i; break; }
            }
            if (QueueList.ItemsSource is ObservableCollection<Song> list && list.Count > 0)
                QueueList.ScrollTo(idx, position: ScrollToPosition.Center, animate: animate);
        }
    }

    private void OnBackClicked(object? sender, EventArgs e) => GoBack();

    private void GoBack()
    {
        var shell = DesktopNavigation.TryGetShell();
        if (shell != null && shell.Navigation.NavigationStack.Count > 1)
        {
            _ = shell.Navigation.PopAsync();
            return;
        }
        if (shell != null)
        {
            _ = shell.GoToAsync("..");
            return;
        }
        DesktopNavigation.CloseEmbedded();
    }
}