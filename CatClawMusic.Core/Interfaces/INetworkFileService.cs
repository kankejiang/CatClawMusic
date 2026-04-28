namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 网络文件服务接口
/// </summary>
public interface INetworkFileService
{
    /// <summary>
    /// 列出指定路径下的文件
    /// </summary>
    Task<List<RemoteFile>> ListFilesAsync(string path);
    
    /// <summary>
    /// 打开文件流（用于读取）
    /// </summary>
    Task<Stream> OpenReadAsync(string filePath);
    
    /// <summary>
    /// 测试连接
    /// </summary>
    Task<bool> TestConnectionAsync(Models.ConnectionProfile profile);
    
    /// <summary>
    /// 获取文件信息
    /// </summary>
    Task<RemoteFile?> GetFileInfoAsync(string filePath);
}

/// <summary>
/// 远程文件信息
/// </summary>
public class RemoteFile
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public long LastModified { get; set; }
}
