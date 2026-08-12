using IOFile = System.IO.File;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Core.Services;
/// <summary>
/// M4A/MP4 手动 atom 树解析器。
/// 当 TagLibSharp 无法解析某些 m4a 文件时，用此类手动遍历 MP4 atom 提取封面、歌词、音频属性和元数据。
/// </summary>

public class M4aMetadata
{
    /// <summary>标题</summary>
    public string? Title { get; set; }

    /// <summary>艺术家</summary>
    public string? Artist { get; set; }

    /// <summary>专辑</summary>
    public string? Album { get; set; }

    /// <summary>嵌入歌词文本</summary>
    public string? Lyrics { get; set; }

    /// <summary>时长（秒）</summary>
    public int DurationSeconds { get; set; }

    /// <summary>比特率（kbps）</summary>
    public int Bitrate { get; set; }

    /// <summary>采样率（Hz）</summary>
    public int SampleRate { get; set; }

    /// <summary>声道数</summary>
    public int Channels { get; set; }

    /// <summary>位深度（bit）</summary>
    public int BitDepth { get; set; }

    /// <summary>编码格式名称（AAC/ALAC/FLAC 等）</summary>
    public string? Codec { get; set; }
}
