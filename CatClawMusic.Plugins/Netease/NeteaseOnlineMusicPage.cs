using CatClawMusic.Core.Models;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace CatClawMusic.Plugins.Netease;

/// <summary>
/// 网易云在线音乐页面（C# 代码构建 UI，避免跨程序集 XAML 编译问题）。
/// <para>
/// 这是原客户端 OnlineMusicPage 的插件化版本，UI 用 C# 代码构建。
/// 通过 DynamicResource 访问宿主应用的全局资源（颜色、样式）。
/// </para>
/// </summary>
public class NeteaseOnlineMusicPage : ContentPage
{
    private readonly NeteaseOnlineMusicViewModel _vm;
    private readonly IServiceProvider _services;

    // 控件引用（事件处理需要）
    private readonly CollectionView _playlistsView;
    private readonly GridItemsLayout _playlistsLayout;
    private readonly CollectionView _songsView;
    private readonly ActivityIndicator _loadingIndicator;

    public NeteaseOnlineMusicPage(NeteaseOnlineMusicViewModel vm, IServiceProvider services)
    {
        _vm = vm;
        _services = services;
        BindingContext = _vm;

        Title = "网易云音乐";
        BackgroundColor = Application.Current?.Resources.TryGetValue("WindowBackgroundColor", out var bg) == true
            ? (Color)bg
            : Color.FromArgb("#0B0D20");

        // ── 顶部：返回 + 标题 + 账号 ──
        var backButton = CreateBackButton();
        var titleLabel = new Label
        {
            Text = "网易云音乐",
            FontSize = 17,
            FontFamily = "OpenSansSemibold",
            VerticalOptions = LayoutOptions.Center,
        };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");

        var accountButton = new Border
        {
            Padding = new Thickness(12, 7),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
        };
        accountButton.SetDynamicResource(Border.BackgroundColorProperty, "SurfaceColor");
        accountButton.SetBinding(Border.IsVisibleProperty, nameof(NeteaseOnlineMusicViewModel.SupportsLogin));
        var accountLabel = new Label { FontSize = 12, FontFamily = "OpenSansSemibold", VerticalOptions = LayoutOptions.Center };
        accountLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");
        accountLabel.SetBinding(Label.TextProperty, nameof(NeteaseOnlineMusicViewModel.AccountButtonText));
        accountButton.Content = accountLabel;
        var accountTap = new TapGestureRecognizer();
        accountTap.Tapped += OnAccountTapped;
        accountButton.GestureRecognizers.Add(accountTap);

        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = GridLength.Auto },
                new() { Width = GridLength.Star },
                new() { Width = GridLength.Auto },
            },
            ColumnSpacing = 12,
            Padding = new Thickness(16, 12, 16, 8),
            Children = { backButton, titleLabel, accountButton },
        };
        Grid.SetColumn(titleLabel, 1);
        Grid.SetColumn(accountButton, 2);

        // ── 搜索框 ──
        var searchEntry = new Entry { Placeholder = "搜索歌曲..." };
        searchEntry.SetDynamicResource(Entry.TextColorProperty, "TextPrimaryColor");
        searchEntry.SetBinding(Entry.TextProperty, new Binding(nameof(NeteaseOnlineMusicViewModel.SearchQuery), mode: BindingMode.TwoWay));
        searchEntry.ReturnType = ReturnType.Search;
        searchEntry.Completed += async (_, _) => await _vm.SearchSongsAsync();

        var searchBorder = new Border
        {
            Content = searchEntry,
            Padding = new Thickness(14, 8),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Margin = new Thickness(16, 0, 16, 8),
        };
        searchBorder.SetDynamicResource(Border.BackgroundColorProperty, "SurfaceColor");

        // ── 私人漫游 / 每日推荐 / 排行榜 入口 ──
        var fmCard = CreateEntryCard("🎧 私人漫游", "随机推荐", "#667eea", "#764ba2");
        var fmTap = new TapGestureRecognizer();
        fmTap.Tapped += async (_, _) => await _vm.LoadPrivateFmAsync();
        fmCard.GestureRecognizers.Add(fmTap);

        var dailyCard = CreateEntryCard("📅 每日推荐", "今天想听什么", "#f7971e", "#ffd200");
        var dailyTap = new TapGestureRecognizer();
        dailyTap.Tapped += async (_, _) => await _vm.LoadDailyRecommendAsync();
        dailyCard.GestureRecognizers.Add(dailyTap);

        var toplistCard = CreateEntryCard("🔥 排行榜", "飙升 · 新歌 · 热歌", "#f953c6", "#b91d73");
        var toplistTap = new TapGestureRecognizer();
        toplistTap.Tapped += async (_, _) => await _vm.LoadToplistsAsync();
        toplistCard.GestureRecognizers.Add(toplistTap);

        var entryRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new() { Width = GridLength.Star }, new() { Width = GridLength.Star } },
            ColumnSpacing = 10,
            Padding = new Thickness(16, 2, 16, 6),
            Children = { fmCard, dailyCard },
        };
        Grid.SetColumn(dailyCard, 1);

        // ── 分类 chips（水平滚动）──
        var categoriesLayout = new HorizontalStackLayout { Spacing = 6, Padding = new Thickness(16, 4, 16, 6) };
        categoriesLayout.SetBinding(HorizontalStackLayout.IsVisibleProperty, nameof(NeteaseOnlineMusicViewModel.ShowPlaylists));
        // 用 BindableLayout 绑定分类列表
        BindableLayout.SetItemsSource(categoriesLayout, _vm.Categories);
        BindableLayout.SetItemTemplate(categoriesLayout, new DataTemplate(() =>
        {
            var chip = new Border
            {
                Padding = new Thickness(10, 5),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
            };
            chip.SetDynamicResource(Border.BackgroundColorProperty, "SurfaceColor");
            var chipLabel = new Label { FontSize = 11, VerticalOptions = LayoutOptions.Center };
            chipLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
            chipLabel.SetBinding(Label.TextProperty, nameof(CategoryChipItem.Name));
            chip.Content = chipLabel;
            // 选中态样式
            chip.Triggers.Add(new DataTrigger(typeof(Border))
            {
                Binding = new Binding(nameof(CategoryChipItem.IsSelected)),
                Value = true,
                Setters = { new Setter { Property = Border.BackgroundColorProperty, Value = Application.Current?.Resources["PrimaryColor"] } },
            });
            chipLabel.Triggers.Add(new DataTrigger(typeof(Label))
            {
                Binding = new Binding(nameof(CategoryChipItem.IsSelected)),
                Value = true,
                Setters = { new Setter { Property = Label.TextColorProperty, Value = Colors.White } },
            });
            var tap = new TapGestureRecognizer();
            tap.SetBinding(TapGestureRecognizer.CommandProperty, new Binding(
                nameof(NeteaseOnlineMusicViewModel.SelectCategoryCommand), source: _vm));
            tap.SetBinding(TapGestureRecognizer.CommandParameterProperty, new Binding(nameof(CategoryChipItem.Name)));
            chip.GestureRecognizers.Add(tap);
            return chip;
        }));

        var categoriesScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            HeightRequest = 42,
            Content = categoriesLayout,
        };
        categoriesScroll.SetBinding(ScrollView.IsVisibleProperty, nameof(NeteaseOnlineMusicViewModel.ShowPlaylists));

        // ── 歌单网格 ──
        _playlistsLayout = new GridItemsLayout(2, ItemsLayoutOrientation.Vertical)
        {
            HorizontalItemSpacing = 10,
            VerticalItemSpacing = 10,
        };
        _playlistsView = new CollectionView
        {
            ItemsLayout = _playlistsLayout,
            SelectionMode = SelectionMode.Single,
            Margin = new Thickness(16, 6, 16, 0),
        };
        _playlistsView.SetBinding(CollectionView.IsVisibleProperty, nameof(NeteaseOnlineMusicViewModel.ShowPlaylists));
        _playlistsView.SetBinding(CollectionView.ItemsSourceProperty, nameof(NeteaseOnlineMusicViewModel.Playlists));
        _playlistsView.ItemTemplate = new DataTemplate(() => CreatePlaylistItemTemplate());
        _playlistsView.SelectionChanged += OnPlaylistSelected;

        // ── 歌曲列表模式 ──
        var songsBackButton = new Border
        {
            Padding = new Thickness(10, 5),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Content = new Label { Text = "‹ 歌单", FontSize = 12 },
        };
        songsBackButton.SetDynamicResource(Border.BackgroundColorProperty, "SurfaceColor");
        var songsBackLabel = (Label)songsBackButton.Content!;
        songsBackLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");
        var songsBackTap = new TapGestureRecognizer();
        songsBackTap.Tapped += async (_, _) => await _vm.BackToPlaylistsAsync();
        songsBackButton.GestureRecognizers.Add(songsBackTap);

        var songsTitleLabel = new Label
        {
            FontSize = 14,
            FontFamily = "OpenSansSemibold",
            MaxLines = 1,
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalOptions = LayoutOptions.Center,
        };
        songsTitleLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");
        songsTitleLabel.SetBinding(Label.TextProperty, nameof(NeteaseOnlineMusicViewModel.CurrentListTitle));

        var playAllButton = new Border
        {
            Padding = new Thickness(12, 6),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Content = new HorizontalStackLayout
            {
                Spacing = 5,
                Children =
                {
                    new Label { Text = "▶", FontSize = 11, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center },
                    new Label { Text = "全部播放", FontSize = 12, FontFamily = "OpenSansSemibold", TextColor = Colors.White, VerticalOptions = LayoutOptions.Center },
                },
            },
        };
        playAllButton.SetDynamicResource(Border.BackgroundColorProperty, "PrimaryColor");
        playAllButton.SetBinding(Border.IsVisibleProperty, nameof(NeteaseOnlineMusicViewModel.HasPlaylistSongs));
        var playAllTap = new TapGestureRecognizer();
        playAllTap.Tapped += async (_, _) => await _vm.PlayAllAsync();
        playAllButton.GestureRecognizers.Add(playAllTap);

        var songsHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new() { Width = GridLength.Auto }, new() { Width = GridLength.Star }, new() { Width = GridLength.Auto } },
            ColumnSpacing = 8,
            Padding = new Thickness(16, 4, 16, 8),
            Children = { songsBackButton, songsTitleLabel, playAllButton },
        };
        Grid.SetColumn(songsTitleLabel, 1);
        Grid.SetColumn(playAllButton, 2);
        songsHeader.SetBinding(Grid.IsVisibleProperty, nameof(NeteaseOnlineMusicViewModel.ShowSongs));

        _songsView = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            Margin = new Thickness(0, 6, 0, 0),
        };
        _songsView.SetBinding(CollectionView.IsVisibleProperty, nameof(NeteaseOnlineMusicViewModel.ShowSongs));
        _songsView.SetBinding(CollectionView.ItemsSourceProperty, nameof(NeteaseOnlineMusicViewModel.Songs));
        _songsView.ItemTemplate = new DataTemplate(() => CreateSongItemTemplate());
        _songsView.SelectionChanged += OnSongSelected;

        // ── 加载指示器 ──
        _loadingIndicator = new ActivityIndicator
        {
            WidthRequest = 36,
            HeightRequest = 36,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
        };
        _loadingIndicator.SetDynamicResource(ActivityIndicator.ColorProperty, "PrimaryColor");
        _loadingIndicator.SetBinding(ActivityIndicator.IsRunningProperty, nameof(NeteaseOnlineMusicViewModel.IsLoading));
        _loadingIndicator.SetBinding(ActivityIndicator.IsVisibleProperty, nameof(NeteaseOnlineMusicViewModel.IsLoading));

        // ── 组装页面 ──
        var contentGrid = new Grid
        {
            RowDefinitions = new RowDefinitionCollection { new() { Height = GridLength.Auto }, new() { Height = GridLength.Auto }, new() { Height = GridLength.Auto }, new() { Height = GridLength.Auto }, new() { Height = GridLength.Star } },
            Children = { headerGrid, searchBorder, entryRow, categoriesScroll, _playlistsView, songsHeader, _songsView, _loadingIndicator },
        };
        Grid.SetRow(searchBorder, 1);
        Grid.SetRow(entryRow, 2);
        Grid.SetRow(categoriesScroll, 3);
        Grid.SetRow(_playlistsView, 4);
        Grid.SetRow(songsHeader, 4);
        Grid.SetRow(_songsView, 4);
        Grid.SetRow(_loadingIndicator, 4);

        // 网格中的歌曲 header 和歌曲列表叠加在歌单列表之上（通过 IsVisible 互斥）
        Content = contentGrid;

        // 页面尺寸变化时调整歌单列数
        SizeChanged += (_, _) => AdjustPlaylistSpan();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        AdjustPlaylistSpan();
        await _vm.OnAppearingAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.Detach();
    }

    /// <summary>点击账号按钮：已登录则退出，未登录则跳转 WebView 登录页</summary>
    private async void OnAccountTapped(object? sender, EventArgs e)
    {
        if (_vm.IsLoggedIn)
        {
            await _vm.LogoutAsync();
            return;
        }
        if (!_vm.SupportsLogin || _vm.CurrentLoginInfo == null) return;

        // 跳转宿主的 WebView 登录页（通过 Shell 路由）。
        // 宿主 WebViewLoginViewModel 通过 platform 参数匹配 IOnlineMusicPlugin.PlatformName，
        // 网易云插件的 PlatformName 固定为 "netease"。
        await Shell.Current.GoToAsync($"webviewlogin?platform=netease");
    }

    private void AdjustPlaylistSpan()
    {
        var w = Width > 0 ? Width : (Content?.Width ?? 0);
        if (w <= 0) return;
        int span = w switch
        {
            < 600 => 2,
            < 900 => 3,
            < 1200 => 4,
            < 1500 => 5,
            _ => 6,
        };
        if (_playlistsLayout.Span != span) _playlistsLayout.Span = span;
    }

    private async void OnPlaylistSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is CollectionView cv) cv.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is not OnlinePlaylist playlist) return;
        await _vm.OpenPlaylistAsync(playlist);
    }

    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is CollectionView cv) cv.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is not OnlineSong song) return;
        await _vm.PlaySongAsync(song);
    }

    // ── UI 构建辅助方法 ──

    private Border CreateBackButton()
    {
        var border = new Border
        {
            Padding = new Thickness(12, 7),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Content = new Label { Text = "‹", FontSize = 26, Margin = new Thickness(0, -4, 0, 0), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center },
        };
        border.SetDynamicResource(Border.BackgroundColorProperty, "SurfaceColor");
        var backLabel = (Label)border.Content!;
        backLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => { try { await Shell.Current.Navigation.PopAsync(); } catch { } };
        border.GestureRecognizers.Add(tap);
        return border;
    }

    private static Border CreateEntryCard(string title, string subtitle, string color1, string color2)
    {
        var titleLabel = new Label { Text = title, FontSize = 15, FontFamily = "OpenSansSemibold", TextColor = Colors.White };
        var subtitleLabel = new Label { Text = subtitle, FontSize = 11, TextColor = Color.FromArgb("#CCFFFFFF") };
        var content = new VerticalStackLayout
        {
            Spacing = 3,
            Children = { titleLabel, subtitleLabel },
        };
        return new Border
        {
            Padding = new Thickness(14, 12),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(color1), Offset = 0 },
                    new GradientStop { Color = Color.FromArgb(color2), Offset = 1 },
                },
            },
            Content = content,
        };
    }

    private static View CreatePlaylistItemTemplate()
    {
        var coverBorder = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            HeightRequest = 150,
        };
        coverBorder.SetDynamicResource(Border.BackgroundColorProperty, "SurfaceColor");
        var coverImage = new Image { Aspect = Aspect.AspectFill, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill };
        coverImage.SetBinding(Image.SourceProperty, new Binding(nameof(OnlinePlaylist.CoverUrl)) { TargetNullValue = "ic_music_note" });
        coverBorder.Content = coverImage;

        var nameLabel = new Label
        {
            FontSize = 12,
            FontFamily = "OpenSansSemibold",
            MaxLines = 2,
            LineBreakMode = LineBreakMode.TailTruncation,
            Padding = new Thickness(6, 0, 6, 6),
        };
        nameLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");
        nameLabel.SetBinding(Label.TextProperty, nameof(OnlinePlaylist.Name));

        var countLabel = new Label
        {
            FontSize = 10,
            Padding = new Thickness(6, 0, 6, 6),
        };
        countLabel.SetDynamicResource(Label.TextColorProperty, "TextHintColor");
        countLabel.SetBinding(Label.TextProperty, new Binding(nameof(OnlinePlaylist.SongCount), stringFormat: "{0} 首"));

        var card = new Border
        {
            Padding = new Thickness(0),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
        };
        card.SetDynamicResource(Border.BackgroundColorProperty, "CardBackgroundColor");
        card.Content = new VerticalStackLayout
        {
            Spacing = 6,
            Children = { coverBorder, nameLabel, countLabel },
        };
        return card;
    }

    private static View CreateSongItemTemplate()
    {
        var coverBorder = new Border
        {
            WidthRequest = 40,
            HeightRequest = 40,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            StrokeThickness = 0,
        };
        coverBorder.SetDynamicResource(Border.BackgroundColorProperty, "SurfaceColor");
        var coverImage = new Image { Aspect = Aspect.AspectFill, WidthRequest = 40, HeightRequest = 40 };
        coverImage.SetBinding(Image.SourceProperty, new Binding(nameof(OnlineSong.CoverUrl)) { TargetNullValue = "ic_music_note" });
        coverBorder.Content = coverImage;

        var titleLabel = new Label { FontSize = 14, FontFamily = "OpenSansSemibold", MaxLines = 1 };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");
        titleLabel.SetBinding(Label.TextProperty, nameof(OnlineSong.Title));

        var artistLabel = new Label { FontSize = 11, MaxLines = 1 };
        artistLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
        artistLabel.SetBinding(Label.TextProperty, nameof(OnlineSong.Artist));

        var textLayout = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { titleLabel, artistLabel },
        };

        return new Grid
        {
            Padding = new Thickness(14, 8),
            ColumnDefinitions = new ColumnDefinitionCollection { new() { Width = GridLength.Auto }, new() { Width = GridLength.Star } },
            ColumnSpacing = 12,
            Children = { coverBorder, textLayout },
        };
    }
}
