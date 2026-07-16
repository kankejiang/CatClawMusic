using CatClawMusic.Maui.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// 歌曲详情页面，按 song-detail-prototype.html 原型展示单首歌曲的完整信息：
/// 封面、歌曲标题/艺术家、可滚动歌词、基本信息、文件与音质信息。
/// </summary>
[QueryProperty(nameof(SongId), "songId")]
public partial class SongDetailPage : ContentPage
{
    private readonly SongDetailViewModel _viewModel;

    /// <summary>
    /// 初始化 <see cref="SongDetailPage"/> 类的新实例，并绑定对应的视图模型。
    /// </summary>
    /// <param name="viewModel">歌曲详情视图模型，由 DI 注入。</param>
    public SongDetailPage(SongDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>获取或设置歌曲 ID（来自导航查询参数），设置时触发数据加载。</summary>
    public string SongId
    {
        set
        {
            var id = int.TryParse(Uri.UnescapeDataString(value ?? string.Empty), out var v) ? v : 0;
            if (id > 0)
                _ = _viewModel.LoadAsync(id);
        }
    }

    /// <summary>ViewModel 属性变化时同步处理封面占位符</summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SongDetailViewModel.HasCover) ||
            e.PropertyName == nameof(SongDetailViewModel.Song))
        {
            UpdateCoverPlaceholder();
        }
    }

    /// <summary>当无封面时，将标题首字作为占位符显示</summary>
    private void UpdateCoverPlaceholder()
    {
        var title = _viewModel.Song?.Title;
        CoverPlaceholderLabel.Text = string.IsNullOrEmpty(title) ? "♪" : title[..1];
    }

    /// <summary>点击返回按钮：返回到上一级页面</summary>
    private async void OnBackClicked(object? sender, EventArgs e)
    {
        try
        {
            if (Shell.Current.Navigation.NavigationStack.Count > 1)
                await Shell.Current.Navigation.PopAsync();
            else
                await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SongDetailPage] Back failed: {ex.Message}");
        }
    }

    /// <summary>点击更多按钮：弹出操作菜单（播放/复制路径）</summary>
    private async void OnMoreClicked(object? sender, EventArgs e)
    {
        try
        {
            var song = _viewModel.Song;
            if (song == null || song.Id <= 0) return;

            var action = await DisplayActionSheet(song.Title, "取消", null,
                "播放此曲", "复制文件路径");
            switch (action)
            {
                case "播放此曲":
                    await PlaySongAsync(song);
                    break;
                case "复制文件路径":
                    await Clipboard.Default.SetTextAsync(song.FilePath ?? "");
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SongDetailPage] More failed: {ex.Message}");
        }
    }

    /// <summary>播放当前歌曲：通过 DI 拿到的 PlayQueue 与 IAudioPlayerService</summary>
    private async Task PlaySongAsync(Core.Models.Song song)
    {
        try
        {
            var queue = Handler?.MauiContext?.Services.GetService<PlayQueue>();
            var audio = Handler?.MauiContext?.Services.GetService<Core.Interfaces.IAudioPlayerService>();
            if (queue == null || audio == null) return;
            if (!string.IsNullOrEmpty(song.FilePath))
            {
                queue.SetSongs(new[] { song });
                queue.SelectSong(song.Id);
                await audio.PlayAsync(song.FilePath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SongDetailPage] Play failed: {ex.Message}");
        }
    }
}
