using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.Maui.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Text;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.ApplicationModel;
using System.ComponentModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 探索/搜索页 ViewModel：加载每日推荐、艺术家、专辑、最多播放、最新音乐等内容，
/// 提供搜索过滤、Tab 切换、AI 聊天模式入口及消息收发等能力。
/// </summary>

public class SearchArtistItem : ObservableObject
{
    /// <summary>艺术家 ID</summary>
    public int Id { get; set; }
    /// <summary>艺术家名称</summary>
    public string Name { get; set; } = "";
    /// <summary>副标题（如歌曲数量）</summary>
    public string Subtitle { get; set; } = "";

    private ImageSource? _coverSource;
    /// <summary>封面图源（后台解析后刷新）</summary>
    public ImageSource? CoverSource
    {
        get => _coverSource;
        set
        {
            if (_coverSource != value)
            {
                _coverSource = value;
                OnPropertyChanged();
            }
        }
    }
}

public class SearchAlbumItem : ObservableObject
{
    /// <summary>专辑 ID</summary>
    public int Id { get; set; }
    /// <summary>专辑标题</summary>
    public string Title { get; set; } = "";
    /// <summary>艺术家名称</summary>
    public string ArtistName { get; set; } = "";
    /// <summary>副标题（如歌曲数量）</summary>
    public string Subtitle { get; set; } = "";

    private ImageSource? _coverSource;
    /// <summary>封面图源（后台解析后刷新）</summary>
    public ImageSource? CoverSource
    {
        get => _coverSource;
        set
        {
            if (_coverSource != value)
            {
                _coverSource = value;
                OnPropertyChanged();
            }
        }
    }
}

public class AiRecItem
{
    /// <summary>歌曲 ID（对应本地曲库）</summary>
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public int SongId { get; set; }
    /// <summary>推荐理由文案</summary>
    [System.Text.Json.Serialization.JsonPropertyName("reason")]
    public string Reason { get; set; } = "";
}

public class AiRecCache
{
    /// <summary>缓存日期 "yyyy-MM-dd"</summary>
    public string Date { get; set; } = "";
    /// <summary>当天推荐项列表</summary>
    public List<AiRecItem> Items { get; set; } = new();
}

/// <summary>AI 歌单模型（含封面 INPC：第一首歌封面后台解析完成后自动刷新卡片封面）</summary>
public class AiPlaylist : INotifyPropertyChanged
{
    /// <summary>歌单名</summary>
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>推荐理由</summary>
    [System.Text.Json.Serialization.JsonPropertyName("reason")]
    public string Reason { get; set; } = "";

    /// <summary>歌单内歌曲 ID（AI 返回，解析时按 ID 匹配本地曲库）</summary>
    [System.Text.Json.Serialization.JsonPropertyName("song_ids")]
    public List<int> SongIds { get; set; } = new();

    private List<Song>? _songs;

    /// <summary>解析后映射的本地歌曲（UI 使用，不落盘）。
    /// 赋值时订阅第一首歌的 CoverArtPath 变化 → 触发 CoverPath 通知（封面后台解析完成后自动刷新）。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<Song> Songs
    {
        get => _songs ?? new();
        set
        {
            if (_songs is { Count: > 0 })
                _songs[0].PropertyChanged -= OnSongPropertyChanged;
            _songs = value;
            if (_songs is { Count: > 0 })
                _songs[0].PropertyChanged += OnSongPropertyChanged;
            OnPropertyChanged(nameof(CoverPath));
            OnPropertyChanged(nameof(Subtitle));
        }
    }

    /// <summary>歌单封面：取第一首歌封面（封面后台解析完成后经 INPC 自动刷新）</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string CoverPath => Songs.FirstOrDefault()?.CoverArtPath ?? "";

    /// <summary>副标题（卡片展示：歌曲数）</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Subtitle => $"{Songs.Count} 首 · AI 主题歌单";

    private void OnSongPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Song.CoverArtPath))
            OnPropertyChanged(nameof(CoverPath));
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

/// <summary>AI 歌单磁盘缓存（每天一份）</summary>
public class AiPlaylistsCache
{
    /// <summary>缓存日期 "yyyy-MM-dd"</summary>
    public string Date { get; set; } = "";
    /// <summary>当天歌单列表</summary>
    public List<AiPlaylist> Playlists { get; set; } = new();
}

public class HeroCardItem
{
    /// <summary>标签</summary>
    public string Tag { get; set; } = "";
    /// <summary>标题</summary>
    public string Title { get; set; } = "";
    /// <summary>描述</summary>
    public string Description { get; set; } = "";
    /// <summary>关联歌曲</summary>
    public Song? Song { get; set; }
    /// <summary>渐变起始色</summary>
    public Color GradientStart { get; set; } = Colors.Blue;
    /// <summary>渐变结束色</summary>
    public Color GradientEnd { get; set; } = Colors.Purple;
    /// <summary>播放按钮图标（WinUI 需代码赋值，XAML 字面量不渲染）</summary>
    public ImageSource? PlayIcon { get; set; }
}

public class OnlineProviderView
{
    /// <summary>音源展示名（如 网易云 / QQ音乐）</summary>
    public string Name { get; set; } = "";
    /// <summary>音源图标 Emoji</summary>
    public string Icon { get; set; } = "🎧";
}

public class ChatHistoryLoadedEventArgs : EventArgs
{
    public bool IsInitialLoad { get; set; }
    public bool ScrollToEnd { get; set; }
    public int ItemsAdded { get; set; }
    public int PreviousCount { get; set; }
}
