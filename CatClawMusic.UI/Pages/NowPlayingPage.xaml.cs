using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
using CatClawMusic.UI.ViewModels;

namespace CatClawMusic.UI.Pages;

public partial class NowPlayingPage : ContentPage
{
    private NowPlayingViewModel _viewModel = null!;

    public NowPlayingPage()
    {
        InitializeComponent();
        var services = Application.Current?.Handler?.MauiContext?.Services;
        if (services != null)
        {
            _viewModel = services.GetRequiredService<NowPlayingViewModel>();
            BindingContext = _viewModel;
        }
    }

    public NowPlayingPage(NowPlayingViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    private void OnCoverSwiped(object? sender, SwipedEventArgs e)
    {
        var dir = e.Direction switch
        {
            Microsoft.Maui.SwipeDirection.Left => SwipeDirection.Left,
            Microsoft.Maui.SwipeDirection.Right => SwipeDirection.Right,
            _ => SwipeDirection.Left
        };
        _viewModel.SwipeCommand.Execute(dir);
    }
}
