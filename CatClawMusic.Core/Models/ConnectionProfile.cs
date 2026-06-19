using SQLite;

namespace CatClawMusic.Core.Models;

/// <summary>
/// 网络协议类型
/// </summary>
public enum ProtocolType
{
    WebDAV = 0,
    Navidrome = 1,
    SMB = 2
}

/// <summary>
/// WebDAV 服务器类型（影响 PROPFIND 深度、重定向策略等）
/// </summary>
public enum WebDavServerType
{
    Standard = 0,   // 标准 WebDAV（NAS、Apache、IIS 等）
    OpenList = 1    // OpenList / Alist
}

/// <summary>
/// 连接配置（WebDAV / Navidrome / SMB 等）
/// </summary>
[Table("ConnectionProfiles")]
public class ConnectionProfile
{
    /// <summary>主键，自增</summary>
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>显示名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>协议类型</summary>
    public ProtocolType Protocol { get; set; } = ProtocolType.WebDAV;

    /// <summary>主机地址</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>端口</summary>
    public int Port { get; set; } = 5005;

    /// <summary>用户名</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>密码 / Token</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>基础路径</summary>
    public string BasePath { get; set; } = "/";

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;

    // — Navidrome 专用字段 —

    /// <summary>Subsonic API 版本（Navidrome: 1.16.1）</summary>
    public string ApiVersion { get; set; } = "1.16.1";

    /// <summary>客户端名称（用于 Subsonic 认证）</summary>
    public string ClientName { get; set; } = "CatClawMusic";

    /// <summary>是否使用 HTTPS</summary>
    public bool UseHttps { get; set; }

    // — SMB 专用字段 —

    /// <summary>SMB 域名（可选）</summary>
    public string DomainName { get; set; } = "";

    /// <summary>SMB 共享名</summary>
    public string ShareName { get; set; } = "";

    // — WebDAV 专用字段 —

    /// <summary>WebDAV 服务器类型（0=标准, 1=OpenList/Alist）</summary>
    public int ServerType { get; set; } = 0;

    // — 便捷方法 —

    /// <summary>构建完整 Base URL</summary>
    /// <returns>拼接后的完整 URL 字符串</returns>
    public string GetBaseUrl()
    {
        var scheme = UseHttps ? "https" : "http";
        var path = BasePath.TrimEnd('/');
        if (!string.IsNullOrEmpty(path) && path != "/")
            return $"{scheme}://{Host}:{Port}{path}";
        return $"{scheme}://{Host}:{Port}";
    }
}
