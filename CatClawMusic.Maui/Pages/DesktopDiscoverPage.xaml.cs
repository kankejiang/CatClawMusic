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
        HeroDots.SelectedIndex = index;
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
            HeroDots.SelectedIndex = idx;
        }
    }

    private void OnHeroPrevClicked(object? sender, EventArgs e) => _ = ScrollHeroTo(_heroIndex - 1);
    private void OnHeroNextClicked(object? sender, EventArgs e) => _ = ScrollHeroTo(_heroIndex + 1);

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
            return;
        }

        if (_vm.DailyRecommendSongs.Count > 0 || _vm.TopPlayedSongs.Count > 0) return;

        try { await _vm.LoadExploreDataAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"DesktopDiscover OnAppearing: {ex.Message}"); }

        if (_vm.HeroCards.Count > 0)
        {
            _heroIndex = 0;
            _heroTimer?.Start();
        }
    }

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

    private async void OnHeroCardTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border && border.BindingContext is HeroCardItem heroItem && heroItem.Song != null)
        {
            await PlaySongAsync(heroItem.Song, _vm.DailyRecommendSongs.ToList());
        }
    }

    private async void OnHeroPlayTapped(object? sender, EventArgs e)
    {
        if (sender is ImageButton btn && btn.BindingContext is HeroCardItem heroItem && heroItem.Song != null)
        {
            await PlaySongAsync(heroItem.Song, _vm.DailyRecommendSongs.ToList());
        }
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
