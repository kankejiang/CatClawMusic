using CatClawMusic.Core.Interfaces;
using CatClawMusic.WinUI.Services;
using CatClawMusic.WinUI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace CatClawMusic.WinUI.Views;

/// <summary>
/// 音乐库页（照 Android 端 LandscapeLibrary）：左统计格 + 入口 + 工具，右资料库卡。
/// 资料库的五行由代码生成 —— 它们只是一组静态入口，用 XAML 逐个写五遍要点会更长。
/// </summary>
public sealed partial class LibraryView : UserControl
{
    private readonly PlaylistsViewModel _playlists;
    private readonly INavigationService _navigation;

    public LibraryView(LibraryViewModel viewModel, PlaylistsViewModel playlists, INavigationService navigation)
    {
        ViewModel = viewModel;
        _playlists = playlists;
        _navigation = navigation;

        InitializeComponent();

        Loaded += OnLoaded;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LibraryViewModel.IsBusy) or nameof(LibraryViewModel.StatusText))
                UpdateStatus();
        };
    }

    public LibraryViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
        BuildSourceList();
        UpdateStatus();
    }

    /// <summary>资料库五行：本地曲库 / 网络曲库 / 我喜欢的 / 最近播放 / 远程音乐服务。</summary>
    private void BuildSourceList()
    {
        SourceList.Children.Clear();

        var rows = new (string Glyph, string Name, string Subtitle, Func<Task> Action)[]
        {
            ("\uE8F1", "本地曲库", ViewModel.LocalSubtitle, () => ShowSongsAsync()),
            ("\uE753", "网络曲库", "远程同步的曲目", () => ShowSongsAsync()),
            ("\uEB51", "我喜欢的", "收藏的歌曲", () => ShowSongsAsync()),
            ("\uE81C", "最近播放", "按时间倒序", () => ShowSongsAsync()),
            ("\uE753", "远程音乐服务", "WebDAV / Navidrome / SMB", () => ShowRemoteAsync()),
        };

        foreach (var (glyph, name, subtitle, action) in rows)
        {
            var icon = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(11),
                Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
                Child = new FontIcon
                {
                    Glyph = glyph,
                    FontSize = 16,
                    Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                },
            };

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            texts.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 11.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            });
            texts.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 9,
                Foreground = (Brush)Application.Current.Resources["TextTertiaryBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            });

            var chevron = new FontIcon
            {
                Glyph = "\uE76C",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextTertiaryBrush"],
            };

            var grid = new Grid { ColumnSpacing = 11, Padding = new Thickness(0, 9, 0, 9) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(icon, 0);
            Grid.SetColumn(texts, 1);
            Grid.SetColumn(chevron, 2);
            grid.Children.Add(icon);
            grid.Children.Add(texts);
            grid.Children.Add(chevron);

            // 每行底部一条发丝线（最后一行不加）
            var row = new Border { Child = grid, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            if (name != rows[^1].Name)
            {
                row.BorderBrush = (Brush)Application.Current.Resources["DividerBrush"];
                row.BorderThickness = new Thickness(0, 0, 0, 1);
            }

            row.Tapped += async (_, _) => await action();
            SourceList.Children.Add(row);
        }
    }

    private void UpdateStatus()
    {
        BusyRing.IsActive = ViewModel.IsBusy;
        StatusText.Text = ViewModel.StatusText;
    }

    // ── 入口 ──

    private async void OnRescanClick(object sender, RoutedEventArgs e) =>
        await ViewModel.RescanAsync();

    private async void OnRescanTapped(object sender, TappedRoutedEventArgs e) =>
        await ViewModel.RescanAsync();

    private async void OnPickFolderTapped(object sender, TappedRoutedEventArgs e) =>
        await ViewModel.PickAndScanAsync();

    private async void OnAllSongsClick(object sender, RoutedEventArgs e) => await ShowSongsAsync();

    private async void OnAlbumsClick(object sender, RoutedEventArgs e) =>
        await ShowTipAsync("专辑", $"曲库里有 {ViewModel.AlbumCount} 张专辑。");

    private async void OnArtistsClick(object sender, RoutedEventArgs e) =>
        await ShowTipAsync("艺术家", $"曲库里有 {ViewModel.ArtistCount} 位艺术家。");

    private async void OnFavoritesClick(object sender, RoutedEventArgs e) =>
        await ShowTipAsync("收藏", "收藏列表还在移植中。");

    private async void OnStatsClick(object sender, RoutedEventArgs e) =>
        await ShowTipAsync("听歌统计", "统计页还在移植中。");

    private Task ShowSongsAsync() => _navigation.NavigateToAsync("playlists");

    private Task ShowRemoteAsync() => _navigation.NavigateToAsync("settings");

    private async Task ShowTipAsync(string title, string message) =>
        await App.Services.GetRequiredService<IDialogService>().ShowAlertAsync(title, message);
}
