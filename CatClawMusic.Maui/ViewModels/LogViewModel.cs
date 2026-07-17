using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CatClawMusic.Maui.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>日志级别</summary>
public enum LogLevel { Debug, Info, Warn, Error }

/// <summary>单条日志条目（对应原型 .log 元素）</summary>
public partial class LogEntryViewModel : ObservableObject
{
    /// <summary>时间戳 HH:mm:ss.fff</summary>
    [ObservableProperty] private string _timestamp = "";
    /// <summary>级别（Info/Warn/Error → i/w/e，用于配色）</summary>
    [ObservableProperty] private LogLevel _level;
    /// <summary>级别简称（d/i/w/e，供 XAML 配色用）</summary>
    public string LevelCode => Level switch { LogLevel.Error => "e", LogLevel.Warn => "w", LogLevel.Info => "i", _ => "d" };
    /// <summary>级别标签文本（DEBUG/INFO/WARN/ERROR）</summary>
    public string LevelText => Level switch { LogLevel.Error => "ERROR", LogLevel.Warn => "WARN", LogLevel.Info => "INFO", _ => "DEBUG" };
    /// <summary>模块标签</summary>
    [ObservableProperty] private string _tag = "";
    /// <summary>日志消息</summary>
    [ObservableProperty] private string _message = "";

    partial void OnLevelChanged(LogLevel value) { OnPropertyChanged(nameof(LevelCode)); OnPropertyChanged(nameof(LevelText)); }
}

/// <summary>
/// 诊断日志页 ViewModel（对应 docs/log-page-prototype.html）。
/// 读取 debug.log 文件，解析为结构化条目，支持按级别/标签筛选和搜索。
/// </summary>
public partial class LogViewModel : ObservableObject
{
    /// <summary>日志文件路径（与 LogService 一致）</summary>
    private readonly string _logFilePath;
    /// <summary>所有已加载的原始日志条目（筛选前全集）</summary>
    private List<LogEntryViewModel> _allEntries = new();
    /// <summary>上次选中的级别筛选（all/i/w/e）</summary>
    private string _levelFilter = "all";
    /// <summary>上次选中的标签筛选（all/具体标签）</summary>
    private string _tagFilter = "all";
    /// <summary>上次输入的搜索词</summary>
    private string _searchQuery = "";

    /// <summary>筛选后的日志条目（绑定到 CollectionView）</summary>
    public ObservableCollection<LogEntryViewModel> FilteredEntries { get; } = new();

    /// <summary>设备型号</summary>
    [ObservableProperty] private string _deviceModel = "";
    /// <summary>系统版本</summary>
    [ObservableProperty] private string _deviceOs = "";
    /// <summary>应用版本</summary>
    [ObservableProperty] private string _appVersion = "";
    /// <summary>构建号</summary>
    [ObservableProperty] private string _buildNumber = "";
    /// <summary>首次安装时间</summary>
    [ObservableProperty] private string _installDate = "";
    /// <summary>日志文件路径（显示用）</summary>
    [ObservableProperty] private string _logPath = "files/debug.log";

    /// <summary>总条数</summary>
    [ObservableProperty] private int _totalCount;
    /// <summary>错误条数</summary>
    [ObservableProperty] private int _errorCount;
    /// <summary>警告条数</summary>
    [ObservableProperty] private int _warnCount;
    /// <summary>信息条数</summary>
    [ObservableProperty] private int _infoCount;
    /// <summary>调试条数</summary>
    [ObservableProperty] private int _debugCount;
    /// <summary>日志文件大小（KB）</summary>
    [ObservableProperty] private int _sizeKb;

    /// <summary>是否附加设备信息（导出选项，默认开）</summary>
    [ObservableProperty] private bool _includeDeviceInfo = true;
    /// <summary>是否包含启动日志（导出选项，默认开）</summary>
    [ObservableProperty] private bool _includeStartupLog = true;
    /// <summary>是否为空状态（无匹配日志）</summary>
    [ObservableProperty] private bool _isEmpty;

    /// <summary>可选标签列表（从日志动态提取，供 chips 显示）</summary>
    public ObservableCollection<string> AvailableTags { get; } = new();

