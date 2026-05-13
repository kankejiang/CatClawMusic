using SQLite;

namespace CatClawMusic.Core.Models;

[Table("PlaylistSongs")]
public class PlaylistSong
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int PlaylistId { get; set; }

    [Indexed]
    public int SongId { get; set; }

    public int Position { get; set; }
}
