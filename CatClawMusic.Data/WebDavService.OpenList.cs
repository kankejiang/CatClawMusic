using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Interfaces;

using System.Net.Security;

using System.Text.Json;

namespace CatClawMusic.Data;

/// <summary>WebDAV 文件服务 —— partial 分域文件。</summary>
public partial class WebDavService
{
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
                    RemoteCertificateValidationCallback = WebDavCertPolicy.CreateCertValidationCallback("WebDAV")
                },
                ConnectTimeout = TimeSpan.FromSeconds(15),
                AllowAutoRedirect = false
            })
            { Timeout = TimeSpan.FromSeconds(30) };
        }
        return _openListApiClient;
    }

    /// <summary>将 WebDAV 路径转换为 OpenList 虚拟文件系统路径（去除自动探测的 dav 前缀）</summary>
    private string ToOpenListPath(string webDavPath)
    {
        var cleanPath = webDavPath.TrimStart('/');
        // 如果路径以 _davPrefix 开头，先去掉此前缀
        if (!string.IsNullOrEmpty(_davPrefix))
        {
            var prefixTrimmed = _davPrefix.Trim('/');
            if (cleanPath.StartsWith(prefixTrimmed + "/", StringComparison.OrdinalIgnoreCase))
                cleanPath = cleanPath[prefixTrimmed.Length..].TrimStart('/');
            else if (string.Equals(cleanPath, prefixTrimmed, StringComparison.OrdinalIgnoreCase))
                cleanPath = "";
        }
        // 也兼容用户手动在 basePath 中写了 /dav 前缀的情况
        if (cleanPath.StartsWith("dav/", StringComparison.OrdinalIgnoreCase))
            cleanPath = cleanPath[4..].TrimStart('/');
        else if (string.Equals(cleanPath, "dav", StringComparison.OrdinalIgnoreCase))
            cleanPath = "";

        return string.IsNullOrEmpty(cleanPath) ? "/" : "/" + cleanPath;
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
                Log.Debug("WebDavService", $"[OpenList] 登录失败: {response.StatusCode} - {json}");
                return null;
            }

            using var doc = JsonDocument.Parse(json);
            var code = doc.RootElement.GetProperty("code").GetInt32();
            if (code != 200)
            {
                var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : "unknown";
                Log.Debug("WebDavService", $"[OpenList] 登录失败: code={code}, {msg}");
                return null;
            }

            var token = doc.RootElement.GetProperty("data").GetProperty("token").GetString();
            _openListToken = token;
            Log.Debug("WebDavService", $"[OpenList] 登录成功, token={token?[..Math.Min(20, token?.Length ?? 0)]}...");
            return token;
        }
        catch (Exception ex)
        {
            Log.Debug("WebDavService", $"[OpenList] 登录异常: {ex.Message}");
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

            Log.Debug("WebDavService", $"[OpenList] ListFiles: {openListPath}");
            var response = await OpenListSendAsync(url,
                new StringContent(body, Encoding.UTF8, "application/json"));
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Log.Debug("WebDavService", $"[OpenList] ListFiles 失败: {response.StatusCode}");
                return new List<RemoteFile>();
            }

            using var doc = JsonDocument.Parse(json);
            var code = doc.RootElement.GetProperty("code").GetInt32();
            if (code != 200)
            {
                var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : "";
                Log.Debug("WebDavService", $"[OpenList] ListFiles 错误: code={code}, {msg}");
                return new List<RemoteFile>();
            }

            var data = doc.RootElement.GetProperty("data");
            var files = new List<RemoteFile>();

            if (data.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in content.EnumerateArray())
                {
                    var name = item.GetProperty("name").GetString() ?? "";
                    var isDir = item.GetProperty("is_dir").GetBoolean();
                    var size = item.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                    var modified = item.TryGetProperty("modified", out var m2) ? m2.GetString() ?? "" : "";

                    // openListItemPath 是 OpenList 虚拟路径（从根开始的完整路径），也是 WebDAV 路径（BuildUrl 会自动加 /dav 前缀）
                    var openListItemPath = "/" + name;
                    if (!string.IsNullOrEmpty(openListPath) && openListPath != "/")
                        openListItemPath = openListPath.TrimEnd('/') + "/" + name;

                    files.Add(new RemoteFile
                    {
                        Name = name,
                        Path = openListItemPath,
                        IsDirectory = isDir,
                        Size = size,
                        LastModified = DateTimeOffset.TryParse(modified, out var dt) ? dt.ToUnixTimeSeconds() : 0
                    });
                }
            }

            Log.Debug("WebDavService", $"[OpenList] ListFiles 结果: {files.Count} 个条目 ({path} → {openListPath})");
            return files;
        }
        catch (Exception ex)
        {
            Log.Debug("WebDavService", $"[OpenList] ListFiles 失败: {ex.Message}");
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

        Log.Debug("WebDavService", $"[OpenList] 递归扫描完成: {allFiles.Count} 个文件");
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
            else if (rawPath.Contains('%'))
            {
                // 纯路径也可能是 URL 编码形式（如来自 uri.AbsolutePath 或库中保存的编码路径），
                // OpenList /api/fs/get 只认解码后的虚拟路径，编码路径会返回 object not found
                try { rawPath = Uri.UnescapeDataString(rawPath); } catch { }
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
            Log.Debug("WebDavService", $"[OpenList] GetDownloadUrl: {filePath} → {rawUrl?[..Math.Min(80, rawUrl?.Length ?? 0)]}...");
            return rawUrl;
        }
        catch (Exception ex)
        {
            Log.Debug("WebDavService", $"[OpenList] GetDownloadUrl 失败: {ex.Message}");
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

            // 输入可能是完整 URL（http://user:pass@host/dav/WEBDAV/file）或纯路径（可能为 URL 编码形式）
            var rawPath = filePath;
            if (rawPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                rawPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try { rawPath = Uri.UnescapeDataString(new Uri(rawPath).AbsolutePath); } catch { }
            }
            else if (rawPath.Contains('%'))
            {
                // 纯路径也可能是 URL 编码形式（如来自 uri.AbsolutePath 或库中保存的编码路径），
                // OpenList /api/fs/get 只认解码后的虚拟路径，编码路径会返回 object not found
                try { rawPath = Uri.UnescapeDataString(rawPath); } catch { }
            }
            // /d/ 端点路径（去掉 WebDAV 挂载前缀）
            var openListPath = ToOpenListPath(rawPath);
            var baseUrl = BuildApiBaseUrl();

            // /api/fs/get 使用 OpenList 虚拟路径（和 GetDownloadUrl 一致）
            var getUrl = $"{baseUrl}/api/fs/get";
            var getBody = JsonSerializer.Serialize(new { path = openListPath, password = "" });
            Log.Debug("WebDavService", $"[OpenList] StreamUrl fs/get path: {openListPath[..Math.Min(80, openListPath.Length)]}");
            var getResponse = await OpenListSendAsync(getUrl,
                new StringContent(getBody, Encoding.UTF8, "application/json"));
            var getJson = await getResponse.Content.ReadAsStringAsync();
            Log.Debug("WebDavService", $"[OpenList] StreamUrl fs/get response: {(int)getResponse.StatusCode}, body={getJson[..Math.Min(200, getJson.Length)]}");

            string? sign = null;
            string? rawUrl = null;
            if (getResponse.IsSuccessStatusCode)
            {
                using var getDoc = JsonDocument.Parse(getJson);
                var code = getDoc.RootElement.GetProperty("code").GetInt32();
                var msg = getDoc.RootElement.TryGetProperty("message", out var msgElem) ? msgElem.GetString() : "";
                Log.Debug("WebDavService", $"[OpenList] StreamUrl fs/get code={code}, message={msg}");
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

            // 优先使用 raw_url（CDN 直链）：同一日志中 GetDownloadUrl 已证明该服务器
            // 的 raw_url（EOS 直链）HttpClient 可 206 成功；而 sign 302 链在 ExoPlayer
            // 下曾出现 BAD_HTTP_STATUS（UA 或重定向处理差异）。raw_url 直连 CDN 最稳。
            if (!string.IsNullOrEmpty(rawUrl))
            {
                Log.Debug("WebDavService", $"[OpenList] StreamUrl (raw_url): /d/{openListPath[..Math.Min(40, openListPath.Length)]}");
                return rawUrl;
            }

            // raw_url 不可用：使用 sign（Alist /d/ 端点认证方式）
            if (!string.IsNullOrEmpty(sign))
            {
                var url = $"{baseUrl}/d/{encodedPath}?sign={Uri.EscapeDataString(sign)}";
                Log.Debug("WebDavService", $"[OpenList] StreamUrl (sign): /d/{openListPath[..Math.Min(40, openListPath.Length)]}");
                return url;
            }

            // 最终回退：优先使用 GetDownloadUrl 的 raw_url（直连 CDN、无需认证，
            // 同一份日志里已验证该服务器 fs/get 解码路径时能正常返回 EOS 直链）
            var downloadRaw = await GetOpenListDownloadUrlAsync(filePath);
            if (!string.IsNullOrEmpty(downloadRaw))
            {
                Log.Debug("WebDavService", $"[OpenList] StreamUrl (raw_url fallback): /d/{openListPath[..Math.Min(40, openListPath.Length)]}");
                return downloadRaw;
            }

            // 兜底：/d/ + token
            var fallbackUrl = $"{baseUrl}/d/{encodedPath}?token={_openListToken}";
            Log.Debug("WebDavService", $"[OpenList] StreamUrl (token fallback): /d/{openListPath[..Math.Min(40, openListPath.Length)]}");
            return fallbackUrl;
        }
        catch (Exception ex)
        {
            Log.Debug("WebDavService", $"[OpenList] StreamUrl 构建失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>OpenList CDN 直连下载共享客户端（连接池/TLS 复用；静态持有保证返回的流安全可读）</summary>
    private static readonly HttpClient OpenListDownloadClient = new(new SocketsHttpHandler
    {
        SslOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = WebDavCertPolicy.CreateCertValidationCallback("OpenList")
        },
        ConnectTimeout = TimeSpan.FromSeconds(15),
        AllowAutoRedirect = true
    })
    { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>通过 OpenList raw_url 下载文件（无认证直连 CDN）</summary>
    private async Task<Stream> OpenListDownloadViaRawUrlAsync(string filePath)
    {
        var rawUrl = await GetOpenListDownloadUrlAsync(filePath);
        if (string.IsNullOrEmpty(rawUrl))
            throw new HttpRequestException($"无法获取 OpenList 下载链接: {filePath}");

        var response = await OpenListDownloadClient.GetAsync(rawUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync();
    }

    /// <summary>通过 OpenList raw_url 读取文件指定范围（Range 请求）</summary>
    private async Task<byte[]> OpenListDownloadRangeViaRawUrlAsync(string filePath, long offset, long length)
    {
        var rawUrl = await GetOpenListDownloadUrlAsync(filePath);
        if (string.IsNullOrEmpty(rawUrl))
            return Array.Empty<byte>();

        var request = new HttpRequestMessage(HttpMethod.Get, rawUrl);
        request.Headers.Range = new RangeHeaderValue(offset, offset + length - 1);

        var response = await OpenListDownloadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode) return Array.Empty<byte>();
        return await response.Content.ReadAsByteArrayAsync();
    }

    /// <summary>
    /// 释放 HTTP 客户端资源
}
