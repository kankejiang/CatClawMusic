using System.Text;
using CatClawMusic.Data;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 音乐库快照服务，生成紧凑的音乐库文本快照供 AI Agent 读取上下文
/// 格式紧凑，尽量节省 token
/// </summary>
public class MusicLibrarySnapshotService
{
    private const string SnapshotFileName = "music_library.md";

    public static string SnapshotPath => Path.Combine(FileSystem.AppDataDirectory, SnapshotFileName);

    public async Task GenerateSnapshotAsync(MusicDatabase db)
    {
        try
        {
            var songs = await db.GetSongsAsync();
            var artists = await db.GetAllArtistsAsync();
            var albums = await db.GetAllAlbumsAsync();

            var topArtists = songs
                .Where(s => !string.IsNullOrEmpty(s.Artist))
                .GroupBy(s => s.Artist!)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(a => a.Count)
                .Take(10)
                .ToList();

            var topAlbums = albums
                .Select(a => new
                {
                    Title = a.Title,
                    Count = songs.Count(s => s.AlbumId == a.Id)
                })
                .OrderByDescending(a => a.Count)
                .Take(8)
                .ToList();

            var topSongs = songs
                .OrderByDescending(s => s.PlayCount)
                .Take(20)
                .ToList();

            var recentSongs = songs
                .OrderByDescending(s => s.DateAdded)
                .Take(8)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"概览:{songs.Count}曲/{artists.Count}艺人/{albums.Count}专辑");

            if (topArtists.Count > 0)
            {
                sb.Append("艺人:");
                sb.AppendJoin(",", topArtists.Select(a => $"{a.Name}({a.Count})"));
                sb.AppendLine();
            }

            if (topAlbums.Count > 0)
            {
                sb.Append("专辑:");
                sb.AppendJoin(",", topAlbums.Select(a => $"《{a.Title}》({a.Count})"));
                sb.AppendLine();
            }

            if (topSongs.Count > 0)
            {
                sb.Append("常听:");
                sb.AppendJoin(";", topSongs.Select(s => $"{s.Title}-{s.Artist}"));
                sb.AppendLine();
            }

            var recentNotInTop = recentSongs.Where(r => !topSongs.Any(t => t.Id == r.Id)).Take(5).ToList();
            if (recentNotInTop.Count > 0)
            {
                sb.Append("新增:");
                sb.AppendJoin(";", recentNotInTop.Select(s => $"{s.Title}-{s.Artist}"));
                sb.AppendLine();
            }

            var content = sb.ToString();
            var utf8NoBom = new UTF8Encoding(false);
            await File.WriteAllTextAsync(SnapshotPath, content, utf8NoBom);

            Log.Debug("MusicLibrarySnapshotService", $"[MusicLibrarySnapshot] 快照生成成功({content.Length}字符)：{SnapshotPath}");
        }
        catch (Exception ex)
        {
            Log.Debug("MusicLibrarySnapshotService", $"[MusicLibrarySnapshot] 快照生成失败：{ex.Message}");
        }
    }

    public static string LoadSnapshot()
    {
        try
        {
            if (File.Exists(SnapshotPath))
            {
                return File.ReadAllText(SnapshotPath, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            Log.Debug("MusicLibrarySnapshotService", $"[MusicLibrarySnapshot] 读取快照失败：{ex.Message}");
        }
        return string.Empty;
    }
}
