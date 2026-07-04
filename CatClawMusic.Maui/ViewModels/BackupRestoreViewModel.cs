using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>备份文件信息展示模型，用于在 UI 中呈现单个备份文件。</summary>
public class BackupFileInfo
{
    /// <summary>文件名（含扩展名）</summary>
    public string FileName { get; set; } = "";
    /// <summary>文件完整路径</summary>
    public string FilePath { get; set; } = "";
    /// <summary>展示用的备份日期文本</summary>
    public string DisplayDate { get; set; } = "";
    /// <summary>展示用的文件大小文本</summary>
    public string SizeStr { get; set; } = "";
    /// <summary>文件创建时间</summary>
    public DateTime CreatedDate { get; set; }
}

/// <summary>
/// 备份与恢复页 ViewModel：管理本地备份文件列表，提供创建备份、恢复备份、删除备份以及读取备份信息等能力。
/// </summary>
public partial class BackupRestoreViewModel : ObservableObject
{
    private readonly BackupService _backupService;
    private readonly IDialogService? _dialogService;

    /// <summary>本地备份文件集合</summary>
    [ObservableProperty]
    private ObservableCollection<BackupFileInfo> _backupFiles = new();

    /// <summary>是否正在加载备份列表</summary>
    [ObservableProperty]
    private bool _isLoading = false;

    /// <summary>是否正在创建备份</summary>
    [ObservableProperty]
    private bool _isBackingUp = false;

    /// <summary>是否正在恢复备份</summary>
    [ObservableProperty]
    private bool _isRestoring = false;

    /// <summary>状态文本（用于向用户展示当前操作结果）</summary>
    [ObservableProperty]
    private string _statusText = "";

    /// <summary>操作进度（0.0 - 1.0）</summary>
    [ObservableProperty]
    private double _progress = 0;

    /// <summary>进度提示文本</summary>
    [ObservableProperty]
    private string _progressMessage = "";

    /// <summary>
    /// 初始化 <see cref="BackupRestoreViewModel"/> 实例。
    /// </summary>
    /// <param name="backupService">备份服务</param>
    /// <param name="dialogService">对话框服务，可为空（设计时支持）</param>
    public BackupRestoreViewModel(BackupService backupService, IDialogService? dialogService = null)
    {
        _backupService = backupService;
        _dialogService = dialogService;
    }

    /// <summary>
    /// 加载备份文件列表：扫描备份目录，解析每个备份文件并填充到集合中。
    /// </summary>
    [RelayCommand]
    public async Task LoadBackupFilesAsync()
    {
        IsLoading = true;
        try
        {
            var baseDir = GetBackupDirectory();
            var files = BackupService.ListBackups(baseDir);

            BackupFiles.Clear();
            foreach (var path in files)
            {
                try
                {
                    var fileName = Path.GetFileName(path);
                    var displayDate = ParseBackupDate(fileName);
                    var fi = new FileInfo(path);
                    var sizeStr = FormatSize(fi.Length);

                    BackupFiles.Add(new BackupFileInfo
                    {
                        FileName = fileName,
                        FilePath = path,
                        DisplayDate = displayDate,
                        SizeStr = sizeStr,
                        CreatedDate = fi.CreationTime
                    });
                }
                catch { /* skip corrupt files */ }
            }

            StatusText = BackupFiles.Count > 0 ? $"共 {BackupFiles.Count} 个备份" : "";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BackupVM] LoadBackupFiles failed: {ex}");
            StatusText = "加载失败";
        }
        finally { IsLoading = false; }
    }

    /// <summary>
    /// 创建备份：备份全部数据（数据库 + 封面等），实时反馈进度并刷新列表。
    /// </summary>
    [RelayCommand]
    public async Task CreateBackupAsync()
    {
        if (IsBackingUp) return;
        IsBackingUp = true;
        Progress = 0;
        ProgressMessage = "正在备份...";

        try
        {
            var progress = new Progress<BackupProgress>(p =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Progress = p.Percent / 100.0;
                    ProgressMessage = p.Message;
                });
            });

            var path = await _backupService.BackupAsync(GetBackupDirectory(), BackupItems.All, progress);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                ProgressMessage = $"备份完成: {Path.GetFileName(path)}";
                StatusText = "备份成功";
            });

            await LoadBackupFilesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BackupVM] Backup failed: {ex}");
            StatusText = $"备份失败: {ex.Message}";
        }
        finally
        {
            IsBackingUp = false;
            Progress = 0;
        }
    }

    /// <summary>
    /// 从指定文件恢复备份：还原数据库与封面等数据，实时反馈进度。
    /// </summary>
    /// <param name="file">要恢复的备份文件信息，为空则忽略</param>
    [RelayCommand]
    public async Task RestoreBackupAsync(BackupFileInfo? file)
    {
        if (file == null || IsRestoring) return;
        IsRestoring = true;
        Progress = 0;
        ProgressMessage = "正在恢复...";

        try
        {
            var progress = new Progress<BackupProgress>(p =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Progress = p.Percent / 100.0;
                    ProgressMessage = p.Message;
                });
            });

            await _backupService.RestoreAsync(file.FilePath, BackupItems.All, progress);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                ProgressMessage = "恢复完成";
                StatusText = "恢复成功";
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BackupVM] Restore failed: {ex}");
            StatusText = $"恢复失败: {ex.Message}";
        }
        finally
        {
            IsRestoring = false;
            Progress = 0;
        }
    }

    /// <summary>
    /// 删除备份文件：移除备份本体及其同名 covers 目录，并刷新列表。
    /// </summary>
    /// <param name="file">要删除的备份文件信息，为空则忽略</param>
    [RelayCommand]
    public async Task DeleteBackupAsync(BackupFileInfo? file)
    {
        if (file == null) return;
        try
        {
            if (File.Exists(file.FilePath))
            {
                File.Delete(file.FilePath);
                // 同时删除同名的 covers 目录
                var coversDir = Path.Combine(
                    Path.GetDirectoryName(file.FilePath) ?? "",
                    Path.GetFileNameWithoutExtension(file.FilePath) + "_covers");
                if (Directory.Exists(coversDir))
                    Directory.Delete(coversDir, true);
            }
            await LoadBackupFilesAsync();
            StatusText = "已删除";
        }
        catch (Exception ex)
        {
            StatusText = $"删除失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 读取备份文件信息（用于确认恢复内容）。
    /// </summary>
    /// <param name="filePath">备份文件完整路径</param>
    /// <returns>备份内含的数据摘要；读取失败时返回 null</returns>
    public async Task<BackupData?> ReadBackupInfoAsync(string filePath)
    {
        try { return await BackupService.ReadBackupInfoAsync(filePath); }
        catch { return null; }
    }

    // ──── Helpers ────

    private static string GetBackupDirectory()
    {
        var dir = Path.Combine(FileSystem.AppDataDirectory, "backups");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string ParseBackupDate(string fileName)
    {
        // backup_20250115_143022.zip → 2025-01-15 14:30:22
        if (fileName.StartsWith("backup_") && fileName.Length >= 22)
        {
            var datePart = fileName.Substring(7);
            if (datePart.Length >= 15)
                return $"{datePart.Substring(0, 4)}-{datePart.Substring(4, 2)}-{datePart.Substring(6, 2)} " +
                       $"{datePart.Substring(9, 2)}:{datePart.Substring(11, 2)}:{datePart.Substring(13, 2)}";
        }
        return fileName;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes}B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1}KB";
        return $"{bytes / (1024.0 * 1024):F1}MB";
    }
}
