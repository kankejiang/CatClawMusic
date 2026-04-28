using CatClawMusic.Core.Models;
using CatClawMusic.UI.ViewModels;

namespace CatClawMusic.UI.Pages;

[QueryProperty(nameof(PlaylistId), "playlistId")]
[QueryProperty(nameof(PlaylistName), "playlistName")]
public partial class PlaylistDetailPage : ContentPage
{
    private PlaylistDetailViewModel _vm = null!;

    public int PlaylistId { get; set; }
    public string PlaylistName { get; set; } = "";

    public PlaylistDetailPage()
    {
        InitializeComponent();
        var services = Application.Current?.Handler?.MauiContext?.Services;
        if (services != null)
        {
            _vm = services.GetRequiredService<PlaylistDetailViewModel>();
            BindingContext = _vm;
        }
    }

    public PlaylistDetailPage(PlaylistDetailViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (PlaylistId > 0)
            await _vm.LoadAsync(PlaylistId, PlaylistName);
    }

    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Song song)
            await _vm.PlaySongAsync(song);
    }
}
