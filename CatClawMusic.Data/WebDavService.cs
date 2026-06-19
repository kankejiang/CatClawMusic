using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Data;

/// <summary>
/// WebDAV 网络文件服务，提供文件列表、读取、上传和连接测试功能
/// </summary>
public class WebDavService : INetworkFileService, IDisposable
{
    private HttpClient? _client;
    private ConnectionProfile? _profile;

    /// <summary>最近一次 TestConnection 检测到的服务器类型</summary>
    public WebDavServerType DetectedServerType { get; private set; } = WebDavServerType.Standard;

    /// <summary>当前配置的服务器类型（Configure 时从 profile 读取）</summary>
    public WebDavServerType CurrentServerType { get; private set; } = WebDavServerType.Standard;

    // ── OpenList / Alist REST API 字段 ──
    private string? _openListToken;
    private HttpClient? _openListApiClient;
    // ── 检测缓存：同一 host 只检测一次 ──
    private string? _lastDetectedHost;

    /// <summary>
    /// 检测 WebDAV 服务器类型（标准 vs OpenList/Alist）
    /// 通过 OPTIONS 或 PROPFIND 响应的 Server 头判断
    /// </summary>
    public async Task<WebDavServerType> DetectServerTypeAsync(ConnectionProfile profile)
    {
        try
        {
            EnsureClient(profile);
            var basePath = profile.BasePath?.Trim() ?? "/";
            if (string.IsNullOrEmpty(basePath)) basePath = "/";
            var url = BuildUrlForProfile(profile, basePath, isDirectory: true);

            // 使用 PROPFIND depth=0 检查 Server 响应头
            var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
            request.Headers.Add("Depth", "0");
            request.Content = new StringContent(PropFindBody, Encoding.UTF8, "application/xml");

            var response = await GetClient().SendAsync(request);

            // 405 Method Not Allowed on root: OpenList/Alist 特征（根路径不支持 PROPFIND）
            if ((int)response.StatusCode == 405 && (basePath == "/" || basePath == ""))
            {
                // 尝试 /dav/ 路径确认
                foreach (var tryPath in new[] { "/dav", "/webdav" })
                {
                    try
                    {
                        var tryUrl = BuildUrlForProfile(profile, tryPath, isDirectory: true);
                        var tryReq = new HttpRequestMessage(new HttpMethod("PROPFIND"), tryUrl);
                        tryReq.Headers.Add("Depth", "0");
                        tryReq.Content = new StringContent(PropFindBody, Encoding.UTF8, "application/xml");
                        var tryResp = await GetClient().SendAsync(tryReq);
                        if (tryResp.IsSuccessStatusCode)
                        {
                            System.Diagnostics.Debug.WriteLine($"[WebDAV] 根路径 405 + {tryPath} 可用 → OpenList");
                            DetectedServerType = WebDavServerType.OpenList;
                            return WebDavServerType.OpenList;
                        }
                    }
                    catch { }
                }
            }

            if (!response.IsSuccessStatusCode)
                return WebDavServerType.Standard;

            // 检查 Server 头
            var serverHeader = response.Headers.Server?.ToString() ?? "";
            System.Diagnostics.Debug.WriteLine($"[WebDAV] Server 头: '{serverHeader}'");
            if (serverHeader.Contains("Alist", StringComparison.OrdinalIgnoreCase) ||
                serverHeader.Contains("OpenList", StringComparison.OrdinalIgnoreCase))
            {
                DetectedServerType = WebDavServerType.OpenList;
                return WebDavServerType.OpenList;
            }

            // 部分 OpenList 实例不在 Server 头中标识，尝试访问 /api/public/settings 判断
            // （OpenList/Alist 特有的 REST API 端点）
            try
            {
                var scheme = profile.UseHttps ? "https" : "http";
                var host = (profile.Host ?? "").TrimEnd('/');
                if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) host = host[7..];
                else if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) host = host[8..];
                var colonIdx = host.LastIndexOf(':');
                if (colonIdx > 0 && int.TryParse(host[(colonIdx + 1)..], out _)) host = host[..colonIdx];
                var port = profile.Port;
                var apiUrl = port == 80 || port == 443
                    ? $"{scheme}://{host}/api/public/settings"
                    : $"{scheme}://{host}:{port}/api/public/settings";

                using var apiClient = new HttpClient(new SocketsHttpHandler
                {
                    SslOptions = new SslClientAuthenticationOptions
                    {
                        RemoteCertificateValidationCallback = (_, _, _, _) => true
                    },
                    ConnectTimeout = TimeSpan.FromSeconds(5)
                })
                { Timeout = TimeSpan.FromSeconds(5) };

                var apiResp = await apiClient.GetAsync(apiUrl);
                if (apiResp.IsSuccessStatusCode)
                {
                    var body = await apiResp.Content.ReadAsStringAsync();
                    if (body.Contains("\"version\"", StringComparison.Ordinal) &&
                        (body.Contains("alist", StringComparison.OrdinalIgnoreCase) ||
                         body.Contains("openlist", StringComparison.OrdinalIgnoreCase)))
                    {
                        DetectedServerType = WebDavServerType.OpenList;
                        return WebDavServerType.OpenList;
                    }
                }
            }
            catch { /* API 检测失败不影响主流程 */ }

