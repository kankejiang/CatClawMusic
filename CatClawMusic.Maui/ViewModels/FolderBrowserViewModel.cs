using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 文件系统项展示模型：表示文件夹浏览器中的单个文件或目录（含上级目录项）。
/// </summary>
public partial class FileSystemItem : ObservableObject
{
    /// <summary>显示名称</summary>
    [ObservableProperty]
    private string _name = "";

    /// <summary>完整路径</summary>
    [ObservableProperty]
    private string _fullPath = "";

    /// <summary>是否为目录</summary>
    [ObservableProperty]
    private bool _isDirectory;

    /// <summary>是否为“上级目录”特殊项</summary>
    [ObservableProperty]
    private bool _isParent;

    /// <summary>文件大小（字节），仅对文件有效</summary>
    [ObservableProperty]
    private long _fileSize;

    /// <summary>图标资源名称</summary>
    [ObservableProperty]
    private string _icon = "ic_folder";
}

/// <summary>
/// 文件夹浏览器页 ViewModel：浏览设备本地文件系统，
/// 支持目录导航、上级返回、选择当前目录等交互，主要用于音乐库自定义目录选择。
/// </summary>
public partial class FolderBrowserViewModel : ObservableObject, IQueryAttributable
{
    /// <summary>当前目录下的文件系统项集合</summary>
    [ObservableProperty]
    private ObservableCollection<FileSystemItem> _items = new();

    /// <summary>当前所在路径</summary>
    [ObservableProperty]
    private string _currentPath = "/";

    /// <summary>页面标题</summary>
    [ObservableProperty]
    private string _title = "选择文件夹";

    /// <summary>浏览器模式（"music" 仅显示目录，其他模式同时显示文件）</summary>
    [ObservableProperty]
    private string _mode = "music";

    /// <summary>是否正在加载目录内容</summary>
    [ObservableProperty]
    private bool _isLoading = false;

    /// <summary>是否可以返回上级目录</summary>
    [ObservableProperty]
    private bool _canGoBack = false;

    private readonly Stack<string> _pathHistory = new();

    /// <summary>
    /// 应用查询参数：从导航参数中读取标题与模式，并跳转到根目录。
    /// </summary>
    /// <param name="query">导航查询参数字典</param>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("title", out var title))
            Title = title?.ToString() ?? "选择文件夹";
        if (query.TryGetValue("mode", out var mode))
            Mode = mode?.ToString() ?? "music";
        _ = NavigateToRootAsync();
    }

    /// <summary>导航到根目录：清空路径历史并加载根路径内容</summary>
    [RelayCommand]
    public async Task NavigateToRootAsync()
    {
        _pathHistory.Clear();
#if ANDROID
        var root = Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath ?? "/sdcard";
        CurrentPath = root;
#else
        CurrentPath = "/";
#endif
        await LoadDirectoryAsync(CurrentPath);
    }

    /// <summary>返回上级目录：优先从路径历史弹出，否则取当前路径的父目录</summary>
    [RelayCommand]
    public async Task NavigateUpAsync()
    {
        if (_pathHistory.Count > 0)
        {
            var prev = _pathHistory.Pop();
            CurrentPath = prev;
            await LoadDirectoryAsync(CurrentPath);
            CanGoBack = _pathHistory.Count > 0;
        }
        else
        {
            var parent = Path.GetDirectoryName(CurrentPath);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
            {
                CurrentPath = parent;
                await LoadDirectoryAsync(CurrentPath);
            }
        }
    }

    /// <summary>打开指定项：上级项则返回上级，目录项则进入该目录</summary>
    /// <param name="item">要打开的文件系统项，为空则忽略</param>
    [RelayCommand]
    public async Task OpenItemAsync(FileSystemItem? item)
    {
        if (item == null) return;

        if (item.IsParent)
        {
            await NavigateUpAsync();
            return;
        }

        if (item.IsDirectory)
        {
            _pathHistory.Push(CurrentPath);
            CurrentPath = item.FullPath;
            CanGoBack = true;
            await LoadDirectoryAsync(CurrentPath);
        }
    }

    /// <summary>选择当前目录作为目标：在音乐模式下将路径加入自定义文件夹列表并返回上一页</summary>
    [RelayCommand]
    public async Task SelectCurrentFolderAsync()
    {
        try
        {
            if (Mode == "music")
            {
                LocalMusicSettingsViewModel.AddCustomFolder(CurrentPath);
            }
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FolderBrowser] Select error: {ex}");
        }
    }

    /// <summary>取消选择并返回上一页</summary>
    [RelayCommand]
    public async Task CancelAsync()
    {
        await Shell.Current.GoToAsync("..");
    }

    private async Task LoadDirectoryAsync(string path)
    {
        IsLoading = true;
        Items.Clear();

        try
        {
            await Task.Run(() =>
            {
                var list = new List<FileSystemItem>();

                var parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent) && parent != path)
                {
                    list.Add(new FileSystemItem
                    {
                        Name = ".. (上级目录)",
                        FullPath = parent,
                        IsDirectory = true,
                        IsParent = true,
                        Icon = "ic_arrow_back"
                    });
                }

                try
                {
                    var dirs = Directory.GetDirectories(path)
                        .Where(d =>
                        {
                            try
                            {
                                var dirInfo = new DirectoryInfo(d);
                                return !dirInfo.Attributes.HasFlag(FileAttributes.Hidden) ||
                                       (dirInfo.Name.StartsWith(".") == false);
                            }
                            catch { return false; }
                        })
                        .OrderBy(d => Path.GetFileName(d));

                    foreach (var dir in dirs)
                    {
                        try
                        {
                            var info = new DirectoryInfo(dir);
                            list.Add(new FileSystemItem
                            {
                                Name = info.Name,
                                FullPath = dir,
                                IsDirectory = true,
                                Icon = "ic_folder"
                            });
                        }
                        catch { }
                    }

                    if (Mode != "music")
                    {
                        var files = Directory.GetFiles(path)
                            .Where(f =>
                            {
                                try
                                {
                                    var info = new FileInfo(f);
                                    return !info.Attributes.HasFlag(FileAttributes.Hidden);
                                }
                                catch { return false; }
                            })
                            .OrderBy(f => Path.GetFileName(f));

                        foreach (var file in files)
                        {
                            try
                            {
                                var info = new FileInfo(file);
                                list.Add(new FileSystemItem
                                {
                                    Name = info.Name,
                                    FullPath = file,
                                    IsDirectory = false,
                                    FileSize = info.Length,
                                    Icon = GetFileIcon(info.Extension)
                                });
                            }
                            catch { }
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    System.Diagnostics.Debug.WriteLine($"[FolderBrowser] Access denied: {path}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FolderBrowser] Error listing {path}: {ex}");
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    foreach (var item in list)
                        Items.Add(item);
                });
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FolderBrowser] LoadDirectory error: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string GetFileIcon(string ext)
    {
        return ext.ToLowerInvariant() switch
        {
            ".mp3" or ".flac" or ".wav" or ".m4a" or ".aac" or ".ogg" or ".wma" or ".ape" or ".opus" => "ic_music_note",
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" => "ic_image",
            ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" => "ic_video",
            ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" => "ic_document",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "ic_archive",
            _ => "ic_file"
        };
    }
}
