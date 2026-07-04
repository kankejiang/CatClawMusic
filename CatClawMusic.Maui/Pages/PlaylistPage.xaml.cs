using CatClawMusic.Core.Models;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>歌单列表页面，展示用户创建与系统的歌单集合。</summary>
public partial class PlaylistPage : ContentPage
{
    private readonly PlaylistViewModel _viewModel;
    private bool _isFirstAppearing = true;

    /// <summary>初始化 <see cref="PlaylistPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="viewModel">歌单列表页面对应的视图模型。</param>
    public PlaylistPage(PlaylistViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <summary>当页面显示在屏幕上时触发，首次出现时加载歌单列表数据。</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_isFirstAppearing)
        {
            _isFirstAppearing = false;
            if (_viewModel.Playlists.Count > 0) return;
        }

        await _viewModel.LoadPlaylistsCommand.ExecuteAsync(null);
    }

    /// <summary>在歌单列表中选中某个歌单时触发，导航到该歌单的详情页。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">选择变更事件参数。</param>
    private async void OnPlaylistSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Playlist playlist)
        {
            await Shell.Current.GoToAsync($"playlistdetail?playlistId={playlist.Id}&name={Uri.EscapeDataString(playlist.Name)}");
        }
    }

    /// <summary>点击歌单项的菜单按钮时触发，弹出操作菜单以执行删除歌单等操作。</summary>
    /// <param name="sender">事件源，通常为携带歌单上下文的图片按钮。</param>
    /// <param name="e">事件参数。</param>
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

    /// <summary>点击新建歌单按钮时触发，弹出输入对话框以创建新的歌单。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnCreatePlaylistClicked(object? sender, EventArgs e)
    {
        var name = await DisplayPromptAsync("新建歌单", "请输入歌单名称", "创建", "取消", maxLength: 30);
        if (string.IsNullOrWhiteSpace(name)) return;

        await _viewModel.CreatePlaylistAsync(name.Trim());
        await _viewModel.LoadPlaylistsCommand.ExecuteAsync(null);
    }
}
