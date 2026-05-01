using System.Net;
using System.Net.Http.Headers;
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
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
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

    public async Task<(bool Success, string Message)> TestConnectionAsync(ConnectionProfile profile)
    {
        try
        {
            EnsureClient(profile);
            var url = BuildUrl(profile.BasePath ?? "/");
            var doc = await PropFindAsync(url, 0);
            return (true, "连接成功");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"连接失败: HTTP {(int)(ex.StatusCode ?? 0)} {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return (false, "连接超时，请检查服务器地址和端口");
        }
        catch (Exception ex)
        {
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

            foreach (var resp in doc.Descendants(ns + "response"))
            {
                var href = resp.Element(ns + "href")?.Value ?? "";
                if (href == url + "/" || href == url) continue; // skip self

                var propstat = resp.Element(ns + "propstat");
                var prop = propstat?.Element(ns + "prop");
                if (prop == null) continue;

                var displayName = prop.Element(ns + "displayname")?.Value ?? "";
                var contentType = prop.Element(ns + "getcontenttype")?.Value ?? "";
                var contentLength = prop.Element(ns + "getcontentlength")?.Value ?? "0";
                var lastModified = prop.Element(ns + "getlastmodified")?.Value ?? "";
                var resType = prop.Element(ns + "resourcetype");

                bool isDir = resType?.Element(ns + "collection") != null;

                // 从 href 提取文件名
                var name = !string.IsNullOrEmpty(displayName) ? displayName
                    : href.Split('/').LastOrDefault(s => !string.IsNullOrEmpty(s)) ?? href;

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
