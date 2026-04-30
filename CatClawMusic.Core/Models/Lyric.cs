using SQLite;

namespace CatClawMusic.Core.Models;

[Table("Lyrics")]
public class Lyric
{
    [PrimaryKey]
    public int SongId { get; set; }

    public string? LrcPath { get; set; }
    public string? Content { get; set; }
}
