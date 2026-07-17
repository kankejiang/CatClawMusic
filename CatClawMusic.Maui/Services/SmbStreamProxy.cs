using System.Net;
using System.Text;
using System.Threading;
using CatClawMusic.Data;
using ConnectionProfile = CatClawMusic.Core.Models.ConnectionProfile;
using ProtocolType = CatClawMusic.Core.Models.ProtocolType;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// SMB 流媒体本地 HTTP 代理服务。
/// 将 smb://user:pass@host/share/path 转换为 http://127.0.0.1:port/...，
/// 使 ExoPlayer 可以通过 HTTP 协议播放 SMB 共享中的音频文件。
/// 支持 HTTP Range 请求，支持拖动进度条。
/// </summary>
public class SmbStreamProxy : IDisposable
{
    private HttpListener? _listener;
    private readonly SmbService _smb;
    private int _port;
    private bool _disposed;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _smbLock = new(1, 1);

    /// <summary>全局静态实例（由 MauiProgram 设置）</summary>
    public static SmbStreamProxy? Current { get; set; }

    /// <summary>代理是否正在运行</summary>
    public bool IsRunning => _listener?.IsListening ?? false;
    /// <summary>代理监听端口</summary>
    public int Port => _port;

    public SmbStreamProxy(SmbService smb)
    {
        _smb = smb;
    }

    /// <summary>
    /// 启动本地 HTTP 代理服务器（在随机可用端口上）。
    /// </summary>
    public void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();

