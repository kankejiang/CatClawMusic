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
