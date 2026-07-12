using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Maui.Services;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// PC（Windows 桌面端）发现页：宽屏多列布局，复用 <see cref="SearchViewModel"/> 数据层。
/// 包含页头（发现 + 时段问候 + AI 智能推荐开关 + 主题切换）、分段标签、
/// Hero 横向多卡轮播（2 张同屏 + 箭头/圆点/自动轮播）、每日推荐/推荐艺人横滑、
/// 排行榜双列（最多播放 + 我的最爱）、歌手网格、推荐专辑网格。
/// </summary>
public partial class DesktopDiscoverPage : ContentPage
{
    private readonly SearchViewModel _vm;
    private readonly PlayQueue _queue;
    private readonly MusicDatabase _db;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly IServiceProvider _services;
    private readonly IThemeService? _themeService;

    private IDispatcherTimer? _heroTimer;
    private int _heroIndex;
    private double _heroCardWidth;
    private const double HeroSpacing = 18;

    /// <summary>初始化 <see cref="DesktopDiscoverPage"/> 并注入所需服务与视图模型。</summary>
    public DesktopDiscoverPage(MusicDatabase db, PlayQueue queue, SearchViewModel vm,
        IAudioPlayerService audioPlayer, IServiceProvider services, IThemeService? themeService)
    {
        InitializeComponent();
        _db = db;
        _queue = queue;
        _vm = vm;
        _audioPlayer = audioPlayer;
        _services = services;
        _themeService = themeService;
        BindingContext = _vm;

        // 用 ImageSourceHelper 在代码后台设图标源（WinUI 上 XAML 字面量 Source="ic_xxx" 不渲染）
        HeroPrev.Source = Helpers.ImageSourceHelper.FromNameOriginal("ic_arrow_left");
        HeroNext.Source = Helpers.ImageSourceHelper.FromNameOriginal("ic_arrow_right");
        DailyPrev.Source = Helpers.ImageSourceHelper.FromNameOriginal("ic_arrow_left");
        DailyNext.Source = Helpers.ImageSourceHelper.FromNameOriginal("ic_arrow_right");
        ArtistPrev.Source = Helpers.ImageSourceHelper.FromNameOriginal("ic_arrow_left");
        ArtistNext.Source = Helpers.ImageSourceHelper.FromNameOriginal("ic_arrow_right");

        UpdateTabVisualState(0);
        UpdateThemeIcon();
        SetupHeroTimer();

        _vm.PropertyChanged += OnViewModelPropertyChanged;
        // 注意：本页被 DesktopMainPage 提取 Content 后，ContentPage 自身脱离可视化树，
        // 因此监听 HeroScroll（仍留在树中）的尺寸变化来重排 Hero 卡片宽度。
        HeroScroll.SizeChanged += OnHeroSizeChanged;
    }

    // ─── Hero carousel ───

    private void SetupHeroTimer()
    {
        _heroTimer = Dispatcher.CreateTimer();
        _heroTimer.Interval = TimeSpan.FromSeconds(5);
        _heroTimer.Tick += OnHeroTimerTick;
    }

    private void OnHeroTimerTick(object? sender, EventArgs e)
    {
        if (_vm.HeroCards.Count == 0) return;
        if (!IsVisible || !PanelRecommend.IsVisible) return;
        _ = ScrollHeroTo(_heroIndex + 1);
    }

