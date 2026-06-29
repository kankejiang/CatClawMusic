using CatClawMusic.Core.Models;
using CatClawMusic.Mau.i.ViewModels;
using Microsoft.Mau.i.Controls;

namespace CatClawMusic.Mau.i.Pages;

public partial class PlaylistDetailPage : ContentPage
{
    public PlaylistDetailPage(PlaylistDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        
        if (BindingContext is PlaylistDetailViewModel vm && vm.Playlist.Id > 0)
        {
            await vm.LoadPlaylistCommand.ExecuteAsync(vm.Playlist.Id);
        }
    }

    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Song song)
        {
            if (BindingContext is PlaylistDetailViewModel vm)
            {
                vm.PlaySongCommand.Execute(song);
            }
        }
    }

    private async void OnRemoveSongClicked(object? sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is Song song)
        {
            if (BindingContext is PlaylistDetailViewModel vm)
            {
                await vm.RemoveSongCommand.ExecuteAsync(song);
            }
        }
    }
}
