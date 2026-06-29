using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Pages;

public partial class SearchPage : ContentPage
{
    private readonly SearchViewModel _vm;
    private readonly MusicDatabase _db;
    private readonly PlayQueue _queue;

    public SearchPage(MusicDatabase db, PlayQueue queue, SearchViewModel vm)
    {
        InitializeComponent();
        _db = db;
        _queue = queue;
        _vm = vm;
        BindingContext = _vm;

        // Note: Event subscriptions removed - using commands and method calls instead
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        
        try
        {
            await _vm.LoadExploreDataAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SearchPage OnAppearing error: {ex.Message}");
        }
    }

    private void OnSearchCompleted(object? sender, EventArgs e)
    {
        var entry = sender as Entry;
        var query = entry?.Text?.Trim();
        
        if (!string.IsNullOrWhiteSpace(query))
        {
            _vm.SearchQuery = query;
            _ = _vm.LoadExploreDataAsync();
        }
    }

    private void OnPillClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string param)
        {
            if (int.TryParse(param, out int index))
            {
                _vm.SwitchTabCommand.Execute(index);
                UpdatePillStyles(index);
            }
        }
    }

    private void UpdatePillStyles(int activeIndex)
    {
        var pills = new[] { PillDaily, PillArtists, PillAlbums, PillTopPlayed, PillRecent };
        
        for (int i = 0; i < pills.Length; i++)
        {
            if (i == activeIndex)
            {
                pills[i].BackgroundColor = Color.FromArgb("#9B7ED8");
                pills[i].TextColor = Colors.White;
            }
            else
            {
                pills[i].BackgroundColor = Color.FromRgba(0x1E, 0x78, 0x78, 0x80);
                pills[i].TextColor = Color.FromArgb("#6B5E7A");
            }
        }
    }

    private void OnYukiAvatarClicked(object? sender, EventArgs e)
    {
        _vm.EnterChatModeCommand.Execute(null);
    }

    private void OnChatBackClicked(object? sender, EventArgs e)
    {
        _vm.ExitChatModeCommand.Execute(null);
    }

    private void OnChatInputCompleted(object? sender, EventArgs e)
    {
        _ = _vm.SendMessageCommand.ExecuteAsync(null);
    }

    private void OnSendClicked(object? sender, EventArgs e)
    {
        _ = _vm.SendMessageCommand.ExecuteAsync(null);
    }

    private void OnChatModeChanged(object? sender, bool isChatMode)
    {
        ExploreMain.IsVisible = !isChatMode;
        ChatOverlay.IsVisible = isChatMode;
    }

    private async void OnSongPlayRequested(object? sender, Song song)
    {
        try
        {
            await Shell.Current.GoToAsync("//nowplaying");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Navigation error: {ex.Message}");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        
        // Note: Events removed - no need to unsubscribe
    }
}