    private void OnHeroSizeChanged(object? sender, EventArgs e)
    {
        LayoutHeroCards();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_vm.HeroCards))
        {
            Dispatcher.Dispatch(() =>
            {
                LayoutHeroCards();
                _ = ScrollHeroTo(0);
            });
        }
    }

    /// <summary>根据可见宽度计算每张 Hero 卡宽度，使一屏显示约 2 张；并刷新圆点数量。</summary>
    private void LayoutHeroCards()
    {
        HeroDots.Count = _vm.HeroCards.Count;
        if (HeroScroll.Width <= 0) return;
        var cardW = (HeroScroll.Width - HeroSpacing) / 2;
        if (cardW < 280) cardW = 280;
        _heroCardWidth = cardW;

        foreach (View child in HeroTrack.Children)
        {
            child.WidthRequest = cardW;
        }
    }

    private async Task ScrollHeroTo(int index)
    {
        if (_vm.HeroCards.Count == 0) return;
        var count = _vm.HeroCards.Count;
        index = ((index % count) + count) % count;
        _heroIndex = index;

        var step = _heroCardWidth > 0 ? _heroCardWidth + HeroSpacing : HeroScroll.Width / 2;
        await HeroScroll.ScrollToAsync(index * step, 0, true);
        HeroDots.Position = index;
    }

    private void OnHeroScrolled(object? sender, ScrolledEventArgs e)
    {
        if (_heroCardWidth <= 0) return;
        var count = _vm.HeroCards.Count;
        if (count == 0) return;
        var idx = (int)Math.Round(e.ScrollX / (_heroCardWidth + HeroSpacing));
        idx = Math.Clamp(idx, 0, count - 1);
        if (idx != _heroIndex)
        {
            _heroIndex = idx;
            HeroDots.Position = idx;
        }
    }

    private void OnHeroPrevClicked(object? sender, EventArgs e) => _ = ScrollHeroTo(_heroIndex - 1);
    private void OnHeroNextClicked(object? sender, EventArgs e) => _ = ScrollHeroTo(_heroIndex + 1);

    // ─── 每日推荐左右箭头 ───

    /// <summary>每次翻页的卡片数（根据可见宽度动态计算）。</summary>
    private int DailyPageSize => DailyList.Width > 0 ? Math.Max(1, (int)((DailyList.Width + 12) / (150 + 12))) : 4;

    private void OnDailyPrevClicked(object? sender, EventArgs e)
    {
        var items = _vm.DailyRecommendSongs;
        if (items.Count == 0) return;
        var page = DailyPageSize;
        var targetIdx = Math.Max(0, _dailyVisibleStart - page);
        ScrollDailyTo(targetIdx);
    }

    private void OnDailyNextClicked(object? sender, EventArgs e)
    {
        var items = _vm.DailyRecommendSongs;
        if (items.Count == 0) return;
        var page = DailyPageSize;
        var targetIdx = Math.Min(items.Count - 1, _dailyVisibleStart + page);
        ScrollDailyTo(targetIdx);
    }

    private int _dailyVisibleStart;

    private void ScrollDailyTo(int index)
    {
        var items = _vm.DailyRecommendSongs;
        if (items.Count == 0 || index < 0 || index >= items.Count) return;
        _dailyVisibleStart = index;
        DailyList.ScrollTo(items[index], null, ScrollToPosition.Start);
        UpdateDailyArrowVisibility();
    }

    /// <summary>根据滚动位置更新每日推荐箭头的显示/隐藏。</summary>
    private void UpdateDailyArrowVisibility()
    {
        var items = _vm.DailyRecommendSongs;
        if (items.Count == 0) return;
        var page = DailyPageSize;
        DailyPrev.IsVisible = _dailyVisibleStart > 0;
        DailyNext.IsVisible = (_dailyVisibleStart + page) < items.Count;
    }

    // ─── 推荐艺人左右箭头 ───

    private int ArtistPageSize => ArtistsRow.Width > 0 ? Math.Max(1, (int)((ArtistsRow.Width + 14) / (92 + 14))) : 5;

    private void OnArtistPrevClicked(object? sender, EventArgs e)
    {
        var items = _vm.Artists;
        if (items.Count == 0) return;
        var page = ArtistPageSize;
        var targetIdx = Math.Max(0, _artistVisibleStart - page);
        ScrollArtistTo(targetIdx);
    }

    private void OnArtistNextClicked(object? sender, EventArgs e)
    {
        var items = _vm.Artists;
        if (items.Count == 0) return;
        var page = ArtistPageSize;
        var targetIdx = Math.Min(items.Count - 1, _artistVisibleStart + page);
        ScrollArtistTo(targetIdx);
    }

    private int _artistVisibleStart;

    private void ScrollArtistTo(int index)
    {
        var items = _vm.Artists;
        if (items.Count == 0 || index < 0 || index >= items.Count) return;
        _artistVisibleStart = index;
        ArtistsRow.ScrollTo(items[index], null, ScrollToPosition.Start);
        UpdateArtistArrowVisibility();
    }

    private void UpdateArtistArrowVisibility()
    {
        var items = _vm.Artists;
        if (items.Count == 0) return;
        var page = ArtistPageSize;
        ArtistPrev.IsVisible = _artistVisibleStart > 0;
        ArtistNext.IsVisible = (_artistVisibleStart + page) < items.Count;
    }

    // ─── Lifecycle ───

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _heroTimer?.Stop();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _vm.GreetingText = CalculateGreeting();

        if (_vm.HeroCards.Count > 0)
        {
            LayoutHeroCards();
            _heroTimer?.Start();
        }

