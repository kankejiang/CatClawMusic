using SQLite;

namespace CatClawMusic.Core.Models;

[Table("CachedSongs")]
public class CachedSong
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int SongId { get; set; }
    public string LocalPath { get; set; } = string.Empty;
    public long CachedAt { get; set; }
    public long FileSize { get; set; }
}