            DetectedServerType = WebDavServerType.Standard;
            return WebDavServerType.Standard;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV] 检测服务器类型失败: {ex.Message}");
            return WebDavServerType.Standard;
        }
    }

    private HttpClient GetClient()
    {
        if (_client == null || _profile == null)
            throw new InvalidOperationException("WebDAV 未配置连接");
        return _client;
    }

    private void EnsureClient(ConnectionProfile profile, bool forceNew = false)
    {
        if (!forceNew && _client != null && _profile?.Host == profile.Host
            && _profile?.Port == profile.Port
            && _profile?.UserName == profile.UserName
            && _profile?.Password == profile.Password
            && _profile?.UseHttps == profile.UseHttps)
            return;

        _client?.Dispose();

        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            },
            ConnectTimeout = TimeSpan.FromSeconds(30),
            AllowAutoRedirect = false
        };
        _client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        if (!string.IsNullOrEmpty(profile.UserName))
        {
            var byteArray = Encoding.ASCII.GetBytes($"{profile.UserName}:{profile.Password}");
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
        }

        _profile = profile;
    }

    private string BuildUrl(string path, bool isDirectory = false)
    {
        var profile = _profile ?? throw new InvalidOperationException("未配置连接");
        return BuildUrlForProfile(profile, path, isDirectory);
    }

    private static string BuildUrlForProfile(ConnectionProfile profile, string path, bool isDirectory = false)
    {
        var scheme = profile.UseHttps ? "https" : "http";
        var host = profile.Host.TrimEnd('/');
        if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            host = host[7..];
        else if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            host = host[8..];
        var colonIdx = host.LastIndexOf(':');
        if (colonIdx > 0 && int.TryParse(host[(colonIdx + 1)..], out _))
            host = host[..colonIdx];
        var port = profile.Port;

        var normalizedPath = NormalizeHrefToPath(path).TrimStart('/');
        if (string.IsNullOrEmpty(normalizedPath)) normalizedPath = "";

        var baseUrl = port == 80 || port == 443
            ? $"{scheme}://{host}"
            : $"{scheme}://{host}:{port}";

        var url = $"{baseUrl}/{normalizedPath}";
        if (isDirectory && !url.EndsWith("/"))
            url += "/";
        else if (!isDirectory)
            url = url.TrimEnd('/');

        return url;
    }

    private static string NormalizeHrefToPath(string href)
    {
        if (string.IsNullOrEmpty(href)) return "/";
        if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return new Uri(href).AbsolutePath;
            }
            catch { }
        }
        return href;
    }

    private static HttpClient CreateTestClient(ConnectionProfile profile)
    {
        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            },
            ConnectTimeout = TimeSpan.FromSeconds(30),
            AllowAutoRedirect = false
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        if (!string.IsNullOrEmpty(profile.UserName))
        {
            var byteArray = Encoding.ASCII.GetBytes($"{profile.UserName}:{profile.Password}");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
        }

        return client;
    }

    private const string PropFindBody = @"<?xml version='1.0' encoding='utf-8'?>
<D:propfind xmlns:D='DAV:'>
  <D:prop>
    <D:resourcetype/>
    <D:getcontentlength/>
    <D:getlastmodified/>
    <D:displayname/>
  </D:prop>
