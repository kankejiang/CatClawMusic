using CatClawMusic.Core.Models;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

public partial class PlaylistPage : ContentPage
{
    private readonly PlaylistViewModel _viewModel;

    public PlaylistPage(PlaylistViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadPlaylistsCommand.ExecuteAsync(null);
    }

    private async void OnPlaylistSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Playlist playlist)
        {
            await Shell.Current.GoToAsync($"playlistdetail?playlistId={playlist.Id}&name={Uri.EscapeDataString(playlist.Name)}");
        }
    }

    private async void OnPlaylistMenuClicked(object? sender, EventArgs e)
    {
        if (sender is ImageButton button && button.BindingContext is Playlist playlist)
        {
            if (playlist.IsSystem)
                return;

            var action = await DisplayActionSheet(
                playlist.Name, "取消", null,
                "删除歌单");

            if (action == "删除歌单")
            {
                var confirm = await DisplayAlert("确认删除", $"确定要删除歌单「{playlist.Name}」吗？\n歌曲不会被删除。", "删除", "取消");
                if (confirm)
                {
                    await _viewModel.DeletePlaylistAsync(playlist.Id);
                    await _viewModel.LoadPlaylistsCommand.ExecuteAsync(null);
                }
            }
        }
    }

    private async void OnCreatePlaylistClicked(object? sender, EventArgs e)
    {
        var name = await DisplayPromptAsync("新建歌单", "请输入歌单名称", "创建", "取消", maxLength: 30);
        if (string.IsNullOrWhiteSpace(name)) return;

        await _viewModel.CreatePlaylistAsync(name.Trim());
        await _viewModel.LoadPlaylistsCommand.ExecuteAsync(null);
    }
}
