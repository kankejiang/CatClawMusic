using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.UI.ViewModels;

namespace CatClawMusic.UI.Pages;

public partial class LibraryPage : ContentPage
{
    private IAudioPlayerService _audioPlayer = null!;
    private PlayQueue _playQueue = null!;
    private LibraryViewModel _viewModel = null!;

    public LibraryPage()
    {
        InitializeComponent();
        ResolveServices();
        BindingContext = _viewModel;
    }

    public LibraryPage(LibraryViewModel viewModel, IAudioPlayerService audioPlayer, PlayQueue playQueue)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _audioPlayer = audioPlayer;
        _playQueue = playQueue;
        BindingContext = viewModel;
    }

    private void ResolveServices()
    {
        var services = Application.Current?.Handler?.MauiContext?.Services;
        if (services != null)
        {
            _viewModel = services.GetRequiredService<LibraryViewModel>();
            _audioPlayer = services.GetRequiredService<IAudioPlayerService>();
            _playQueue = services.GetRequiredService<PlayQueue>();
        }
    }
    
    private void OnSongSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Song selectedSong)
        {
            // 设置播放队列并开始播放
            _playQueue.SetSongs(_viewModel.Songs);
            _playQueue.SelectSong(selectedSong.Id);
            _ = _audioPlayer.PlayAsync(selectedSong.FilePath);
            
            // 导航到播放页面
            Shell.Current.GoToAsync("NowPlayingPage");
        }
    }
}
