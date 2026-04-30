using SQLite;

namespace CatClawMusic.Core.Models;

[Table("Favorites")]
public class Favorite
{
    [PrimaryKey]
    public int SongId { get; set; }

    public long AddedAt { get; set; }
}
