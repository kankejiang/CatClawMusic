using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CatClawMusic.Core.ClawCircle;

/// <summary>直连 UDP 通道上的传输帧类型。</summary>
public enum P2PFrameType : byte
{
    Punch = 1,        // 打洞探测包
    ChunkRequest = 2, // 请求某片
    Chunk = 3,        // 某片数据
    Ack = 4,          // 确认某片已收且校验通过
    Done = 5,         // 整文件校验完成
    Manifest = 6      // 片清单（做种方先发，供接收方校验）
}

/// <summary>
/// 直连 UDP 通道的二进制帧编解码 + 各类型载荷构造/解析。
/// 帧格式：[1 byte type][4 byte 大端 length L][L byte payload]。
/// 这样 UDP 上实现一个轻量可靠传输（请求/确认/重传/滑动窗口）。
/// </summary>
public static class P2PFrame
{
    public static byte[] Encode(P2PFrameType type, byte[] payload)
    {
        var buf = new byte[5 + payload.Length];
        buf[0] = (byte)type;
        buf[1] = (byte)(payload.Length >> 24);
        buf[2] = (byte)(payload.Length >> 16);
        buf[3] = (byte)(payload.Length >> 8);
        buf[4] = (byte)payload.Length;
        Buffer.BlockCopy(payload, 0, buf, 5, payload.Length);
        return buf;
    }

    public static bool TryDecode(ReadOnlySpan<byte> frame, out P2PFrameType type, out byte[] payload)
    {
        type = 0;
        payload = Array.Empty<byte>();
        if (frame.Length < 5) return false;
        type = (P2PFrameType)frame[0];
        int len = (frame[1] << 24) | (frame[2] << 16) | (frame[3] << 8) | frame[4];
        if (frame.Length - 5 < len) return false;
        payload = frame.Slice(5, len).ToArray();
        return true;
    }

    // ── 载荷构造 ──

    public static byte[] MakePunch(string deviceId, string nonce)
        => Encoding.UTF8.GetBytes($"{deviceId}:{nonce}");

    public static byte[] MakeChunkRequest(string songKey, int pieceIndex)
    {
        var sk = Encoding.UTF8.GetBytes(songKey);
        var buf = new byte[2 + sk.Length + 4];
        WriteUtf8WithLen(buf, 0, sk);
        WriteInt32(buf, 2 + sk.Length, pieceIndex);
        return buf;
    }

    public static byte[] MakeChunk(string songKey, int pieceIndex, int totalPieces, byte[] data)
    {
        var sk = Encoding.UTF8.GetBytes(songKey);
        var buf = new byte[2 + sk.Length + 4 + 4 + 4 + data.Length];
        int off = 0;
        WriteUtf8WithLen(buf, off, sk); off += 2 + sk.Length;
        WriteInt32(buf, off, pieceIndex); off += 4;
        WriteInt32(buf, off, totalPieces); off += 4;
        WriteInt32(buf, off, data.Length); off += 4;
        Buffer.BlockCopy(data, 0, buf, off, data.Length);
        return buf;
    }

    public static byte[] MakeAck(string songKey, int pieceIndex)
    {
        var sk = Encoding.UTF8.GetBytes(songKey);
        var buf = new byte[2 + sk.Length + 4];
        WriteUtf8WithLen(buf, 0, sk);
        WriteInt32(buf, 2 + sk.Length, pieceIndex);
        return buf;
    }

