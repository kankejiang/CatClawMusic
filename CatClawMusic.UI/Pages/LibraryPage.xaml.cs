using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.UI.ViewModels;

namespace CatClawMusic.UI.Pages;

public partial class LibraryPage : ContentPage
{
    private readonly IAudioPlayerService _audioPlayer;
    private readonly PlayQueue _playQueue;
    private readonly LibraryViewModel _viewModel;

    public LibraryPage(LibraryViewModel viewModel, IAudioPlayerService audioPlayer, PlayQueue playQueue)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _audioPlayer = audioPlayer;
        _playQueue = playQueue;
        BindingContext = viewModel;
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
