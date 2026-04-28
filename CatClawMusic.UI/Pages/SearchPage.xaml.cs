using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.UI.ViewModels;

namespace CatClawMusic.UI.Pages;

public partial class SearchPage : ContentPage
{
    private readonly SearchViewModel _viewModel;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly Core.Services.PlayQueue _playQueue;

    public SearchPage()
        : this(
            ResolveService<SearchViewModel>(),
            ResolveService<IAudioPlayerService>(),
            ResolveService<Core.Services.PlayQueue>())
    {
    }

    public SearchPage(SearchViewModel viewModel, IAudioPlayerService audioPlayer, Core.Services.PlayQueue playQueue)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _audioPlayer = audioPlayer;
        _playQueue = playQueue;
        BindingContext = viewModel;
        SearchResults.ItemsSource = viewModel.SearchResults;
    }

    private async void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        await _viewModel.SearchAsync(e.NewTextValue);
    }

    private async void OnSongSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Song selectedSong)
        {
            _playQueue.SetSongs(_viewModel.SearchResults);
            _playQueue.SelectSong(selectedSong.Id);
            await _audioPlayer.PlayAsync(selectedSong.FilePath);
            await Shell.Current.GoToAsync("NowPlayingPage");
        }
    }

    private static T ResolveService<T>() where T : notnull
    {
        var services = Application.Current?.Handler?.MauiContext?.Services;
        return services!.GetRequiredService<T>();
    }
}
