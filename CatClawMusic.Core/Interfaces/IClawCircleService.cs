using CatClawMusic.Core.Models;
using System.Collections.Generic;

namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 猫爪圈服务：在同局域网内发现其他猫爪音乐用户，并对外共享/获取曲库。
/// <para>
/// 设计为自包含、无需任何外部服务器：通过 UDP 广播发现邻近设备，
/// 通过内置迷你 HTTP 服务对外提供本机曲库，并通过 HttpClient 从对端拉取。
/// </para>
/// </summary>
public interface IClawCircleService
{
    /// <summary>服务是否正在运行（已开启猫爪圈且网络就绪）</summary>
    bool IsRunning { get; }

    /// <summary>本机 HTTP 服务监听端口（未运行时为 0）</summary>
    int Port { get; }

    /// <summary>本机用于发现的局域网地址（未运行时为空）</summary>
    string LocalAddress { get; }

    /// <summary>发现的对端列表发生变化时触发（新增 / 离线 / 信息更新）</summary>
    event EventHandler? PeersChanged;

    /// <summary>
    /// 启动猫爪圈：开始 UDP 发现并启动 HTTP 服务对外共享曲库。
    /// </summary>
    /// <param name="deviceName">本机显示名。</param>
    /// <param name="shareLibrary">是否对外共享本地曲库。</param>
    /// <param name="ct">取消令牌。</param>
    Task StartAsync(string deviceName, bool shareLibrary, CancellationToken ct = default);

    /// <summary>停止猫爪圈，关闭 UDP 与 HTTP 监听。</summary>
    Task StopAsync();

    /// <summary>返回当前已知的对端列表（含可能已离线的，调用方按 IsOnline 过滤）。</summary>
    IReadOnlyList<ClawCirclePeer> GetPeers();

    /// <summary>主动广播一次 Ping 并收集回应，刷新对端列表。</summary>
    Task RefreshPeersAsync();

    /// <summary>获取对端共享的歌曲列表（对端未共享时返回空列表）。</summary>
    Task<List<ClawCircleSongInfo>?> GetPeerSongsAsync(ClawCirclePeer peer, CancellationToken ct = default);

    /// <summary>从对端按歌曲 ID 拉取音频流（用于下载/播放）。失败返回 null。</summary>
    Task<ClawCircleSongStreamResult?> GetPeerSongStreamAsync(ClawCirclePeer peer, int songId, CancellationToken ct = default);
}
