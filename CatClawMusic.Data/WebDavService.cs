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
    /// <summary>
    /// HTTP 客户端实例
    /// </summary>
    private HttpClient? _client;

    /// <summary>
    /// 当前连接配置
    /// </summary>
    private ConnectionProfile? _profile;

    /// <summary>
    /// 获取已配置的 HTTP 客户端，未配置时抛出异常
    /// </summary>
    private HttpClient GetClient()
    {
        if (_client == null || _profile == null)
            throw new InvalidOperationException("WebDAV 未配置连接");
        return _client;
    }

    /// <summary>
    /// 确保 HTTP 客户端已按指定配置初始化，复用已有连接
    /// </summary>
    private void EnsureClient(ConnectionProfile profile, bool forceNew = false)
    {
        if (!forceNew && _client != null && _profile?.Host == profile.Host
            && _profile?.Port == profile.Port
            && _profile?.UserName == profile.UserName
            && _profile?.Password == profile.Password
            && _profile?.UseHttps == profile.UseHttps)
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
    }

    /// <summary>
    /// 根据路径构建完整的请求 URL
    /// </summary>
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
        var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
        request.Headers.Add("Depth", depth.ToString());

        // PROPFIND body - request all basic properties
        var body = @"<?xml version='1.0' encoding='utf-8'?>
<D:propfind xmlns:D='DAV:'>
  <D:allprop/>
</D:propfind>";
        request.Content = new StringContent(body, Encoding.UTF8, "application/xml");

        var response = await GetClient().SendAsync(request);

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        return XDocument.Parse(content);
    }

    /// <summary>
    /// 测试 WebDAV 服务器连接
    /// </summary>
    public async Task<(bool Success, string Message)> TestConnectionAsync(ConnectionProfile profile)
    {
        try
        {
            EnsureClient(profile, forceNew: true);
            var basePath = profile.BasePath?.Trim() ?? "/";
            if (string.IsNullOrEmpty(basePath)) basePath = "/";

            try
            {
                var basePathUrl = BuildUrl(basePath);
                await PropFindAsync(basePathUrl, 0);
                return (true, "连接成功");
            }
            catch (HttpRequestException ex) when ((int?)ex.StatusCode == 401 || (int?)ex.StatusCode == 403)
            {
                return (false, "认证失败，请检查账号和密码");
            }
            catch (HttpRequestException) { }

            if (basePath != "/")
            {
                try
                {
                    var rootUrl = BuildUrl("/");
                    await PropFindAsync(rootUrl, 0);
                    return (false, $"服务器连接成功，但路径 {basePath} 不可访问。请确认路径前缀");
                }
                catch (HttpRequestException ex) when ((int?)ex.StatusCode == 401 || (int?)ex.StatusCode == 403)
                {
                    return (false, "认证失败，请检查账号和密码");
                }
                catch { }
            }

            return (false, "无法连接到 WebDAV 服务器，请检查地址和端口");
        }
        catch (HttpRequestException ex)
        {
            var statusCode = ex.StatusCode.HasValue ? $"{(int)ex.StatusCode} {ex.StatusCode}" : "未知";
            var errorMsg = ex.Message;
            System.Diagnostics.Debug.WriteLine($"[WebDAV] HTTP 错误: {statusCode} - {errorMsg}");

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

    /// <summary>
    /// 列出指定路径下的文件和目录
    /// </summary>
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

            return files;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV] ListFiles 失败: {ex.Message}");
            return new List<RemoteFile>();
        }
    }

    /// <summary>
    /// 使用指定配置初始化 WebDAV 连接
    /// </summary>
    public void Configure(ConnectionProfile profile)
    {
        EnsureClient(profile);
    }

    /// <summary>
    /// 以流的方式打开远程文件读取
    /// </summary>
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

    /// <summary>
    /// 按字节范围读取远程文件的指定片段
    /// </summary>
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

    /// <summary>
    /// 获取远程文件的元数据信息
    /// </summary>
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

    /// <summary>
    /// 释放 HTTP 客户端资源
    /// </summary>
    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
    }
}
