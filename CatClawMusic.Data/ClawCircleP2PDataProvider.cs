using System.Security.Cryptography;
using CatClawMusic.Core.ClawCircle;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Data;

/// <summary>
/// 基于 <see cref="MusicDatabase"/> 的猫爪圈 P2P 数据提供者：
/// 做种时按 songKey 定位本地歌曲、按片读取字节；下载时写入临时分片文件、整体校验后落盘。
/// </summary>
public class ClawCircleP2PDataProvider : IClawCircleDataProvider
{
    private readonly MusicDatabase _db;
    private readonly string _downloadDir;
    private Dictionary<string, Song>? _byKey;

    /// <summary>按 FilePath 打开只读流（平台注入：Windows 用 File.OpenRead，Android 走 ContentResolver）。</summary>
    public Func<string, Stream?>? SongStreamOpener { get; set; }

    public ClawCircleP2PDataProvider(MusicDatabase db, string downloadDir)
    {
        _db = db;
        _downloadDir = downloadDir;
        try { Directory.CreateDirectory(_downloadDir); } catch { }
    }

    private async Task<Dictionary<string, Song>> EnsureIndexAsync(CancellationToken ct)
    {
        if (_byKey != null) return _byKey;
        var songs = await _db.GetSongsWithDetailsAsync();
        var dict = new Dictionary<string, Song>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in songs)
        {
            var key = SongKey.Of(s.Artist, s.Title);
            if (!dict.ContainsKey(key)) dict[key] = s;
        }
        _byKey = dict;
        return dict;
    }

    public async Task<List<string>> GetLocalSongKeysAsync(CancellationToken ct)
    {
        var idx = await EnsureIndexAsync(ct);
        return idx.Keys.ToList();
    }

    public async Task<long?> GetLocalSongSizeAsync(string songKey, CancellationToken ct)
    {
        var idx = await EnsureIndexAsync(ct);
        return idx.TryGetValue(songKey, out var s) ? (s.FileSize > 0 ? s.FileSize : TryFileSize(s.FilePath)) : null;
    }

    public async Task<byte[]?> ReadLocalPieceAsync(string songKey, int pieceIndex, int pieceSize, CancellationToken ct)
    {
        var idx = await EnsureIndexAsync(ct);
        if (!idx.TryGetValue(songKey, out var song)) return null;
        var opener = SongStreamOpener ?? DefaultOpenRead;
        try
        {
            using var stream = opener(song.FilePath);
            if (stream == null || !stream.CanSeek) return null;
            long off = (long)pieceIndex * pieceSize;
            if (off >= stream.Length) return Array.Empty<byte>();
            stream.Seek(off, SeekOrigin.Begin);
            int len = (int)Math.Min(pieceSize, stream.Length - off);
            var buf = new byte[len];
            int read = 0;
            while (read < len && !ct.IsCancellationRequested)
            {
                int n = await stream.ReadAsync(buf.AsMemory(read, len - read), ct);
                if (n == 0) break;
                read += n;
            }
            if (read < len) Array.Resize(ref buf, read);
            return buf;
        }
        catch { return null; }
    }

    // ── 接收侧：临时分片文件 ──

    public Task<object> BeginReceiveAsync(string songKey, PieceManifest manifest, CancellationToken ct)
    {
        var dir = Path.Combine(_downloadDir, "tmp");
        try { Directory.CreateDirectory(dir); } catch { }
        var path = Path.Combine(dir, $"{Guid.NewGuid():N}.part");
        var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        fs.SetLength(manifest.TotalSize);
        return Task.FromResult<object>(new RecvSession { Path = path, Stream = fs, Manifest = manifest });
    }

    public Task WriteReceivedPieceAsync(object session, int pieceIndex, byte[] data, CancellationToken ct)
    {
        var s = (RecvSession)session;
        long off = (long)pieceIndex * s.Manifest.PieceSize;
        s.Stream.Seek(off, SeekOrigin.Begin);
        return s.Stream.WriteAsync(data, ct).AsTask();
    }

    public async Task FinalizeReceiveAsync(object session, string destinationPath, CancellationToken ct)
    {
        var s = (RecvSession)session;
        try
        {
            await s.Stream.FlushAsync(ct);
            s.Stream.Close();
            // 整体校验
            await using var fs = new FileStream(s.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(fs, ct);
            var hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            if (!string.Equals(hex, s.Manifest.OverallHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("整体哈希不匹配");
            // 落盘到目标路径
            try { Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!); } catch { }
            File.Copy(s.Path, destinationPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(s.Path)) File.Delete(s.Path); } catch { }
        }
    }

    private static Stream? DefaultOpenRead(string path)
    {
        try { return File.OpenRead(path); } catch { return null; }
    }

    private static long TryFileSize(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    private sealed class RecvSession
    {
        public string Path = "";
        public FileStream Stream = null!;
        public PieceManifest Manifest = null!;
    }
}