    public LogViewModel()
    {
        _logFilePath = Path.Combine(FileSystem.AppDataDirectory, "debug.log");
        LoadDeviceInfo();
    }

    /// <summary>加载设备信息（型号/系统/版本/安装时间）</summary>
    private void LoadDeviceInfo()
    {
        try
        {
            DeviceModel = DeviceInfo.Current.Model;
            var apiLevel = 0;
#if ANDROID
            apiLevel = (int)global::Android.OS.Build.VERSION.SdkInt;
#endif
            DeviceOs = $"{DeviceInfo.Current.Platform} {DeviceInfo.Current.VersionString}" +
                       (apiLevel > 0 ? $" (API {apiLevel})" : "");
        }
        catch { DeviceModel = "未知"; DeviceOs = "未知"; }

        try
        {
            var v = AppInfo.Current?.VersionString;
            AppVersion = string.IsNullOrEmpty(v) ? "v1.0.0" : $"v{v}";
            BuildNumber = AppInfo.Current?.BuildString ?? "";
        }
        catch { AppVersion = "v1.0.0"; }

        try
        {
            InstallDate = DeviceInfo.Current?.Idiom == DeviceIdiom.Phone
                ? "" : "";
            // Android 首次安装时间
#if ANDROID
            var ctx = global::Android.App.Application.Context;
            if (ctx?.PackageManager?.GetPackageInfo(ctx.PackageName, 0) is { } pi)
            {
                // FirstInstallTime 返回毫秒时间戳
                InstallDate = DateTimeOffset.FromUnixTimeMilliseconds(pi.FirstInstallTime).LocalDateTime.ToString("yyyy-MM-dd");
            }
#endif
        }
        catch { }

        try { LogPath = _logFilePath.Replace(FileSystem.AppDataDirectory, "files"); }
        catch { }
    }

    /// <summary>加载日志文件并解析（页面 OnAppearing 时调用）</summary>
    [RelayCommand]
    public void LoadLogs()
    {
        _allEntries.Clear();
        AvailableTags.Clear();
        AvailableTags.Add("全部模块");

        try
        {
            if (!File.Exists(_logFilePath))
            {
                UpdateStats();
                ApplyFilter();
                return;
            }

            var fi = new FileInfo(_logFilePath);
            SizeKb = Math.Max(1, (int)(fi.Length / 1024));

            // 按行读取，逐行解析（格式：HH:mm:ss.fff\t[L][tag] msg）
            var lines = File.ReadAllLines(_logFilePath);
            var seenTags = new HashSet<string>();
            // 正则：HH:mm:ss.fff [L][tag] msg，L 可以是 D/I/W/E
            var regex = new Regex(@"^(\d{2}:\d{2}:\d{2}\.\d{3})\s*\[(D|I|W|E)\]\s*\[([^\]]+)\]\s?(.*)$");

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var m = regex.Match(line);
                if (!m.Success)
                {
                    // 无法解析的行作为 Info 级别、空 tag 的消息
                    _allEntries.Add(new LogEntryViewModel
                    {
                        Timestamp = "",
                        Level = LogLevel.Info,
                        Tag = "",
                        Message = line
                    });
                    continue;
                }

                var level = m.Groups[2].Value switch
                {
                    "E" => LogLevel.Error,
                    "W" => LogLevel.Warn,
                    "I" => LogLevel.Info,
                    "D" => LogLevel.Debug,
                    _ => LogLevel.Info
                };
                var tag = m.Groups[3].Value;
                var entry = new LogEntryViewModel
                {
                    Timestamp = m.Groups[1].Value,
                    Level = level,
                    Tag = tag,
                    Message = m.Groups[4].Value
                };
                _allEntries.Add(entry);
                if (seenTags.Add(tag) && !string.IsNullOrEmpty(tag))
                    AvailableTags.Add(tag);
            }
        }
        catch (Exception ex)
        {
            _allEntries.Add(new LogEntryViewModel
            {
                Timestamp = DateTime.Now.ToString("HH:mm:ss.fff"),
                Level = LogLevel.Error,
                Tag = "UI",
                Message = $"读取日志失败：{ex.Message}"
            });
        }

