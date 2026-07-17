using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
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
    private bool _isFirstAppearing = true;

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
                _vm.SwitchTab("Local");
                break;
            case "network":
                if (_vm.HasNetworkProtocols)
                    _vm.SwitchTab("Network");
                break;
            case "favorite":
                _ = Shell.Current.GoToAsync("library/favorites");
                break;
            case "recent":
                _ = Shell.Current.GoToAsync("library/recent");
                break;
            case "trash":
                break;
        }
    }

    private void OnScanCompleted(object? sender, int importedCount)
    {
        _ = MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                if (_vm.CurrentTab == "Local")
                {
                    await _vm.LoadLocalAsync();
                }
                await _vm.RefreshProtocolsAsync();
                await _vm.LoadOverviewDataAsync();
                if (_searchVm != null)
                {
                    await _searchVm.ReloadAfterScanAsync();
                }
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
        await Shell.Current.GoToAsync("musicfoldersettings");
    }

    private async void OnEditExcludeTapped(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("musicfoldersettings");
    }

    private async void OnAlbumsClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("library/albums");
    }
}
