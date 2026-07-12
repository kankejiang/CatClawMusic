using System.Security.Cryptography;
using CatClawMusic.Core.ClawCircle;

namespace CatClawMusic.P2P.Harness;

/// <summary>
/// 猫爪圈 Stage 3 无头验证：起两个节点（一个做种、一个下载），经真实 Stage 2 tracker
/// 完成 STUN 反射端点发现 → NAT 打洞 → 分块直传 + 逐片/整体哈希校验。
/// 用法：先启动 CatClawMusicServer（--token testtoken --port 37823），再 dotnet run。
/// </summary>
internal static class Program
{
    private const string TrackerUrl = "ws://127.0.0.1:37823/ws/clawcircle";
    private const string Token = "testtoken";
    private const string ServerHost = "127.0.0.1";
    private const int StunPort = 37824;
    private const string SongKey = "test_artist\u0001test_song";

    private static async Task Main()
    {
        // 做种方 B 的原始歌曲字节（1.5MB 随机）
        var rng = new Random(42);
        var original = new byte[1_572_864]; // 1.5MB
        rng.NextBytes(original);
        var originalHash = BytesToHex(SHA256.HashData(original));
        Console.WriteLine($"[harness] 原始歌曲 {original.Length} 字节, sha256={originalHash[..12]}…");

        // B：做种方
        var providerB = new MemDataProvider();
        providerB.Songs[SongKey] = original;
        var sigB = new ClawCircleTrackerClient(TrackerUrl, Token);
        var engineB = new P2PTransferEngine(sigB, providerB, "SEED-B", ServerHost, StunPort, pieceSize: 16 * 1024);
        engineB.ProgressChanged += p => Console.WriteLine($"  [B-seed] {p.State} {p.Message}");
        await sigB.ConnectAsync("SEED-B", "做种节点B", new LibrarySummary
        {
            SongCount = 1, AlbumCount = 1, ArtistCount = 1, SongKeys = new() { SongKey }
        }, CancellationToken.None);
        engineB.Start();
        Console.WriteLine("[harness] B 已连接并做种");

        // A：下载方
        var providerA = new MemDataProvider();
        var sigA = new ClawCircleTrackerClient(TrackerUrl, Token);
        var engineA = new P2PTransferEngine(sigA, providerA, "LEECH-A", ServerHost, StunPort, pieceSize: 16 * 1024);
        engineA.ProgressChanged += p => Console.WriteLine($"  [A-leech] {p.State} {p.ReceivedPieces}/{p.TotalPieces} {p.Message}");
        await sigA.ConnectAsync("LEECH-A", "下载节点A", new LibrarySummary(), CancellationToken.None);
        engineA.Start();
        Console.WriteLine("[harness] A 已连接");

        // 等 STUN + 注册稳定
        await Task.Delay(1500);

        var destPath = Path.Combine(Path.GetTempPath(), $"clawcircle_recv_{Guid.NewGuid():N}.bin");
        Console.WriteLine($"[harness] A 开始下载 {SongKey} …");
        var ok = await engineA.RequestSongAsync(SongKey, destPath, CancellationToken.None);

        if (!ok)
        {
            Console.WriteLine("[harness] ❌ 下载失败");
            return;
        }

        var recvBytes = await File.ReadAllBytesAsync(destPath);
        var recvHash = BytesToHex(SHA256.HashData(recvBytes));
        Console.WriteLine($"[harness] 收到 {recvBytes.Length} 字节, sha256={recvHash[..12]}…");
        var match = recvHash == originalHash && recvBytes.Length == original.Length;
        Console.WriteLine(match
            ? "[harness] ✅ 整体哈希匹配，分块直传 + 做种 验证通过"
            : "[harness] ❌ 哈希不匹配");

        try { File.Delete(destPath); } catch { }
        engineA.Stop(); engineB.Stop();
        await sigA.DisconnectAsync(); await sigB.DisconnectAsync();
        Environment.Exit(match ? 0 : 1);
    }

    private static string BytesToHex(byte[] b)
    {
        var sb = new System.Text.StringBuilder(b.Length * 2);
        foreach (var x in b) sb.Append(x.ToString("x2"));
        return sb.ToString();
    }
}

/// <summary>内存版数据提供者：用 Dictionary 存歌曲字节，做种时切片、下载时缓存并整体校验落盘。</summary>
internal sealed class MemDataProvider : IClawCircleDataProvider
{
    public Dictionary<string, byte[]> Songs { get; } = new();

    public Task<List<string>> GetLocalSongKeysAsync(CancellationToken ct)
        => Task.FromResult(Songs.Keys.ToList());

    public Task<long?> GetLocalSongSizeAsync(string songKey, CancellationToken ct)
        => Task.FromResult(Songs.TryGetValue(songKey, out var d) ? (long?)d.Length : null);

    public Task<byte[]?> ReadLocalPieceAsync(string songKey, int pieceIndex, int pieceSize, CancellationToken ct)
    {
        if (!Songs.TryGetValue(songKey, out var d)) return Task.FromResult<byte[]?>(null);
        long off = (long)pieceIndex * pieceSize;
        if (off >= d.Length) return Task.FromResult<byte[]?>(Array.Empty<byte>());
        int len = (int)Math.Min(pieceSize, d.Length - off);
        var seg = new byte[len];
        Array.Copy(d, off, seg, 0, len);
        return Task.FromResult<byte[]?>(seg);
    }

    public Task<object> BeginReceiveAsync(string songKey, PieceManifest manifest, CancellationToken ct)
        => Task.FromResult<object>(new RecvSession { Buf = new byte[manifest.TotalSize], Manifest = manifest });

    public Task WriteReceivedPieceAsync(object session, int pieceIndex, byte[] data, CancellationToken ct)
    {
        var s = (RecvSession)session;
        long off = (long)pieceIndex * s.Manifest.PieceSize;
        Array.Copy(data, 0, s.Buf, off, data.Length);
        return Task.CompletedTask;
    }

    public Task FinalizeReceiveAsync(object session, string destinationPath, CancellationToken ct)
    {
        var s = (RecvSession)session;
        var hash = SHA256.HashData(s.Buf);
        var hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        if (hex != s.Manifest.OverallHash)
            throw new InvalidDataException("整体哈希不匹配: " + hex[..12] + " vs " + s.Manifest.OverallHash[..12]);
        File.WriteAllBytes(destinationPath, s.Buf);
        return Task.CompletedTask;
    }

    private sealed class RecvSession
    {
        public byte[] Buf = null!;
        public PieceManifest Manifest = null!;
    }
}