        UpdateStats();
        ApplyFilter();
    }

    /// <summary>更新统计数字</summary>
    private void UpdateStats()
    {
        TotalCount = _allEntries.Count;
        ErrorCount = _allEntries.Count(e => e.Level == LogLevel.Error);
        WarnCount = _allEntries.Count(e => e.Level == LogLevel.Warn);
        InfoCount = _allEntries.Count(e => e.Level == LogLevel.Info);
        DebugCount = _allEntries.Count(e => e.Level == LogLevel.Debug);
    }

    /// <summary>应用当前筛选条件（级别+标签+搜索）到 FilteredEntries</summary>
    private void ApplyFilter()
    {
        FilteredEntries.Clear();
        var q = SearchQuery?.Trim() ?? "";
        foreach (var e in _allEntries)
        {
            if (_levelFilter != "all")
            {
                var code = e.LevelCode;
                if (code != _levelFilter) continue;
            }
            if (_tagFilter != "all" && e.Tag != _tagFilter) continue;
            if (!string.IsNullOrEmpty(q))
            {
                if ((e.Message?.Contains(q, StringComparison.OrdinalIgnoreCase) != true) &&
                    (e.Tag?.Contains(q, StringComparison.OrdinalIgnoreCase) != true))
                    continue;
            }
            FilteredEntries.Add(e);
        }
        IsEmpty = FilteredEntries.Count == 0;
    }

    /// <summary>搜索查询（双向绑定）</summary>
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery == value) return;
            _searchQuery = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    /// <summary>设置级别筛选（all/i/w/e）</summary>
    public void SetLevelFilter(string level)
    {
        _levelFilter = level;
        ApplyFilter();
    }

    /// <summary>设置标签筛选（all/具体标签）</summary>
    public void SetTagFilter(string tag)
    {
        _tagFilter = tag == "全部模块" ? "all" : tag;
        ApplyFilter();
    }

    /// <summary>复制筛选后日志到剪贴板</summary>
    [RelayCommand]
    public async Task CopyLogsAsync()
    {
        try
        {
            var text = string.Join("\n", FilteredEntries.Select(e =>
                $"{e.Timestamp}\t[{e.LevelText}][{e.Tag}] {e.Message}"));
            await Clipboard.Default.SetTextAsync(text);
        }
        catch { }
    }

    /// <summary>清空日志文件</summary>
    [RelayCommand]
    public async Task ClearLogsAsync()
    {
        try
        {
            if (File.Exists(_logFilePath))
                File.WriteAllText(_logFilePath, "");
            _allEntries.Clear();
            UpdateStats();
            ApplyFilter();
        }
        catch { }
    }

    /// <summary>生成诊断包并分享（调用系统分享）</summary>
    [RelayCommand]
    public async Task ShareDiagnosticPackageAsync()
    {
        try
        {
            var tmpDir = Path.Combine(FileSystem.CacheDirectory, "diag");
            Directory.CreateDirectory(tmpDir);
            var logFile = Path.Combine(tmpDir, "debug.log");
            if (File.Exists(_logFilePath))
                File.Copy(_logFilePath, logFile, true);

            // 附加设备信息
            if (IncludeDeviceInfo)
            {
                var infoFile = Path.Combine(tmpDir, "device.txt");
                var info = $"应用版本: {AppVersion} (build {BuildNumber})\n" +
                           $"设备型号: {DeviceModel}\n" +
                           $"系统版本: {DeviceOs}\n" +
                           $"首次安装: {InstallDate}\n" +
                           $"日志条数: {TotalCount} (错误 {ErrorCount}, 警告 {WarnCount})\n" +
                           $"导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n";
                await File.WriteAllTextAsync(infoFile, info);
            }

            // 打包成 zip
            var zipPath = Path.Combine(FileSystem.CacheDirectory,
                $"catclaw_diag_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
            if (File.Exists(zipPath)) File.Delete(zipPath);
            System.IO.Compression.ZipFile.CreateFromDirectory(tmpDir, zipPath);

            // 清理临时目录
            try { Directory.Delete(tmpDir, true); } catch { }

            // 调用系统分享
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "猫爪音乐诊断包",
                File = new ShareFile(zipPath)
            });
        }
        catch (Exception ex)
        {
            Log.Debug("LogViewModel", $"[LogVM] Share failed: {ex.Message}");
        }
    }
}
