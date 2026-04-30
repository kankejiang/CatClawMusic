using SQLite;

namespace CatClawMusic.Core.Models;

[Table("Artists")]
public class Artist
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Unique, NotNull]
    public string Name { get; set; } = string.Empty;

    public string? Cover { get; set; }
}
