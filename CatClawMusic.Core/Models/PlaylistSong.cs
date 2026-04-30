using SQLite;

namespace CatClawMusic.Core.Models;

[Table("PlaylistSongs")]
public class PlaylistSong
{
    [Indexed]
    public int PlaylistId { get; set; }

    [Indexed]
    public int SongId { get; set; }

    public int Position { get; set; }
}
