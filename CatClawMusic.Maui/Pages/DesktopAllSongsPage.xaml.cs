using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Maui.Pages;

/// <summary>横屏桌面模式全部歌曲页：双列网格歌曲列表，复用 AllSongsViewModel。</summary>
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

    private async void OnBackTapped(object? sender, EventArgs e)
    {
        if (PagerNavigator.TryPopOverlay())
            return;
        // 横屏嵌入模式：返回到音乐库 tab
        if (App.IsLandscapeMode() && DesktopMainPage.Instance != null)
        {
            DesktopMainPage.Instance.CloseEmbeddedSubPage("library");
            return;
        }
        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync();
        else
            await Shell.Current.GoToAsync("..");
    }

    private void OnSortChipTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string key)
        {
            _vm.ToggleSort(key);
        }
    }

    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Song song)
        {
            ((CollectionView)sender!).SelectedItem = null;
            await _vm.PlaySongCommand.ExecuteAsync(song);
        }
    }

    private void OnMoreTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not Song song) return;

        var primaryColor = (Color)Application.Current!.Resources["PrimaryColor"];
        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var textSecondary = (Color)Application.Current!.Resources["TextSecondaryColor"];
        var cardBg = (Color)Application.Current!.Resources["CardBackgroundStrongColor"];

        SongMorePopup.Title = song.Title;
        SongMorePopup.ClearContent();

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
                    SongMorePopup.Close();
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
            SongMorePopup.AddContent(row);
        }

        SongMorePopup.Open();
    }

    private async Task HandleSongAction(string action, Song song)
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

    private void OnIndexTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is int index && index >= 0 && index < _vm.Songs.Count)
        {
            var targetSong = _vm.Songs[index];
            SongListView?.ScrollTo(targetSong, position: ScrollToPosition.MakeVisible);
        }
    }
}
