using System.Net;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using SMBLibrary;
using SMBLibrary.Client;

namespace CatClawMusic.Data;

/// <summary>
/// SMB 网络文件服务，提供文件列表、读取和传输功能
/// </summary>
public class SmbService : INetworkFileService, IDisposable
{
    /// <summary>同步锁，保护 SMB 客户端和文件存储的并发访问</summary>
    private readonly object _lock = new();
    /// <summary>SMB2 客户端实例</summary>
    private SMB2Client? _client;
    /// <summary>当前已配置的连接信息</summary>
    private ConnectionProfile? _profile;
    /// <summary>当前已连接的共享名</summary>
    private string? _connectedShare;
    /// <summary>SMB 文件存储句柄，用于文件列举/读取操作</summary>
    private ISMBFileStore? _fileStore;

    /// <summary>
    /// SMBLibrary 的 SMB2Client 仅支持 TCP 445 端口，无法指定其他端口。
    /// 当用户配置了非 445 端口时给出明确提示。
    /// </summary>
    private static string BuildSmbPortHint(ConnectionProfile profile)
    {
        var port = profile.Port > 0 ? profile.Port : 445;
        return port != 445
            ? $"\n\n注意：当前使用的 SMB 库仅支持 445 端口，配置端口 {port} 将被忽略。"
            : "";
    }

