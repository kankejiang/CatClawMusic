using CatClawMusic.Core.Models;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

[QueryProperty(nameof(ArtistName), "artistName")]
public partial class ArtistDetailPage : ContentPage
{
    private readonly ArtistDetailViewModel _viewModel;

    public string ArtistName
    {
        set => _ = _viewModel.LoadArtistCommand.ExecuteAsync(value);
    }

    public ArtistDetailPage(ArtistDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    private async void OnAlbumSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Album album)
        {
            if (sender is CollectionView collectionView)
            {
                collectionView.SelectedItem = null;
            }

            await Shell.Current.GoToAsync($"albumdetail?title={Uri.EscapeDataString(album.Title ?? string.Empty)}");
        }
    }

    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Song song)
        {
            if (sender is CollectionView collectionView)
            {
                collectionView.SelectedItem = null;
            }
            await _viewModel.PlaySongCommand.ExecuteAsync(song);
        }
    }
}
