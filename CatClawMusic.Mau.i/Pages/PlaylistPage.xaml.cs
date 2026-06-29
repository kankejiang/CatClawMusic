using CatClawMusic.Core.Models;
using CatClawMusic.Mau.i.ViewModels;
using Microsoft.Mau.i.Controls;

namespace CatClawMusic.Mau.i.Pages;

public partial class PlaylistPage : ContentPage
{
    public PlaylistPage(PlaylistViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        
        if (BindingContext is PlaylistViewModel vm)
        {
            await vm.LoadPlaylistsCommand.ExecuteAsync();
        }
    }

    private async void OnPlaylistSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Playlist playlist)
        {
            if (BindingContext is PlaylistViewModel vm)
            {
                vm.OnPlaylistSelected(playlist);
            }
        }
    }

    private async void OnCreatePlaylistClicked(object? sender, EventArgs e)
    {
        if (BindingContext is PlaylistViewModel vm)
        {
            vm.CreatePlaylistCommand.Execute(this);
        }
    }
}
