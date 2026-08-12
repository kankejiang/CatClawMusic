using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Interfaces;

using System.Text.Json;

namespace CatClawMusic.Data;

/// <summary>WebDAV 文件服务 —— partial 分域文件。</summary>
public partial class WebDavService
{
    public async Task<(bool Success, string Message)> TestConnectionAsync(ConnectionProfile profile)
    {
        var hostInfo = $"{profile.Host}:{profile.Port}";
        if (profile.UseHttps) hostInfo = "https://" + hostInfo;

        try
        {
            EnsureClient(profile, forceNew: true);
            _davPrefix = ""; // 重置前缀
            var basePath = profile.BasePath?.Trim() ?? "/";
            if (string.IsNullOrEmpty(basePath)) basePath = "/";

            // ── 第1步：通过 API 快速检测是否为 OpenList ──
            bool isApiOpenList = await IsOpenListByApiAsync(profile);

            // ── 第2步：尝试 PROPFIND 多种组合 ──
            // 构建要尝试的 (prefix, path) 组合列表
            var attempts = new List<(string prefix, string path, string desc)>();

            // (a) 无前缀 + 用户路径
            attempts.Add(("", basePath, $"basePath {basePath}"));
            // (b) 已知前缀 + 用户路径（OpenList/Alist：WebDAV 端点在 /dav 下）
            foreach (var p in new[] { "/dav", "/webdav" })
                attempts.Add((p, basePath, $"{p}{basePath}"));
            // (c) 非根路径时，也尝试无前缀根路径
            if (basePath != "/")
                attempts.Add(("", "/", "/"));
            // (d) 非根路径时，尝试前缀 + 根路径
            if (basePath != "/")
                foreach (var p in new[] { "/dav", "/webdav" })
                    attempts.Add((p, "/", $"{p}/"));

            string? workingPrefix = null;
            string? workingUrl = null;

            foreach (var (prefix, path, desc) in attempts)
            {
                var (ok, url) = await TryPropFindWithPrefixAsync(profile, prefix, path, GetClient());
                if (ok)
                {
                    workingPrefix = prefix;
                    workingUrl = url;
                    Log.Debug("WebDavService", $"[WebDAV] 测试连接 PROPFIND 成功: {desc} → {url}");
                    break;
                }
                Log.Debug("WebDavService", $"[WebDAV] 测试连接 PROPFIND 失败: {desc}");
            }

            if (workingPrefix != null)
            {
                _davPrefix = workingPrefix;
                // 启动后台检测获取完整服务器类型
                _ = Task.Run(async () =>
                {
                    try { CurrentServerType = await DetectServerTypeAsync(profile); }
                    catch { }
                });

                if (!string.IsNullOrEmpty(workingPrefix))
                    return (true, $"连接成功 → {hostInfo}\n检测到 OpenList/Alist，WebDAV 前缀为 {workingPrefix}");
                return (true, $"连接成功 → {hostInfo}");
            }

            // ── 第3步：PROPFIND 全部失败，但 API 检测到 OpenList → 验证 API 可访问 ──
            if (isApiOpenList)
            {
                try
                {
                    _openListToken = null;
                    var apiBaseUrl = BuildApiBaseUrl(profile);
                    var listUrl = $"{apiBaseUrl}/api/fs/list";
                    var openListVirtualPath = ToOpenListPath(basePath);
                    // 确保 _profile 已设置（用于 BuildApiBaseUrl 和 OpenListSendAsync）
                    if (_profile == null) _profile = profile;

                    var body = JsonSerializer.Serialize(new
                    {
                        path = openListVirtualPath,
                        password = "",
                        page = 1,
                        per_page = 1,
                        refresh = false
                    });

                    // 尝试登录
                    var token = await OpenListLoginAsync(profile);
                    if (!string.IsNullOrEmpty(token))
                    {
                        var req = new HttpRequestMessage(HttpMethod.Post, listUrl);
                        req.Headers.Add("Authorization", token);
                        req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                        var resp = await GetOpenListApiClient().SendAsync(req);
                        var json = await resp.Content.ReadAsStringAsync();
                        if (resp.IsSuccessStatusCode)
                        {
                            using var doc = JsonDocument.Parse(json);
                            var code = doc.RootElement.GetProperty("code").GetInt32();
                            if (code == 200)
                            {
                                _davPrefix = "/dav";
                                CurrentServerType = WebDavServerType.OpenList;
                                DetectedServerType = WebDavServerType.OpenList;
                                Log.Debug("WebDavService", $"[WebDAV] PROPFIND 不可用但 REST API 正常，使用 API 模式");
                                return (true, $"连接成功 → {hostInfo}\n检测到 OpenList/Alist，将使用 REST API 模式（PROPFIND 不可用）");
                            }
                            var apiMsg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : "";
                            Log.Debug("WebDavService", $"[WebDAV] OpenList API 返回 code={code}: {apiMsg}");
                            if (code == 403 || apiMsg?.Contains("permission") == true || apiMsg?.Contains("密码") == true)
                                return (false, $"认证失败：{hostInfo}，请检查账号和密码");
                            if (code == 400 || apiMsg?.Contains("not found") == true || apiMsg?.Contains("不存在") == true)
                                return (false, $"路径不存在：{basePath}\nURL: {BuildUrlForProfile(profile, basePath, isDirectory: true)}");
                        }
                    }
                    else
                    {
                        return (false, $"认证失败：{hostInfo}，OpenList 登录不成功，请检查账号和密码");
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("WebDavService", $"[WebDAV] OpenList API 验证失败: {ex.Message}");
                }
            }

            // ── 第4步：区分认证错误（手动跟随重定向以获取最终状态码） ──
            try
            {
                var rootUrl = BuildUrlForProfile(profile, "/", isDirectory: true);
                var currentUrl = rootUrl;
                for (var i = 0; i <= 3; i++)
                {
                    var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), currentUrl);
                    req.Headers.Add("Depth", "0");
                    req.Content = new StringContent(PropFindBody, Encoding.UTF8, "application/xml");
                    var resp = await GetClient().SendAsync(req);
                    var statusCode = (int)resp.StatusCode;

                    // 跟随重定向（域名经反向代理时常见 HTTP→HTTPS、路径规范化等）
                    if (statusCode == 301 || statusCode == 302 || statusCode == 307 || statusCode == 308)
                    {
                        var location = resp.Headers.Location;
                        if (location == null) break;
                        currentUrl = location.IsAbsoluteUri
                            ? location.ToString()
                            : new Uri(new Uri(currentUrl), location).ToString();
                        Log.Debug("WebDavService", $"[WebDAV] 第4步重定向: {statusCode} -> {currentUrl}");
                        continue;
                    }

                    if (statusCode == 401 || statusCode == 403)
                    {
                        // 检查 WWW-Authenticate 头以区分 Basic/Digest 认证
                        var authHeader = resp.Headers.WwwAuthenticate;
                        var authSchemes = authHeader?.Select(a => a.Scheme)?.ToList();
                        var schemesText = authSchemes != null && authSchemes.Count > 0
                            ? string.Join(", ", authSchemes)
                            : "未知";

                        // 域名场景下常见反向代理剥离 Authorization 头
                        var isDomain = !System.Net.IPAddress.TryParse(profile.Host, out _);
                        var hint = isDomain
                            ? "\n\n可能原因：\n• 域名经反向代理（Nginx/Caddy）时可能未转发 Authorization 头\n• 请在反代配置中添加：proxy_set_header Authorization $http_authorization;\n• 或检查域名是否指向了正确的 WebDAV 服务端口"
                            : "";

                        return (false, $"认证失败：{hostInfo}（HTTP {statusCode}）\n服务器要求认证方式：{schemesText}\n请检查账号和密码{hint}");
                    }

                    break;
                }
            }
            catch (HttpRequestException aex) when ((int?)aex.StatusCode == 401 || (int?)aex.StatusCode == 403)
            {
                var isDomain = !System.Net.IPAddress.TryParse(profile.Host, out _);
                var hint = isDomain
                    ? "\n\n提示：使用域名时如果密码正确但仍报此错误，可能是反向代理未转发 Authorization 头"
                    : "";
                return (false, $"认证失败：{hostInfo}，请检查账号和密码{hint}");
            }
            catch { }

            return (false, $"连接失败：{hostInfo}\nURL: {BuildUrlForProfile(profile, basePath, isDirectory: true)}\n服务器不响应 PROPFIND，请确认地址、端口和 WebDAV 路径是否正确\n（OpenList/Alist 用户请确保 WebDAV 功能已开启）");
        }
        catch (HttpRequestException ex)
        {
            var msg = ex.Message;
            if ((int?)ex.StatusCode == 401 || (int?)ex.StatusCode == 403)
            {
                var isDomain = !System.Net.IPAddress.TryParse(profile.Host, out _);
                msg = isDomain
                    ? $"认证失败，请检查账号和密码\n\n提示：使用域名时可能是反向代理未转发 Authorization 头"
                    : "认证失败，请检查账号和密码";
            }
            else if ((int?)ex.StatusCode == 404) msg = $"路径不存在 → {hostInfo}";
            else if (ex.Message.Contains("timeout") || ex.Message.Contains("timed out")) msg = $"连接超时：{hostInfo}";
            else if (ex.Message.Contains("refused")) msg = $"连接被拒绝：{hostInfo}";
            return (false, msg);
        }
        catch (TaskCanceledException)
        {
            return (false, $"连接超时：{hostInfo}，请检查地址和端口");
        }
        catch (Exception ex)
        {
            Log.Debug("WebDavService", $"[WebDAV] 测试异常: {ex}");
            return (false, $"{hostInfo}: {ex.Message}");
        }
    }

    /// <summary>
    /// 列出指定路径下的文件和目录
    /// </summary>
    /// <param name="path">WebDAV 目录路径</param>
}
