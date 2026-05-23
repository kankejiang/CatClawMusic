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
    /// 读取文件指定范围（Range 请求），用于只下载文件头部提取 Tag
    /// </summary>
    Task<byte[]> OpenReadRangeAsync(string filePath, long offset, long length);
    
    /// <summary>
    /// 测试连接，返回 (成功, 消息)
    /// </summary>
    Task<(bool Success, string Message)> TestConnectionAsync(Models.ConnectionProfile profile);
    
    /// <summary>
    /// 获取文件信息
    /// </summary>
    Task<RemoteFile?> GetFileInfoAsync(string filePath);

    /// <summary>
    /// 上传文件到远程路径（WebDAV PUT）
    /// </summary>
    Task<(bool Success, string Message)> UploadFileAsync(string remotePath, byte[] content, string? contentType = null);

    /// <summary>
    /// 配置连接（初始化 HttpClient），在调用其他方法前使用
    /// </summary>
    void Configure(Models.ConnectionProfile profile);
}

/// <summary>
/// 远程文件信息
/// </summary>
public class RemoteFile
{
    /// <summary>文件名</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>文件路径</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>是否为目录</summary>
    public bool IsDirectory { get; set; }
    /// <summary>文件大小（字节）</summary>
    public long Size { get; set; }
    /// <summary>最后修改时间（Unix 时间戳）</summary>
    public long LastModified { get; set; }
}
