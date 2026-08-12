using System.Net.Security;

using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Data;

/// <summary>
/// 统一 TLS 证书策略（全局开关 + 回调工厂），供项目所有 HttpClient 使用。
/// 独立于 WebDavService 单放一个类：WebDavService、NetworkMusicService、歌词流打开器等
/// 多处共享同一策略入口，避免各持一份互相漂移。
/// </summary>
public static class WebDavCertPolicy
{
    /// <summary>
    /// 是否信任所有 TLS 证书（默认 true，保持局域网/自签 NAS 的可用性）。
    /// 可由上层（如设置页）设为 false 启用严格证书校验。
    /// </summary>
    public static bool TrustAllCertificates { get; set; } = true;

    /// <summary>
    /// 创建统一的 TLS 证书校验回调：有效证书直接通过；无效证书按 <see cref="TrustAllCertificates"/>
    /// 决定接受（记录中间人风险告警）或拒绝（严格模式）。项目内所有 HttpClient 的证书策略统一走此入口。
    /// </summary>
    /// <param name="host">服务器主机名，仅用于日志定位。</param>
    public static RemoteCertificateValidationCallback CreateCertValidationCallback(string host)
    {
        return (_, _, _, sslErrors) =>
        {
            if (sslErrors == SslPolicyErrors.None)
                return true;
            if (!TrustAllCertificates)
            {
                Log.Warn("WebDavCertPolicy", $"[WebDAV] 严格校验：拒绝服务器 {host} 的无效 TLS 证书（{sslErrors}）。");
                return false;
            }
            Log.Warn("WebDavCertPolicy",
                $"[WebDAV] 已接受服务器 {host} 的无效 TLS 证书（{sslErrors}），存在中间人攻击风险。" +
                $"建议配置可信证书；如需强制校验请关闭“忽略证书错误”。");
            return true;
        };
    }
}