    /// <summary>
    /// 尝试连接 SMB 服务器。
    /// 优先使用字符串主机名让 SMBLibrary 内部解析（支持 NetBIOS 和 Failover Cluster）；
    /// 失败后回退到 Dns.GetHostAddresses，依次尝试 IPv6 和 IPv4 地址。
    /// SMB2Client 不支持自定义端口，始终使用 445。
    /// </summary>
    private static bool TryConnectSMB(SMB2Client client, string host, out string? error)
    {
        error = null;
        host = host.Trim();

        // 1) 优先使用字符串主机名连接（SMBLibrary 内部会做 DNS 解析，CSV 故障转移集群需要此方式）
        try
        {
            if (client.Connect(host, SMBTransportType.DirectTCPTransport))
                return true;
            error = $"SMB 服务器 {host}:445 拒绝连接";
        }
        catch (Exception ex)
        {
            error = $"通过主机名连接失败: {ex.Message}";
        }

        // 2) 回退：手动 DNS 解析并尝试所有地址（IPv6 优先，再 IPv4）
        try
        {
            var addresses = System.Net.Dns.GetHostAddresses(host);
            if (addresses == null || addresses.Length == 0)
            {
                error = $"无法解析主机 {host} 到任何 IP 地址";
                return false;
            }

            // 按地址族排序：IPv6 -> IPv4，相同族内按原始顺序
            var ordered = addresses
                .OrderByDescending(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                .ThenByDescending(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .ToList();

            foreach (var addr in ordered)
            {
                try
                {
                    Log.Debug("SmbService", $"[SMB] 尝试连接 {addr} ({addr.AddressFamily})");
                    if (client.Connect(addr, SMBTransportType.DirectTCPTransport))
                    {
                        Log.Debug("SmbService", $"[SMB] 成功连接到 {addr}");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("SmbService", $"[SMB] 连接 {addr} 失败: {ex.Message}");
                }
            }

            var addrList = string.Join(", ", addresses.Select(a => a.ToString()));
            error = $"无法连接到 {host}:445\nDNS 解析结果: {addrList}\n已尝试 {addresses.Length} 个地址，请确认：\n• 该 IP 是否确实运行了 SMB 服务\n• 445 端口是否被防火墙/运营商封锁\n• 若在内网使用，域名是否解析到了正确的内网 IP";
        }
        catch (Exception ex)
        {
            error = $"DNS 解析失败: {ex.Message}";
        }

        return false;
    }

    /// <summary>
    /// 确保 SMB 客户端已按 profile 完成连接和共享挂载。
    /// 若 host/共享名/账号密码未变化，则复用现有连接；否则重新建立连接。
    /// </summary>
    /// <param name="profile">连接配置。</param>
    /// <exception cref="InvalidOperationException">连接、登录或挂载共享失败时抛出。</exception>
    private void EnsureConnected(ConnectionProfile profile)
    {
        lock (_lock)
        {
            if (_client != null && _profile?.Host == profile.Host && _profile?.Port == profile.Port
                && _connectedShare == profile.ShareName
                && _profile?.UserName == profile.UserName
                && _profile?.Password == profile.Password)
                return;

            DisconnectLocked();

            _client = new SMB2Client();
            var host = profile.Host.Trim();

            if (!TryConnectSMB(_client, host, out var connectError))
                throw new InvalidOperationException($"无法连接到 SMB 服务器 {host}:445\n{connectError}{BuildSmbPortHint(profile)}");

            var shareName = string.IsNullOrEmpty(profile.ShareName) ? "" : profile.ShareName.Trim();
            if (string.IsNullOrEmpty(shareName))
                throw new InvalidOperationException("未指定 SMB 共享名，请在连接配置中填写共享名或通过浏览路径选择。");
            var status = _client.Login(string.IsNullOrEmpty(profile.DomainName) ? "" : profile.DomainName,
                profile.UserName, profile.Password);

            if (status != NTStatus.STATUS_SUCCESS)
                throw new InvalidOperationException($"SMB 登录失败: {status}");

            _fileStore = _client.TreeConnect(shareName, out status);
            if (status != NTStatus.STATUS_SUCCESS || _fileStore == null)
                throw new InvalidOperationException($"无法连接共享 '{shareName}': {status}");

            _profile = profile;
            _connectedShare = shareName;
        }
    }

    /// <summary>
    /// 配置并连接到 SMB 服务器
    /// </summary>
    /// <param name="profile">包含主机、端口、凭据和共享名等信息的连接配置</param>
    public void Configure(ConnectionProfile profile)
    {
        EnsureConnected(profile);
    }

    /// <summary>
    /// 列出 SMB 服务器上可用的共享名列表
    /// </summary>
    /// <param name="profile">连接配置</param>
    /// <returns>包含是否成功、共享名列表和消息的元组</returns>
    public async Task<(bool Success, List<string> Shares, string Message)> ListSharesAsync(ConnectionProfile profile)
    {
        return await Task.Run(() =>
        {
            var shares = new List<string>();
            try
            {
                var tempClient = new SMB2Client();
                var host = profile.Host.Trim();

                if (!TryConnectSMB(tempClient, host, out var connectError))
                    return (false, shares, $"无法连接到 SMB 服务器 {host}:445\n{connectError}{BuildSmbPortHint(profile)}");

                var status = tempClient.Login(
                    string.IsNullOrEmpty(profile.DomainName) ? "" : profile.DomainName,
                    profile.UserName, profile.Password);

                if (status != NTStatus.STATUS_SUCCESS)
                {
                    tempClient.Disconnect();
                    return (false, shares, $"认证失败: {status}");
                }

                // 列出共享（登录成功后、TreeConnect 前可调用）
                shares = tempClient.ListShares(out NTStatus listStatus);
                tempClient.Logoff();
                tempClient.Disconnect();

                if (listStatus != NTStatus.STATUS_SUCCESS || shares.Count == 0)
                    return (false, shares, "未能列出共享");

                // 过滤掉管理共享（$ 结尾的共享名）
                shares = shares.Where(s => !s.EndsWith("$")).ToList();

                return (true, shares, $"找到 {shares.Count} 个共享");
            }
            catch (Exception ex)
            {
                return (false, shares, $"列出共享失败: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 测试 SMB 服务器连接是否可用
    /// </summary>
    /// <param name="profile">连接配置</param>
    /// <returns>包含是否成功和消息的元组</returns>
    public async Task<(bool Success, string Message)> TestConnectionAsync(ConnectionProfile profile)
    {
        return await Task.Run(() =>
        {
            try
            {
                var tempClient = new SMB2Client();
                var host = profile.Host.Trim();

                if (!TryConnectSMB(tempClient, host, out var connectError))
                    return (false, $"无法连接到 SMB 服务器 {host}:445\n{connectError}{BuildSmbPortHint(profile)}");

                var status = tempClient.Login(
                    string.IsNullOrEmpty(profile.DomainName) ? "" : profile.DomainName,
                    profile.UserName, profile.Password);

                if (status != NTStatus.STATUS_SUCCESS)
                {
                    tempClient.Disconnect();
                    return (false, $"认证失败: {status}");
                }

                var shareName = string.IsNullOrEmpty(profile.ShareName) ? "" : profile.ShareName.Trim();
                if (string.IsNullOrEmpty(shareName))
                {
                    tempClient.Logoff();
                    tempClient.Disconnect();
                    return (false, "未指定 SMB 共享名，请填写共享名或通过浏览路径选择");
                }
                var fileStore = tempClient.TreeConnect(shareName, out status);
                if (status != NTStatus.STATUS_SUCCESS || fileStore == null)
                {
                    tempClient.Logoff();
                    tempClient.Disconnect();
                    return (false, $"无法连接共享 '{shareName}': {status}");
                }

                fileStore.Disconnect();
                tempClient.Logoff();
                tempClient.Disconnect();
                return (true, "连接成功");
            }
            catch (Exception ex)
            {
                return (false, $"连接失败: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 列出指定路径下的文件和目录
    /// </summary>
    /// <param name="path">SMB 共享中的目录路径</param>
    /// <returns>远程文件信息列表</returns>
    public async Task<List<RemoteFile>> ListFilesAsync(string path)
    {
        return await Task.Run(() =>
        {
            var files = new List<RemoteFile>();

            ISMBFileStore? fileStore;
            lock (_lock) { fileStore = _fileStore; }
            if (fileStore == null)
            {
                Log.Debug("SmbService", "[SMB] ListFiles _fileStore 为空，未连接");
                return files;
            }

            try
            {
                var normalizedPath = NormalizePath(path);
                Log.Debug("SmbService", $"[SMB] ListFiles 开始 path={path}, normalized={normalizedPath}");
                object? queryHandle;

                lock (_lock)
                {
                    var status = fileStore.CreateFile(
                        out queryHandle,
                        out _,
                        normalizedPath,
                        AccessMask.GENERIC_READ,
                        SMBLibrary.FileAttributes.Directory,
                        ShareAccess.Read | ShareAccess.Delete,
                        CreateDisposition.FILE_OPEN,
                        CreateOptions.FILE_DIRECTORY_FILE,
                        null);

                    Log.Debug("SmbService", $"[SMB] CreateFile path='{normalizedPath}' status={status}");

                    if (status != NTStatus.STATUS_SUCCESS)
                    {
                        if (string.IsNullOrEmpty(normalizedPath))
                        {
                            status = fileStore.CreateFile(
                                out queryHandle,
                                out _,
                                @"\",
                                AccessMask.GENERIC_READ,
                                SMBLibrary.FileAttributes.Directory,
                                ShareAccess.Read | ShareAccess.Delete,
                                CreateDisposition.FILE_OPEN,
                                CreateOptions.FILE_DIRECTORY_FILE,
                                null);

                            Log.Debug("SmbService", $"[SMB] CreateFile 回退 path='\\' status={status}");
                        }

                        if (status != NTStatus.STATUS_SUCCESS)
                        {
                            Log.Debug("SmbService", $"[SMB] ListFiles 打开目录失败 {path}: {status}");
                            return files;
                        }
                    }
                }

                List<QueryDirectoryFileInformation>? entries;
                NTStatus queryStatus;
                lock (_lock)
                {
                    queryStatus = fileStore.QueryDirectory(
                        out entries,
                        queryHandle,
                        "*",
                        FileInformationClass.FileBothDirectoryInformation);
                    fileStore.CloseFile(queryHandle);
                }

                Log.Debug("SmbService", $"[SMB] QueryDirectory status={queryStatus}, entries={entries?.Count ?? 0}");

                if ((queryStatus != NTStatus.STATUS_SUCCESS && queryStatus != NTStatus.STATUS_NO_MORE_FILES)
                    || entries == null || entries.Count == 0)
                {
                    Log.Debug("SmbService", $"[SMB] ListFiles 查询结果为空 {path}: {queryStatus}");
                    return files;
                }

                // 诊断：打印前几条和 "." 的 FileAttributes
                for (int i = 0; i < entries.Count && i < 5; i++)
                {
                    if (entries[i] is FileBothDirectoryInformation diag)
                    {
                        Log.Debug("SmbService", $"[SMB] 诊断 entry[{i}]: name='{diag.FileName}' attr={diag.FileAttributes} (0x{(uint)diag.FileAttributes:X8}) isDir={(diag.FileAttributes & SMBLibrary.FileAttributes.Directory) != 0}");
                    }
                }

                foreach (var entry in entries)
                {
                    if (entry is FileBothDirectoryInformation info)
                    {
                        var name = info.FileName;
                        if (name == "." || name == "..") continue;

                        // 有些 Samba 服务器不设置 Directory 标志位（但对 "."/".." 正常）
                        // 因此用 EndOfFile == 0 作为辅助判断
                        var isDir = (info.FileAttributes & SMBLibrary.FileAttributes.Directory) != 0
                                    || (long)info.EndOfFile == 0;
                        var entryPath = string.IsNullOrEmpty(normalizedPath)
                            ? name
                            : $"{normalizedPath}\\{name}";

                        files.Add(new RemoteFile
                        {
                            Name = name,
                            Path = entryPath,
                            IsDirectory = isDir,
                            Size = isDir ? 0 : (long)info.EndOfFile,
                            LastModified = new DateTimeOffset(info.LastWriteTime, TimeSpan.Zero).ToUnixTimeSeconds()
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("SmbService", $"[SMB] ListFiles 异常: {ex.Message}");
            }

            return files;
        });
    }

    /// <summary>
    /// SMB 协议不支持深度 PROPFIND，递归扫描由调用方通过 ListFilesAsync 实现。
    /// 此方法直接返回空列表（接口兼容实现）。
    /// </summary>
    /// <param name="path">起始目录路径。</param>
    /// <param name="serverType">服务器类型（SMB 忽略此参数）。</param>
    /// <returns>空列表。</returns>
    public Task<List<RemoteFile>> ListAllFilesAsync(string path, WebDavServerType serverType = WebDavServerType.Standard)
        => Task.FromResult(new List<RemoteFile>());

    /// <summary>
    /// 以流的方式读取远程文件全部内容
    /// </summary>
    /// <param name="filePath">远程文件路径</param>
    /// <returns>包含文件内容的可读流</returns>
    public async Task<Stream> OpenReadAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            ISMBFileStore? fileStore;
            lock (_lock) { fileStore = _fileStore; }
            if (fileStore == null) throw new InvalidOperationException("SMB 未连接");

            var normalizedPath = NormalizePath(filePath);
            object? handle;
            NTStatus status;
            lock (_lock)
            {
                status = fileStore.CreateFile(
                    out handle,
                    out _,
                    normalizedPath,
                    AccessMask.GENERIC_READ,
                    SMBLibrary.FileAttributes.Normal,
                    ShareAccess.Read,
                    CreateDisposition.FILE_OPEN,
                    CreateOptions.FILE_NON_DIRECTORY_FILE,
                    null);
            }

            if (status != NTStatus.STATUS_SUCCESS)
                throw new InvalidOperationException($"打开文件失败: {status}");

            var ms = new MemoryStream();
            const int bufferSize = 1024 * 64;
            long offset = 0;

            while (true)
            {
                byte[]? data;
                lock (_lock)
                {
                    status = fileStore.ReadFile(out data, handle, offset, bufferSize);
                }
                if (status != NTStatus.STATUS_SUCCESS || data == null || data.Length == 0)
                    break;
                ms.Write(data, 0, data.Length);
                offset += data.Length;
            }

            lock (_lock) { fileStore.CloseFile(handle); }
            ms.Position = 0;
            return (Stream)ms;
        });
    }

    /// <summary>
    /// 读取远程文件指定范围的字节数据
    /// </summary>
    /// <param name="filePath">远程文件路径</param>
    /// <param name="offset">起始偏移量</param>
    /// <param name="length">读取长度</param>
    /// <returns>指定范围的字节数组</returns>
    public async Task<byte[]> OpenReadRangeAsync(string filePath, long offset, long length)
    {
        return await Task.Run(() =>
        {
            ISMBFileStore? fileStore;
            lock (_lock) { fileStore = _fileStore; }
            if (fileStore == null) return Array.Empty<byte>();

            var normalizedPath = NormalizePath(filePath);
            object? handle;
            NTStatus status;
            lock (_lock)
            {
                status = fileStore.CreateFile(
                    out handle,
                    out _,
                    normalizedPath,
                    AccessMask.GENERIC_READ,
                    SMBLibrary.FileAttributes.Normal,
                    ShareAccess.Read,
                    CreateDisposition.FILE_OPEN,
                    CreateOptions.FILE_NON_DIRECTORY_FILE,
                    null);
            }

            if (status != NTStatus.STATUS_SUCCESS)
                return Array.Empty<byte>();

            byte[]? data;
            lock (_lock)
            {
                status = fileStore.ReadFile(out data, handle, offset, (int)length);
                fileStore.CloseFile(handle);
            }

            return status == NTStatus.STATUS_SUCCESS && data != null ? data : Array.Empty<byte>();
        });
    }

    /// <summary>
    /// 获取远程文件的信息
    /// </summary>
    /// <param name="filePath">远程文件路径</param>
    /// <returns>文件信息，失败时返回 null</returns>
    public async Task<RemoteFile?> GetFileInfoAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            ISMBFileStore? fileStore;
            lock (_lock) { fileStore = _fileStore; }
            if (fileStore == null) return null;

            // 通过“打开父目录句柄 + QueryDirectory(文件名)”获取文件信息。
            // 关键：不能对“文件句柄”调用 QueryDirectory —— SMB 协议下这会对文件句柄返回失败，
            // 导致本方法始终返回 null。而 SmbStreamProxy 正是用本方法判断文件存在性与大小，
            // 一旦返回 null 代理就回 404，结果是所有 SMB 歌曲都无法播放。
            var normalizedPath = NormalizePath(filePath);
            var lastSep = normalizedPath.LastIndexOf('\\');
            var parentDir = lastSep < 0 ? "" : normalizedPath.Substring(0, lastSep);
            var fileName = lastSep < 0 ? normalizedPath : normalizedPath.Substring(lastSep + 1);
            if (string.IsNullOrEmpty(fileName)) return null;

            object? dirHandle;
            NTStatus dirStatus;
            lock (_lock)
            {
                dirStatus = fileStore.CreateFile(
                    out dirHandle,
                    out _,
                    parentDir,
                    AccessMask.GENERIC_READ,
                    SMBLibrary.FileAttributes.Directory,
                    ShareAccess.Read | ShareAccess.Delete,
                    CreateDisposition.FILE_OPEN,
                    CreateOptions.FILE_DIRECTORY_FILE,
                    null);
                // 根目录打开失败时回退到 "\"（与 ListFilesAsync 一致）
                if (dirStatus != NTStatus.STATUS_SUCCESS && string.IsNullOrEmpty(parentDir))
                {
                    dirStatus = fileStore.CreateFile(
                        out dirHandle,
                        out _,
                        @"\",
                        AccessMask.GENERIC_READ,
                        SMBLibrary.FileAttributes.Directory,
                        ShareAccess.Read | ShareAccess.Delete,
                        CreateDisposition.FILE_OPEN,
                        CreateOptions.FILE_DIRECTORY_FILE,
                        null);
                }
            }

            if (dirStatus != NTStatus.STATUS_SUCCESS)
                return null;

            List<QueryDirectoryFileInformation>? entries;
            NTStatus queryStatus;
            lock (_lock)
            {
                // 先按精确文件名查询；部分服务器对精确名过滤支持不好时回退到 "*" 再按名匹配
                queryStatus = fileStore.QueryDirectory(
                    out entries,
                    dirHandle,
                    fileName,
                    FileInformationClass.FileBothDirectoryInformation);
                if (queryStatus != NTStatus.STATUS_SUCCESS || entries == null || entries.Count == 0)
                {
                    queryStatus = fileStore.QueryDirectory(
                        out entries,
                        dirHandle,
                        "*",
                        FileInformationClass.FileBothDirectoryInformation);
                }
                fileStore.CloseFile(dirHandle);
            }

            if (queryStatus != NTStatus.STATUS_SUCCESS || entries == null || entries.Count == 0)
                return null;

            FileBothDirectoryInformation? match = null;
            foreach (var e in entries)
            {
                if (e is FileBothDirectoryInformation fi
                    && string.Equals(fi.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    match = fi;
                    break;
                }
            }
            // 精确名查询通常只返回 1 条，回退到 "*" 且无同名匹配时退化为首条
            if (match == null && entries.Count == 1 && entries[0] is FileBothDirectoryInformation fi2)
                match = fi2;

            if (match != null)
            {
                return new RemoteFile
                {
                    Name = match.FileName,
                    Path = filePath,
                    IsDirectory = (match.FileAttributes & SMBLibrary.FileAttributes.Directory) != 0,
                    Size = (long)match.EndOfFile,
                    LastModified = new DateTimeOffset(match.LastWriteTime, TimeSpan.Zero).ToUnixTimeSeconds()
                };
            }

            return null;
        });
    }

    /// <summary>
    /// 上传文件到远程路径（SMB 上传暂不支持）
    /// </summary>
    /// <param name="remotePath">远程目标路径</param>
    /// <param name="content">文件内容</param>
    /// <param name="contentType">MIME 类型</param>
    /// <returns>包含是否成功和消息的元组</returns>
    public Task<(bool Success, string Message)> UploadFileAsync(string remotePath, byte[] content, string? contentType = null)
    {
        return Task.FromResult((false, "SMB 上传暂不支持"));
    }

    /// <summary>
    /// 将路径规范化为 SMB 客户端期望的格式（去除前导反斜杠，正斜杠转反斜杠）。
    /// 根路径返回空字符串，由调用方特殊处理。
    /// </summary>
    /// <param name="path">原始路径。</param>
    /// <returns>规范化后的路径。</returns>
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/" || path == "\\" || path == @"\")
            return "";
        var p = path.Replace('/', '\\').TrimStart('\\');
        return p;
    }

    /// <summary>
    /// 在已持有 _lock 的情况下断开 SMB 连接并释放资源（无锁版本）。
    /// </summary>
    private void DisconnectLocked()
    {
        if (_fileStore != null)
        {
            try { _fileStore.Disconnect(); } catch { }
            _fileStore = null;
        }
        if (_client != null)
        {
            try { _client.Logoff(); } catch { }
            try { _client.Disconnect(); } catch { }
            _client = null;
        }
        _connectedShare = null;
    }

    /// <summary>
    /// 释放 SMB 客户端连接资源
    /// </summary>
    public void Dispose()
    {
        lock (_lock) { DisconnectLocked(); }
    }
}
