using System.Collections.ObjectModel;
using CatClawMusic.Maui.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 下载管理页 ViewModel：展示下载任务列表（进度/状态/速度），
/// 提供暂停、继续、取消、重试、删除与新建下载等操作。
/// </summary>
public partial class DownloadsViewModel : ObservableObject, IDisposable
{
    private readonly DownloadManager _manager;

    /// <summary>下载任务列表（直接引用 DownloadManager 的任务集合）</summary>
    public ObservableCollection<DownloadTaskItem> Tasks => _manager.Tasks;

    /// <summary>任务总数</summary>
    [ObservableProperty]
    private int _totalCount;

    /// <summary>进行中任务数（排队 + 下载中）</summary>
    [ObservableProperty]
    private int _activeCount;

    /// <summary>已完成任务数</summary>
    [ObservableProperty]
    private int _completedCount;

    /// <summary>当前下载目录</summary>
    [ObservableProperty]
    private string _downloadPath = "";

    /// <summary>是否为空列表</summary>
    public bool IsEmpty => TotalCount == 0;

    public DownloadsViewModel(DownloadManager manager)
    {
        _manager = manager;
        _manager.TasksChanged += OnTasksChanged;
        _manager.TaskUpdated += OnTaskUpdated;
        RefreshStats();
    }

    private void OnTasksChanged() => RefreshStats();
    private void OnTaskUpdated(DownloadTaskItem _) => RefreshStats();

    /// <summary>刷新下载统计（由 DownloadsPage 在 Windows 选择下载目录后调用）</summary>
    public void RefreshStats()
    {
        TotalCount = Tasks.Count;
        ActiveCount = Tasks.Count(t => t.Status is DownloadStatus.Queued or DownloadStatus.Downloading);
        CompletedCount = Tasks.Count(t => t.Status == DownloadStatus.Completed);
        DownloadPath = _manager.DownloadFolderPath;
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>新建 URL 下载任务</summary>
    public void AddUrlDownload(string url, string? fileName = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        _manager.EnqueueUrl(url.Trim(), fileName);
    }

    /// <summary>暂停任务</summary>
    [RelayCommand]
    public void PauseTask(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id)) _manager.Pause(id);
    }

    /// <summary>继续任务</summary>
    [RelayCommand]
    public void ResumeTask(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id)) _manager.Resume(id);
    }

    /// <summary>取消任务</summary>
    [RelayCommand]
    public void CancelTask(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id)) _manager.Cancel(id);
    }

    /// <summary>失败任务重试</summary>
    [RelayCommand]
    public void RetryTask(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id)) _manager.Retry(id);
    }

    /// <summary>删除任务记录（deleteFile=true 时同时删除已下载文件）</summary>
    [RelayCommand]
    public void DeleteTask(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id)) _manager.Delete(id);
    }

    /// <summary>删除任务并删除已下载文件</summary>
    [RelayCommand]
    public void DeleteTaskWithFile(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id)) _manager.Delete(id, deleteFile: true);
    }

    /// <summary>清空已结束的任务（完成/失败/取消）</summary>
    [RelayCommand]
    public void ClearFinished()
    {
        var finished = Tasks.Where(t => t.Status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Canceled).ToList();
        foreach (var t in finished) _manager.Delete(t.Id);
    }

    public void Dispose()
    {
        _manager.TasksChanged -= OnTasksChanged;
        _manager.TaskUpdated -= OnTaskUpdated;
    }
}
