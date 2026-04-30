using SQLite;

namespace CatClawMusic.Core.Models;

[Table("Albums")]
public class Album
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [NotNull]
    public string Title { get; set; } = string.Empty;

    // 旧字段兼容
    public string Name { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string? CoverArtPath { get; set; }
    public int SongCount { get; set; }
    public int? Year { get; set; }

    [Indexed]
    public int ArtistId { get; set; }

    public string? Cover { get; set; }
    public int? ReleaseYear { get; set; }
}
