namespace CatClawMusic.Core.Models;

/// <summary>
/// 连接配置（WebDAV 等）
/// </summary>
public class ConnectionProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Protocol { get; set; } = "WebDAV";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 5005;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string BasePath { get; set; } = "/";
    public bool IsEnabled { get; set; } = true;
}