#if WINDOWS
        // PC 端：将横向 CollectionView 的纵向滚轮事件转发给父级 ScrollView，
        // 解决"鼠标滚轮被横向内容截获、无法翻页"的问题
        FixHorizontalMouseWheelCapture();
#endif

        if (LocalScanService.NeedsReload)
        {
            LocalScanService.NeedsReload = false;
            try { await _vm.ReloadAfterScanAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"DesktopDiscover reload: {ex.Message}"); }
            if (_vm.HeroCards.Count > 0)
            {
                _heroIndex = 0;
                _heroTimer?.Start();
            }
            RefreshArrowVisibility();
            return;
        }

        if (_vm.DailyRecommendSongs.Count > 0 || _vm.TopPlayedSongs.Count > 0)
        {
            RefreshArrowVisibility();
            return;
        }

        try { await _vm.LoadExploreDataAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"DesktopDiscover OnAppearing: {ex.Message}"); }

        if (_vm.HeroCards.Count > 0)
        {
            _heroIndex = 0;
            _heroTimer?.Start();
        }
        RefreshArrowVisibility();
    }

    // ─── Arrow visibility helpers ───

    private void RefreshArrowVisibility()
    {
        UpdateDailyArrowVisibility();
        UpdateArtistArrowVisibility();
    }

#if WINDOWS
    // ─── PC 鼠标滚轮修复：横向区域不再截获纵向滚动 ───

    /// <summary>
    /// 在 WinUI 层找到每日推荐/推荐艺人的 CollectionView 内部 ScrollViewer，
    /// 将纵向鼠标滚轮事件转发给父级 ScrollView，让用户可以正常上下翻页。
    /// </summary>
    private void FixHorizontalMouseWheelCapture()
    {
        if (this.Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement pageEl) return;

        var horizontalTargets = new[] { DailyList, ArtistsRow };
        foreach (var cv in horizontalTargets)
        {
            if (cv?.Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement cvEl) continue;
            var sv = FindWinUIChild<Microsoft.UI.Xaml.Controls.ScrollViewer>(cvEl);
            if (sv == null) continue;
            sv.PointerWheelChanged += OnHorizontalAreaWheelChanged;
        }
    }

    private void OnHorizontalAreaWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(sender as Microsoft.UI.Xaml.UIElement);
        var delta = point.Properties.MouseWheelDelta;
        if (Math.Abs(delta) > 0.1)
        {
            e.Handled = true;
            if (this.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement pageEl)
            {
                var parentSv = FindWinUIParent<Microsoft.UI.Xaml.Controls.ScrollViewer>(pageEl);
                if (parentSv != null)
                {
                    var offset = parentSv.VerticalOffset - delta;
                    parentSv.ChangeView(null, Math.Max(0, offset), null);
                }
            }
        }
    }

    private static T? FindWinUIChild<T>(Microsoft.UI.Xaml.DependencyObject parent) where T : Microsoft.UI.Xaml.DependencyObject
    {
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            var result = FindWinUIChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    private static T? FindWinUIParent<T>(Microsoft.UI.Xaml.DependencyObject child) where T : Microsoft.UI.Xaml.DependencyObject
    {
        var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T found) return found;
            parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
        }
        return null;
    }
