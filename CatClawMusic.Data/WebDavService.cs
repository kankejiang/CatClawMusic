using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using System.Xml.Linq;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Data;

/// <summary>
/// WebDAV 网络文件服务实现（基于 HttpClient + PROPFIND 协议）
/// </summary>
public class WebDavService : INetworkFileService, IDisposable
{
    private HttpClient? _client;
    private ConnectionProfile? _profile;

    private HttpClient GetClient()
    {
        if (_client == null || _profile == null)
            throw new InvalidOperationException("WebDAV 未配置连接");
        return _client;
    }

    private void EnsureClient(ConnectionProfile profile)
    {
        if (_client != null && _profile?.Host == profile.Host && _profile?.Port == profile.Port)
            return;

        _client?.Dispose();

        // 使用 SocketsHttpHandler 而不是 HttpClientHandler
        // 这样可以避免 Android 网络栈对 HTTP 方法的限制
        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            },
            ConnectTimeout = TimeSpan.FromSeconds(30)
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
        System.Diagnostics.Debug.WriteLine($"[WebDAV] 创建 HttpClient: {profile.Host}:{profile.Port}");
    }

    private string BuildUrl(string path)
    {
        var profile = _profile ?? throw new InvalidOperationException("未配置连接");
        var scheme = profile.UseHttps ? "https" : "http";
        var host = profile.Host.TrimEnd('/');
        var port = profile.Port;
        var normalizedPath = (path ?? "/").TrimStart('/');
        if (string.IsNullOrEmpty(normalizedPath)) normalizedPath = "";

        var baseUrl = port == 80 || port == 443
            ? $"{scheme}://{host}"
            : $"{scheme}://{host}:{port}";

        return $"{baseUrl}/{normalizedPath}".TrimEnd('/');
    }

    /// <summary>
    /// 检测服务器是否支持 WebDAV（通过 OPTIONS 请求）
    /// </summary>
    private async Task<(bool Supported, string AllowMethods, string Error)> CheckWebDavSupportAsync(string url)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Options, url);
            var response = await GetClient().SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                return (false, "", $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var allowHeader = "";
            if (response.Headers.TryGetValues("Allow", out var values))
                allowHeader = string.Join(", ", values);

            var supportsWebDav = allowHeader.Contains("PROPFIND", StringComparison.OrdinalIgnoreCase) ||
                                 allowHeader.Contains("MKCOL", StringComparison.OrdinalIgnoreCase) ||
                                 allowHeader.Contains("COPY", StringComparison.OrdinalIgnoreCase);

            System.Diagnostics.Debug.WriteLine($"[WebDAV] OPTIONS 响应: Allow={allowHeader}, WebDAV={supportsWebDav}");

            return (supportsWebDav, allowHeader, "");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV] OPTIONS 请求失败: {ex.Message}");
            return (false, "", ex.Message);
        }
    }

    /// <summary>
    /// 发送 WebDAV PROPFIND 请求
    /// </summary>
    private async Task<XDocument?> PropFindAsync(string url, int depth = 1)
    {
        System.Diagnostics.Debug.WriteLine($"[WebDAV] PROPFIND 请求: {url}, Depth={depth}");

        var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
        request.Headers.Add("Depth", depth.ToString());

        // PROPFIND body - request all basic properties
        var body = @"<?xml version='1.0' encoding='utf-8'?>
<D:propfind xmlns:D='DAV:'>
  <D:allprop/>
</D:propfind>";
        request.Content = new StringContent(body, Encoding.UTF8, "application/xml");

        var response = await GetClient().SendAsync(request);
        System.Diagnostics.Debug.WriteLine($"[WebDAV] PROPFIND 响应: {(int)response.StatusCode} {response.ReasonPhrase}");

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        return XDocument.Parse(content);
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(ConnectionProfile profile)
    {
        try
        {
            EnsureClient(profile);
            var url = BuildUrl(profile.BasePath ?? "/");

            System.Diagnostics.Debug.WriteLine($"[WebDAV] 测试连接: {url}");

            // 直接尝试 PROPFIND（跳过 OPTIONS，避免 Android 上 Allow 头读取问题）
            await PropFindAsync(url, 0);
            return (true, "连接成功");
        }
        catch (HttpRequestException ex)
        {
            var statusCode = ex.StatusCode.HasValue ? $"{(int)ex.StatusCode} {ex.StatusCode}" : "未知";
            var errorMsg = ex.Message;
            System.Diagnostics.Debug.WriteLine($"[WebDAV] HTTP 错误: {statusCode} - {errorMsg}");

            // 特殊处理：HTTP 方法不支持
            if (errorMsg.Contains("PROPFIND") && errorMsg.Contains("Expected"))
                errorMsg = "服务器不支持 WebDAV。请确认已在 NAS 设置中启用 WebDAV 服务。";
            else if ((int?)ex.StatusCode == 401 || (int?)ex.StatusCode == 403)
                errorMsg = "认证失败，请检查账号和密码";
            else if ((int?)ex.StatusCode == 404)
                errorMsg = "路径不存在，请确认路径前缀（如 /webdav/ 或 /dav/）";

            return (false, $"{errorMsg}");
        }
        catch (TaskCanceledException)
        {
            return (false, "连接超时，请检查服务器地址和端口");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV] 连接异常: {ex}");
            return (false, $"连接失败: {ex.Message}");
        }
    }

    public async Task<List<RemoteFile>> ListFilesAsync(string path)
    {
        try
        {
            var url = BuildUrl(path);
            var doc = await PropFindAsync(url, 1);
            var ns = XNamespace.Get("DAV:");
            var files = new List<RemoteFile>();

            // 提取 URL 的路径部分用于 self-skip 比较
            var selfPath = new Uri(url).AbsolutePath.TrimEnd('/');
            if (string.IsNullOrEmpty(selfPath)) selfPath = "/";

            foreach (var resp in doc.Descendants(ns + "response"))
            {
                var href = resp.Element(ns + "href")?.Value ?? "";

                // 跳过自身（兼容相对路径和绝对路径）
                try
                {
                    var hrefPath = (href.StartsWith("http://") || href.StartsWith("https://"))
                        ? new Uri(href).AbsolutePath.TrimEnd('/')
                        : href.TrimEnd('/');
                    if (hrefPath == selfPath) continue;
                }
                catch { /* href 解析失败，跳过 self-skip */ }

                var propstat = resp.Element(ns + "propstat");
                var prop = propstat?.Element(ns + "prop");
                if (prop == null) continue;

                var displayName = prop.Element(ns + "displayname")?.Value ?? "";
                var contentLength = prop.Element(ns + "getcontentlength")?.Value ?? "0";
                var lastModified = prop.Element(ns + "getlastmodified")?.Value ?? "";
                var resType = prop.Element(ns + "resourcetype");

                bool isDir = resType?.Element(ns + "collection") != null;

                // 从 href 提取文件名（需要 URL 解码，因为 href 中的中文是编码后的）
                var rawName = href.Split('/').LastOrDefault(s => !string.IsNullOrEmpty(s)) ?? href;
                var name = !string.IsNullOrEmpty(displayName) ? displayName : Uri.UnescapeDataString(rawName);

                files.Add(new RemoteFile
                {
                    Name = name,
                    Path = href,
                    IsDirectory = isDir,
                    Size = long.TryParse(contentLength, out var sz) ? sz : 0,
                    LastModified = DateTimeOffset.TryParse(lastModified, out var dt)
                        ? dt.ToUnixTimeSeconds() : 0
                });
            }

            System.Diagnostics.Debug.WriteLine($"[WebDAV] ListFiles({path}) 返回 {files.Count} 个项目 (Dirs={files.Count(f => f.IsDirectory)}, Files={files.Count(f => !f.IsDirectory)})");
            return files;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV] ListFiles 失败: {ex.Message}");
            return new List<RemoteFile>();
        }
    }

    public void Configure(ConnectionProfile profile)
    {
        EnsureClient(profile);
    }

    public async Task<Stream> OpenReadAsync(string filePath)
    {
        try
        {
            var url = BuildUrl(filePath);
            var response = await GetClient().GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStreamAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV] OpenRead 失败: {ex.Message}");
            throw;
        }
    }

    public async Task<byte[]> OpenReadRangeAsync(string filePath, long offset, long length)
    {
        try
        {
            var url = BuildUrl(filePath);
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, offset + length - 1);
            var response = await GetClient().SendAsync(request);
            if (!response.IsSuccessStatusCode) return Array.Empty<byte>();
            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV] OpenReadRange 失败: {ex.Message}");
            return Array.Empty<byte>();
        }
    }

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

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
    }
}
