using SQLite;

namespace CatClawMusic.Core.Models;

/// <summary>
/// 网络协议类型
/// </summary>
public enum ProtocolType
{
    WebDAV = 0,
    Navidrome = 1,
    SMB = 2,
    DLNA = 3,
    FTP = 4,
    NFS = 5
}

/// <summary>
/// 连接配置（WebDAV / Navidrome / SMB 等）
/// </summary>
[Table("ConnectionProfiles")]
public class ConnectionProfile
{
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

    /// <summary>基础路径（WebDAV / FTP）</summary>
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

    // — 便捷方法 —

    /// <summary>构建完整 Base URL</summary>
    public string GetBaseUrl()
    {
        var scheme = UseHttps ? "https" : "http";
        var path = BasePath.TrimEnd('/');
        if (!string.IsNullOrEmpty(path) && path != "/")
            return $"{scheme}://{Host}:{Port}{path}";
        return $"{scheme}://{Host}:{Port}";
    }
}