#endif

    // ─── Category tabs ───

    private void OnCategoryTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string p && int.TryParse(p, out int idx))
        {
            _vm.CurrentCategory = idx;
            UpdateTabVisualState(idx);
        }
    }

    private void UpdateTabVisualState(int selectedIndex)
    {
        var primary = (Color)(Application.Current?.Resources["PrimaryColor"] ?? Colors.Purple);
        var cardBg = (Color)(Application.Current?.Resources["CardBackgroundColor"] ?? Colors.Transparent);
        var textSecondary = (Color)(Application.Current?.Resources["TextSecondaryColor"] ?? Colors.Gray);

        TabRec.BackgroundColor = selectedIndex == 0 ? primary : cardBg;
        TabRecLabel.TextColor = selectedIndex == 0 ? Colors.White : textSecondary;

        TabRank.BackgroundColor = selectedIndex == 1 ? primary : cardBg;
        TabRankLabel.TextColor = selectedIndex == 1 ? Colors.White : textSecondary;

        TabArtist.BackgroundColor = selectedIndex == 2 ? primary : cardBg;
        TabArtistLabel.TextColor = selectedIndex == 2 ? Colors.White : textSecondary;

        TabAlbum.BackgroundColor = selectedIndex == 3 ? primary : cardBg;
        TabAlbumLabel.TextColor = selectedIndex == 3 ? Colors.White : textSecondary;
    }

    private void OnArtistsViewAllClicked(object? sender, EventArgs e)
    {
        _vm.CurrentCategory = 2;
        UpdateTabVisualState(2);
    }

    // ─── Theme switch ───

    private void OnThemeToggleClicked(object? sender, EventArgs e)
    {
        if (_themeService == null) return;
        var next = _themeService.IsEffectivelyDark() ? DarkModeSetting.Light : DarkModeSetting.Dark;
        _themeService.SetDarkModeSetting(next);
        UpdateThemeIcon();
    }

    private void UpdateThemeIcon()
    {
        if (ThemeIcon == null) return;
        ThemeIcon.Text = _themeService != null && _themeService.IsEffectivelyDark()
            ? "\uD83C\uDF19"   // 🌙
            : "\u2600\uFE0F";  // ☀️
    }

    // ─── Shuffle ───

    private void OnShuffleDailyClicked(object? sender, EventArgs e)
    {
        _vm.ShuffleDailyCommand.Execute(null);
        // 换批后回到开头并刷新箭头
        _dailyVisibleStart = 0;
        ScrollDailyTo(0);
    }

    // ─── Song selection handlers ───

    private async void OnDailySongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Song song) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        await PlaySongAsync(song, _vm.DailyRecommendSongs.ToList());
    }

    private async void OnTopPlayedSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Song song) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        await PlaySongAsync(song, _vm.TopPlayedSongs.ToList());
    }

    private async void OnFavoriteSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Song song) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        await PlaySongAsync(song, _vm.FavoriteSongs.ToList());
    }

    // ─── Hero play ───

    /// <summary>从手势/按钮的 sender 中解析出绑定的 HeroCardItem。
    /// 注意：PC 端 Hero 用 ScrollView+BindableLayout，TapGestureRecognizer.Tapped 的 sender 是识别器本身（非 Border），
    /// 但二者都会从可视化树继承 BindingContext，故统一用 BindableObject.BindingContext 取值。</summary>
    private static HeroCardItem? ResolveHeroItem(object? sender)
        => (sender as BindableObject)?.BindingContext as HeroCardItem;

    private async void OnHeroCardTapped(object? sender, TappedEventArgs e)
    {
        var heroItem = ResolveHeroItem(sender);
        if (heroItem?.Song != null)
        {
            await PlayHeroSongAsync(heroItem.Song);
        }
    }

    private async void OnHeroPlayTapped(object? sender, EventArgs e)
    {
        var heroItem = ResolveHeroItem(sender);
        if (heroItem?.Song != null)
        {
            await PlayHeroSongAsync(heroItem.Song);
        }
    }

    /// <summary>播放 Hero 卡歌曲：以每日推荐为播放队列，但确保被点击的歌曲（如 AI 推荐歌）也在队列中。</summary>
    private async Task PlayHeroSongAsync(Song song)
    {
        var list = _vm.DailyRecommendSongs.ToList();
        if (!list.Any(s => s.Id == song.Id))
            list.Insert(0, song);
        await PlaySongAsync(song, list);
    }

    // ─── Rank list ───

    private async void OnRankItemTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border && border.BindingContext is Song song)
        {
            await PlaySongAsync(song, _vm.TopPlayedSongs.ToList());
        }
    }

    private async void OnRankPlayTapped(object? sender, EventArgs e)
    {
        if (sender is ImageButton btn && btn.BindingContext is Song song)
        {
            await PlaySongAsync(song, _vm.TopPlayedSongs.ToList());
        }
    }

    private async void OnFavItemTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border && border.BindingContext is Song song)
        {
            await PlaySongAsync(song, _vm.FavoriteSongs.ToList());
        }
    }

    private async void OnFavPlayTapped(object? sender, EventArgs e)
    {
        if (sender is ImageButton btn && btn.BindingContext is Song song)
        {
            await PlaySongAsync(song, _vm.FavoriteSongs.ToList());
        }
    }

    /// <summary>排名标签着色：前 3 名用主色，其余用次要色。</summary>
    private void OnRankItemBindingContextChanged(object? sender, EventArgs e)
    {
        if (sender is not Border border) return;
        if (border.BindingContext is not Song song) return;

        var list = _vm.TopPlayedSongs;
        var index = list.IndexOf(song);
        if (index < 0) return;

        var rank = index + 1;
        if (border.Content is Grid grid && grid.Children.Count > 0 && grid.Children[0] is Label rankLabel)
        {
            rankLabel.Text = rank.ToString();
            rankLabel.TextColor = rank <= 3
                ? (Color)Application.Current?.Resources["PrimaryColor"]!
                : (Color)Application.Current?.Resources["TextHintColor"]!;
        }
    }

    // ─── Artist / Album navigation ───

    private async void OnArtistSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SearchArtistItem artist) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        await Shell.Current.GoToAsync($"artistdetail?artistName={Uri.EscapeDataString(artist.Name)}");
    }

    private async void OnAlbumSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SearchAlbumItem album) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        await Shell.Current.GoToAsync($"albumdetail?title={Uri.EscapeDataString(album.Title)}");
    }

    // ─── Playback ───

    private async Task PlaySongAsync(Song song, IReadOnlyList<Song> songs)
    {
        try
        {
            if (songs.Count > 0) _queue.SetSongs(songs);
            _queue.SelectSong(song.Id);
            if (!string.IsNullOrWhiteSpace(song.FilePath))
            {
                await _audioPlayer.PlayAsync(song.FilePath);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("播放失败", ex.Message, "确定");
        }
    }

    private static string CalculateGreeting()
    {
        var hour = DateTime.Now.Hour;
        return hour switch
        {
            >= 0 and < 6 => "凌晨好，为你精选深夜好歌",
            >= 6 and < 12 => "早上好，为你精选晨间好歌",
            >= 12 and < 18 => "下午好，为你精选午后好歌",
            _ => "晚上好，为你精选今日好歌"
        };
    }
}
