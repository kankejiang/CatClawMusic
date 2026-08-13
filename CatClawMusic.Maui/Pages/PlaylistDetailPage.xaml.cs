using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// 歌单详情页面：展示指定歌单中的歌曲列表，支持搜索、排序、随机播放、A-Z 索引。
/// 样式与 <see cref="AllSongsPage"/> 保持一致。
/// </summary>
[QueryProperty(nameof(PlaylistId), "playlistId")]
[QueryProperty(nameof(PlaylistName), "name")]
public partial class PlaylistDetailPage : ContentPage, ISongContextMenuHost
{
    private readonly PlaylistDetailViewModel _viewModel;
    private int _playlistId;
    private string _playlistName = "";

    /// <summary>获取或设置歌单标识，作为导航查询参数传入，用于加载对应歌单数据。</summary>
    public int PlaylistId
    {
        get => _playlistId;
        set
        {
            _playlistId = value;
            _ = LoadPlaylistIfReady();
        }
    }

    /// <summary>获取或设置歌单名称，作为导航查询参数传入，用于加载对应歌单数据。</summary>
    public string PlaylistName
    {
        get => _playlistName;
        set
        {
            _playlistName = Uri.UnescapeDataString(value ?? "");
            _ = LoadPlaylistIfReady();
        }
    }

    /// <summary>初始化 <see cref="PlaylistDetailPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    public PlaylistDetailPage(PlaylistDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    private async Task LoadPlaylistIfReady()
    {
        if (_playlistId != 0 && !string.IsNullOrEmpty(_playlistName))
            await _viewModel.LoadPlaylistAsync(_playlistId, _playlistName);
    }

    // === 事件处理 ===

    /// <summary>
    /// 歌曲行加载完成（Windows）：为行挂载右键 PointerGestureRecognizer。
    /// 不在 XAML 中声明——Android 上行的直接手势识别器会让 MAUI 安装 OnTouchListener
    /// 消费 ACTION_DOWN，绕过 View.onTouchEvent，导致原生长按检测失效。
    /// </summary>
    private void OnRowLoaded(object? sender, EventArgs e)
    {
#if WINDOWS
        if (sender is not View row) return;
        if (row.GestureRecognizers.OfType<PointerGestureRecognizer>().Any()) return;

        var pointer = new PointerGestureRecognizer();
        pointer.PointerPressed += OnRowPointerPressed;
        row.GestureRecognizers.Add(pointer);
#endif
    }

    /// <summary>歌曲行右键（Windows）触发：仅响应右键，经识别器所在行取 Song 弹出上下文菜单。</summary>
    private void OnRowPointerPressed(object? sender, PointerEventArgs e)
    {
        if (e.Button != ButtonsMask.Secondary) return;
        if (sender is PointerGestureRecognizer gr && gr.Parent is View row && row.BindingContext is Song song)
            ShowSongMenu(song);
    }

    /// <summary>弹出歌曲上下文菜单（Android 长按由 SongContextMenuBehavior 调用，Windows 右键由 OnRowPointerPressed 调用）。</summary>
    public void ShowSongMenu(Song song)
    {
        SongContextMenu.Show(SongContextPopup, song, new SongMenuActions
        {
            Play = () => _viewModel.PlaySongCommand.ExecuteAsync(song),
            PlayNext = () => PlaySongNextAsync(song),
            ToggleFavorite = () => ToggleFavoriteAsync(song),
            SongInfo = () => OpenSongInfoAsync(song),
            GetPlaylists = () => _viewModel.GetPlaylistsAsync(),
            AddSongToPlaylist = playlistId => _viewModel.AddSongToPlaylistAsync(playlistId, song.Id)
        });
    }

    private async Task PlaySongNextAsync(Song song)
    {
        if (await _viewModel.PlaySongNextAsync(song))
            SongContextMenu.Toast("已加入播放队列");
    }

    private async Task ToggleFavoriteAsync(Song song)
    {
        var isFavorite = await _viewModel.ToggleFavoriteForSongAsync(song);
        SongContextMenu.Toast(isFavorite ? "已收藏" : "已取消收藏");
    }

    private async Task OpenSongInfoAsync(Song song)
    {
        if (DesktopNavigation.TryGoToShell($"songdetail?songId={song.Id}")) return;
        DesktopNavigation.OpenSongDetail(song.Id.ToString());
        await Task.CompletedTask;
    }

    private async void OnBackTapped(object? sender, EventArgs e)
    {
        if (PagerNavigator.TryPopOverlay()) return;

        // 横屏嵌入模式：返回到歌单 tab，保持侧边栏可见
        if (App.IsLandscapeMode() && DesktopMainPage.Instance != null)
        {
            DesktopMainPage.Instance.CloseEmbeddedSubPage("playlists");
            return;
        }

        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync();
        else
            DesktopNavigation.GoBack();
    }

    // ChipList.ChipTapped 事件签名：EventHandler<object?>
    private void OnSortChipTapped(object? sender, object? item)
    {
        if (item is SortOption option)
            _viewModel.ToggleSort(option.Key);
    }

    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Song song)
        {
            if (sender is CollectionView cv) cv.SelectedItem = null;
            await _viewModel.PlaySongCommand.ExecuteAsync(song);
        }
    }

    private void OnMoreTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not Song song) return;
        ShowSongMorePopup(song);
    }

    private void OnIndexTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is int index && index >= 0 && index < _viewModel.Songs.Count)
        {
            var targetSong = _viewModel.Songs[index];
            SongListView?.ScrollTo(targetSong, position: ScrollToPosition.MakeVisible);
        }
    }

    // === AppPopup 弹窗（MAUI 11 Android 端 DisplayActionSheet 兼容性问题） ===

    private void ShowSongMorePopup(Song song)
    {
        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var cardBg = (Color)Application.Current!.Resources["CardBackgroundStrongColor"];

        SongMorePopupControl.Title = song.Title;
        SongMorePopupControl.ClearContent();

        string[] actions = { "添加到播放队列", "添加到歌单", "查看歌曲详情", "分享" };
        foreach (var action in actions)
        {
            var captured = action;
            var row = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                Stroke = cardBg,
                StrokeThickness = 0,
                BackgroundColor = cardBg,
                Padding = new Thickness(14, 11),
                Margin = new Thickness(0, 0, 0, 6),
                HorizontalOptions = LayoutOptions.Fill
            };
            row.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () =>
                {
                    await SongMorePopupControl.CloseAsync();
                    await HandleSongAction(captured, song);
                })
            });
            row.Content = new Label
            {
                Text = action,
                FontSize = 14,
                TextColor = textPrimary,
                VerticalOptions = LayoutOptions.Center
            };
            SongMorePopupControl.AddContent(row);
        }

        SongContextMenu.OpenAtTop(SongMorePopupControl);
    }

    private async Task HandleSongAction(string? action, Song song)
    {
        switch (action)
        {
            case "查看歌曲详情":
                {
                    if (DesktopNavigation.TryGoToShell($"songdetail?songId={song.Id}")) break;
                    DesktopNavigation.OpenSongDetail(song.Id.ToString());
                    break;
                }
            case "添加到播放队列":
                // TODO: 添加到播放队列
                break;
            case "添加到歌单":
                // TODO: 添加到歌单
                break;
        }
    }
}
