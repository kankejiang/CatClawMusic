using CatClawMusic.UI.ViewModels;

namespace CatClawMusic.UI.Pages;

public partial class PlaylistPage : ContentPage
{
    private readonly PlaylistViewModel _viewModel;

    public PlaylistPage()
        : this(new PlaylistViewModel())
    {
    }

    public PlaylistPage(PlaylistViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        PlaylistCollection.ItemsSource = viewModel.Playlists;
    }

    private async void OnPlaylistSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Core.Models.Playlist selected)
        {
            await Shell.Current.GoToAsync($"NowPlayingPage?playlistId={selected.Id}");
        }
    }
}
