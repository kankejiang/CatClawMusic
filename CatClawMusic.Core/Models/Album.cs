using SQLite;
using System.ComponentModel;

namespace CatClawMusic.Core.Models;

/// <summary>
/// 专辑模型，对应数据库 Albums 表
/// </summary>
[Table("Albums")]
public class Album : INotifyPropertyChanged
{
    /// <summary>主键，自增</summary>
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>专辑标题</summary>
    [NotNull]
    public string Title { get; set; } = string.Empty;

    /// <summary>专辑名称（旧字段兼容）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>艺术家名称</summary>
    public string Artist { get; set; } = string.Empty;

    private string? _coverArtPath;

    /// <summary>封面路径</summary>
    public string? CoverArtPath
    {
        get => _coverArtPath;
        set
        {
            if (_coverArtPath != value)
            {
                _coverArtPath = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>歌曲数量</summary>
    public int SongCount { get; set; }

    /// <summary>发行年份</summary>
    public int? Year { get; set; }

    /// <summary>艺术家 ID（外键）</summary>
    [Indexed]
    public int ArtistId { get; set; }

    private string? _cover;

    /// <summary>封面 URL 或路径</summary>
    public string? Cover
    {
        get => _cover;
        set
        {
            if (_cover != value)
            {
                _cover = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>发行年份（新字段）</summary>
    public int? ReleaseYear { get; set; }

    /// <summary>属性变更通知（封面在后台解析后回填，UI 自动刷新）</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>触发属性变更通知</summary>
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
