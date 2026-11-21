using CatClawMusic.Core.Interfaces;
using CatClawMusic.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.WinUI.Views;

/// <summary>
/// 歌单页（照 Android 端 LandscapePlaylists）：左摘要大卡，右自建歌单 + 选中歌单的歌曲。
/// 歌单行与歌曲行都用代码生成：条目是「图标 + 两行文字 + 右侧尾部」，
/// 在 XAML 里写两遍 DataTemplate 反而更长，而且 ItemsControl 组合在本 SDK 上不稳。
/// </summary>
public sealed partial class PlaylistsView : UserControl
{
    public PlaylistsView(PlaylistsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        Loaded += OnLoaded;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PlaylistsViewModel.SelectedPlaylist)
                or nameof(PlaylistsViewModel.HasSongs)
                or nameof(PlaylistsViewModel.HasSelection)
                or nameof(PlaylistsViewModel.CanEditSelection))
            {
                UpdateState();
            }
        };
    }

    public PlaylistsViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
        BuildPlaylistList();
        UpdateState();
    }

    /// <summary>左侧大卡的数字与右侧「自建歌单 · N」——两处都要跟着刷新。</summary>
    private void UpdateState()
    {
        var custom = ViewModel.Playlists.Count(p => p.CanEdit);
        PlaylistCountText.Text = custom.ToString();
        PlaylistTotalText.Text = $"共 {custom} 个";
        PlaylistSummaryText.Text = $"自建歌单 · 收藏 {ViewModel.Playlists.FirstOrDefault(p => !p.CanEdit)?.Playlist.SongCount ?? 0} 首";

        SelectedNameText.Text = ViewModel.HasSelection ? ViewModel.SelectedName : "未选择歌单";

        var hasSongs = ViewModel.HasSongs;
        SongsScroller.Visibility = hasSongs ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasSongs ? Visibility.Collapsed : Visibility.Visible;

        EmptyTitle.Text = ViewModel.HasSelection ? "这个歌单还没有歌" : "还没有歌单";
        EmptyHint.Text = ViewModel.HasSelection
            ? "到音乐库里右键一首歌 →「添加到歌单」。"
            : "点右上角「＋」创建一个歌单，再从音乐库里把歌加进来。";

        PlayAllButton.IsEnabled = hasSongs;
        RenameButton.IsEnabled = ViewModel.CanEditSelection;
        DeleteButton.IsEnabled = ViewModel.CanEditSelection;

        BuildSongList();
    }

    /// <summary>自建歌单行（点击切换选中）。系统歌单走左侧那两个彩色块，这里只列自建的。</summary>
    private void BuildPlaylistList()
    {
        PlaylistList.Children.Clear();

        var custom = ViewModel.Playlists.Where(p => p.CanEdit).ToList();
        if (custom.Count == 0)
        {
            PlaylistList.Children.Add(new TextBlock
            {
                Text = "还没有自建歌单，点右上角「＋」新建一个。",
                FontSize = 9,
                Foreground = (Brush)Application.Current.Resources["TextTertiaryBrush"],
                Margin = new Thickness(0, 6, 0, 4),
            });
            return;
        }

        foreach (var row in custom)
        {
            var badge = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(10),
                Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
                Child = new TextBlock
                {
                    Text = row.Name.Length > 0 ? row.Name[..1] : "♪",
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            texts.Children.Add(new TextBlock
            {
                Text = row.Name,
                FontSize = 11.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            });
            texts.Children.Add(new TextBlock
            {
                Text = row.CountText,
                FontSize = 9,
                Foreground = (Brush)Application.Current.Resources["TextTertiaryBrush"],
            });

            var grid = new Grid { ColumnSpacing = 10, Padding = new Thickness(0, 8, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(badge, 0);
            Grid.SetColumn(texts, 1);
            grid.Children.Add(badge);
            grid.Children.Add(texts);

            var item = new Border { Child = grid, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            var playlist = row;
            item.Tapped += (_, _) => ViewModel.SelectedPlaylist = playlist;
            PlaylistList.Children.Add(item);
        }
    }

    /// <summary>选中歌单的歌曲行。</summary>
    private void BuildSongList()
    {
        SongsList.Children.Clear();

        var index = 0;
        foreach (var row in ViewModel.Songs)
        {
            index++;
            var number = new TextBlock
            {
                Text = index.ToString(),
                FontSize = 9.5,
                Width = 22,
                Foreground = (Brush)Application.Current.Resources["TextTertiaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            texts.Children.Add(new TextBlock
            {
                Text = row.Title,
                FontSize = 11.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            });
            texts.Children.Add(new TextBlock
            {
                Text = row.Artist,
                FontSize = 9,
                Foreground = (Brush)Application.Current.Resources["TextTertiaryBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            });

            var duration = new TextBlock
            {
                Text = row.DurationText,
                FontSize = 9,
                Foreground = (Brush)Application.Current.Resources["TextTertiaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };

            var grid = new Grid { ColumnSpacing = 10, Padding = new Thickness(0, 7, 0, 7) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(number, 0);
            Grid.SetColumn(texts, 1);
            Grid.SetColumn(duration, 2);
            grid.Children.Add(number);
            grid.Children.Add(texts);
            grid.Children.Add(duration);

            var item = new Border
            {
                Child = grid,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderBrush = (Brush)Application.Current.Resources["DividerBrush"],
                BorderThickness = new Thickness(0, 0, 0, 1),
            };

            var songRow = row;
            item.Tapped += (_, _) => ViewModel.PlaySongCommand.Execute(songRow);
            SongsList.Children.Add(item);
        }
    }

    // ── 操作 ──

    private async void OnCreateClick(object sender, RoutedEventArgs e)
    {
        var name = await PromptAsync("新建歌单", string.Empty);
        if (string.IsNullOrWhiteSpace(name))
            return;

        await ViewModel.CreateAsync(name);
        BuildPlaylistList();
        UpdateState();
    }

    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanEditSelection)
            return;

        var name = await PromptAsync("重命名歌单", ViewModel.SelectedName);
        if (string.IsNullOrWhiteSpace(name))
            return;

        await ViewModel.RenameAsync(name);
        BuildPlaylistList();
        UpdateState();
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanEditSelection)
            return;

        var dialog = new ContentDialog
        {
            Title = "删除歌单",
            Content = $"确定删除「{ViewModel.SelectedName}」吗？歌单里的歌曲不会被删除。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        await ViewModel.DeleteAsync();
        BuildPlaylistList();
        UpdateState();
    }

    private void OnPlayAllClick(object sender, RoutedEventArgs e) =>
        ViewModel.PlayAllCommand.Execute(null);

    private async void OnFavoritesClick(object sender, RoutedEventArgs e) =>
        await ShowTipAsync("收藏", "收藏歌单已在列表左侧，点一下即可查看。");

    private async void OnRecentClick(object sender, RoutedEventArgs e) =>
        await ShowTipAsync("最近播放", "最近播放歌单已在列表左侧，点一下即可查看。");

    private async Task<string?> PromptAsync(string title, string initial)
    {
        var input = new TextBox { Text = initial, PlaceholderText = "歌单名称" };
        input.SelectAll();

        var dialog = new ContentDialog
        {
            Title = title,
            Content = input,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? input.Text?.Trim() : null;
    }

    private async Task ShowTipAsync(string title, string message) =>
        await App.Services.GetRequiredService<IDialogService>().ShowAlertAsync(title, message);
}
