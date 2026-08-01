using System;
using System.Threading;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.Services;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Storage;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.Pages;

public partial class LibraryPage : ContentPage
{
    private readonly MusicDatabase _db;
    private readonly PlayQueue _queue;
    private readonly LibraryViewModel _vm;
    private readonly IAudioPlayerService? _audioPlayer;
    private readonly INetworkMusicService? _networkMusicService;
    private readonly SearchViewModel? _searchVm;
    private readonly ExploreDataService? _exploreDataService;
    private readonly LocalScanService? _scanService;
    private readonly IServiceProvider _sp;
    private CancellationTokenSource? _refreshCts;
    private bool _isFirstAppearing = true;

    // 数据洞察环形图导航状态
    private List<PieDataset>? _pieDatasets;
    private int _pieIndex;
    private readonly List<Border> _pieDotViews = new();

    public LibraryPage(MusicDatabase db, PlayQueue queue, LibraryViewModel vm, IServiceProvider sp)
    {
        InitializeComponent();
        _db = db;
        _queue = queue;
        _vm = vm;
        _audioPlayer = sp.GetService<IAudioPlayerService>();
        _networkMusicService = sp.GetService<INetworkMusicService>();
        _searchVm = sp.GetService<SearchViewModel>();
        _exploreDataService = sp.GetService<ExploreDataService>();
        _scanService = sp.GetService<LocalScanService>();
        _sp = sp;
        BindingContext = _vm;

        // 事件订阅由 OnHandlerChanging 管理，支持页面实例复用（Singleton MainPage）
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.LibraryCards))
            RenderLibraryCards();
        else if (e.PropertyName == nameof(LibraryViewModel.FormatSizeItems))
            RenderFormatBars();
        else if (e.PropertyName == nameof(LibraryViewModel.RecentAddItems))
            RenderRecentAdd();
        else if (e.PropertyName == nameof(LibraryViewModel.PieDatasets))
            RenderDataInsight();
    }

    private void RenderLibraryCards()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LibraryCardsContainer.Children.Clear();
            foreach (var card in _vm.LibraryCards)
            {
                LibraryCardsContainer.Children.Add(CreateLibraryCard(card));
            }
        });
    }

    private View CreateLibraryCard(LibraryCardItem item)
    {
        var (statusBg, statusColor) = item.StatusType switch
        {
            "ok" => (Color.FromArgb("#1E7AF0C8"), Color.FromArgb("#7AF0C8")),
            "on" => (Color.FromArgb("#1E55D6FF"), Color.FromArgb("#55D6FF")),
            "sync" => (Color.FromArgb("#1EFFB36B"), Color.FromArgb("#FFB36B")),
            "off" => (Color.FromArgb("#1E8D93B7"), Color.FromArgb("#8D93B7")),
            _ => (Color.FromArgb("#1E7AF0C8"), Color.FromArgb("#7AF0C8"))
        };

        var (iconColor1, iconColor2) = item.IconBackground switch
        {
            var s when s.Contains("6250F6") => (Color.FromArgb("#6250F6"), Color.FromArgb("#8C7BFF")),
            var s when s.Contains("1E9FE0") => (Color.FromArgb("#1E9FE0"), Color.FromArgb("#55D6FF")),
            var s when s.Contains("FF5C8A") => (Color.FromArgb("#FF5C8A"), Color.FromArgb("#FF7AAE")),
            var s when s.Contains("7A6CF0") => (Color.FromArgb("#7A6CF0"), Color.FromArgb("#A78BFA")),
            var s when s.Contains("5A6280") => (Color.FromArgb("#5A6280"), Color.FromArgb("#8D93B7")),
            _ => (Color.FromArgb("#6250F6"), Color.FromArgb("#8C7BFF"))
        };

        var cardBorder = new Border
        {
            Padding = new Thickness(14),
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Stroke = (Color)Application.Current!.Resources["GlassStrokeColor"],
            BackgroundColor = (Color)Application.Current!.Resources["CardOverlayColor"]
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = GridLength.Auto },
                new() { Width = GridLength.Star },
                new() { Width = GridLength.Auto }
            },
            ColumnSpacing = 13
        };

        var iconBorder = new Border
        {
            WidthRequest = 50,
            HeightRequest = 50,
            StrokeShape = new RoundRectangle { CornerRadius = 15 },
            StrokeThickness = 0,
            Background = new LinearGradientBrush
            {
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new(iconColor1, 0),
                    new(iconColor2, 1)
                }
            }
        };
        iconBorder.Content = new Image
        {
            Source = item.IconSource,
            WidthRequest = 25,
            HeightRequest = 25,
            Aspect = Aspect.AspectFit,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        };
        grid.Add(iconBorder, 0);

        var bodyStack = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center };
        var nameRow = new HorizontalStackLayout { Spacing = 8 };
        nameRow.Add(new Label
        {
            Text = item.Name,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = (Color)Application.Current!.Resources["TextPrimaryColor"]
        });

        if (!string.IsNullOrEmpty(item.StatusText))
        {
            var statusBadge = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = 99 },
                StrokeThickness = 1,
                Stroke = statusBg,
                BackgroundColor = statusBg,
                Padding = new Thickness(8, 2),
                VerticalOptions = LayoutOptions.Center
            };
            statusBadge.Content = new Label
            {
                Text = item.StatusText,
                FontSize = 10.5,
                FontAttributes = FontAttributes.Bold,
                TextColor = statusColor
            };
            nameRow.Add(statusBadge);
        }
        bodyStack.Add(nameRow);

        bodyStack.Add(new Label
        {
            Text = item.Subtitle,
            FontSize = 12,
            TextColor = (Color)Application.Current!.Resources["TextHintColor"],
            Margin = new Thickness(0, 3, 0, 0),
            LineBreakMode = LineBreakMode.TailTruncation
        });
        grid.Add(bodyStack, 1);

        var arrowBorder = new Border
        {
            WidthRequest = 34,
            HeightRequest = 34,
            StrokeShape = new RoundRectangle { CornerRadius = 11 },
            StrokeThickness = 1,
            Stroke = (Color)Application.Current!.Resources["GlassStrokeColor"],
            BackgroundColor = (Color)Application.Current!.Resources["ButtonOverlayColor"],
            VerticalOptions = LayoutOptions.Center
        };
        arrowBorder.Content = new Image
        {
            Source = ImageSourceHelper.FromNameThemed("ic_arrow_forward"),
            WidthRequest = 16,
            HeightRequest = 16,
            Aspect = Aspect.AspectFit,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        };
        grid.Add(arrowBorder, 2);

        var target = item.Target;
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => OnLibraryCardTapped(target);
        cardBorder.GestureRecognizers.Add(tap);

        cardBorder.Content = grid;
        return cardBorder;
    }

    private void RenderFormatBars()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            FormatBarsContainer.Children.Clear();
            foreach (var fmt in _vm.FormatSizeItems)
            {
                FormatBarsContainer.Children.Add(CreateFormatBar(fmt));
            }
        });
    }

    private View CreateFormatBar(FormatSizeItem item)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = GridLength.Auto },
                new() { Width = new GridLength(64) },
                new() { Width = GridLength.Star },
                new() { Width = new GridLength(70) }
            },
            ColumnSpacing = 10
        };

        grid.Add(new Border
        {
            WidthRequest = 9,
            HeightRequest = 9,
            StrokeShape = new RoundRectangle { CornerRadius = 3 },
            StrokeThickness = 0,
            BackgroundColor = item.Color,
            VerticalOptions = LayoutOptions.Center
        }, 0);

        grid.Add(new Label
        {
            Text = item.Name,
            FontSize = 12.5,
            TextColor = (Color)Application.Current!.Resources["TextSecondaryColor"],
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        }, 1);

        var trackBorder = new Border
        {
            HeightRequest = 8,
            StrokeShape = new RoundRectangle { CornerRadius = 99 },
            StrokeThickness = 0,
            BackgroundColor = (Color)Application.Current!.Resources["ProgressTrackColor"],
            VerticalOptions = LayoutOptions.Center,
            Padding = new Thickness(0)
        };
        var fillWidth = Math.Max(0, Math.Min(1, item.Progress));
        var fillBorder = new Border
        {
            HeightRequest = 8,
            StrokeShape = new RoundRectangle { CornerRadius = 99 },
            StrokeThickness = 0,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            WidthRequest = Math.Max(4, fillWidth * 280),
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops = new GradientStopCollection
                {
                    new(Color.FromArgb("#8C7BFF"), 0),
                    new(Color.FromArgb("#55D6FF"), 1)
                }
            }
        };
        trackBorder.Content = fillBorder;
        grid.Add(trackBorder, 2);

        grid.Add(new Label
        {
            Text = item.SizeText,
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = (Color)Application.Current!.Resources["TextPrimaryColor"],
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.End
        }, 3);

        return grid;
    }

    private void RenderRecentAdd()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RecentAddContainer.Children.Clear();
            foreach (var item in _vm.RecentAddItems)
            {
                RecentAddContainer.Children.Add(CreateRecentCard(item));
            }
        });
    }

    private View CreateRecentCard(RecentAddItem item)
    {
        var stack = new VerticalStackLayout { WidthRequest = 132 };

        var cover = new Border
        {
            WidthRequest = 132,
            HeightRequest = 132,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            StrokeThickness = 1,
            Stroke = (Color)Application.Current!.Resources["GlassStrokeColor"],
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new(item.CoverColor1, 0),
                    new(item.CoverColor2, 1)
                }
            }
        };
        cover.Content = new Image
        {
            Source = "ic_music_note.svg",
            WidthRequest = 34,
            HeightRequest = 34,
            Aspect = Aspect.AspectFit,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        };
        stack.Add(cover);

        stack.Add(new Label
        {
            Text = item.Title,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = (Color)Application.Current!.Resources["TextPrimaryColor"],
            Margin = new Thickness(0, 8, 0, 0),
            LineBreakMode = LineBreakMode.TailTruncation
        });

        stack.Add(new Label
        {
            Text = item.Artist,
            FontSize = 11.5,
            TextColor = (Color)Application.Current!.Resources["TextHintColor"],
            Margin = new Thickness(0, 2, 0, 0),
            LineBreakMode = LineBreakMode.TailTruncation
        });

        return stack;
    }

    private void OnLibraryCardTapped(string target)
    {
        switch (target)
        {
            case "local":
                OpenLibrarySubPage(typeof(AllSongsPage), "library/allsongs?source=local", source: "local");
                break;
            case "network":
                OpenLibrarySubPage(typeof(AllSongsPage), "library/allsongs?source=network", source: "network");
                break;
            case "favorite":
                OpenLibrarySubPage(typeof(AllSongsPage), "library/allsongs?source=favorites", source: "favorites");
                break;
            case "recent":
                OpenLibrarySubPage(typeof(AllSongsPage), "library/allsongs?source=recent", source: "recent");
                break;
            case "trash":
                break;
        }
    }

    // === Hero 统计数字点击导航 ===

    private void OnStatSongsTapped(object? sender, EventArgs e)
        => OpenLibrarySubPage(typeof(AllSongsPage), "library/allsongs?source=all", source: "all");

    private void OnStatArtistsTapped(object? sender, EventArgs e)
        => OpenLibrarySubPage(typeof(ArtistsPage), "library/artists");

    private void OnStatAlbumsTapped(object? sender, EventArgs e)
        => OpenLibrarySubPage(typeof(AlbumsPage), "library/albums");

    private void OnStatRecentTapped(object? sender, EventArgs e)
        => OpenLibrarySubPage(typeof(AllSongsPage), "library/allsongs?source=recent", source: "recent");

    private void OnScanCompleted(object? sender, int importedCount)
    {
        // 本地扫描后刷新音乐库（列表/总览/当前 tab 同步）
        RefreshLibraryAfterDataChangedAsync();
    }

    private void OnNetworkSyncCompleted(object? sender, int importedCount)
    {
        // 网络音乐库（WebDAV/SMB/Navidrome）同步后，网络音乐库卡片与网络 tab 需重新同步，
        // 避免"网络音乐有缓存但网络音乐库未同步"的问题。
        RefreshLibraryAfterDataChangedAsync();
    }

    /// <summary>
    /// 本地扫描或网络音乐库同步完成后统一刷新：清空各列表页缓存、刷新协议、
    /// 总览与当前 tab（本地加载本地、网络加载网络），确保音乐库视图即时同步。
    /// </summary>
    private void RefreshLibraryAfterDataChangedAsync()
    {
        // 扫描/同步后歌曲/专辑/艺术家列表可能变化：清空各列表页缓存，下次进入重新拉取最新列表
        AllSongsViewModel.InvalidateCache();
        AlbumsViewModel.InvalidateCache();
        ArtistsViewModel.InvalidateCache();
        // 立即清空 ExploreDataService 的内存聚合缓存，确保扫描/同步后进入列表页拿到最新数据
        _exploreDataService?.InvalidateDailyRecommendCache();

        // 所有重型操作并行跑在后台线程，避免阻塞 UI
        _ = Task.Run(async () =>
        {
            try
            {
                var tasks = new List<Task>();

                if (_vm.CurrentTab == "Local")
                    tasks.Add(_vm.LoadLocalAsync());
                else if (_vm.CurrentTab == "Network")
                    tasks.Add(_vm.LoadNetworkAsync());

                tasks.Add(_vm.RefreshProtocolsAsync());
                tasks.Add(_vm.LoadOverviewDataAsync());

                if (_searchVm != null)
                    tasks.Add(_searchVm.ReloadAfterScanAsync());

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                Log.Debug("LibraryPage.xaml", $"[LibraryPage] 数据变更后刷新失败: {ex.Message}");
            }
        });
    }

    private void OnDiscoverSourceChanged()
    {
        _exploreDataService?.InvalidateDailyRecommendCache();
        if (_searchVm != null)
        {
            _ = MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    await _searchVm.ReloadAfterScanAsync();
                }
                catch (Exception ex)
                {
                    Log.Debug("LibraryPage.xaml", $"[LibraryPage] 发现页数据源切换后重新加载失败: {ex.Message}");
                }
            });
        }
    }

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        base.OnHandlerChanging(args);

        if (args.NewHandler == null)
        {
            // 页面分离：取消订阅
            _vm.DiscoverSourceChanged -= OnDiscoverSourceChanged;
            Services.LocalScanService.ScanCompleted -= OnScanCompleted;
            Services.LocalScanService.NetworkSyncCompleted -= OnNetworkSyncCompleted;
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }
        else
        {
            // 页面挂载（或重新挂载）：订阅事件（先 -= 再 += 避免重复）
            _vm.DiscoverSourceChanged -= OnDiscoverSourceChanged;
            _vm.DiscoverSourceChanged += OnDiscoverSourceChanged;
            Services.LocalScanService.ScanCompleted -= OnScanCompleted;
            Services.LocalScanService.ScanCompleted += OnScanCompleted;
            Services.LocalScanService.NetworkSyncCompleted -= OnNetworkSyncCompleted;
            Services.LocalScanService.NetworkSyncCompleted += OnNetworkSyncCompleted;
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // 后台预热专辑/艺术家聚合缓存：让用户点击"专辑"/"艺术家"前，重聚合已完成，
        // 进入列表页时直接命中缓存 → 进入即显示内容（与"全部歌曲"一致的秒开体验）。
        WarmExploreCaches();

        if (_isFirstAppearing)
        {
            _isFirstAppearing = false;
            // 三个无依赖的 IO 操作并行执行，缩短首屏等待
            // （同文件 OnScanCompleted 已用 Task.WhenAll 并行这组操作，证明互不依赖）
            await Task.WhenAll(
                _vm.RefreshProtocolsAsync(),
                LoadInitialDataAsync(),
                _vm.LoadOverviewDataAsync()
            );
        }
        else if (Services.LocalScanService.NeedsReload || Services.LocalScanService.NetworkNeedsReload)
        {
            try
            {
                if (_vm.CurrentTab == "Local")
                {
                    await _vm.LoadLocalAsync();
                }
                else if (_vm.CurrentTab == "Network")
                {
                    await _vm.LoadNetworkAsync();
                }
                await _vm.RefreshProtocolsAsync();
                await _vm.LoadOverviewDataAsync();
            }
            catch (Exception ex)
            {
                Log.Debug("LibraryPage.xaml", $"[LibraryPage] 扫描/同步后刷新音乐库失败: {ex.Message}");
            }
            finally
            {
                // 消费标记：网络同步标记始终重置，避免反复刷新；本地标记沿用既有行为
                Services.LocalScanService.NetworkNeedsReload = false;
            }
        }

        // 兜底重渲染：确保三类数据容器始终反映 VM 最新状态。
        // 离屏预加载阶段（NativeTabPager 常驻全部 tab 页）可能错过 LibraryCards 等的
        // PropertyChanged 通知，导致切换回本页时内容空白；此处显式重绘以消除该时序隐患。
        RenderLibraryCards();
        RenderFormatBars();
        RenderRecentAdd();
        RenderDataInsight();
    }

    private async Task LoadInitialDataAsync()
    {
        try
        {
            if (_vm.CurrentTab == "Local")
                await _vm.LoadLocalAsync();
            else
                await _vm.LoadNetworkAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("错误", $"加载失败: {ex.Message}", "确定");
        }
    }

    /// <summary>
    /// 后台预热专辑/艺术家聚合缓存：重聚合（按歌曲分组统计）耗时较长，提前在 hub 页
    /// 进行时执行，用户点击"专辑"/"艺术家"时 GetAllAlbumsAsync/GetAllArtistsAsync 直接
    /// 命中内存缓存返回，列表页进入即显示内容（避免进入后才慢慢聚合的卡顿感）。
    /// 此外顺带预热两个列表 VM 的静态缓存（分组/筛选/字母索引集合），使首次进入列表页时
    /// 命中 VM 的 instant 路径 → 主线程零重建，与"全部歌曲"一致的秒开体验。
    /// 缓存已热时调用几乎零成本，可安全在每次 OnAppearing 调用。
    /// </summary>
    private void WarmExploreCaches()
    {
        if (_exploreDataService == null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await _exploreDataService.GetAllAlbumsAsync();
                await _exploreDataService.GetAllArtistsAsync();

                // 预热 VM 静态缓存：让首次进入列表页时直接复用已构建好的集合，
                // 主线程不再做 BuildLetterRail/ApplyFiltersAndSort 等重活。
                var albumsVm = _sp.GetService<AlbumsViewModel>();
                var artistsVm = _sp.GetService<ArtistsViewModel>();
                if (albumsVm != null) await albumsVm.LoadAsync();
                if (artistsVm != null) await artistsVm.LoadAsync();
            }
            catch { }
        });
    }

    private async Task PlaySongAsync(Song song)
    {
        try
        {
            _queue.SetSongs([.. _vm.FilteredSongs]);
            _queue.SelectSong(song.Id);

            if (_audioPlayer != null && !string.IsNullOrEmpty(song.FilePath))
            {
                await _audioPlayer.PlayAsync(song.FilePath);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("播放失败", ex.Message, "确定");
        }
    }

    private async void ShowDiscoverSourcePopup()
    {
        var primaryColor = (Color)Application.Current!.Resources["PrimaryColor"];
        var inactiveColor = (Color)Application.Current!.Resources["ChipInactiveColor"];
        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var textHint = (Color)Application.Current!.Resources["TextHintColor"];
        var cardBg = (Color)Application.Current!.Resources["CardBackgroundStrongColor"];

        var options = new[]
        {
            ("自动", "auto", "有本地和网络优先显示本地；仅网络时显示网络"),
            ("本地", "local", "仅显示本地音乐库内容"),
            ("网络", "network", "仅显示网络音乐源内容")
        };

        var currentSource = _vm.DiscoverSource ?? "auto";
        // 兼容旧偏好：旧版本可能存了 "all"，回退到 "auto"
        if (currentSource == "all") currentSource = "auto";

        DiscoverSourcePopup.ClearContent();

        foreach (var (label, value, desc) in options)
        {
            var isSelected = value == currentSource;

            var optionBorder = new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
                Stroke = isSelected ? primaryColor : inactiveColor,
                StrokeThickness = isSelected ? 1.5 : 1,
                BackgroundColor = isSelected ? Color.FromArgb("#1A") : cardBg,
                Padding = new Thickness(14, 10),
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalOptions = LayoutOptions.Fill
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new() { Width = new GridLength(1, GridUnitType.Star) },
                    new() { Width = GridLength.Auto }
                },
                ColumnSpacing = 8
            };

            var textStack = new VerticalStackLayout { Spacing = 2 };
            textStack.Add(new Label
            {
                Text = label,
                FontSize = 15,
                FontAttributes = isSelected ? FontAttributes.Bold : FontAttributes.None,
                TextColor = isSelected ? primaryColor : textPrimary
            });
            textStack.Add(new Label
            {
                Text = desc,
                FontSize = 11,
                TextColor = textHint,
                MaxLines = 1,
                LineBreakMode = LineBreakMode.TailTruncation
            });
            grid.Add(textStack, 0);

            if (isSelected)
            {
                grid.Add(new Label
                {
                    Text = "\u2713",
                    FontSize = 16,
                    TextColor = primaryColor,
                    FontAttributes = FontAttributes.Bold,
                    VerticalOptions = LayoutOptions.Center
                }, 1);
            }

            optionBorder.Content = grid;

            var capturedValue = value;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                _vm.SetDiscoverSource(capturedValue);
                _ = DiscoverSourcePopup.CloseAsync();
            };
            optionBorder.GestureRecognizers.Add(tap);

            DiscoverSourcePopup.AddContent(optionBorder);
        }

        DiscoverSourcePopup.Open();
    }

    /// <summary>点击音乐库 Hero 卡片右上角设置按钮，打开发现页数据源选择弹窗。</summary>
    private void OnSettingsTapped(object? sender, EventArgs e)
    {
        ShowDiscoverSourcePopup();
    }

    private async void OnAlbumsClicked(object? sender, EventArgs e)
    {
        OpenLibrarySubPage(typeof(AlbumsPage), "library/albums");
    }

    // === 数据洞察：环形图渲染与导航 ===

    private void RenderDataInsight()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_vm.PieDatasets == null || _vm.PieDatasets.Count == 0) return;

            // 数据集数量变化（或首次）时重建圆点导航
            if (_pieDatasets == null || _pieDatasets.Count != _vm.PieDatasets.Count)
            {
                _pieDatasets = _vm.PieDatasets.ToList();
                PieDots.Children.Clear();
                _pieDotViews.Clear();
                for (int i = 0; i < _pieDatasets.Count; i++)
                {
                    var dot = new Border
                    {
                        WidthRequest = 7,
                        HeightRequest = 7,
                        StrokeShape = new RoundRectangle { CornerRadius = 99 },
                        StrokeThickness = 0,
                        BackgroundColor = Color.FromArgb("#8D93B7")
                    };
                    var idx = i;
                    var tap = new TapGestureRecognizer();
                    tap.Tapped += (s, e) => GoToPie(idx);
                    dot.GestureRecognizers.Add(tap);
                    PieDots.Children.Add(dot);
                    _pieDotViews.Add(dot);
                }
                _pieIndex = 0;
            }

            UpdatePieSelection();
        });
    }

    private void UpdatePieSelection()
    {
        if (_pieDatasets == null || _pieDatasets.Count == 0) return;
        var ds = _pieDatasets[_pieIndex];

        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var textSecondary = (Color)Application.Current!.Resources["TextSecondaryColor"];

        PieDonut.Dataset = ds;

        // 横屏紧凑模式下压缩图例尺寸，避免卡片高度减半后内容溢出
        double legendFontSize = _dataInsightCompact ? 10.5 : 12.5;
        double legendRowPadding = _dataInsightCompact ? 3 : 5.5;
        double legendBoxSize = _dataInsightCompact ? 8 : 10;
        double legendSpacing = _dataInsightCompact ? 6 : 9;

        PieLegend.Children.Clear();
        foreach (var seg in ds.Segments)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new() { Width = GridLength.Auto },
                    new() { Width = GridLength.Auto },
                    new() { Width = GridLength.Auto }
                },
                ColumnSpacing = legendSpacing,
                Padding = new Thickness(0, legendRowPadding, 0, legendRowPadding)
            };

            row.Add(new BoxView
            {
                WidthRequest = legendBoxSize,
                HeightRequest = legendBoxSize,
                CornerRadius = _dataInsightCompact ? 2 : 3,
                Color = seg.Color,
                VerticalOptions = LayoutOptions.Center
            }, 0);

            row.Add(new Label
            {
                Text = seg.Name,
                FontSize = legendFontSize,
                TextColor = textSecondary,
                VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.TailTruncation
            }, 1);

            var pct = ds.Total > 0 ? seg.Count * 100.0 / ds.Total : 0;
            row.Add(new Label
            {
                Text = $"{seg.Count}  {pct:F1}%",
                FontSize = legendFontSize,
                FontAttributes = FontAttributes.Bold,
                TextColor = textPrimary,
                VerticalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.End
            }, 2);

            PieLegend.Children.Add(row);
        }

        PieNameBadge.Text = ds.Name;
        PieCounter.Text = $"{_pieIndex + 1} / {_pieDatasets.Count}";

        for (int i = 0; i < _pieDotViews.Count; i++)
        {
            var active = i == _pieIndex;
            _pieDotViews[i].BackgroundColor = active ? Color.FromArgb("#8C7BFF") : Color.FromArgb("#8D93B7");
            _pieDotViews[i].WidthRequest = active ? 18 : 7;
        }

        PiePrev.Opacity = _pieIndex == 0 ? 0.3 : 1;
        PieNext.Opacity = _pieIndex == _pieDatasets.Count - 1 ? 0.3 : 1;
    }

    private void GoToPie(int index)
    {
        if (_pieDatasets == null || _pieDatasets.Count == 0) return;
        _pieIndex = Math.Max(0, Math.Min(_pieDatasets.Count - 1, index));
        UpdatePieSelection();
    }

    private void OnPiePrevTapped(object? sender, EventArgs e) => GoToPie(_pieIndex - 1);
    private void OnPieNextTapped(object? sender, EventArgs e) => GoToPie(_pieIndex + 1);

    // === 横屏桌面模式（DesktopLibraryPage）公共 API ===
    // 这些访问器和方法把"摘出卡片重组布局"和"压缩 Hero 内部样式"等操作从 DesktopLibraryPage
    // 移交回 LibraryPage 自身，避免反射和基于字符串匹配的硬编码视觉树操作。

    /// <summary>Hero 区域卡片（含统计数字）。</summary>
    public Border HeroCardView => HeroCard;
    /// <summary>资料库列表卡片。</summary>
    public Border LibraryListCardView => LibraryListCard;
    /// <summary>数据洞察卡片（环形图）。</summary>
    public Border DataInsightCardView => DataInsightCard;
    /// <summary>存储占用卡片。</summary>
    public Border StorageCardView => StorageCard;
    /// <summary>最近添加卡片。</summary>
    public Border RecentCardView => RecentCard;
    /// <summary>发现页来源弹窗。</summary>
    public Controls.AppPopup DiscoverSourcePopupView => DiscoverSourcePopup;

    /// <summary>根 ScrollView（横屏桌面页直接复用，避免双重滚动嵌套）。</summary>
    public ScrollView? RootScrollView => Content is Grid g && g.Children.Count > 0 && g.Children[0] is ScrollView sv ? sv : null;

    /// <summary>根 VerticalStackLayout（横屏桌面页在此重组卡片布局）。</summary>
    public VerticalStackLayout? RootStack => RootScrollView?.Content as VerticalStackLayout;

    /// <summary>触发 <see cref="OnAppearing"/> 生命周期：供横屏包装页在 OnAppearing 时调用。
    /// LibraryPage 实例化后并未真正加入可视化树（Content 被摘出），无法依赖系统触发的 OnAppearing。</summary>
    public void TriggerOnAppearing() => OnAppearing();

    /// <summary>应用紧凑 Hero 样式：手机横屏下激进压缩 padding/margin/字号，将 Hero 从 ~150dp 压到 ~70dp。
    /// 移除原本基于字符串匹配的视觉树硬编码，改为按结构层级递归处理（更稳健，标签文案变化不会失效）。
    /// 通过 Label.Tag 字段保存原始 FontSize，<see cref="ResetHeroStyle"/> 据此还原，避免重复调用导致样式错乱。</summary>
    public void ApplyCompactHeroStyle()
    {
        try
        {
            // Hero 内部结构：Grid > [刷新按钮, VerticalStackLayout(Padding=22,20)]
            if (HeroCard.Content is Grid heroGrid)
            {
                foreach (var child in heroGrid.Children)
                {
                    if (child is VerticalStackLayout vsl)
                    {
                        // 主容器 padding：22,20 → 12,6
                        vsl.Padding = new Thickness(12, 6);
                        vsl.Spacing = 0;

                        foreach (var sub in vsl.Children)
                        {
                            // 标题区 VerticalStackLayout（含主标题与副标题）
                            if (sub is VerticalStackLayout titleVsl)
                            {
                                foreach (var lbl in titleVsl.Children.OfType<Label>())
                                {
                                    CompactFontSize(lbl, 14, 10);
                                    lbl.Margin = new Thickness(0);
                                }
                            }
                            // 统计卡片网格（4 列）
                            else if (sub is Grid statGrid && statGrid.ColumnDefinitions.Count == 4)
                            {
                                statGrid.Margin = new Thickness(0, 6, 0, 0); // 18 → 6
                                statGrid.ColumnSpacing = 4; // 8 → 4

                                // 压缩每个统计卡片
                                foreach (var statCard in statGrid.Children.OfType<Border>())
                                {
                                    statCard.Padding = new Thickness(4, 4); // 12,10 → 4,4
                                    statCard.StrokeShape = new RoundRectangle { CornerRadius = 10 }; // 16 → 10

                                    if (statCard.Content is VerticalStackLayout cardVsl)
                                    {
                                        cardVsl.Spacing = 0;
                                        foreach (var lbl in cardVsl.Children.OfType<Label>())
                                        {
                                            // 数字（≥14）压到 13；标签压到 9
                                            CompactFontSize(lbl, lbl.FontSize >= 14 ? 13 : 9, lbl.FontSize >= 14 ? 13 : 9);
                                            if (lbl.FontSize < 14)
                                                lbl.Margin = new Thickness(0, 1, 0, 0);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { /* 内部结构变化时静默忽略，不影响主布局 */ }
    }

    /// <summary>还原紧凑 Hero 样式（切换回宽屏布局时调用）。
    /// 通过 Label.Tag 中保存的原始 FontSize 还原，幂等可重复调用。</summary>
    public void ResetHeroStyle()
    {
        try
        {
            if (HeroCard.Content is Grid heroGrid)
            {
                foreach (var child in heroGrid.Children)
                {
                    if (child is VerticalStackLayout vsl)
                    {
                        vsl.Padding = new Thickness(22, 20);
                        vsl.Spacing = 0;

                        foreach (var sub in vsl.Children)
                        {
                            if (sub is VerticalStackLayout titleVsl)
                            {
                                foreach (var lbl in titleVsl.Children.OfType<Label>())
                                {
                                    RestoreFontSize(lbl);
                                    lbl.Margin = new Thickness(0);
                                }
                            }
                            else if (sub is Grid statGrid && statGrid.ColumnDefinitions.Count == 4)
                            {
                                statGrid.Margin = new Thickness(0, 18, 0, 0);
                                statGrid.ColumnSpacing = 8;

                                foreach (var statCard in statGrid.Children.OfType<Border>())
                                {
                                    statCard.Padding = new Thickness(12, 10);
                                    statCard.StrokeShape = new RoundRectangle { CornerRadius = 16 };

                                    if (statCard.Content is VerticalStackLayout cardVsl)
                                    {
                                        cardVsl.Spacing = 0;
                                        foreach (var lbl in cardVsl.Children.OfType<Label>())
                                        {
                                            RestoreFontSize(lbl);
                                            lbl.Margin = new Thickness(0);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>保存 Hero 内部 Label 的原始 FontSize，供 <see cref="ResetHeroStyle"/> 还原。
    /// 使用 ConditionalWeakTable 避免 Label 被 GC 时残留条目。</summary>
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Label, System.Runtime.CompilerServices.StrongBox<double>> _heroOriginalFontSizes = new();

    // 数据洞察卡片在横屏桌面模式下需要减半高度，以下字段保存原始尺寸并标记紧凑状态
    private bool _dataInsightCompact;
    private double _originalDonutSize = -1;
    private double _originalPieButtonSize = -1;

    /// <summary>压缩字号：首次调用时把原始 FontSize 保存到表，后续调用直接覆盖。
    /// 注意 primarySize 与 secondarySize 在当前实现中相同（按调用方判断后传入）。</summary>
    private void CompactFontSize(Label lbl, double primarySize, double secondarySize)
    {
        if (!_heroOriginalFontSizes.TryGetValue(lbl, out _))
            _heroOriginalFontSizes.Add(lbl, new System.Runtime.CompilerServices.StrongBox<double>(lbl.FontSize));
        lbl.FontSize = primarySize;
    }

    /// <summary>还原字号：从表取回原始 FontSize。若未保存过（未压缩过）则保持当前值。</summary>
    private void RestoreFontSize(Label lbl)
    {
        if (_heroOriginalFontSizes.TryGetValue(lbl, out var box))
        {
            lbl.FontSize = box.Value;
            _heroOriginalFontSizes.Remove(lbl);
        }
    }

    /// <summary>压缩资料库列表项间距（手机横屏紧凑模式）。</summary>
    public void ApplyCompactLibraryListStyle()
    {
        try
        {
            if (LibraryListCard.Content is VerticalStackLayout vsl)
            {
                foreach (var child in vsl.Children.OfType<VerticalStackLayout>())
                {
                    child.Spacing = 6; // 原始 12
                }
            }
        }
        catch { }
    }

    /// <summary>横屏桌面模式下压缩数据洞察卡片内部尺寸，使卡片高度接近减半。</summary>
    public void ApplyCompactDataInsightStyle()
    {
        try
        {
            if (_originalDonutSize < 0)
                _originalDonutSize = PieDonut.WidthRequest;
            if (_originalPieButtonSize < 0)
                _originalPieButtonSize = PiePrev.WidthRequest;

            _dataInsightCompact = true;

            // 圆环图与分页按钮尺寸减半
            PieDonut.WidthRequest = 70;
            PieDonut.HeightRequest = 70;
            PiePrev.WidthRequest = 20;
            PiePrev.HeightRequest = 20;
            PieNext.WidthRequest = 20;
            PieNext.HeightRequest = 20;

            // 标题区 / 分页区 margin 压缩
            if (DataInsightCard.Content is Grid grid)
            {
                if (grid.Children.Count > 0 && grid.Children[0] is Grid headerGrid)
                    headerGrid.Margin = new Thickness(0, 0, 0, 4);
                if (grid.Children.Count > 2 && grid.Children[2] is Grid footerGrid)
                    footerGrid.Margin = new Thickness(0, 4, 0, 0);
            }

            // 重新渲染图例以应用压缩字号/间距
            UpdatePieSelection();
        }
        catch { /* 内部结构变化时静默忽略 */ }
    }

    /// <summary>还原数据洞察卡片到原始尺寸（切回竖屏或宽屏宽松布局时）。</summary>
    public void ResetDataInsightStyle()
    {
        try
        {
            _dataInsightCompact = false;

            PieDonut.WidthRequest = _originalDonutSize > 0 ? _originalDonutSize : 138;
            PieDonut.HeightRequest = _originalDonutSize > 0 ? _originalDonutSize : 138;
            PiePrev.WidthRequest = _originalPieButtonSize > 0 ? _originalPieButtonSize : 26;
            PiePrev.HeightRequest = _originalPieButtonSize > 0 ? _originalPieButtonSize : 26;
            PieNext.WidthRequest = _originalPieButtonSize > 0 ? _originalPieButtonSize : 26;
            PieNext.HeightRequest = _originalPieButtonSize > 0 ? _originalPieButtonSize : 26;

            if (DataInsightCard.Content is Grid grid)
            {
                if (grid.Children.Count > 0 && grid.Children[0] is Grid headerGrid)
                    headerGrid.Margin = new Thickness(0, 0, 0, 8);
                if (grid.Children.Count > 2 && grid.Children[2] is Grid footerGrid)
                    footerGrid.Margin = new Thickness(0, 8, 0, 0);
            }

            UpdatePieSelection();
        }
        catch { }
    }

    /// <summary>
    /// 打开音乐库二级页：竖屏走 Shell 标准导航（路由已注册于 AppShell）；
    /// 横屏复用桌面布局，直接 Push 对应的 Desktop 页面实例。
    /// fallbackRoute 已编码查询参数（如 AllSongsPage.Source），由 [QueryProperty] 自动注入。
    /// </summary>
    private void OpenLibrarySubPage(Type pageType, string fallbackRoute, string? source = null)
    {
        if (App.IsLandscapeMode())
        {
            Type desktopType = pageType.Name switch
            {
                nameof(AllSongsPage) => typeof(DesktopAllSongsPage),
                nameof(ArtistsPage) => typeof(DesktopArtistsPage),
                nameof(AlbumsPage) => typeof(DesktopAlbumsPage),
                _ => pageType
            };

            var page = (ContentPage)_sp.GetRequiredService(desktopType);
            if (!string.IsNullOrEmpty(source) && page is DesktopAllSongsPage desktopAllSongs)
                desktopAllSongs.Source = source;

            // 嵌入 ContentArea 而非全屏 Push，保持侧边栏和播放栏可见
            DesktopMainPage.Instance?.OpenSubPageEmbedded(page);
            return;
        }

        _ = Shell.Current.GoToAsync(fallbackRoute);
    }
}