    public static byte[] MakeManifest(string songKey, PieceManifest manifest)
    {
        var sk = Encoding.UTF8.GetBytes(songKey);
        var json = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(manifest));
        var buf = new byte[2 + sk.Length + json.Length];
        WriteUtf8WithLen(buf, 0, sk);
        Buffer.BlockCopy(json, 0, buf, 2 + sk.Length, json.Length);
        return buf;
    }

    public static byte[] MakeDone(string songKey, byte[] overallHash)
    {
        var sk = Encoding.UTF8.GetBytes(songKey);
        var buf = new byte[2 + sk.Length + overallHash.Length];
        WriteUtf8WithLen(buf, 0, sk);
        Buffer.BlockCopy(overallHash, 0, buf, 2 + sk.Length, overallHash.Length);
        return buf;
    }

    // ── 载荷解析 ──

    public static void ParsePunch(byte[] p, out string deviceId, out string nonce)
    {
        var s = Encoding.UTF8.GetString(p);
        var idx = s.IndexOf(':');
        if (idx < 0) { deviceId = s; nonce = ""; return; }
        deviceId = s.Substring(0, idx);
        nonce = s.Substring(idx + 1);
    }

    public static void ParseChunkRequest(byte[] p, out string songKey, out int pieceIndex)
    {
        ReadUtf8WithLen(p, 0, out var sk, out int off);
        songKey = sk;
        pieceIndex = ReadInt32(p, off);
    }

    public static void ParseChunk(byte[] p, out string songKey, out int pieceIndex, out int totalPieces, out byte[] data)
    {
        ReadUtf8WithLen(p, 0, out var sk, out int off);
        songKey = sk;
        pieceIndex = ReadInt32(p, off); off += 4;
        totalPieces = ReadInt32(p, off); off += 4;
        int dataLen = ReadInt32(p, off); off += 4;
        data = new byte[dataLen];
        Buffer.BlockCopy(p, off, data, 0, dataLen);
    }

    public static void ParseAck(byte[] p, out string songKey, out int pieceIndex)
    {
        ReadUtf8WithLen(p, 0, out var sk, out int off);
        songKey = sk;
        pieceIndex = ReadInt32(p, off);
    }

    public static void ParseManifest(byte[] p, out string songKey, out PieceManifest manifest)
    {
        ReadUtf8WithLen(p, 0, out var sk, out int off);
        songKey = sk;
        var json = Encoding.UTF8.GetString(p, off, p.Length - off);
        manifest = System.Text.Json.JsonSerializer.Deserialize<PieceManifest>(json) ?? new PieceManifest();
    }

    public static void ParseDone(byte[] p, out string songKey, out byte[] overallHash)
    {
        ReadUtf8WithLen(p, 0, out var sk, out int off);
        songKey = sk;
        overallHash = new byte[p.Length - off];
        Buffer.BlockCopy(p, off, overallHash, 0, overallHash.Length);
    }

    // ── 内部辅助 ──

    private static void WriteUtf8WithLen(byte[] buf, int off, byte[] utf8)
    {
        buf[off] = (byte)(utf8.Length >> 8);
        buf[off + 1] = (byte)utf8.Length;
        Buffer.BlockCopy(utf8, 0, buf, off + 2, utf8.Length);
    }

    private static void ReadUtf8WithLen(byte[] buf, int off, out string s, out int nextOff)
    {
        int len = (buf[off] << 8) | buf[off + 1];
        s = Encoding.UTF8.GetString(buf, off + 2, len);
        nextOff = off + 2 + len;
    }

    private static void WriteInt32(byte[] buf, int off, int v)
    {
        buf[off] = (byte)(v >> 24);
        buf[off + 1] = (byte)(v >> 16);
        buf[off + 2] = (byte)(v >> 8);
        buf[off + 3] = (byte)v;
    }

    private static int ReadInt32(byte[] buf, int off)
        => (buf[off] << 24) | (buf[off + 1] << 16) | (buf[off + 2] << 8) | buf[off + 3];
}

/// <summary>
/// 直连 UDP 通道封装：一个 UdpClient 绑定临时端口，用于 NAT 打洞探测与分块数据收发。
/// </summary>
public class UdpDirectChannel : IDisposable
{
    private readonly UdpClient _udp;

    public UdpDirectChannel()
    {
        // IPv6 双栈优先（可同时收发 IPv4 映射流量），失败退 IPv4
        try
        {
            _udp = new UdpClient(0, AddressFamily.InterNetworkV6);
            _udp.Client.DualMode = true;
        }
        catch
        {
            _udp = new UdpClient(0);
        }
        LocalPort = ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
    }

    /// <summary>本机 UDP 绑定端口（NAT 反射端口由 tracker 服务端观察得到）。</summary>
    public int LocalPort { get; }

    /// <summary>本机首选地址（IPv6 全局优先 → 链路本地 → IPv4 兜底），供同网段直连回退。</summary>
    public string LocalAddress
    {
        get
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                // ① IPv6 全局
                foreach (var ip in host.AddressList)
                    if (ip.AddressFamily == AddressFamily.InterNetworkV6
                        && !IPAddress.IsLoopback(ip) && !ip.IsIPv6LinkLocal && !ip.IsIPv6SiteLocal)
                        return ip.ToString();
                // ② IPv6 链路本地
                foreach (var ip in host.AddressList)
                    if (ip.AddressFamily == AddressFamily.InterNetworkV6
                        && !IPAddress.IsLoopback(ip))
                        return ip.ToString();
                // ③ IPv4
                foreach (var ip in host.AddressList)
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        return ip.ToString();
            }
            catch { }
            return "127.0.0.1";
        }
    }

    public async Task SendAsync(string host, int port, byte[] frame, CancellationToken ct)
    {
        var ep = new IPEndPoint(IPAddress.Parse(host), port);
        await _udp.SendAsync(frame, frame.Length, ep).WaitAsync(ct);
    }

    public async Task<(string host, int port, byte[] frame)> ReceiveAsync(CancellationToken ct)
    {
        var t = _udp.ReceiveAsync();
        var result = await t.WaitAsync(ct);
        return (result.RemoteEndPoint.Address.ToString(), result.RemoteEndPoint.Port, result.Buffer);
    }

    public void Dispose()
    {
        try { _udp.Close(); } catch { }
        try { _udp.Dispose(); } catch { }
    }
}
