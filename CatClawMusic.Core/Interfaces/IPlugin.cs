using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 插件基类接口
/// </summary>
public interface IPlugin
{
    /// <summary>
    /// 插件名称
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// 插件版本
    /// </summary>
    string Version { get; }
    
    /// <summary>
    /// 作者
    /// </summary>
    string Author { get; }
    
    /// <summary>
    /// 初始化插件
    /// </summary>
    Task InitializeAsync();
    
    /// <summary>
    /// 关闭插件
    /// </summary>
    Task ShutdownAsync();
}

/// <summary>
/// 歌词提供者插件接口
/// </summary>
public interface ILyricsProviderPlugin : IPlugin
{
    /// <summary>
    /// 获取歌词
    /// </summary>
    Task<LrcLyrics?> GetLyricsAsync(Song song);
    
    /// <summary>
    /// 是否可用
    /// </summary>
    bool IsAvailable { get; }
}

/// <summary>
/// 协议提供者插件接口
/// </summary>
public interface IProtocolProviderPlugin : IPlugin
{
    /// <summary>
    /// 协议名称（如 "SMB", "FTP"）
    /// </summary>
    string ProtocolName { get; }
    
    /// <summary>
    /// 列出文件
    /// </summary>
    Task<List<RemoteFile>> ListFilesAsync(string path);
    
    /// <summary>
    /// 打开文件流
    /// </summary>
    Task<Stream> OpenReadAsync(string filePath);
    
    /// <summary>
    /// 测试连接
    /// </summary>
    Task<bool> TestConnectionAsync(ConnectionProfile profile);
}
