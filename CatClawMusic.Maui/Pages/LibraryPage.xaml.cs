using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.Pages;

public partial class LibraryPage : ContentPage
{
    private readonly MusicDatabase _db;
    private readonly PlayQueue _queue;
    private readonly LibraryViewModel _vm;
    private readonly IAudioPlayerService? _audioPlayer;

    public LibraryPage(MusicDatabase db, PlayQueue queue, LibraryViewModel vm, IServiceProvider sp)
    {
        InitializeComponent();
        _db = db;
        _queue = queue;
        _vm = vm;
        _audioPlayer = sp.GetService<IAudioPlayerService>();
        BindingContext = _vm;

        // Note: Event subscriptions removed - using commands and method calls instead
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        
        try
        {
            await _vm.LoadLocalAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("错误", $"加载失败: {ex.Message}", "确定");
        }
    }

    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Song song)
        {
            // Clear selection
            SongList.SelectedItem = null;
            
            await PlaySongAsync(song);
        }
    }

    private async Task PlaySongAsync(Song song)
    {
        try
        {
            // Add all songs to queue and play selected
            _queue.SetSongs([.. _vm.Songs]);
            _queue.SelectSong(song.Id);

            if (_audioPlayer != null && !string.IsNullOrEmpty(song.FilePath))
            {
                await _audioPlayer.PlayAsync(song.FilePath);
            }

            // Navigate to now playing page
            await Shell.Current.GoToAsync("//nowplaying");
        }
        catch (Exception ex)
        {
            await DisplayAlert("播放失败", ex.Message, "确定");
        }
    }

    private void OnViewModelSongClicked(object? sender, Song song)
    {
        // Handle special actions from ViewModel
        if (song.Title == "SORT_REQUEST")
        {
            ShowSortDialog();
        }
        else if (song.Title == "CLEAR_REQUEST")
        {
            ConfirmClearAsync();
        }
    }

    private void OnViewModelSongLongClicked(object? sender, Song song)
    {
        ShowSongContextMenu(song);
    }

    private async void ShowSortDialog()
    {
        var result = await DisplayActionSheet(
            "排序方式",
            "取消",
            null,
            "文件名",
            "入库时间",
            "文件大小",
            "文件夹",
            "艺术家",
            "标题"
        );

        if (!string.IsNullOrEmpty(result) && result != "取消")
        {
            ApplySort(result);
        }
    }

    private void ApplySort(string sortBy)
    {
        var songs = _vm.FilteredSongs.ToList();
        var sorted = sortBy switch
        {
            "文件名" => songs.OrderBy(s => Path.GetFileNameWithoutExtension(s.FilePath ?? "")).ToList(),
            "入库时间" => songs.OrderByDescending(s => s.DateAdded).ToList(),
            "文件大小" => songs.OrderByDescending(s => s.FileSize).ToList(),
            "文件夹" => songs.OrderBy(s => Path.GetDirectoryName(s.FilePath ?? "")).ToList(),
            "艺术家" => songs.OrderBy(s => s.Artist ?? "").ToList(),
            "标题" => songs.OrderBy(s => s.Title ?? "").ToList(),
            _ => songs
        };

        _vm.FilteredSongs = new ObservableCollection<Song>(sorted);
    }

    private async void ConfirmClearAsync()
    {
        var type = _vm.CurrentTab == "Local" ? "本地音乐库" : "网络音乐库";
        
        var result = await DisplayAlert(
            "确认清除",
            $"确定要清除{type}中的所有歌曲吗？\n\n此操作不可撤销。",
            "确认清除",
            "取消"
        );

        if (result)
        {
            try
            {
                if (_vm.CurrentTab == "Local")
                {
                    await _db.ClearLocalSongsAsync();
                }
                else
                {
                    await _db.ClearCachedNetworkSongsAsync();
                }
                
                _vm.Songs.Clear();
                _vm.FilteredSongs.Clear();
                _vm.StatusText = $"{type}已清空";
            }
            catch (Exception ex)
            {
                await DisplayAlert("错误", $"清除失败: {ex.Message}", "确定");
            }
        }
    }

    private async void ShowSongContextMenu(Song song)
    {
        var actions = new Dictionary<string, Action>();
        
        actions.Add("▶  播放", async () => await PlaySongAsync(song));
        
        actions.Add("⏭  下一首播放", () =>
        {
            _queue.AddNext(song);
            DisplayAlert("提示", $"已添加: {song.Title}", "确定");
        });

        actions.Add("📋  添加到歌单", () => ShowAddToPlaylistDialog(song));
        
        actions.Add("❤  收藏", async () =>
        {
            var isFav = await _db.IsFavoriteAsync(song.Id);
            await _db.SetFavoriteAsync(song.Id, !isFav);
            await DisplayAlert("提示", isFav ? "已取消收藏" : "已收藏", "确定");
        });

        actions.Add("ℹ  歌曲详情", () =>
        {
            ShowSongDetails(song);
        });

        var actionSheetResult = await DisplayActionSheet(
            $"{song.Title}",
            "取消",
            null,
            actions.Keys.ToArray()
        );

        if (actionSheetResult != null && actions.ContainsKey(actionSheetResult))
        {
            actions[actionSheetResult]();
        }
    }

    private async void ShowAddToPlaylistDialog(Song song)
    {
        try
        {
            var musicLibrary = Handler?.MauiContext?.Services.GetService<IMusicLibraryService>();
            if (musicLibrary == null) return;

            var playlists = await musicLibrary.GetAllPlaylistsAsync();
            if (playlists.Count == 0)
            {
                await DisplayAlert("提示", "暂无歌单，请先创建歌单", "确定");
                return;
            }

            var result = await DisplayActionSheet(
                "添加到歌单",
                "取消",
                null,
                playlists.Select(p => p.Name).ToArray()
            );

            if (!string.IsNullOrEmpty(result) && result != "取消")
            {
                var playlist = playlists.FirstOrDefault(p => p.Name == result);
                if (playlist != null)
                {
                    await musicLibrary.AddSongToPlaylistAsync(playlist.Id, song.Id);
                    await DisplayAlert("提示", $"已添加到「{playlist.Name}」", "确定");
                }
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("错误", ex.Message, "确定");
        }
    }

    private async void ShowSongDetails(Song song)
    {
        var durationStr = TimeSpan.FromSeconds(song.Duration).ToString(@"mm\:ss");
        var sourceStr = song.Source switch
        {
            SongSource.Local => "本地",
            SongSource.WebDAV => "网络",
            SongSource.SMB => "SMB",
            SongSource.Cache => "缓存",
            _ => "未知"
        };
        var info = $"艺术家：{song.Artist}\n" +
                  $"专辑：{song.Album}\n" +
                  $"时长：{durationStr}\n" +
                  $"来源：{sourceStr}\n" +
                  $"比特率：{(song.Bitrate > 0 ? $"{song.Bitrate}kbps" : "未知")}\n" +
                  $"文件大小：{FormatFileSize(song.FileSize)}\n" +
                  $"路径：{song.FilePath}";

        await DisplayAlert(song.Title ?? "未知歌曲", info, "确定");
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    private void OnSortClicked(object? sender, EventArgs e)
    {
        ShowSortDialog();
    }

    private async void OnClearClicked(object? sender, EventArgs e)
    {
        ConfirmClearAsync();
    }

    private void OnLoadMore(object? sender, EventArgs e)
    {
        // Load more songs if using pagination
        System.Diagnostics.Debug.WriteLine("Load more songs triggered");
    }
}
