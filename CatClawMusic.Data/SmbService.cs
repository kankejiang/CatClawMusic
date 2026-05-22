using System.Net;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using SMBLibrary;
using SMBLibrary.Client;

namespace CatClawMusic.Data;

public class SmbService : INetworkFileService, IDisposable
{
    private readonly object _lock = new();
    private SMB2Client? _client;
    private ConnectionProfile? _profile;
    private string? _connectedShare;
    private ISMBFileStore? _fileStore;

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
            var port = profile.Port > 0 ? profile.Port : 445;
            var host = profile.Host.Trim();

            bool connected;
            try
            {
                connected = _client.Connect(IPAddress.Parse(host), SMBTransportType.DirectTCPTransport);
            }
            catch
            {
                try
                {
                    var addresses = System.Net.Dns.GetHostAddresses(host);
                    var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    if (ipv4 == null) throw new InvalidOperationException($"无法解析主机 {host}");
                    connected = _client.Connect(ipv4, SMBTransportType.DirectTCPTransport);
                }
                catch
                {
                    throw new InvalidOperationException($"无法连接到 {host}:{port}");
                }
            }

            if (!connected)
                throw new InvalidOperationException($"无法连接到 {host}:{port}");

            var shareName = string.IsNullOrEmpty(profile.ShareName) ? "share" : profile.ShareName.Trim();
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

    public void Configure(ConnectionProfile profile)
    {
        EnsureConnected(profile);
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(ConnectionProfile profile)
    {
        return await Task.Run(() =>
        {
            try
            {
                var tempClient = new SMB2Client();
                var host = profile.Host.Trim();
                var port = profile.Port > 0 ? profile.Port : 445;

                bool connected;
                try
                {
                    connected = tempClient.Connect(IPAddress.Parse(host), SMBTransportType.DirectTCPTransport);
                }
                catch
                {
                    var addresses = System.Net.Dns.GetHostAddresses(host);
                    var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    if (ipv4 == null) return (false, $"无法解析主机 {host}");
                    connected = tempClient.Connect(ipv4, SMBTransportType.DirectTCPTransport);
                }

                if (!connected)
                    return (false, $"无法连接到 {host}:{port}");

                var status = tempClient.Login(
                    string.IsNullOrEmpty(profile.DomainName) ? "" : profile.DomainName,
                    profile.UserName, profile.Password);

                if (status != NTStatus.STATUS_SUCCESS)
                {
                    tempClient.Disconnect();
                    return (false, $"认证失败: {status}");
                }

                var shareName = string.IsNullOrEmpty(profile.ShareName) ? "share" : profile.ShareName.Trim();
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

    public async Task<List<RemoteFile>> ListFilesAsync(string path)
    {
        return await Task.Run(() =>
        {
            var files = new List<RemoteFile>();

            ISMBFileStore? fileStore;
            lock (_lock) { fileStore = _fileStore; }
            if (fileStore == null) return files;

            try
            {
                var normalizedPath = NormalizePath(path);
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
                        }

                        if (status != NTStatus.STATUS_SUCCESS)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SMB] ListFiles 打开目录失败 {path}: {status}");
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

                if ((queryStatus != NTStatus.STATUS_SUCCESS && queryStatus != NTStatus.STATUS_NO_MORE_FILES)
                    || entries == null || entries.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[SMB] ListFiles 查询失败 {path}: {queryStatus}");
                    return files;
                }

                foreach (var entry in entries)
                {
                    if (entry is FileBothDirectoryInformation info)
                    {
                        var name = info.FileName;
                        if (name == "." || name == "..") continue;

                        var isDir = (info.FileAttributes & SMBLibrary.FileAttributes.Directory) != 0;
                        var entryPath = string.IsNullOrEmpty(normalizedPath)
                            ? $"\\{name}"
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
                System.Diagnostics.Debug.WriteLine($"[SMB] ListFiles 异常: {ex.Message}");
            }

            return files;
        });
    }

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

    public async Task<RemoteFile?> GetFileInfoAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            ISMBFileStore? fileStore;
            lock (_lock) { fileStore = _fileStore; }
            if (fileStore == null) return null;

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
                return null;

            List<QueryDirectoryFileInformation>? info;
            NTStatus queryStatus;
            lock (_lock)
            {
                queryStatus = fileStore.QueryDirectory(
                    out info,
                    handle,
                    System.IO.Path.GetFileName(filePath),
                    FileInformationClass.FileBothDirectoryInformation);
                fileStore.CloseFile(handle);
            }

            if (queryStatus != NTStatus.STATUS_SUCCESS || info == null || info.Count == 0)
                return null;

            if (info[0] is FileBothDirectoryInformation fi)
            {
                return new RemoteFile
                {
                    Name = fi.FileName,
                    Path = filePath,
                    IsDirectory = (fi.FileAttributes & SMBLibrary.FileAttributes.Directory) != 0,
                    Size = (long)fi.EndOfFile,
                    LastModified = new DateTimeOffset(fi.LastWriteTime, TimeSpan.Zero).ToUnixTimeSeconds()
                };
            }

            return null;
        });
    }

    public Task<(bool Success, string Message)> UploadFileAsync(string remotePath, byte[] content, string? contentType = null)
    {
        return Task.FromResult((false, "SMB 上传暂不支持"));
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/" || path == "\\" || path == @"\")
            return "";
        var p = path.Replace('/', '\\').TrimStart('\\');
        return p;
    }

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

    public void Dispose()
    {
        lock (_lock) { DisconnectLocked(); }
    }
}
