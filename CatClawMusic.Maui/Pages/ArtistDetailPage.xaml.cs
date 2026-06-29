namespace CatClawMusic.Maui.Pages;

public partial class ArtistDetailPage : ContentPage
{
    public ArtistDetailPage()
    {
        InitializeComponent();
    }

    private void OnAlbumSelected(object? sender, SelectionChangedEventArgs e)
    {
        // Handle album selection
        if (e.CurrentSelection.FirstOrDefault() is CatClawMusic.Core.Models.Album album)
        {
            // Navigate to album detail page
        }
    }

    private void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        // Handle song selection
        if (e.CurrentSelection.FirstOrDefault() is CatClawMusic.Core.Models.Song song)
        {
            // Play the song
        }
    }
}
