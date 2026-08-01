using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Maui.Pages;

/// <summary>横屏桌面模式全部歌曲页：双列网格歌曲列表，复用 AllSongsViewModel。
/// 公共 UI 组件见 <see cref="Controls"/> 命名空间；本类仅保留横屏专属的网格列数自适应与 AppPopup 弹窗。</summary>
[QueryProperty(nameof(Source), "source")]
public partial class DesktopAllSongsPage : ContentPage
{
    private readonly AllSongsViewModel _vm;

    public string Source { get; set; } = "local";

    public DesktopAllSongsPage(AllSongsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _vm.LoadAsync(Source);
    }

    // === 事件处理 ===

    private void OnBackTapped(object? sender, EventArgs e)
    {
        if (PagerNavigator.TryPopOverlay()) return;

        // 横屏嵌入模式：返回到音乐库 tab，保持侧边栏可见
        if (App.IsLandscapeMode() && DesktopMainPage.Instance != null)
        {
            DesktopMainPage.Instance.CloseEmbeddedSubPage("library");
            return;
        }

        _ = Shell.Current.GoToAsync("..");
    }

    // FlexLayout 排序项点击：从 sender 的 BindingContext 中获取 SortOption
    private void OnSortChipTapped(object? sender, EventArgs e)
    {
        if (sender is BindableObject bo && bo.BindingContext is SortOption option)
            _vm.ToggleSort(option.Key);
    }

    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Song song)
        {
            ((CollectionView)sender!).SelectedItem = null;
            await _vm.PlaySongCommand.ExecuteAsync(song);
        }
    }

    private async void OnGridSongTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is Song song)
            await _vm.PlaySongCommand.ExecuteAsync(song);
    }

    private void OnMoreTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not Song song) return;
        ShowSongMorePopup(song);
    }

    private void OnIndexTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is int index && index >= 0 && index < _vm.Songs.Count)
        {
            var targetSong = _vm.Songs[index];
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

        SongMorePopupControl.Open();
    }

    private async Task HandleSongAction(string? action, Song song)
    {
        switch (action)
        {
            case "查看歌曲详情":
                await Shell.Current.GoToAsync($"songdetail?songId={song.Id}");
                break;
            case "添加到播放队列":
                // TODO: 添加到播放队列
                break;
            case "添加到歌单":
                // TODO: 添加到歌单
                break;
        }
    }
}