</D:propfind>";

    private async Task<XDocument?> PropFindAsync(string url, int depth = 1)
    {
        return await PropFindWithRedirectAsync(GetClient(), url, depth);
    }

    private static async Task<XDocument?> PropFindWithRedirectAsync(HttpClient client, string url, int depth = 1, int maxRedirects = 3)
    {
        var currentUrl = url;
        for (var i = 0; i <= maxRedirects; i++)
        {
            var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), currentUrl);
            request.Headers.Add("Depth", depth.ToString());
            request.Content = new StringContent(PropFindBody, Encoding.UTF8, "application/xml");

            var response = await client.SendAsync(request);
            var statusCode = response.StatusCode;

            if ((int)statusCode == 301 || (int)statusCode == 302 || (int)statusCode == 307 || (int)statusCode == 308)
            {
                var location = response.Headers.Location;
                if (location == null)
                    throw new HttpRequestException($"服务器返回重定向 {(int)statusCode} 但缺少 Location 头");

                currentUrl = location.IsAbsoluteUri
                    ? location.ToString()
                    : new Uri(new Uri(currentUrl), location).ToString();

                System.Diagnostics.Debug.WriteLine($"[WebDAV] PROPFIND 重定向: {statusCode} -> {currentUrl}");
                continue;
            }

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            return XDocument.Parse(content);
        }

        throw new HttpRequestException("重定向次数过多");
    }

    /// <summary>
    /// 测试 WebDAV 服务器连接是否可用
    /// </summary>
    /// <param name="profile">连接配置</param>
    /// <returns>包含是否成功和消息的元组</returns>
    public async Task<(bool Success, string Message)> TestConnectionAsync(ConnectionProfile profile)
    {
        var hostInfo = $"{profile.Host}:{profile.Port}";
        if (profile.UseHttps) hostInfo = "https://" + hostInfo;

        try
        {
            EnsureClient(profile, forceNew: true);
            var basePath = profile.BasePath?.Trim() ?? "/";
            if (string.IsNullOrEmpty(basePath)) basePath = "/";

            var basePathUrl = BuildUrlForProfile(profile, basePath, isDirectory: true);
            System.Diagnostics.Debug.WriteLine($"[WebDAV] 测试连接: {basePathUrl}");

            try
            {
                await PropFindAsync(basePathUrl, 1);
                // 连接成功，尝试自动检测服务器类型
                _ = Task.Run(async () =>
                {
                    try { await DetectServerTypeAsync(profile); }
                    catch { /* 检测失败不影响连接结果 */ }
                });
                return (true, $"连接成功 → {hostInfo}");
            }
            catch (HttpRequestException ex) when ((int?)ex.StatusCode == 401 || (int?)ex.StatusCode == 403)
            {
                return (false, $"认证失败：{hostInfo}，请检查账号和密码");
            }
            catch (HttpRequestException ex)
            {
                var innerMsg = ex.InnerException?.Message ?? ex.Message;
                var statusCode = (int?)ex.StatusCode;
                System.Diagnostics.Debug.WriteLine($"[WebDAV] PROPFIND 失败 basePath: {ex.StatusCode} - {ex.Message}");

                // 405 Method Not Allowed: OpenList/Alist 根路径不支持 PROPFIND，尝试常见 WebDAV 路径
                if (statusCode == 405 && (basePath == "/" || basePath == ""))
                {
                    foreach (var tryPath in new[] { "/dav", "/webdav" })
                    {
                        try
                        {
                            var tryUrl = BuildUrlForProfile(profile, tryPath, isDirectory: true);
                            System.Diagnostics.Debug.WriteLine($"[WebDAV] 405 回退，尝试: {tryUrl}");
                            await PropFindAsync(tryUrl, 1);
                            return (true, $"连接成功 → {hostInfo}\n检测到 OpenList/Alist，WebDAV 路径为 {tryPath}");
                        }
                        catch { /* 继续尝试下一个路径 */ }
                    }
                }

                if (basePath != "/")
                {
                    try
                    {
                        var rootUrl = BuildUrlForProfile(profile, "/", isDirectory: true);
                        System.Diagnostics.Debug.WriteLine($"[WebDAV] 尝试根 {rootUrl}");
                        await PropFindAsync(rootUrl, 1);
                        return (false, $"{hostInfo} 连接成功，但路径 {basePath} 不可访问。\nURL: {basePathUrl}");
                    }
                    catch (HttpRequestException ex2) when ((int?)ex2.StatusCode == 401 || (int?)ex2.StatusCode == 403)
                    {
                        return (false, $"认证失败：{hostInfo}，请检查账号和密码");
                    }
                    catch (Exception ex2)
                    {
                        System.Diagnostics.Debug.WriteLine($"[WebDAV] 根路径也失败: {ex2.Message}");
                    }
                }

                if (innerMsg.Contains("PROPFIND") && innerMsg.Contains("Expected"))
                    innerMsg = "服务器不支持 WebDAV，请确认已启用 WebDAV 服务";
                else if (innerMsg.Contains("SSL") || innerMsg.Contains("certificate") || innerMsg.Contains("TLS"))
                    innerMsg = $"SSL/TLS 连接失败：{innerMsg}";
                else if (innerMsg.Contains("timed out") || innerMsg.Contains("timeout"))
                    innerMsg = "连接超时，请检查服务器地址和端口";
                else if (innerMsg.Contains("refused"))
                    innerMsg = $"连接被拒绝：{hostInfo}，请检查地址和端口";
                else if (innerMsg.Contains("Name or service not known") || innerMsg.Contains("No such host"))
                    innerMsg = $"无法解析主机名：{hostInfo}，请检查地址";

                return (false, $"{hostInfo}\nURL: {basePathUrl}\n{innerMsg}");
            }
        }
        catch (HttpRequestException ex)
        {
            var msg = ex.Message;
            if ((int?)ex.StatusCode == 401 || (int?)ex.StatusCode == 403) msg = "认证失败，请检查账号和密码";
            else if ((int?)ex.StatusCode == 404) msg = $"路径不存在 → {hostInfo}";
            return (false, msg);
        }
        catch (TaskCanceledException)
        {
            return (false, $"连接超时：{hostInfo}，请检查地址和端口");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV] 测试异常: {ex}");
            return (false, $"{hostInfo}: {ex.Message}");
        }
    }

    /// <summary>
    /// 列出指定路径下的文件和目录
    /// </summary>
    /// <param name="path">WebDAV 目录路径</param>
    /// <returns>远程文件信息列表</returns>
    public async Task<List<RemoteFile>> ListFilesAsync(string path)
    {
        // OpenList: 使用 /api/fs/list 替代 PROPFIND（更快、无 405/深度限制）
        if (CurrentServerType == WebDavServerType.OpenList)
        {
            return await OpenListListFilesAsync(path);
        }

        try
        {
            var url = BuildUrl(path, isDirectory: true);
            System.Diagnostics.Debug.WriteLine($"[WebDAV] ListFiles: {url}");
            var doc = await PropFindAsync(url, 1);
            var ns = XNamespace.Get("DAV:");
            var files = new List<RemoteFile>();

            var selfPath = new Uri(url).AbsolutePath.TrimEnd('/');
            if (string.IsNullOrEmpty(selfPath)) selfPath = "/";

            foreach (var resp in doc.Descendants(ns + "response"))
            {
                var href = resp.Element(ns + "href")?.Value ?? "";

                try
                {
                    var hrefPath = (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                    href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        ? new Uri(href).AbsolutePath.TrimEnd('/')
                        : href.TrimEnd('/');
                    if (hrefPath == selfPath) continue;
                }
                catch { }

                var propstat = resp.Element(ns + "propstat");
                var prop = propstat?.Element(ns + "prop");
                if (prop == null) continue;

                var displayName = prop.Element(ns + "displayname")?.Value ?? "";
                var contentLength = prop.Element(ns + "getcontentlength")?.Value ?? "0";
                var lastModified = prop.Element(ns + "getlastmodified")?.Value ?? "";
                var resType = prop.Element(ns + "resourcetype");

                bool isDir = resType?.Element(ns + "collection") != null;

                var rawName = href.Split('/').LastOrDefault(s => !string.IsNullOrEmpty(s)) ?? href;
                var displayFromHref = Uri.UnescapeDataString(rawName);
                var name = !string.IsNullOrEmpty(displayName) ? displayName : displayFromHref;

                var normalizedPath = NormalizeHrefToPath(href);

                files.Add(new RemoteFile
                {
                    Name = name,
                    Path = normalizedPath,
                    IsDirectory = isDir,
                    Size = long.TryParse(contentLength, out var sz) ? sz : 0,
                    LastModified = DateTimeOffset.TryParse(lastModified, out var dt)
                        ? dt.ToUnixTimeSeconds() : 0
                });
            }

            System.Diagnostics.Debug.WriteLine($"[WebDAV] ListFiles 结果: {files.Count} 个条目 ({path})");
            return files;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV] ListFiles 失败: {ex.Message}");
            return new List<RemoteFile>();
        }
    }

    public async Task<List<RemoteFile>> ListAllFilesAsync(string path, WebDavServerType serverType = WebDavServerType.Standard)
    {
        // OpenList/Alist: 使用 /api/fs/list 递归扫描（PROPFIND depth>1 被当作 depth=1）
        if (serverType == WebDavServerType.OpenList)
        {
            System.Diagnostics.Debug.WriteLine("[WebDAV] OpenList 模式：使用 /api/fs/list 递归扫描");
            return await OpenListListAllFilesRecursiveAsync(path);
        }

        try
        {
            var url = BuildUrl(path, isDirectory: true);
            System.Diagnostics.Debug.WriteLine($"[WebDAV] ListAllFiles (depth=infinity): {url}");
            var doc = await PropFindAsync(url, 899);
            var ns = XNamespace.Get("DAV:");
            var files = new List<RemoteFile>();

            foreach (var resp in doc.Descendants(ns + "response"))
            {
                var href = resp.Element(ns + "href")?.Value ?? "";
                var propstat = resp.Element(ns + "propstat");
                var prop = propstat?.Element(ns + "prop");
                if (prop == null) continue;

                var resType = prop.Element(ns + "resourcetype");
                bool isDir = resType?.Element(ns + "collection") != null;
                if (isDir) continue;

                var displayName = prop.Element(ns + "displayname")?.Value ?? "";
                var contentLength = prop.Element(ns + "getcontentlength")?.Value ?? "0";
                var lastModified = prop.Element(ns + "getlastmodified")?.Value ?? "";

                var rawName = href.Split('/').LastOrDefault(s => !string.IsNullOrEmpty(s)) ?? href;
                var displayFromHref = Uri.UnescapeDataString(rawName);
                var name = !string.IsNullOrEmpty(displayName) ? displayName : displayFromHref;

                var normalizedPath = NormalizeHrefToPath(href);

                files.Add(new RemoteFile
                {
                    Name = name,
                    Path = normalizedPath,
                    IsDirectory = false,
                    Size = long.TryParse(contentLength, out var sz) ? sz : 0,
                    LastModified = DateTimeOffset.TryParse(lastModified, out var dt)
                        ? dt.ToUnixTimeSeconds() : 0
                });
            }

            System.Diagnostics.Debug.WriteLine($"[WebDAV] ListAllFiles 结果: {files.Count} 个文件 ({path})");
            return files;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV] ListAllFiles 失败 (将回退到递归扫描): {ex.Message}");

            // 自动检测 OpenList：depth>1 PROPFIND 返回 404/405 是 OpenList 特征
            if (serverType == WebDavServerType.Standard && CurrentServerType == WebDavServerType.Standard)
            {
                var msg = ex.Message;
                if (msg.Contains("404") || msg.Contains("405"))
                {
                    System.Diagnostics.Debug.WriteLine("[WebDAV] depth>1 PROPFIND 返回 404/405 → 自动切换 OpenList 模式");
                    CurrentServerType = WebDavServerType.OpenList;
                    try
                    {
                        return await OpenListListAllFilesRecursiveAsync(path);
                    }
                    catch (Exception apiEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[WebDAV] OpenList 自动检测后扫描失败: {apiEx.Message}");
                        CurrentServerType = WebDavServerType.Standard;
                    }
                }
            }

            return new List<RemoteFile>();
        }
    }

    /// <summary>
    /// 配置并连接到 WebDAV 服务器
    /// </summary>
    /// <param name="profile">连接配置</param>
    public void Configure(ConnectionProfile profile)
    {
        EnsureClient(profile);
        // 同一 host 已检测过则跳过，避免每次操作都触发网络请求
        var hostKey = $"{profile.Host}:{profile.Port}";
        if (_lastDetectedHost == hostKey) return;

        _openListToken = null;
        CurrentServerType = WebDavServerType.Standard;
        _lastDetectedHost = hostKey;
        _ = DetectServerTypeAsync(profile).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
                CurrentServerType = t.Result;
        });
    }

    /// <summary>
    /// 无认证头的 HttpClient，专用于跟随重定向后访问 CDN（CDN 拒绝带有 Basic Auth 的请求）
    /// </summary>
    private HttpClient? _redirectClient;
    private HttpClient GetRedirectClient()
    {
        if (_redirectClient == null)
        {
            _redirectClient = new HttpClient(new SocketsHttpHandler
            {
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (_, _, _, _) => true
                },
                ConnectTimeout = TimeSpan.FromSeconds(30),
                AllowAutoRedirect = false
            })
            { Timeout = TimeSpan.FromSeconds(30) };
        }
        return _redirectClient;
    }

    /// <summary>
    /// 发送 GET 请求并手动跟随 302/307 重定向（重定向后使用无 Auth 的 HttpClient 访问 CDN）
    /// </summary>
    private async Task<HttpResponseMessage> GetWithRedirectAsync(string url, Action<HttpRequestMessage>? customizeRequest = null)
    {
        var client = GetClient();
        var currentUrl = url;
        const int maxRedirects = 5;

        for (var i = 0; i <= maxRedirects; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);

            if (i == 0)
            {
                // 首次请求：应用自定义（Range 等），使用主 HttpClient（带 Auth）
                customizeRequest?.Invoke(request);
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                var code = (int)response.StatusCode;

                if (code == 301 || code == 302 || code == 307 || code == 308)
                {
                    var location = response.Headers.Location;
                    if (location == null)
                        throw new HttpRequestException($"服务器返回重定向 {code} 但缺少 Location 头");

                    currentUrl = location.IsAbsoluteUri
                        ? location.ToString()
                        : new Uri(new Uri(currentUrl), location).ToString();

                    System.Diagnostics.Debug.WriteLine($"[WebDAV] GET 重定向: {code} -> {currentUrl}");
                    response.Dispose();
                    continue;
                }

                return response;
            }
            else
            {
                // 重定向后：使用无 Auth 头的 HttpClient 访问 CDN
                // （OpenList 的 CDN 收到 Basic Auth 头会返回 400）
                if (i > 1)
                    customizeRequest = null; // 仅首次重定向保留 Range 等自定义头

                var redirectClient = GetRedirectClient();
                var redirectResponse = await redirectClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                var redirectCode = (int)redirectResponse.StatusCode;

                if (redirectCode == 301 || redirectCode == 302 || redirectCode == 307 || redirectCode == 308)
                {
                    var location = redirectResponse.Headers.Location;
                    if (location == null)
                        throw new HttpRequestException($"CDN 返回重定向 {redirectCode} 但缺少 Location 头");

                    currentUrl = location.IsAbsoluteUri
                        ? location.ToString()
                        : new Uri(new Uri(currentUrl), location).ToString();

                    System.Diagnostics.Debug.WriteLine($"[WebDAV] CDN 重定向: {redirectCode} -> {currentUrl}");
                    redirectResponse.Dispose();
                    continue;
                }

                return redirectResponse;
            }
        }

        throw new HttpRequestException("重定向次数过多");
    }

    /// <summary>
    /// 以流的方式读取远程文件内容（支持 302 重定向）
    /// </summary>
    /// <param name="filePath">远程文件路径</param>
    /// <returns>包含文件内容的可读流</returns>
    public async Task<Stream> OpenReadAsync(string filePath)
    {
        try
        {
            // OpenList: 通过 REST API 获取 raw_url 直连 CDN，避免 302+Auth→400
            if (CurrentServerType == WebDavServerType.OpenList)
            {
                try
                {
                    return await OpenListDownloadViaRawUrlAsync(filePath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[OpenList] raw_url 下载失败，回退 WebDAV: {ex.Message}");
                }
            }

            var url = BuildUrl(filePath);
            var response = await GetWithRedirectAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStreamAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV] OpenRead 失败: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 读取远程文件指定范围的字节数据（支持 302 重定向）
    /// </summary>
    /// <param name="filePath">远程文件路径</param>
    /// <param name="offset">起始偏移量</param>
    /// <param name="length">读取长度</param>
    /// <returns>指定范围的字节数组</returns>
    public async Task<byte[]> OpenReadRangeAsync(string filePath, long offset, long length)
    {
        try
        {
            // OpenList: 通过 REST API 获取 raw_url + Range 请求，避免 302+Auth→400
            if (CurrentServerType == WebDavServerType.OpenList)
            {
                try
                {
                    var result = await OpenListDownloadRangeViaRawUrlAsync(filePath, offset, length);
                    if (result.Length > 0) return result;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[OpenList] raw_url Range 失败，回退 WebDAV: {ex.Message}");
                }
            }

            var url = BuildUrl(filePath);
            var response = await GetWithRedirectAsync(url, req =>
            {
                req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, offset + length - 1);
            });
            if (!response.IsSuccessStatusCode) return Array.Empty<byte>();
            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV] OpenReadRange 失败: {ex.Message}");
            return Array.Empty<byte>();
        }
    }

    /// <summary>
    /// 获取远程文件的信息
    /// </summary>
    /// <param name="filePath">远程文件路径</param>
    /// <returns>文件信息，失败时返回 null</returns>
    public async Task<RemoteFile?> GetFileInfoAsync(string filePath)
    {
        try
        {
            var url = BuildUrl(filePath);
            var doc = await PropFindAsync(url, 0);
            var ns = XNamespace.Get("DAV:");

            var resp = doc.Descendants(ns + "response").FirstOrDefault();
            var propstat = resp?.Element(ns + "propstat");
            var prop = propstat?.Element(ns + "prop");
            if (prop == null) return null;

            var displayName = prop.Element(ns + "displayname")?.Value ?? "";
            var contentLength = prop.Element(ns + "getcontentlength")?.Value ?? "0";
            var lastModified = prop.Element(ns + "getlastmodified")?.Value ?? "";
            var resType = prop.Element(ns + "resourcetype");

            return new RemoteFile
            {
                Name = !string.IsNullOrEmpty(displayName) ? displayName
                    : filePath.Split('/').LastOrDefault(s => !string.IsNullOrEmpty(s)) ?? filePath,
                Path = filePath,
                IsDirectory = resType?.Element(ns + "collection") != null,
                Size = long.TryParse(contentLength, out var sz) ? sz : 0,
                LastModified = DateTimeOffset.TryParse(lastModified, out var dt)
                    ? dt.ToUnixTimeSeconds() : 0
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV] GetFileInfo 失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 上传文件到远程路径
    /// </summary>
    /// <param name="remotePath">远程目标路径</param>
    /// <param name="content">文件内容</param>
    /// <param name="contentType">MIME 类型</param>
    /// <returns>包含是否成功和消息的元组</returns>
    public async Task<(bool Success, string Message)> UploadFileAsync(string remotePath, byte[] content, string? contentType = null)
    {
        try
        {
            var url = BuildUrl(remotePath);
            var ct = contentType ?? "application/octet-stream";
            var requestContent = new ByteArrayContent(content);
            requestContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(ct);

            var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = requestContent
            };

            var response = await GetClient().SendAsync(request);
            if (response.IsSuccessStatusCode || (int)response.StatusCode == 201 || (int)response.StatusCode == 204)
            {
                return (true, "上传成功");
            }

            var errorMsg = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            return (false, errorMsg);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV] PUT 异常: {ex.Message}");
            return (false, $"上传失败: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════
    //  OpenList / Alist REST API 实现
    //  绕过 WebDAV PROPFIND+GET 的 302 重定向问题
    // ════════════════════════════════════════════════════════════

    /// <summary>构建 OpenList API 基础 URL（scheme://host:port），不含 BasePath</summary>
    private string BuildApiBaseUrl(ConnectionProfile? profile = null)
    {
        var p = profile ?? _profile ?? throw new InvalidOperationException("未配置连接");
        var scheme = p.UseHttps ? "https" : "http";
        var host = (p.Host ?? "").TrimEnd('/');
        if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) host = host[7..];
        else if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) host = host[8..];
        var colonIdx = host.LastIndexOf(':');
        if (colonIdx > 0 && int.TryParse(host[(colonIdx + 1)..], out _)) host = host[..colonIdx];
        var port = p.Port;
        return port == 80 || port == 443 ? $"{scheme}://{host}" : $"{scheme}://{host}:{port}";
    }

    /// <summary>获取无 Basic Auth 的 API HttpClient</summary>
    private HttpClient GetOpenListApiClient()
    {
        if (_openListApiClient == null)
        {
            _openListApiClient = new HttpClient(new SocketsHttpHandler
            {
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (_, _, _, _) => true
                },
                ConnectTimeout = TimeSpan.FromSeconds(15),
                AllowAutoRedirect = false
            })
            { Timeout = TimeSpan.FromSeconds(30) };
        }
        return _openListApiClient;
    }

    /// <summary>将包含 WebDAV 挂载前缀的路径转换为 OpenList 虚拟文件系统路径</summary>
    private string ToOpenListPath(string webDavPath)
    {
        var basePath = (_profile?.BasePath?.TrimEnd('/') ?? "").TrimStart('/');
        if (string.IsNullOrEmpty(basePath)) return webDavPath;

        var cleanPath = webDavPath.TrimStart('/');
        if (cleanPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = cleanPath[basePath.Length..].TrimStart('/');
            return string.IsNullOrEmpty(remainder) ? "/" : "/" + remainder;
        }
        // 路径可能已经是不含前缀的 OpenList 路径
        return webDavPath;
    }

    /// <summary>登录 OpenList REST API，获取 Bearer token</summary>
    public async Task<string?> OpenListLoginAsync(ConnectionProfile? profile = null)
    {
        var p = profile ?? _profile;
        if (p == null) return null;

        var baseUrl = BuildApiBaseUrl(p);
        var url = $"{baseUrl}/api/auth/login";

        var client = GetOpenListApiClient();
        var body = JsonSerializer.Serialize(new
        {
            username = p.UserName ?? "",
            password = p.Password ?? ""
        });

        try
        {
            var response = await client.PostAsync(url,
                new StringContent(body, Encoding.UTF8, "application/json"));
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[OpenList] 登录失败: {response.StatusCode} - {json}");
                return null;
            }

            using var doc = JsonDocument.Parse(json);
            var code = doc.RootElement.GetProperty("code").GetInt32();
            if (code != 200)
            {
                var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : "unknown";
                System.Diagnostics.Debug.WriteLine($"[OpenList] 登录失败: code={code}, {msg}");
                return null;
            }

            var token = doc.RootElement.GetProperty("data").GetProperty("token").GetString();
            _openListToken = token;
            System.Diagnostics.Debug.WriteLine($"[OpenList] 登录成功, token={token?[..Math.Min(20, token?.Length ?? 0)]}...");
            return token;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpenList] 登录异常: {ex.Message}");
            return null;
        }
    }

    /// <summary>发送带自动 token 获取和刷新的 OpenList API 请求</summary>
    private async Task<HttpResponseMessage> OpenListSendAsync(string url, HttpContent? content = null, HttpMethod? method = null)
    {
        var client = GetOpenListApiClient();
        var httpMethod = method ?? HttpMethod.Post;

        // 如果 token 为空，先登录
        if (string.IsNullOrEmpty(_openListToken))
        {
            var token = await OpenListLoginAsync();
            if (string.IsNullOrEmpty(token))
                throw new HttpRequestException("OpenList 认证失败，无法获取 token");
        }

        var request = new HttpRequestMessage(httpMethod, url);
        request.Headers.Add("Authorization", _openListToken);
        if (content != null) request.Content = content;

        var response = await client.SendAsync(request);

        // 如果 token 过期（401），重新登录并重试
        if ((int)response.StatusCode == 401)
        {
            _openListToken = null;
            var token = await OpenListLoginAsync();
            if (string.IsNullOrEmpty(token))
                throw new HttpRequestException("OpenList token 已过期，重新认证失败");

            var retryRequest = new HttpRequestMessage(httpMethod, url);
            retryRequest.Headers.Add("Authorization", _openListToken);
            if (content != null) retryRequest.Content = content;
            return await client.SendAsync(retryRequest);
        }

        return response;
    }

    /// <summary>使用 OpenList /api/fs/list 列出目录</summary>
    public async Task<List<RemoteFile>> OpenListListFilesAsync(string path)
    {
        try
        {
            if (_profile == null) throw new InvalidOperationException("未配置连接");

            var openListPath = ToOpenListPath(path);
            var baseUrl = BuildApiBaseUrl();
            var url = $"{baseUrl}/api/fs/list";

            var body = JsonSerializer.Serialize(new
            {
                path = openListPath,
                password = "",
                page = 1,
                per_page = 0,
                refresh = false
            });

            System.Diagnostics.Debug.WriteLine($"[OpenList] ListFiles: {openListPath}");
            var response = await OpenListSendAsync(url,
                new StringContent(body, Encoding.UTF8, "application/json"));
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[OpenList] ListFiles 失败: {response.StatusCode}");
                return new List<RemoteFile>();
            }

            using var doc = JsonDocument.Parse(json);
            var code = doc.RootElement.GetProperty("code").GetInt32();
            if (code != 200)
            {
                var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : "";
                System.Diagnostics.Debug.WriteLine($"[OpenList] ListFiles 错误: code={code}, {msg}");
                return new List<RemoteFile>();
            }

            var data = doc.RootElement.GetProperty("data");
            var files = new List<RemoteFile>();
            var webDavPrefix = (_profile.BasePath?.TrimEnd('/') ?? "").TrimStart('/');

            if (data.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                // 检测挂载包装目录：在根路径查询时，OpenList 可能返回挂载目录本身作为唯一子条目
                // 例如 BasePath="/dav/WEBDAV"，查询 OpenList "/" 时返回 [{name:"WEBDAV", is_dir:true}]
                // 此时自动跟踪到包装目录内部，返回实际内容（避免路径重复和递归循环）
                if ((string.IsNullOrEmpty(openListPath) || openListPath == "/")
                    && !string.IsNullOrEmpty(webDavPrefix)
                    && content.GetArrayLength() == 1)
                {
                    var firstItem = content[0];
                    var firstName = firstItem.GetProperty("name").GetString() ?? "";
                    var firstIsDir = firstItem.GetProperty("is_dir").GetBoolean();
                    var lastSegment = webDavPrefix.Contains('/')
                        ? webDavPrefix[(webDavPrefix.LastIndexOf('/') + 1)..]
                        : webDavPrefix;

                    if (firstIsDir && string.Equals(firstName, lastSegment, StringComparison.OrdinalIgnoreCase))
                    {
                        System.Diagnostics.Debug.WriteLine($"[OpenList] 检测到挂载包装目录: {firstName}，自动跟踪到内部");
                        var innerBody = JsonSerializer.Serialize(new
                        {
                            path = "/" + firstName,
                            password = "",
                            page = 1,
                            per_page = 0,
                            refresh = false
                        });
                        var innerResponse = await OpenListSendAsync(url,
                            new StringContent(innerBody, Encoding.UTF8, "application/json"));
                        var innerJson = await innerResponse.Content.ReadAsStringAsync();

                        if (innerResponse.IsSuccessStatusCode)
                        {
                            using var innerDoc = JsonDocument.Parse(innerJson);
                            var innerCode = innerDoc.RootElement.GetProperty("code").GetInt32();
                            if (innerCode == 200 && innerDoc.RootElement.TryGetProperty("data", out var innerData)
                                && innerData.TryGetProperty("content", out var innerContent)
                                && innerContent.ValueKind == JsonValueKind.Array)
                            {
                                // 直接在 using 块内构建文件条目并返回（避免 JsonElement 生命周期问题）
                                var innerPath = "/" + firstName;
                                foreach (var item in innerContent.EnumerateArray())
                                {
                                    var name = item.GetProperty("name").GetString() ?? "";
                                    var isDir = item.GetProperty("is_dir").GetBoolean();
                                    var size = item.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                                    var modified = item.TryGetProperty("modified", out var m2) ? m2.GetString() ?? "" : "";

                                    var openListItemPath = innerPath.TrimEnd('/') + "/" + name;
                                    var webDavPath = string.IsNullOrEmpty(webDavPrefix)
                                        ? openListItemPath
                                        : "/" + webDavPrefix + openListItemPath;

                                    files.Add(new RemoteFile
                                    {
                                        Name = name,
                                        Path = webDavPath,
                                        IsDirectory = isDir,
                                        Size = size,
                                        LastModified = DateTimeOffset.TryParse(modified, out var dt) ? dt.ToUnixTimeSeconds() : 0
                                    });
                                }

                                System.Diagnostics.Debug.WriteLine($"[OpenList] ListFiles 结果: {files.Count} 个条目 ({path} → {innerPath}，挂载包装自动跟踪)");
                                return files;
                            }
                        }
                    }
                }

                foreach (var item in content.EnumerateArray())
                {
                    var name = item.GetProperty("name").GetString() ?? "";
                    var isDir = item.GetProperty("is_dir").GetBoolean();
                    var size = item.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                    var modified = item.TryGetProperty("modified", out var m2) ? m2.GetString() ?? "" : "";

                    // 构建 OpenList 条目路径
                    var openListItemPath = "/" + name;
                    if (!string.IsNullOrEmpty(openListPath) && openListPath != "/")
                        openListItemPath = openListPath.TrimEnd('/') + "/" + name;

                    // 转换为完整 WebDAV 路径（含挂载前缀）
                    var webDavPath = string.IsNullOrEmpty(webDavPrefix)
                        ? openListItemPath
                        : "/" + webDavPrefix + openListItemPath;

                    files.Add(new RemoteFile
                    {
                        Name = name,
                        Path = webDavPath,
                        IsDirectory = isDir,
                        Size = size,
                        LastModified = DateTimeOffset.TryParse(modified, out var dt) ? dt.ToUnixTimeSeconds() : 0
                    });
                }
            }

            System.Diagnostics.Debug.WriteLine($"[OpenList] ListFiles 结果: {files.Count} 个条目 ({path} → {openListPath})");
            return files;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpenList] ListFiles 失败: {ex.Message}");
            return new List<RemoteFile>();
        }
    }

    /// <summary>使用 OpenList API 递归扫描所有文件（并发，限深度）</summary>
    private async Task<List<RemoteFile>> OpenListListAllFilesRecursiveAsync(string basePath)
    {
        var allFiles = new List<RemoteFile>();
        var dirQueue = new Queue<string>();
        dirQueue.Enqueue(basePath);

        const int maxDepth = 20;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (dirQueue.Count > 0)
        {
            var currentDir = dirQueue.Dequeue();
            if (!visited.Add(currentDir.TrimEnd('/'))) continue;

            // 深度保护
            var depth = currentDir.TrimStart('/').Split('/').Length -
                        (basePath.TrimStart('/').Split('/').Length - 1);
            if (depth > maxDepth) continue;

            var files = await OpenListListFilesAsync(currentDir);
            foreach (var file in files)
            {
                if (file.IsDirectory)
                {
                    // 防御性检查：跳过自引用目录（路径与当前目录相同）
                    var dirPath = file.Path.TrimEnd('/');
                    var curPath = currentDir.TrimEnd('/');
                    if (!string.Equals(dirPath, curPath, StringComparison.OrdinalIgnoreCase))
                        dirQueue.Enqueue(file.Path);
                }
                else
                    allFiles.Add(file);
            }

            // 每次扫描间隔 50ms，避免请求过快
            if (dirQueue.Count > 0)
                await Task.Delay(50);
        }

        System.Diagnostics.Debug.WriteLine($"[OpenList] 递归扫描完成: {allFiles.Count} 个文件");
        return allFiles;
    }

    /// <summary>获取 OpenList 文件的 raw_url（直接 CDN 下载链接）</summary>
    public async Task<string?> GetOpenListDownloadUrlAsync(string filePath)
    {
        try
        {
            if (_profile == null) throw new InvalidOperationException("未配置连接");

            var rawPath = filePath;
            if (rawPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                rawPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try { rawPath = Uri.UnescapeDataString(new Uri(rawPath).AbsolutePath); } catch { }
            }
            var openListPath = ToOpenListPath(rawPath);
            var baseUrl = BuildApiBaseUrl();
            var url = $"{baseUrl}/api/fs/get";

            var body = JsonSerializer.Serialize(new { path = openListPath, password = "" });
            var response = await OpenListSendAsync(url,
                new StringContent(body, Encoding.UTF8, "application/json"));
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(json);
            var code = doc.RootElement.GetProperty("code").GetInt32();
            if (code != 200) return null;

            var rawUrl = doc.RootElement.GetProperty("data").GetProperty("raw_url").GetString();
            System.Diagnostics.Debug.WriteLine($"[OpenList] GetDownloadUrl: {filePath} → {rawUrl?[..Math.Min(80, rawUrl?.Length ?? 0)]}...");
            return rawUrl;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpenList] GetDownloadUrl 失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 构建 OpenList 播放流 URL（使用 /d/ 端点 + token，绕过 302 重定向链）
    /// ExoPlayer 直接访问 /d/ 端点，Alist 返回 302 到 CDN（URL 无 Auth → CDN 不会 400）
    /// </summary>
    public async Task<string?> GetOpenListStreamUrlAsync(string filePath)
    {
        try
        {
            if (_profile == null) throw new InvalidOperationException("未配置连接");

            // 确保已登录获取 token
            if (string.IsNullOrEmpty(_openListToken))
            {
                var token = await OpenListLoginAsync();
                if (string.IsNullOrEmpty(token)) return null;
            }

            // 输入可能是完整 URL（http://user:pass@host/dav/WEBDAV/file）或纯路径
            var rawPath = filePath;
            if (rawPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                rawPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try { rawPath = Uri.UnescapeDataString(new Uri(rawPath).AbsolutePath); } catch { }
            }
            // /d/ 端点路径（去掉 WebDAV 挂载前缀）
            var openListPath = ToOpenListPath(rawPath);
            var baseUrl = BuildApiBaseUrl();

            // /api/fs/get 使用 OpenList 虚拟路径（和 GetDownloadUrl 一致）
            var getUrl = $"{baseUrl}/api/fs/get";
            var getBody = JsonSerializer.Serialize(new { path = openListPath, password = "" });
            System.Diagnostics.Debug.WriteLine($"[OpenList] StreamUrl fs/get path: {openListPath[..Math.Min(80, openListPath.Length)]}");
            var getResponse = await OpenListSendAsync(getUrl,
                new StringContent(getBody, Encoding.UTF8, "application/json"));
            var getJson = await getResponse.Content.ReadAsStringAsync();
            System.Diagnostics.Debug.WriteLine($"[OpenList] StreamUrl fs/get response: {(int)getResponse.StatusCode}, body={getJson[..Math.Min(200, getJson.Length)]}");

            string? sign = null;
            string? rawUrl = null;
            if (getResponse.IsSuccessStatusCode)
            {
                using var getDoc = JsonDocument.Parse(getJson);
                var code = getDoc.RootElement.GetProperty("code").GetInt32();
                var msg = getDoc.RootElement.TryGetProperty("message", out var msgElem) ? msgElem.GetString() : "";
                System.Diagnostics.Debug.WriteLine($"[OpenList] StreamUrl fs/get code={code}, message={msg}");
                if (code == 200 && getDoc.RootElement.TryGetProperty("data", out var getData))
                {
                    if (getData.TryGetProperty("sign", out var signElem) && signElem.ValueKind == JsonValueKind.String)
                        sign = signElem.GetString();
                    if (getData.TryGetProperty("raw_url", out var rawUrlElem) && rawUrlElem.ValueKind == JsonValueKind.String)
                        rawUrl = rawUrlElem.GetString();
                }
            }

            // 构建 /d/ 端点 URL：路径为 OpenList 虚拟路径
            var encodedPath = string.Join("/",
                openListPath.TrimStart('/').Split('/')
                    .Select(s => Uri.EscapeDataString(Uri.UnescapeDataString(s))));

            // 优先使用 sign（Alist /d/ 端点认证方式）
            if (!string.IsNullOrEmpty(sign))
            {
                var url = $"{baseUrl}/d/{encodedPath}?sign={Uri.EscapeDataString(sign)}";
                System.Diagnostics.Debug.WriteLine($"[OpenList] StreamUrl (sign): /d/{openListPath[..Math.Min(40, openListPath.Length)]}");
                return url;
            }

            // sign 不可用：使用 raw_url（直接 CDN 链接，无需认证）
            if (!string.IsNullOrEmpty(rawUrl))
            {
                System.Diagnostics.Debug.WriteLine($"[OpenList] StreamUrl (raw_url): /d/{openListPath[..Math.Min(40, openListPath.Length)]}");
                return rawUrl;
            }

            // 最终回退：/d/ + token
            var fallbackUrl = $"{baseUrl}/d/{encodedPath}?token={_openListToken}";
            System.Diagnostics.Debug.WriteLine($"[OpenList] StreamUrl (token fallback): /d/{openListPath[..Math.Min(40, openListPath.Length)]}");
            return fallbackUrl;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpenList] StreamUrl 构建失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>通过 OpenList raw_url 下载文件（无认证直连 CDN）</summary>
    private async Task<Stream> OpenListDownloadViaRawUrlAsync(string filePath)
    {
        var rawUrl = await GetOpenListDownloadUrlAsync(filePath);
        if (string.IsNullOrEmpty(rawUrl))
            throw new HttpRequestException($"无法获取 OpenList 下载链接: {filePath}");

        using var client = new HttpClient(new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            },
            ConnectTimeout = TimeSpan.FromSeconds(15),
            AllowAutoRedirect = true
        })
        { Timeout = TimeSpan.FromSeconds(60) };

        var response = await client.GetAsync(rawUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync();
    }

    /// <summary>通过 OpenList raw_url 读取文件指定范围（Range 请求）</summary>
    private async Task<byte[]> OpenListDownloadRangeViaRawUrlAsync(string filePath, long offset, long length)
    {
        var rawUrl = await GetOpenListDownloadUrlAsync(filePath);
        if (string.IsNullOrEmpty(rawUrl))
            return Array.Empty<byte>();

        using var client = new HttpClient(new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            },
            ConnectTimeout = TimeSpan.FromSeconds(15),
            AllowAutoRedirect = true
        })
        { Timeout = TimeSpan.FromSeconds(30) };

        var request = new HttpRequestMessage(HttpMethod.Get, rawUrl);
        request.Headers.Range = new RangeHeaderValue(offset, offset + length - 1);

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode) return Array.Empty<byte>();
        return await response.Content.ReadAsByteArrayAsync();
    }

    /// <summary>
    /// 释放 HTTP 客户端资源
    /// </summary>
    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
        _redirectClient?.Dispose();
        _redirectClient = null;
        _openListApiClient?.Dispose();
        _openListApiClient = null;
        _openListToken = null;
    }
}
