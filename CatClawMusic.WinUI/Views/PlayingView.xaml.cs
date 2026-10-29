using CatClawMusic.Core.Interfaces;
using CatClawMusic.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CatClawMusic.WinUI.Views;

/// <summary>
/// 播放页：左侧封面/信息/控制，右侧滚动歌词。
/// 歌词滚动与高亮由 <see cref="NowPlayingViewModel"/> 驱动，这里只负责把当前行滚到视口中间。
/// </summary>
public sealed partial class PlayingView : UserControl
{
    private readonly INavigationService _navigation;
    private bool _seeking;

    public PlayingView(NowPlayingViewModel viewModel, INavigationService navigation)
    {
        ViewModel = viewModel;
        _navigation = navigation;

        InitializeComponent();

        ViewModel.CurrentLineChanged += (_, index) => ScrollToLine(index);
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(NowPlayingViewModel.PositionSeconds) or nameof(NowPlayingViewModel.DurationSeconds))
                SyncProgress();
        };

        LyricsList.SizeChanged += (_, _) => UpdateLyricSpacers();
        Loaded += (_, _) =>
        {
            UpdateLyricSpacers();
            SyncProgress();
        };
    }

    public NowPlayingViewModel ViewModel { get; }

    private async void OnBackClick(object sender, RoutedEventArgs e) => await _navigation.GoBackAsync();

    private async void OnToggleFavoriteClick(object sender, RoutedEventArgs e) =>
        await ViewModel.ToggleFavoriteCommand.ExecuteAsync(null);

    private void OnDesktopLyricsClick(object sender, RoutedEventArgs e) =>
        ViewModel.ToggleDesktopLyricsCommand.Execute(null);

    private async void OnTogglePlayClick(object sender, RoutedEventArgs e) =>
        await ViewModel.TogglePlayCommand.ExecuteAsync(null);

    private async void OnNextClick(object sender, RoutedEventArgs e) =>
        await ViewModel.NextCommand.ExecuteAsync(null);

    private async void OnPreviousClick(object sender, RoutedEventArgs e) =>
        await ViewModel.PreviousCommand.ExecuteAsync(null);

    private void OnCyclePlayModeClick(object sender, RoutedEventArgs e) =>
        ViewModel.CyclePlayModeCommand.Execute(null);

    private async void OnLyricClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LyricLineRow row)
            await ViewModel.SeekToLineAsync(row);
    }

    private void OnProgressPointerPressed(object sender, PointerRoutedEventArgs e) => _seeking = true;

    private async void OnProgressPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _seeking = false;
        await ViewModel.SeekAsync(ProgressSlider.Value);
    }

    private void SyncProgress()
    {
        if (_seeking)
            return;

        ProgressSlider.Maximum = Math.Max(1, ViewModel.DurationSeconds);
        ProgressSlider.Value = Math.Clamp(ViewModel.PositionSeconds, 0, ProgressSlider.Maximum);
    }

    /// <summary>上下留白 = 半屏，配合 ScrollIntoView(Leading) 让当前行停在中间。</summary>
    private void UpdateLyricSpacers()
    {
        var half = Math.Max(0, (LyricsList.ActualHeight - 70) / 2);
        LyricsHeaderSpacer.Height = half;
        LyricsFooterSpacer.Height = half;
    }

    private void ScrollToLine(int index)
    {
        if (index < 0 || index >= ViewModel.LyricLines.Count)
            return;

        LyricsList.ScrollIntoView(ViewModel.LyricLines[index], ScrollIntoViewAlignment.Leading);
    }
}
