using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Maui.Pages;

/// <summary>横屏桌面模式歌单页：独立 XAML 布局，双列网格歌单列表，
/// 复用 PlaylistViewModel 和与 PlaylistPage 相同的事件处理逻辑。</summary>
public partial class DesktopPlaylistView : ContentPage
{
    private readonly PlaylistViewModel _viewModel;
    private readonly IServiceProvider _sp;
    private bool _isFirstAppearing = true;
    private Entry? _playlistNameEntry;

    public DesktopPlaylistView(PlaylistViewModel viewModel, IServiceProvider sp)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _sp = sp;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_isFirstAppearing)
        {
            _isFirstAppearing = false;
            await _viewModel.LoadPlaylistsCommand.ExecuteAsync(null);
        }
        else if (_viewModel.Playlists.Count == 0 || _viewModel.IsDirty)
        {
            await _viewModel.RefreshIfChangedAsync();
        }
    }

    private async void OnPlaylistSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Playlist playlist)
        {
            if (sender is CollectionView collectionView)
            {
                collectionView.SelectedItem = null;
            }
            await Shell.Current.GoToAsync($"playlistdetail?playlistId={playlist.Id}&name={Uri.EscapeDataString(playlist.Name)}");
        }
    }

    /// <summary>点击歌单项的 ⋮ 按钮时触发，弹出操作菜单。</summary>
    private async void OnPlaylistMoreTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is Playlist playlist)
        {
            if (playlist.IsSystem) return;

            var action = await DisplayActionSheet(
                playlist.Name, "取消", null,
                "重命名歌单", "删除歌单");

            if (action == "重命名歌单")
            {
                var newName = await DisplayPromptAsync("重命名歌单", "请输入新的歌单名称", "确定", "取消", initialValue: playlist.Name, maxLength: 30);
                if (string.IsNullOrWhiteSpace(newName) || newName.Trim() == playlist.Name) return;

                await _viewModel.RenamePlaylistAsync(playlist.Id, newName.Trim());
                await _viewModel.LoadPlaylistsCommand.ExecuteAsync(null);
            }
            else if (action == "删除歌单")
            {
                var confirm = await DisplayAlert("确认删除", $"确定要删除歌单「{playlist.Name}」吗？\n歌曲不会被删除。", "删除", "取消");
                if (confirm)
                {
                    await _viewModel.DeletePlaylistAsync(playlist.Id);
                    await _viewModel.LoadPlaylistsCommand.ExecuteAsync(null);
                }
            }
        }
    }

    /// <summary>点击"我喜欢的"卡片，导航到全部歌曲（收藏筛选）。</summary>
    private void OnFavoriteCardTapped(object? sender, EventArgs e)
        => OpenLibrarySubPage(typeof(AllSongsPage), "library/allsongs?source=favorites", source: "favorites");

    /// <summary>点击"最近播放"卡片，导航到全部歌曲（最近播放筛选）。</summary>
    private void OnRecentCardTapped(object? sender, EventArgs e)
        => OpenLibrarySubPage(typeof(AllSongsPage), "library/allsongs?source=recent", source: "recent");

    private async void OnCreatePlaylistClicked(object? sender, TappedEventArgs e)
    {
        try
        {
            ShowCreatePlaylistPopup();
        }
        catch { }
    }

    private void ShowCreatePlaylistPopup()
    {
        var primaryColor = (Color)Application.Current!.Resources["PrimaryColor"];
        var inactiveColor = (Color)Application.Current!.Resources["ChipInactiveColor"];
        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var textSecondary = (Color)Application.Current!.Resources["TextSecondaryColor"];
        var textHint = (Color)Application.Current!.Resources["TextHintColor"];
        var cardBg = (Color)Application.Current!.Resources["CardBackgroundStrongColor"];

        CreatePlaylistPopup.ClearContent();

        var hintLabel = new Label
        {
            Text = "请输入歌单名称",
            FontSize = 13,
            TextColor = textHint,
            Margin = new Thickness(0, 0, 0, 10)
        };
        CreatePlaylistPopup.AddContent(hintLabel);

        _playlistNameEntry = new Entry
        {
            Placeholder = "歌单名称",
            FontSize = 15,
            MaxLength = 30,
            TextColor = textPrimary,
            PlaceholderColor = textHint,
            BackgroundColor = cardBg,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
            HorizontalOptions = LayoutOptions.Fill,
            HeightRequest = 44
        };
        var entryBorder = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            Stroke = inactiveColor,
            StrokeThickness = 1,
            BackgroundColor = cardBg,
            Padding = new Thickness(12, 0),
            HorizontalOptions = LayoutOptions.Fill,
            Content = _playlistNameEntry
        };
        CreatePlaylistPopup.AddContent(entryBorder);

        var btnRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = new GridLength(1, GridUnitType.Star) },
                new() { Width = new GridLength(1, GridUnitType.Star) }
            },
            ColumnSpacing = 12,
            Margin = new Thickness(0, 18, 0, 0)
        };

        var cancelBtn = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            BackgroundColor = inactiveColor,
            StrokeThickness = 0,
            HeightRequest = 44,
            HorizontalOptions = LayoutOptions.Fill,
            Content = new Label
            {
                Text = "取消",
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                TextColor = textSecondary,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
        var cancelTap = new TapGestureRecognizer();
        cancelTap.Tapped += (_, _) => { _ = CreatePlaylistPopup.CloseAsync(); };
        cancelBtn.GestureRecognizers.Add(cancelTap);
        btnRow.Add(cancelBtn, 0);

        var confirmBtn = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            BackgroundColor = primaryColor,
            StrokeThickness = 0,
            HeightRequest = 44,
            HorizontalOptions = LayoutOptions.Fill,
            Content = new Label
            {
                Text = "创建",
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
        var confirmTap = new TapGestureRecognizer();
        confirmTap.Tapped += async (_, _) => await OnCreatePlaylistConfirmedAsync();
        confirmBtn.GestureRecognizers.Add(confirmTap);
        btnRow.Add(confirmBtn, 1);

        CreatePlaylistPopup.AddContent(btnRow);

        _playlistNameEntry.Completed += async (_, _) => await OnCreatePlaylistConfirmedAsync();

        CreatePlaylistPopup.Open();

        _ = Task.Delay(300).ContinueWith(_ =>
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try { _playlistNameEntry?.Focus(); } catch { }
            }));
    }

    private async Task OnCreatePlaylistConfirmedAsync()
    {
        var name = _playlistNameEntry?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            try { _playlistNameEntry?.Focus(); } catch { }
            return;
        }

        await CreatePlaylistPopup.CloseAsync();

        try
        {
            var newId = await _viewModel.CreatePlaylistAsync(name);
            await _viewModel.LoadPlaylistsCommand.ExecuteAsync(null);

            if (newId > 0)
            {
                await Shell.Current.GoToAsync($"playlistdetail?playlistId={newId}&name={Uri.EscapeDataString(name)}");
            }
        }
        catch { }
    }

    /// <summary>
    /// 打开音乐库二级页：竖屏走 Shell 导航；横屏嵌入 ContentArea。
    /// </summary>
    private void OpenLibrarySubPage(Type pageType, string fallbackRoute, string? source = null)
    {
        if (App.IsLandscapeMode())
        {
            Type desktopType = pageType.Name switch
            {
                nameof(AllSongsPage) => typeof(DesktopAllSongsPage),
                _ => pageType
            };

            var page = (ContentPage)_sp.GetRequiredService(desktopType);
            if (!string.IsNullOrEmpty(source) && page is DesktopAllSongsPage desktopAllSongs)
                desktopAllSongs.Source = source;

            DesktopMainPage.Instance?.OpenSubPageEmbedded(page);
            return;
        }

        _ = Shell.Current.GoToAsync(fallbackRoute);
    }
}
