namespace CatClawMusic.Maui.Pages;

public partial class PlaylistPage : ContentPage
{
    public PlaylistPage()
    {
        InitializeComponent();
    }

    private void OnPlaylistSelected(object? sender, SelectionChangedEventArgs e)
    {
        // Handle playlist selection
        if (e.CurrentSelection.FirstOrDefault() is CatClawMusic.Core.Models.Playlist playlist)
        {
            // Navigate to playlist detail page
        }
    }

    private void OnPlaylistMenuClicked(object? sender, EventArgs e)
    {
        // Show playlist menu
    }
}
