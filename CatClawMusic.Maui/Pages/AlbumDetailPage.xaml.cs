using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

[QueryProperty(nameof(AlbumTitle), "title")]
public partial class AlbumDetailPage : ContentPage
{
    private readonly AlbumDetailViewModel _viewModel;

    public string AlbumTitle
    {
        set => _ = _viewModel.LoadAsync(new CatClawMusic.Core.Models.Album { Title = Uri.UnescapeDataString(value ?? string.Empty) });
    }

    public AlbumDetailPage(AlbumDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    private async void OnBackClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");

    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        // handled by ViewModel command
    }
}
