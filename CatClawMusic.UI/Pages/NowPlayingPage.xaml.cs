using CatClawMusic.UI.ViewModels;

namespace CatClawMusic.UI.Pages;

public partial class NowPlayingPage : ContentPage
{
    public NowPlayingPage(NowPlayingViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
