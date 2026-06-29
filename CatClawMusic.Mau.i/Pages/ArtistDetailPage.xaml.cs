using CatClawMusic.Core.Models;
using CatClawMusic.Mau.i.ViewModels;
using Microsoft.Mau.i.Controls;

namespace CatClawMusic.Mau.i.Pages;

public partial class ArtistDetailPage : ContentPage
{
    public ArtistDetailPage(ArtistDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        
        if (BindingContext is ArtistDetailViewModel vm && vm.Artist.Id > 0)
        {
            await vm.LoadArtistCommand.ExecuteAsync(vm.Artist.Id);
        }
    }

    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Song song)
        {
            if (BindingContext is ArtistDetailViewModel vm)
            {
                vm.PlaySongCommand.Execute(song);
            }
        }
    }
}