        // 尝试多个端口
        for (int port = 19876; port < 19976; port++)
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                _listener.Start();
                _port = port;
                break;
            }
            catch
            {
                _listener?.Close();
                _listener = null;
            }
        }

        if (_listener == null || !_listener.IsListening)
        {
            Log.Debug("SmbStreamProxy", "[SmbProxy] 无法绑定端口，代理启动失败");
            return;
        }

        Log.Debug("SmbStreamProxy", $"[SmbProxy] 代理已启动: http://127.0.0.1:{_port}/");

        // 开始监听请求
        _ = Task.Run(() => ListenLoop(_cts.Token));
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        var listener = _listener;
        if (listener == null) return;

        try
        {
            while (!ct.IsCancellationRequested && listener.IsListening)
            {
                try
                {
                    var context = await listener.GetContextAsync().WaitAsync(ct);
                    _ = Task.Run(() => HandleRequest(context), ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        Log.Debug("SmbStreamProxy", $"[SmbProxy] Accept error: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// 将 smb:// URL 转换为本地代理 http:// URL。
    /// </summary>
    public string? ToProxyUrl(string smbUrl)
    {
        if (!smbUrl.StartsWith("smb://", StringComparison.OrdinalIgnoreCase)) return null;
        if (!IsRunning) Start();
        if (!IsRunning) return null;

        // smb://user:pass@host/share/path/file.flac → /path/file.flac 被编码
        // 将 smbUrl URL 安全编码后放入路径
        var encoded = WebUtility.UrlEncode(smbUrl);
        return $"http://127.0.0.1:{_port}/smb/{encoded}";
    }

    private async void HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            var rawUrl = request.RawUrl ?? "";
            const string prefix = "/smb/";
            if (!rawUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                response.StatusCode = 404;
                response.Close();
                return;
            }

            var encodedPart = rawUrl.Substring(prefix.Length);
            // 去掉查询字符串
            var qIdx = encodedPart.IndexOf('?');
            if (qIdx >= 0) encodedPart = encodedPart.Substring(0, qIdx);

            var smbUrl = WebUtility.UrlDecode(encodedPart);
            if (string.IsNullOrEmpty(smbUrl) || !smbUrl.StartsWith("smb://", StringComparison.OrdinalIgnoreCase))
            {
                response.StatusCode = 400;
                response.Close();
                return;
            }

            // 解析 smb:// URL 为 ConnectionProfile 和 远程路径
            if (!TryParseSmbUrl(smbUrl, out var profile, out var remotePath))
            {
                response.StatusCode = 400;
                response.Close();
                return;
            }

            // 配置 SMB 连接并获取文件信息
            RemoteFile? fileInfo;
            await _smbLock.WaitAsync();
            try
            {
                _smb.Configure(profile);
                fileInfo = await _smb.GetFileInfoAsync(remotePath);
            }
            finally
            {
                _smbLock.Release();
            }

            bool unknownLength = false;
            long fileSize;
            if (fileInfo != null && fileInfo.Size > 0)
            {
                fileSize = fileInfo.Size;
            }
            else
            {
                // 兜底：GetFileInfoAsync 取不到元数据时，直接探测首字节确认文件存在。
                // 探测成功则按“未知长度”整文件分块传输（不设置 Content-Length，
                // HttpListener 自动使用分块编码），保证 SMB 歌曲仍可播放（仅不支持拖动）。
                await _smbLock.WaitAsync();
                try
                {
                    _smb.Configure(profile);
                    var probe = await _smb.OpenReadRangeAsync(remotePath, 0, 1);
                    if (probe == null || probe.Length == 0)
                    {
                        response.StatusCode = 404;
                        response.Close();
                        return;
                    }
                    unknownLength = true;
                }
                finally
                {
                    _smbLock.Release();
                }
                fileSize = 0;
            }

            // 解析 Range 头
            long start = 0;
            long end = fileSize - 1;
            bool isRangeRequest = false;

            var rangeHeader = request.Headers["Range"];
            if (!unknownLength && !string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                var range = rangeHeader.Substring(6);
                var dashIdx = range.IndexOf('-');
                if (dashIdx > 0)
                {
                    var startStr = range.Substring(0, dashIdx);
                    var endStr = range.Substring(dashIdx + 1);
                    if (long.TryParse(startStr, out var s) && s >= 0 && s < fileSize)
                    {
                        start = s;
                        isRangeRequest = true;
                        if (long.TryParse(endStr, out var e) && e >= start && e < fileSize)
                            end = e;
                    }
                }
            }

            var contentLength = unknownLength ? 0 : (end - start + 1);

            response.StatusCode = isRangeRequest ? 206 : 200;
            response.ContentType = "application/octet-stream";
            response.Headers["Accept-Ranges"] = "bytes";
            if (!unknownLength)
            {
                response.ContentLength64 = contentLength;
                if (isRangeRequest)
                    response.Headers["Content-Range"] = $"bytes {start}-{end}/{fileSize}";
            }

            // 从 SMB 按范围读取并发送
            const int chunkSize = 128 * 1024; // 128KB
            var output = response.OutputStream;
            long bytesRemaining = unknownLength ? long.MaxValue : contentLength;
            long currentOffset = start;

            try
            {
                while (bytesRemaining > 0 && response.OutputStream.CanWrite)
                {
                    var toRead = unknownLength
                        ? chunkSize
                        : (int)Math.Min(chunkSize, bytesRemaining);
                    byte[] chunk;
                    await _smbLock.WaitAsync();
                    try
                    {
                        _smb.Configure(profile);
                        chunk = await _smb.OpenReadRangeAsync(remotePath, currentOffset, toRead);
                    }
                    finally
                    {
                        _smbLock.Release();
                    }

                    if (chunk == null || chunk.Length == 0) break;

                    await output.WriteAsync(chunk, 0, chunk.Length);
                    await output.FlushAsync();
                    currentOffset += chunk.Length;
                    if (!unknownLength) bytesRemaining -= chunk.Length;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("SmbStreamProxy", $"[SmbProxy] Streaming error: {ex.Message}");
            }

            response.Close();
        }
        catch (Exception ex)
        {
            Log.Debug("SmbStreamProxy", $"[SmbProxy] HandleRequest error: {ex.Message}");
            try
            {
                response.StatusCode = 500;
                response.Close();
            }
            catch { }
        }
    }

    /// <summary>
    /// 解析 smb:// URL 为 ConnectionProfile 和 相对共享路径。
    /// 格式: smb://[user[:password]@]host[:port]/shareName/path/to/file
    /// </summary>
    private static bool TryParseSmbUrl(string smbUrl, out ConnectionProfile profile, out string remotePath)
    {
        profile = new ConnectionProfile { Protocol = ProtocolType.SMB, Port = 445 };
        remotePath = "";

        try
        {
            var uri = new Uri(smbUrl);
            profile.Host = uri.Host;
            if (uri.Port > 0 && uri.Port != 445) profile.Port = uri.Port;

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                var userInfo = Uri.UnescapeDataString(uri.UserInfo);
                var colonIdx = userInfo.IndexOf(':');
                if (colonIdx > 0)
                {
                    profile.UserName = userInfo.Substring(0, colonIdx);
                    profile.Password = userInfo.Substring(colonIdx + 1);
                }
                else
                {
                    profile.UserName = userInfo;
                }
            }

            // 路径: /shareName/path/to/file
            // Uri.AbsolutePath 可能对中文等非 ASCII 字符做 percent-encoding，需要解码还原
            var absPath = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
            var firstSlash = absPath.IndexOf('/');
            if (firstSlash < 0)
            {
                // 只有共享名，没有文件路径
                profile.ShareName = absPath;
                remotePath = "\\";
            }
            else
            {
                profile.ShareName = absPath.Substring(0, firstSlash);
                remotePath = absPath.Substring(firstSlash).Replace('/', '\\');
            }

            return !string.IsNullOrEmpty(profile.Host) && !string.IsNullOrEmpty(profile.ShareName);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts?.Cancel(); } catch { }
        try { _cts?.Dispose(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _smbLock.Dispose();
    }
}
