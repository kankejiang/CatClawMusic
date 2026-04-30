using SQLite;

namespace CatClawMusic.Core.Models;

[Table("PlayHistory")]
public class PlayHistory
{
    [Indexed]
    public int SongId { get; set; }

    public long PlayedAt { get; set; }

    public int PlayCount { get; set; } = 1;
}
