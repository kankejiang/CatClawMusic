namespace CatClawMusic.Core.ClawCircle;

/// <summary>
/// 本地曲库数据访问抽象。引擎据此：① 生成上报的 SongKeys；② 做种时按片读取本地字节；
/// ③ 下载时写入分片并最终校验落盘。便于无头测试用内存实现替换。
/// </summary>
public interface IClawCircleDataProvider
{
    /// <summary>返回本机曲库的 SongKeys（与 Stage 2 库摘要格式一致）。无曲库返回空列表。</summary>
    Task<List<string>> GetLocalSongKeysAsync(CancellationToken ct);

    /// <summary>本机是否拥有该歌曲；若拥有返回总字节数（片数由引擎按 PieceSize 计算）。</summary>
    Task<long?> GetLocalSongSizeAsync(string songKey, CancellationToken ct);

    /// <summary>读取本机某片字节（做种用）。不拥有该歌曲返回 null。</summary>
    Task<byte[]?> ReadLocalPieceAsync(string songKey, int pieceIndex, int pieceSize, CancellationToken ct);

    /// <summary>开始接收一首歌：创建临时接收会话（按片落盘）。</summary>
    Task<object> BeginReceiveAsync(string songKey, PieceManifest manifest, CancellationToken ct);

    /// <summary>写入已收到的分片。</summary>
    Task WriteReceivedPieceAsync(object session, int pieceIndex, byte[] data, CancellationToken ct);

    /// <summary>所有分片收齐后：整体校验 + 持久化落盘到目标路径。</summary>
    Task FinalizeReceiveAsync(object session, string destinationPath, CancellationToken ct);
}
