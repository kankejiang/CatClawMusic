using CatClawMusic.Core.Interfaces;
using CatClawMusic.WinUI.Services;
using CatClawMusic.WinUI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace CatClawMusic.WinUI.Views;

/// <summary>
/// 发现页（照 Android 端 LandscapeDiscover）：左英雄卡 / 右 5 个彩色入口 + 货架面板 / 底本周精选。
/// 数据与命令在 <see cref="DiscoverViewModel"/>；这里只负责：
/// ① 五颗入口的渐变底色；② 封面路径 → BitmapImage；③ 货架三段显隐；④ 入口分发。
/// </summary>
public sealed partial class DiscoverView : UserControl
{
    private readonly INavigationService _navigation;
    private readonly CoverService _covers;

    /// <summary>入口强调色（Android QUICK_* 常量）：每颗按钮一层 α.34→α.05 的线性渐变。</summary>
    private static readonly Dictionary<string, string> TileColors = new()
    {
        ["ai"] = "#7C9BE8",
        ["shuffle"] = "#5FBEAE",
        ["fm"] = "#5FBEAE",
        ["top"] = "#9A8DD6",
        ["favorite"] = "#D8AA6A",
    };

    public DiscoverView(
        DiscoverViewModel viewModel,
        INavigationService navigation,
        CoverService covers)
    {
        ViewModel = viewModel;
        _navigation = navigation;
        _covers = covers;

        InitializeComponent();
        ApplyTileGradients();

        Loaded += OnLoaded;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DiscoverViewModel.HeroSong))
                _ = UpdateHeroAsync();
        };
    }

    public DiscoverViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
        await UpdateHeroAsync();
        await LoadShelfCoversAsync();
        BuildShelves();
        BuildTopStrip();
        UpdateShelfVisibility();
    }

    /// <summary>给五颗入口刷各自的线性渐变（写五套 XAML GradientStop 会把结构淹掉）。</summary>
    private void ApplyTileGradients()
    {
        var tiles = new (Button Button, string Key)[]
        {
            (TileAi, "ai"),
            (TileShuffle, "shuffle"),
            (TileFm, "fm"),
            (TileTop, "top"),
            (TileFavorite, "favorite"),
        };

        foreach (var (button, key) in tiles)
            button.Background = CreateTileBrush(TileColors[key]);
    }

    private static LinearGradientBrush CreateTileBrush(string hex)
    {
        var text = hex.TrimStart('#');
        var color = Windows.UI.Color.FromArgb(
            0xFF,
            Convert.ToByte(text[..2], 16),
            Convert.ToByte(text.Substring(2, 2), 16),
            Convert.ToByte(text.Substring(4, 2), 16));

        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop
        {
            Color = Windows.UI.Color.FromArgb(0x59, color.R, color.G, color.B),
            Offset = 0,
        });
        brush.GradientStops.Add(new GradientStop
        {
            Color = Windows.UI.Color.FromArgb(0x0D, color.R, color.G, color.B),
            Offset = 1,
        });
        return brush;
    }

    private async Task UpdateHeroAsync()
    {
        var hero = ViewModel.HeroSong;

        HeroTitle.Text = hero?.Title ?? "还没有歌曲";
        HeroArtist.Text = hero?.Artist ?? string.Empty;

        if (hero is null)
        {
            // 空库：整块文字都收起来，只留居中那条提示 —— 否则标题与提示会叠在一起
            HeroCover.Source = null;
            HeroCover.Visibility = Visibility.Collapsed;
            HeroBlock.Visibility = Visibility.Collapsed;
            EmptyHint.Visibility = Visibility.Visible;
            EmptyHint.Text = ViewModel.StatusText;
            return;
        }

        EmptyHint.Visibility = Visibility.Collapsed;
        HeroCover.Visibility = Visibility.Visible;
        HeroBlock.Visibility = Visibility.Visible;

        var path = await _covers.GetCoverPathAsync(hero.Song);
        HeroCover.Source = await LoadBitmapAsync(path);
    }

    /// <summary>货架封面：逐张异步补，单张失败不影响整页（占位色块仍在）。</summary>
    private async Task LoadShelfCoversAsync()
    {
        foreach (var row in ViewModel.RecSongs)
        {
            if (row.Cover is not null)
                continue;

            try
            {
                var path = await _covers.GetCoverPathAsync(row.Song);
                row.Cover = await LoadBitmapAsync(path);
            }
            catch
            {
                // 忽略：封面缺失时留在占位底色上。
            }
        }
    }

    /// <summary>
    /// 货架卡片用代码生成，而不是 XAML 的 ItemsControl。
    /// 本 SDK 的 XAML 编译器在「ItemsControl + 横向 ItemsPanelTemplate」上会抛内部空引用
    /// （只表现为无本地化信息的 WMC9999），代码生成可完全绕开，且这里卡数固定、无需虚拟化。
    /// </summary>
    private void BuildShelves()
    {
        FillSongsShelf();
        FillAlbumsShelf();
        FillArtistsShelf();
    }

    private void FillSongsShelf()
    {
        SongsShelf.Content = BuildStrip(ViewModel.RecSongs, (row) =>
        {
            var image = new Image { Stretch = Stretch.UniformToFill };
            image.SetValue(FrameworkElement.WidthProperty, 78.0);
            image.SetValue(FrameworkElement.HeightProperty, 78.0);
            if (row.Cover is not null)
                image.Source = row.Cover;

            var cover = new Border
            {
                Width = 78,
                Height = 78,
                CornerRadius = new CornerRadius(12),
                Background = (Brush)Application.Current.Resources["AccentSoftBrush"],
                Child = image,
            };

            var stack = new StackPanel { Width = 78, Spacing = 5 };
            stack.Children.Add(cover);
            stack.Children.Add(ShelfTitle(row.Title));
            stack.Children.Add(ShelfSubtitle(row.Artist));
            return stack;
        });
    }

    private void FillAlbumsShelf()
    {
        AlbumsShelf.Content = BuildStrip(ViewModel.RecAlbums, (row) => MakeAlbumCard(row.Name, row.Subtitle));
    }

    private void FillArtistsShelf()
    {
        ArtistsShelf.Content = BuildStrip(ViewModel.RecArtists, (row) =>
        {
            var circle = new Border
            {
                Width = 66,
                Height = 66,
                CornerRadius = new CornerRadius(33),
                Background = (Brush)Application.Current.Resources["QuickVioletBrush"],
                Child = new TextBlock
                {
                    Text = row.Initial,
                    FontSize = 16,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            var name = ShelfTitle(row.Name);
            name.TextAlignment = TextAlignment.Center;

            var stack = new StackPanel { Width = 66, Spacing = 5 };
            stack.Children.Add(circle);
            stack.Children.Add(name);
            return stack;
        });
    }

    private StackPanel MakeAlbumCard(string name, string count)
    {
        var cover = new Border
        {
            Width = 78,
            Height = 78,
            CornerRadius = new CornerRadius(12),
            Background = (Brush)Application.Current.Resources["AccentSoftBrush"],
            Child = new FontIcon
            {
                Glyph = "\uE8D6",
                FontSize = 20,
                Foreground = (Brush)Application.Current.Resources["AccentInkBrush"],
            },
        };

        var stack = new StackPanel { Width = 78, Spacing = 5 };
        stack.Children.Add(cover);
        stack.Children.Add(ShelfTitle(name));
        stack.Children.Add(ShelfSubtitle(count));
        return stack;
    }

    private static TextBlock ShelfTitle(string text) => new()
    {
        Text = text,
        FontSize = 8.5,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = 1,
    };

    private static TextBlock ShelfSubtitle(string text) => new()
    {
        Text = text,
        FontSize = 8,
        Foreground = (Brush)Application.Current.Resources["TextTertiaryBrush"],
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = 1,
    };

    /// <summary>把一组展示模型铺成横向条（间距 10，与 Android 货架一致）。</summary>
    private static StackPanel BuildStrip<T>(IEnumerable<T> rows, Func<T, UIElement> project)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        foreach (var row in rows)
            strip.Children.Add(project(row));
        return strip;
    }

    /// <summary>底部「本周精选」：播放次数 Top 5。</summary>
    private void BuildTopStrip()
    {
        TopSongsStrip.Children.Clear();

        foreach (var row in ViewModel.TopSongs)
        {
            var icon = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(8),
                Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
                Child = new FontIcon
                {
                    Glyph = "\uE8D6",
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["AccentInkBrush"],
                },
            };

            var texts = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 100 };
            texts.Children.Add(new TextBlock
            {
                Text = row.Title,
                FontSize = 9.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            });
            texts.Children.Add(new TextBlock
            {
                Text = row.Artist,
                FontSize = 8,
                Foreground = (Brush)Application.Current.Resources["TextTertiaryBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            });

            var item = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                VerticalAlignment = VerticalAlignment.Center,
            };
            item.Children.Add(icon);
            item.Children.Add(texts);
            TopSongsStrip.Children.Add(item);
        }
    }

    private static async Task<BitmapImage?> LoadBitmapAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            using IRandomAccessStream stream = await FileRandomAccessStream.OpenAsync(path, FileAccessMode.Read);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    // ── 货架分段 ──

    private void OnShelfTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } || !int.TryParse(tag, out var index))
            return;

        ViewModel.ShelfTab = index;
        UpdateShelfVisibility();
    }

    private void UpdateShelfVisibility()
    {
        var tab = ViewModel.ShelfTab;

        SongsShelf.Visibility = tab == 0 ? Visibility.Visible : Visibility.Collapsed;
        AlbumsShelf.Visibility = tab == 1 ? Visibility.Visible : Visibility.Collapsed;
        ArtistsShelf.Visibility = tab == 2 ? Visibility.Visible : Visibility.Collapsed;

        var tabs = new (Button Button, int Index)[] { (TabSongs, 0), (TabAlbums, 1), (TabArtists, 2) };
        var primary = (Brush)Application.Current.Resources["TextPrimaryBrush"];
        var tertiary = (Brush)Application.Current.Resources["TextTertiaryBrush"];
        var selectedBg = Application.Current.Resources["CardBackgroundStrongBrush"] as Brush;

        foreach (var (button, index) in tabs)
        {
            var on = index == tab;
            button.Foreground = on ? primary : tertiary;
            button.FontWeight = on ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
            button.Background = on ? selectedBg : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }

    // ── 入口动作 ──

    private async void OnPlayHeroClick(object sender, RoutedEventArgs e) =>
        await ViewModel.PlayHeroCommand.ExecuteAsync(null);

    private void OnRefreshHeroClick(object sender, RoutedEventArgs e)
    {
        ViewModel.RefreshHeroCommand.Execute(null);
        _ = UpdateHeroAsync();
    }

    private async void OnAiClick(object sender, RoutedEventArgs e) =>
        await ShowTipAsync("AI 助手", "桌面端的 AI 助手还在接入中，Android 端已可用。");

    private async void OnShuffleClick(object sender, RoutedEventArgs e) =>
        await ViewModel.ShuffleAllCommand.ExecuteAsync(null);

    private async void OnPrivateFmClick(object sender, RoutedEventArgs e) =>
        await ShowTipAsync("私人 FM", "需要接入在线音源后才能用，目前只有本地曲库。");

    private async void OnTopClick(object sender, RoutedEventArgs e) =>
        await ViewModel.PlayTopCommand.ExecuteAsync(null);

    private async void OnFavoriteClick(object sender, RoutedEventArgs e) =>
        await ViewModel.PlayFavoritesCommand.ExecuteAsync(null);

    private async void OnSearchClick(object sender, RoutedEventArgs e) =>
        await _navigation.NavigateToAsync("library");

    private async void OnStatsClick(object sender, RoutedEventArgs e) =>
        await ShowTipAsync("听歌统计", "统计页还在移植中。");

    private async void OnSeeAllClick(object sender, RoutedEventArgs e) =>
        await _navigation.NavigateToAsync("library");

    /// <summary>货架卡片目前没有独立的下钻页（专辑/艺人详情还没移植），点了先给明确反馈。</summary>
    private async Task ShowTipAsync(string title, string message) =>
        await App.Services.GetRequiredService<IDialogService>().ShowAlertAsync(title, message);
}