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

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // 每次显示页面时尝试加载本地音乐（含权限检查）
        await _viewModel.LoadLocalAsync();
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

    private async void OnSongSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Song selectedSong)
        {
            _playQueue.SetSongs(_viewModel.Songs);
            _playQueue.SelectSong(selectedSong.Id);

            if (!string.IsNullOrEmpty(selectedSong.FilePath))
                await _audioPlayer.PlayAsync(selectedSong.FilePath);

            await Shell.Current.GoToAsync("NowPlayingPage");
        }
    }
}
