using System;
using System.Threading;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.Maui.Controls;
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

        _vm.DiscoverSourceChanged += OnDiscoverSourceChanged;
        Services.LocalScanService.ScanCompleted += OnScanCompleted;

        _vm.PropertyChanged += OnViewModelPropertyChanged;
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
            Source = "ic_arrow_forward.svg",
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
                OpenLibrarySubPage(typeof(AllSongsPage), "library/allsongs?source=local", p => ((AllSongsPage)p).Source = "local");
                break;
            case "network":
                OpenLibrarySubPage(typeof(AllSongsPage), "library/allsongs?source=network", p => ((AllSongsPage)p).Source = "network");
                break;
            case "favorite":
                OpenLibrarySubPage(typeof(AllSongsPage), "library/allsongs?source=favorites", p => ((AllSongsPage)p).Source = "favorites");
                break;
            case "recent":
                OpenLibrarySubPage(typeof(AllSongsPage), "library/allsongs?source=recent", p => ((AllSongsPage)p).Source = "recent");
                break;
            case "trash":
                break;
        }
    }

    // === Hero 统计数字点击导航 ===

    private void OnStatSongsTapped(object? sender, EventArgs e)
        => OpenLibrarySubPage(typeof(AllSongsPage), "library/allsongs?source=local", p => ((AllSongsPage)p).Source = "local");

    private void OnStatArtistsTapped(object? sender, EventArgs e)
        => OpenLibrarySubPage(typeof(ArtistsPage), "library/artists");

    private void OnStatAlbumsTapped(object? sender, EventArgs e)
        => OpenLibrarySubPage(typeof(AlbumsPage), "library/albums");

    private void OnStatRecentTapped(object? sender, EventArgs e)
        => OpenLibrarySubPage(typeof(AllSongsPage), "library/allsongs?source=recent", p => ((AllSongsPage)p).Source = "recent");

    private void OnScanCompleted(object? sender, int importedCount)
    {
        // 扫描后歌曲/专辑/艺术家列表可能变化：清空各列表页缓存，下次进入重新拉取最新列表
        AllSongsViewModel.InvalidateCache();
        AlbumsViewModel.InvalidateCache();
        ArtistsViewModel.InvalidateCache();
        // 立即清空 ExploreDataService 的内存聚合缓存，确保扫描后进入列表页拿到最新数据
        _exploreDataService?.InvalidateDailyRecommendCache();

        // 所有重型操作并行跑在后台线程，避免阻塞 UI
        _ = Task.Run(async () =>
        {
            try
            {
                var tasks = new List<Task>();

                if (_vm.CurrentTab == "Local")
                    tasks.Add(_vm.LoadLocalAsync());

                tasks.Add(_vm.RefreshProtocolsAsync());
                tasks.Add(_vm.LoadOverviewDataAsync());

                if (_searchVm != null)
                    tasks.Add(_searchVm.ReloadAfterScanAsync());

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                Log.Debug("LibraryPage.xaml", $"[LibraryPage] 扫描完成后刷新失败: {ex.Message}");
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
        _vm.DiscoverSourceChanged -= OnDiscoverSourceChanged;
        Services.LocalScanService.ScanCompleted -= OnScanCompleted;
        _vm.PropertyChanged -= OnViewModelPropertyChanged;
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
            await _vm.RefreshProtocolsAsync();
            await LoadInitialDataAsync();
            await _vm.LoadOverviewDataAsync();
        }
        else if (Services.LocalScanService.NeedsReload)
        {
            try
            {
                if (_vm.CurrentTab == "Local")
                {
                    await _vm.LoadLocalAsync();
                }
                await _vm.RefreshProtocolsAsync();
                await _vm.LoadOverviewDataAsync();
            }
            catch (Exception ex)
            {
                Log.Debug("LibraryPage.xaml", $"[LibraryPage] 扫描后刷新本地音乐失败: {ex.Message}");
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
                // 主线程不再做 BuildLetterRail/BuildEraRail/ApplyFiltersAndSort 等重活。
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
        var textSecondary = (Color)Application.Current!.Resources["TextSecondaryColor"];
        var textHint = (Color)Application.Current!.Resources["TextHintColor"];
        var cardBg = (Color)Application.Current!.Resources["CardBackgroundStrongColor"];

        var options = new[]
        {
            ("自动", "auto", "本地和网络都有 → 本地；只有网络 → 网络"),
            ("本地", "local", "仅显示本地音乐库内容"),
            ("网络", "network", "仅显示网络音乐源内容"),
            ("混合", "all", "合并显示本地与网络内容")
        };

        var currentSource = _vm.DiscoverSource ?? "auto";

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
                DiscoverSourcePopup.Close();
            };
            optionBorder.GestureRecognizers.Add(tap);

            DiscoverSourcePopup.AddContent(optionBorder);
        }

        DiscoverSourcePopup.Open();
    }

    private async void OnScanTapped(object? sender, EventArgs e)
    {
        if (_scanService == null) return;
        if (_vm.IsScanning) return; // 防止并发扫描

        _refreshCts?.Cancel();
        _refreshCts = new CancellationTokenSource();
        var ct = _refreshCts.Token;

        try
        {
            MainThread.BeginInvokeOnMainThread(() => _vm.IsScanning = true);

            var useMediaStore = Preferences.Default.Get("use_media_store", false);
            var useSafScan = Preferences.Default.Get("use_saf_scan", false);

            var imported = await _scanService.ScanAsync(null, ct, useMediaStore, useSafScan);

            // ScanCompleted 事件仅在 imported>0 时触发并刷新；无新增（如仅清理已删文件）时
            // 这里手动刷新总览，确保音乐库数据始终最新。
            if (imported <= 0)
            {
                OnScanCompleted(null, imported);
            }
        }
        catch (OperationCanceledException)
        {
            // 用户取消，忽略
        }
        catch (Exception ex)
        {
            Log.Debug("LibraryPage.xaml", $"[LibraryPage] 手动刷新音乐库失败: {ex.Message}");
        }
        finally
        {
            MainThread.BeginInvokeOnMainThread(() => _vm.IsScanning = false);
        }
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

        PieLegend.Children.Clear();
        foreach (var seg in ds.Segments)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new() { Width = GridLength.Auto },
                    new() { Width = GridLength.Star },
                    new() { Width = GridLength.Auto }
                },
                ColumnSpacing = 9,
                Padding = new Thickness(0, 5.5, 0, 5.5)
            };

            row.Add(new BoxView
            {
                WidthRequest = 10,
                HeightRequest = 10,
                CornerRadius = 3,
                Color = seg.Color,
                VerticalOptions = LayoutOptions.Center
            }, 0);

            row.Add(new Label
            {
                Text = seg.Name,
                FontSize = 12.5,
                TextColor = textSecondary,
                VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.TailTruncation
            }, 1);

            var pct = ds.Total > 0 ? seg.Count * 100.0 / ds.Total : 0;
            row.Add(new Label
            {
                Text = $"{seg.Count}  {pct:F1}%",
                FontSize = 12.5,
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

    /// <summary>
    /// 打开音乐库二级页：统一走 Shell 标准导航（路由已注册于 AppShell）。
    /// 二级页的滚动与返回语义由 Shell 稳定托管，避免原生 ViewPager2 overlay 在部分
    /// 布局下遮挡底层 hub 或内部列表无法滚动的问题。fallbackRoute 已编码查询参数
    /// （如 AllSongsPage.Source），由 [QueryProperty] 自动注入。
    /// </summary>
    private void OpenLibrarySubPage(Type pageType, string fallbackRoute, Action<ContentPage>? configure = null)
        => _ = Shell.Current.GoToAsync(fallbackRoute);
}
