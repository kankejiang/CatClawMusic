using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;

namespace CatClawMusic.Maui.Pages;

public partial class NowPlayingPage : ContentPage
{
    private readonly PlayQueue _queue;
    private readonly ILyricsService _lyrics;
    private readonly MusicDatabase _db;

    public NowPlayingPage(PlayQueue queue, ILyricsService lyrics, MusicDatabase db)
    {
        InitializeComponent();
        _queue = queue;
        _lyrics = lyrics;
        _db = db;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshUI();
    }

    private void RefreshUI()
    {
        var song = _queue.CurrentSong;
        if (song == null)
        {
            TitleLabel.Text = "未在播放";
            ArtistLabel.Text = "";
            LyricLabel.Text = "";
            return;
        }

        TitleLabel.Text = song.Title ?? "未知标题";
        ArtistLabel.Text = song.Artist ?? "未知艺术家";
    }

    private void OnPreviousClicked(object? sender, EventArgs e)
    {
        var song = _queue.Previous();
        if (song != null) RefreshUI();
    }

    private void OnPlayPauseClicked(object? sender, EventArgs e)
    {
        // Toggle play/pause — will be wired to IAudioPlayerService later
        PlayPauseButton.Text = PlayPauseButton.Text == "▶" ? "⏸" : "▶";
    }

    private void OnNextClicked(object? sender, EventArgs e)
    {
        var song = _queue.Next();
        if (song != null) RefreshUI();
    }

    private void OnShuffleClicked(object? sender, EventArgs e)
    {
        _queue.EnableShuffle();
    }

    private void OnRepeatClicked(object? sender, EventArgs e)
    {
        _queue.PlayMode = _queue.PlayMode == PlayMode.ListRepeat 
            ? PlayMode.SingleRepeat : PlayMode.ListRepeat;
    }
}
