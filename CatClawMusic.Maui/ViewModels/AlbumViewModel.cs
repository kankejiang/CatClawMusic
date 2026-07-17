using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 专辑视图模型包装类：为 AlbumWithCount 添加 UI 绑定属性（占位颜色、封面图片源、初始字符等）。
/// </summary>
public partial class AlbumViewModel : ObservableObject
{
    private static readonly string[] Palettes = {
        "#8C7BFF,#55D6FF", "#FF7AAE,#FFB36B", "#55D6FF,#7AF0C8", "#A78BFA,#F0ABFC",
        "#5EEAD4,#60A5FA", "#FBBF24,#FB7185", "#818CF8,#22D3EE", "#F472B6,#C084FC"
    };

    private readonly AlbumWithCount _album;

    public AlbumViewModel(AlbumWithCount album)
    {
        _album = album;
    }

    /// <summary>专辑 ID</summary>
    public int Id => _album.Id;

    /// <summary>专辑标题</summary>
    public string Title => _album.Title;

    /// <summary>艺术家名称</summary>
    public string ArtistName => _album.ArtistName;

    /// <summary>歌曲数量</summary>
    public int SongCount => _album.SongCount;

    /// <summary>发行年份</summary>
    public int? Year => _album.Year;

    /// <summary>封面路径</summary>
    public string? CoverArtPath => _album.CoverArtPath;

    /// <summary>封面 URL</summary>
    public string? Cover => _album.Cover;

    /// <summary>初始字符（用于占位显示）</summary>
    public string Initial => string.IsNullOrEmpty(_album.Title) ? "♪" : _album.Title.Trim()[0].ToString().ToUpper();

    /// <summary>是否有有效封面</summary>
    [ObservableProperty]
    private bool _hasCover;

    /// <summary>封面图片源</summary>
    [ObservableProperty]
    private ImageSource? _coverImage;

    /// <summary>占位背景色</summary>
    public Color PlaceholderColor
    {
        get
        {
            var index = Math.Abs(_album.Id) % Palettes.Length;
            var colors = Palettes[index].Split(',');
            return Color.FromArgb(colors[0]);
        }
    }

    /// <summary>来源点颜色（本地蓝色、网络橙色）</summary>
    public Color SourceDotColor => IsLocal ? Color.FromArgb("#55D6FF") : Color.FromArgb("#FF9F6B");

    /// <summary>是否为本地专辑</summary>
    public bool IsLocal
    {
        get
        {
            if (!string.IsNullOrEmpty(_album.SampleFilePath))
            {
                return _album.SampleFilePath.StartsWith("content://") ||
                       _album.SampleFilePath.StartsWith("file://") ||
                       (!_album.SampleFilePath.StartsWith("http") && !_album.SampleFilePath.StartsWith("smb://"));
            }
            return false;
        }
    }

    /// <summary>子信息文本（年份 · 歌曲数）</summary>
    public string SubInfo
    {
        get
        {
            var yearStr = _album.Year.HasValue ? _album.Year.Value.ToString() : "未知";
            return $"{yearStr} · {_album.SongCount} 首";
        }
    }

    /// <summary>播放次数（来自播放历史聚合）</summary>
    public int PlayCount { get; set; }

    /// <summary>设置封面图片</summary>
    public void SetCover(string? path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            CoverImage = ImageSource.FromFile(path);
            HasCover = true;
        }
        else
        {
            CoverImage = null;
            HasCover = false;
        }
    }
}
