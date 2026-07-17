using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 艺术家视图模型包装类：为 ArtistWithCount 添加 UI 绑定属性（占位颜色、封面图片源、初始字符等）。
/// </summary>
public partial class ArtistViewModel : ObservableObject
{
    private static readonly string[] Palettes = {
        "#8C7BFF,#55D6FF", "#FF7AAE,#FFB36B", "#55D6FF,#7AF0C8", "#A78BFA,#F0ABFC",
        "#5EEAD4,#60A5FA", "#FBBF24,#FB7185", "#818CF8,#22D3EE", "#F472B6,#C084FC"
    };

    private readonly ArtistWithCount _artist;

    public ArtistViewModel(ArtistWithCount artist)
    {
        _artist = artist;
    }

    /// <summary>艺术家 ID</summary>
    public int Id => _artist.Id;

    /// <summary>艺术家名称</summary>
    public string Name => _artist.Name;

    /// <summary>歌曲数量</summary>
    public int SongCount => _artist.SongCount;

    /// <summary>封面路径</summary>
    public string? Cover => _artist.Cover;

    /// <summary>来源类型（本地/网络）</summary>
    public string Source
    {
        get
        {
            if (!string.IsNullOrEmpty(_artist.SampleFilePath))
            {
                if (_artist.SampleFilePath.StartsWith("content://") ||
                    _artist.SampleFilePath.StartsWith("file://") ||
                    (!_artist.SampleFilePath.StartsWith("http") && !_artist.SampleFilePath.StartsWith("smb://")))
                {
                    return "本地";
                }
            }
            return "网络";
        }
    }

    /// <summary>初始字符</summary>
    public string Initial => string.IsNullOrEmpty(_artist.Name) ? "♪" : _artist.Name.Trim()[0].ToString().ToUpper();

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
            var index = Math.Abs(_artist.Id) % Palettes.Length;
            var colors = Palettes[index].Split(',');
            return Color.FromArgb(colors[0]);
        }
    }

    /// <summary>索引字母（用于艺术家列表字母 rail）</summary>
    public string IndexLetter
    {
        get
        {
            if (string.IsNullOrEmpty(_artist.Name)) return "#";
            var c = _artist.Name.Trim().ToUpperInvariant()[0];
            if (c >= 'A' && c <= 'Z') return c.ToString();
            if (c >= 0x4E00 && c <= 0x9FFF) return "中";
            return "#";
        }
    }

    /// <summary>子信息文本</summary>
    public string SubInfo => $"{_artist.SongCount} 首 · {Source}";

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
