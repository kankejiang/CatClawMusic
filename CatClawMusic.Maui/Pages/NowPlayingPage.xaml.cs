using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

public partial class NowPlayingPage : ContentPage
{
    private readonly NowPlayingViewModel _viewModel;

    public NowPlayingPage(NowPlayingViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.UpdateCurrentSong();
    }
}
