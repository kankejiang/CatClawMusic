namespace CatClawMusic.Maui.Pages;

public partial class PlaylistDetailPage : ContentPage
{
    public PlaylistDetailPage()
    {
        InitializeComponent();
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
