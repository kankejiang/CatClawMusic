using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

public partial class AlbumDetailViewModel : ObservableObject
{
    [ObservableProperty]
    private Album? _album;
    
    [ObservableProperty]
    private ObservableCollection<Song> _songs = new();
    
    [ObservableProperty]
    private bool _isLoading;

    public AlbumDetailViewModel()
    {
        // Parameterless constructor for DI
    }

    public async Task LoadAsync(Album? album = null, MusicDatabase? database = null)
    {
        try
        {
            IsLoading = true;
            
            // If album is passed as parameter, use it
            if (album != null)
            {
                Album = album;
            }
            
            if (Album == null)
            {
                return;
            }

            // Use provided database or get from DI
            var db = database ?? App.Current.Handler.MauiContext.Services.GetRequiredService<MusicDatabase>();
            
            // Load songs for this album
            var songs = await db.GetSongsByAlbumAsync(Album.Title);
            
            Songs.Clear();
            foreach (var song in songs)
            {
                Songs.Add(song);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading album details: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Get the first song (for playback)
    /// </summary>
    public Song? GetFirstSong()
    {
        return Songs.FirstOrDefault();
    }
}
