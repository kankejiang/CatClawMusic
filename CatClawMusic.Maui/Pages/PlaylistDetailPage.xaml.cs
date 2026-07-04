using CatClawMusic.Core.Models;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>歌单详情页面，展示指定歌单中的歌曲列表。</summary>
[QueryProperty(nameof(PlaylistId), "playlistId")]
[QueryProperty(nameof(PlaylistName), "name")]
public partial class PlaylistDetailPage : ContentPage
{
    private readonly PlaylistDetailViewModel _viewModel;
    private int _playlistId;
    private string _playlistName = "";

    /// <summary>获取或设置歌单标识，作为导航查询参数传入，用于加载对应歌单数据。</summary>
    public int PlaylistId
    {
        get => _playlistId;
        set
        {
            _playlistId = value;
            _ = LoadPlaylistIfReady();
        }
    }

    /// <summary>获取或设置歌单名称，作为导航查询参数传入，用于加载对应歌单数据。</summary>
    public string PlaylistName
    {
        get => _playlistName;
        set
        {
            _playlistName = Uri.UnescapeDataString(value ?? "");
            _ = LoadPlaylistIfReady();
        }
    }

    /// <summary>初始化 <see cref="PlaylistDetailPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="viewModel">歌单详情页面对应的视图模型。</param>
    public PlaylistDetailPage(PlaylistDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    private async Task LoadPlaylistIfReady()
    {
        if (_playlistId != 0 && !string.IsNullOrEmpty(_playlistName))
            await _viewModel.LoadPlaylistAsync(_playlistId, _playlistName);
    }

    /// <summary>在歌曲列表中选中某首歌曲时触发，播放所选歌曲。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">选择变更事件参数。</param>
    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Song song)
        {
            await _viewModel.PlaySongCommand.ExecuteAsync(song);
        }
    }
}
