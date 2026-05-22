using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using System.Xml.Linq;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Data;

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
  <D:allprop/>
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
                return (true, $"连接成功 → {hostInfo}");
            }
            catch (HttpRequestException ex) when ((int?)ex.StatusCode == 401 || (int?)ex.StatusCode == 403)
            {
                return (false, $"认证失败：{hostInfo}，请检查账号和密码");
            }
            catch (HttpRequestException ex)
            {
                var innerMsg = ex.InnerException?.Message ?? ex.Message;
                System.Diagnostics.Debug.WriteLine($"[WebDAV] PROPFIND 失败 basePath: {ex.StatusCode} - {ex.Message}");

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

    public async Task<List<RemoteFile>> ListFilesAsync(string path)
    {
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

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
    }
}
