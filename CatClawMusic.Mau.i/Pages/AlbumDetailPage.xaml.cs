using CatClawMusic.Core.Models;
using CatClawMusic.Mau.i.ViewModels;
using Microsoft.Mau.i.Controls;

namespace CatClawMusic.Mau.i.Pages;

public partial class AlbumDetailPage : ContentPage
{
    public AlbumDetailPage(AlbumDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        
        if (BindingContext is AlbumDetailViewModel vm && vm.Album.Id > 0)
        {
            await vm.LoadAlbumCommand.ExecuteAsync(vm.Album.Id);
        }
    }

    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Song song)
        {
            if (BindingContext is AlbumDetailViewModel vm)
            {
                vm.PlaySongCommand.Execute(song);
            }
        }
    }
}
