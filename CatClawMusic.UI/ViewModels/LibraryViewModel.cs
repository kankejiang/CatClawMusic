using System.Collections.ObjectModel;
using Android.Content;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Data;
using CatClawMusic.UI.Helpers;
using CatClawMusic.UI.Platforms.Android;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreModels = CatClawMusic.Core.Models;

namespace CatClawMusic.UI.ViewModels;

/// <summary>
/// 音乐库页面 ViewModel，管理本地/网络歌曲的展示、扫描、缓存与标签切换
/// <para>
/// 核心职责：
/// <list type="bullet">
///   <item>本地音乐扫描：支持全量扫描与增量扫描，扫描结果持久化到数据库</item>
///   <item>网络音乐加载：按协议（WebDAV/Navidrome/SMB）拉取远程歌曲，支持缓存</item>
///   <item>标签切换：在"本地"与"网络"标签之间切换时，各自维护独立的歌曲缓存</item>
///   <item>协议选择：支持多协议配置，按选中协议过滤网络歌曲</item>
/// </list>
/// </para>
/// <para>
/// 缓存机制说明：
/// <list type="bullet">
///   <item>_localSongsCache / _networkSongsCache：内存级歌曲列表缓存，切换标签时保存/恢复</item>
///   <item>_hasLoadedLocal / _hasLoadedNetwork：标记对应标签是否已完成首次加载，避免重复加载</item>
///   <item>数据库缓存：本地扫描结果和网络歌曲均持久化到 MusicDatabase，下次启动可快速加载</item>
///   <item>SharedPreferences：在 Android 端持久化当前标签页和协议选择索引</item>
/// </list>
/// </para>
/// <para>
/// 扫描流程概述：
/// <list type="number">
///   <item>LoadLocalAsync 入口 → 先检查文件夹权限有效性</item>
///   <item>若权限失效但有数据库缓存 → 直接展示缓存数据，提示用户重新授权</item>
///   <item>若非强制刷新且已加载 → 跳过；否则尝试从数据库缓存批量加载</item>
///   <item>缓存为空或强制刷新 → 进入 BackgroundScanAsync 后台扫描</item>
///   <item>BackgroundScanAsync → 遍历文件夹 → 增量入库 → 清理已删除歌曲 → 完成</item>
/// </list>
/// </para>
/// </summary>
public partial class LibraryViewModel : ObservableObject
{
    /// <summary>本地音乐库服务，负责本地歌曲的 CRUD 与扫描</summary>
    private readonly IMusicLibraryService _musicLibrary;

    /// <summary>网络音乐服务（可选），负责远程协议的歌曲拉取</summary>
    private readonly INetworkMusicService? _networkMusic;

    /// <summary>权限服务（可选），用于检查/请求存储权限</summary>
    private readonly IPermissionService? _permission;

    /// <summary>音乐数据库（可选），用于歌曲持久化与缓存查询</summary>
    private readonly MusicDatabase? _database;

    /// <summary>主线程调度器，确保 UI 更新操作在主线程执行</summary>
    private readonly IMainThreadDispatcher _dispatcher;

    /// <summary>
    /// 当前激活的标签页名称，"Local" 或 "Network"
    /// <para>切换标签时会触发 SwitchTab 方法，保存当前标签歌曲到缓存并恢复目标标签的歌曲</para>
    /// </summary>
    [ObservableProperty] private string _currentTab = "Local";

    /// <summary>扫描完成事件，在 BackgroundScanAsync 结束后触发，通知外部组件</summary>
    public event EventHandler? ScanCompleted;

    /// <summary>请求显示扫描对话框，参数为对话框标题</summary>
    public event EventHandler<string>? ScanDialogRequested;

    /// <summary>
    /// 协议变更事件（静态，跨页面通知）
    /// </summary>
    public static event EventHandler? ProtocolChanged;

    /// <summary>
    /// 触发协议变更通知
    /// </summary>
    /// <param name="sender">触发源</param>
    public static void NotifyProtocolChanged(object sender)
    {
        ProtocolChanged?.Invoke(sender, EventArgs.Empty);
    }

    /// <summary>
    /// 当前显示的歌曲列表
    /// <para>使用 BatchObservableCollection 以支持批量添加，减少 CollectionChanged 事件触发次数，提升性能</para>
    /// </summary>
    public BatchObservableCollection<CoreModels.Song> Songs { get; } = new();

    /// <summary>
    /// 是否正在加载
    /// </summary>
    [ObservableProperty] private bool _isLoading;
    /// <summary>
    /// 是否显示权限提示
    /// </summary>
    [ObservableProperty] private bool _showPermissionPrompt;
    /// <summary>
    /// 权限提示文本
    /// </summary>
    [ObservableProperty] private string _permissionPromptText = "";
    /// <summary>
    /// 状态文本
    /// </summary>
    [ObservableProperty] private string _statusText = "";
    /// <summary>
    /// 本地标签页颜色，激活时为主题色，非激活时为灰色
    /// </summary>
    [ObservableProperty] private string _localTabColor = "#9B7ED8";
    /// <summary>
    /// 网络标签页颜色，激活时为主题色，非激活时为灰色
    /// </summary>
    [ObservableProperty] private string _networkTabColor = "#C0B8CA";
    /// <summary>
    /// 扫描进度（0-100）
    /// </summary>
    [ObservableProperty] private int _scanProgress;
    /// <summary>
    /// 扫描状态文本
    /// </summary>
    [ObservableProperty] private string _scanStatus = "";
    /// <summary>
    /// 是否正在扫描
    /// </summary>
    [ObservableProperty] private bool _isScanning;
    /// <summary>
    /// 当前选择的协议选项索引，对应 ProtocolTypes 列表中的位置
    /// <para>变更时通过 OnSelectedProtocolIndexChanged 自动持久化到 SharedPreferences</para>
    /// </summary>
    [ObservableProperty] private int _selectedProtocolIndex = 0;
    /// <summary>
    /// 搜索关键字
    /// </summary>
    [ObservableProperty] private string _searchQuery = "";

    /// <summary>
    /// 协议选项显示名称列表，与 ProtocolTypes 一一对应
    /// <para>例如：["WebDAV", "Navidrome", "SMB"]</para>
    /// </summary>
    public ObservableCollection<string> ProtocolOptions { get; } = new();
    /// <summary>
    /// 协议类型列表，与 ProtocolOptions 一一对应
    /// <para>用于根据选中索引获取实际的协议枚举值，过滤网络歌曲</para>
    /// </summary>
    public List<CoreModels.ProtocolType> ProtocolTypes { get; } = new();

    /// <summary>
    /// 按搜索关键字过滤后的歌曲列表
    /// <para>搜索范围包括歌曲标题（Title）、艺术家（Artist）、专辑（Album），不区分大小写</para>
    /// </summary>
    public List<CoreModels.Song> FilteredSongs => string.IsNullOrWhiteSpace(SearchQuery)
        ? Songs.ToList()
        : Songs.Where(s =>
            (s.Title?.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) == true) ||
            (s.Artist?.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) == true) ||
            (s.Album?.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) == true)
        ).ToList();

    /// <summary>标记本地标签是否已完成首次加载，避免重复加载</summary>
    private bool _hasLoadedLocal;
    /// <summary>标记网络标签是否已完成首次加载，避免重复加载</summary>
    private bool _hasLoadedNetwork;

    /// <summary>
    /// 本地标签的歌曲内存缓存
    /// <para>切换到网络标签时，当前本地歌曲列表保存到此缓存；切回本地标签时从此缓存恢复</para>
    /// </summary>
    private List<CoreModels.Song> _localSongsCache = new();

    /// <summary>
    /// 网络标签的歌曲内存缓存
    /// <para>切换到本地标签时，当前网络歌曲列表保存到此缓存；切回网络标签时从此缓存恢复</para>
    /// </summary>
    private List<CoreModels.Song> _networkSongsCache = new();

    /// <summary>是否抑制 CollectionChanged 事件（当前未使用，预留）</summary>
    private bool _suppressCollectionChanged;

    /// <summary>SharedPreferences 存储的键名前缀</summary>
    private const string PrefKey = "library_state";
    /// <summary>SharedPreferences 中协议选择索引的键名</summary>
    private const string PrefProtocolIndex = "protocol_index";
    /// <summary>SharedPreferences 中当前标签页的键名</summary>
    private const string PrefCurrentTab = "current_tab";

    /// <summary>
    /// 协议枚举到显示名称的映射字典
    /// <para>用于在 UI 上展示协议的人类可读名称</para>
    /// </summary>
    private static readonly Dictionary<CoreModels.ProtocolType, string> ProtocolDisplayNames = new()
    {
        { CoreModels.ProtocolType.WebDAV, "WebDAV" },
        { CoreModels.ProtocolType.Navidrome, "Navidrome" },
        { CoreModels.ProtocolType.SMB, "SMB" },
    };

    /// <summary>
    /// 初始化音乐库 ViewModel
    /// </summary>
    /// <param name="musicLibrary">本地音乐库服务</param>
    /// <param name="networkMusic">网络音乐服务（可选）</param>
    /// <param name="permission">权限服务（可选）</param>
    /// <param name="dispatcher">主线程调度器</param>
    /// <param name="database">音乐数据库（可选）</param>
    /// <remarks>
    /// 在 Android 平台上，构造函数会从 SharedPreferences 恢复上次保存的协议索引和标签页状态，
    /// 确保应用重启后用户的界面选择不丢失
    /// </remarks>
    public LibraryViewModel(IMusicLibraryService musicLibrary, INetworkMusicService? networkMusic = null,
        IPermissionService? permission = null, IMainThreadDispatcher? dispatcher = null, MusicDatabase? database = null)
    {
        _musicLibrary = musicLibrary;
        _networkMusic = networkMusic;
        _permission = permission;
        _database = database;
        _dispatcher = dispatcher!;

#if ANDROID
        // 从 SharedPreferences 恢复上次保存的协议索引和标签页状态
        try
        {
            var ctx = global::Android.App.Application.Context;
            var prefs = ctx.GetSharedPreferences(PrefKey, FileCreationMode.Private);
            _selectedProtocolIndex = prefs.GetInt(PrefProtocolIndex, 0);
            var activeColor = UiHelper.ResolveThemeColorHex(ctx, Resource.Attribute.catClawPrimaryColor, "#9B7ED8");
            var inactiveColor = UiHelper.ResolveThemeColorHex(ctx, Resource.Attribute.catClawTabInactive, "#C0B8CA");
            var savedTab = prefs.GetString(PrefCurrentTab, "Local");
            if (savedTab == "Network")
            {
                _currentTab = "Network";
                _localTabColor = inactiveColor;
                _networkTabColor = activeColor;
            }
            else
            {
                _localTabColor = activeColor;
                _networkTabColor = inactiveColor;
            }
        }
        catch { }
#endif
    }

    /// <summary>
    /// 刷新协议选项列表，从数据库加载已启用的协议
    /// <para>
    /// 流程：数据库查询 → 过滤已启用协议 → 按 ProtocolType 枚举值排序 → 同步到 ProtocolOptions 和 ProtocolTypes
    /// </para>
    /// <para>如果当前选中的协议索引超出范围，自动重置为 0</para>
    /// </summary>
    public async Task RefreshProtocolOptionsAsync()
    {
        if (_database == null) return;
        try
        {
            await _database.EnsureInitializedAsync();
            var profiles = await _database.GetConnectionProfilesAsync();
            // 仅保留已启用且在显示名称映射中的协议
            var enabledProtocols = profiles
                .Where(p => p.IsEnabled && ProtocolDisplayNames.ContainsKey(p.Protocol))
                .Select(p => p.Protocol)
                .Distinct()
                .OrderBy(p => (int)p)
                .ToList();

            ProtocolOptions.Clear();
            ProtocolTypes.Clear();
            foreach (var proto in enabledProtocols)
            {
                ProtocolTypes.Add(proto);
                ProtocolOptions.Add(ProtocolDisplayNames[proto]);
            }

            // 索引越界保护：若上次选中的协议已被禁用，重置为第一个
            if (_selectedProtocolIndex >= ProtocolTypes.Count)
                _selectedProtocolIndex = 0;

            OnPropertyChanged(nameof(ProtocolOptions));
            OnPropertyChanged(nameof(ProtocolTypes));
            OnPropertyChanged(nameof(SelectedProtocolIndex));
        }
        catch { }
    }

    /// <summary>批量添加歌曲到 Songs 集合，利用 BatchObservableCollection 减少 CollectionChanged 触发次数以提升性能</summary>
    private void AddSongsBatch(IEnumerable<CoreModels.Song> songs)
    {
        Songs.AddRange(songs);
    }

    /// <summary>
    /// 切换本地/网络标签页
    /// <para>
    /// 标签切换逻辑：
    /// <list type="number">
    ///   <item>若目标标签与当前标签相同，直接返回</item>
    ///   <item>将当前标签的歌曲列表保存到对应的内存缓存（_localSongsCache 或 _networkSongsCache）</item>
    ///   <item>更新 CurrentTab 并切换标签颜色（激活标签为紫色，非激活为灰色）</item>
    ///   <item>清空 Songs 并从目标标签的缓存恢复数据</item>
    ///   <item>更新状态文本</item>
    ///   <item>若目标标签尚未加载且缓存为空，自动触发异步加载</item>
    ///   <item>在 Android 端将当前标签持久化到 SharedPreferences</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="tab">目标标签名称，"Local" 或 "Network"</param>
    [RelayCommand]
    private void SwitchTab(string tab)
    {
        // 相同标签不处理
        if (CurrentTab == tab) return;

        // 保存当前标签的歌曲到对应缓存
        if (CurrentTab == "Local")
            _localSongsCache = Songs.ToList();
        else
            _networkSongsCache = Songs.ToList();

        // 切换标签和颜色（从主题属性读取，跟随主题配色）
        CurrentTab = tab;
#if ANDROID
        var ctx = global::Android.App.Application.Context;
        var activeColor = UiHelper.ResolveThemeColorHex(ctx, Resource.Attribute.catClawPrimaryColor, "#9B7ED8");
        var inactiveColor = UiHelper.ResolveThemeColorHex(ctx, Resource.Attribute.catClawTabInactive, "#C0B8CA");
        LocalTabColor = tab == "Local" ? activeColor : inactiveColor;
        NetworkTabColor = tab == "Network" ? activeColor : inactiveColor;
#else
        LocalTabColor = tab == "Local" ? "#9B7ED8" : "#C0B8CA";
        NetworkTabColor = tab == "Network" ? "#9B7ED8" : "#C0B8CA";
#endif

        // 从缓存恢复目标标签的歌曲
        Songs.Clear();
        var cache = tab == "Local" ? _localSongsCache : _networkSongsCache;
        if (cache.Count > 0)
            AddSongsBatch(cache);

        // 更新状态文本
        if (tab == "Local")
            StatusText = cache.Count > 0 ? $"🐱 共 {cache.Count} 首歌曲" : "未加载本地音乐";
        else
            StatusText = cache.Count > 0 ? $"☁️ 共 {cache.Count} 首网络歌曲" : "未加载网络音乐";

        // 若目标标签尚未加载且缓存为空，自动触发异步加载
        if (tab == "Local" && !_hasLoadedLocal && _localSongsCache.Count == 0)
            _ = LoadLocalAsync();
        else if (tab == "Network" && !_hasLoadedNetwork && _networkSongsCache.Count == 0)
            _ = LoadNetworkAsync();

#if ANDROID
        // 持久化当前标签到 SharedPreferences
        try
        {
            var prefsCtx = global::Android.App.Application.Context;
            var prefs = prefsCtx.GetSharedPreferences(PrefKey, FileCreationMode.Private);
            prefs.Edit().PutString(PrefCurrentTab, tab).Apply();
        }
        catch { }
#endif
    }

    /// <summary>
    /// 强制刷新当前标签页的歌曲列表
    /// <para>清空当前标签的缓存和已加载标记，然后重新加载（本地走强制扫描，网络走强制刷新）</para>
    /// </summary>
    [RelayCommand]
    private async Task Refresh()
    {
        Songs.Clear();
        if (CurrentTab == "Local")
        {
            _hasLoadedLocal = false;
            _localSongsCache.Clear();
            await LoadLocalAsync(forceReload: true);
        }
        else
        {
            _hasLoadedNetwork = false;
            _networkSongsCache.Clear();
            await LoadNetworkAsync(forceRefresh: true);
        }
    }

    /// <summary>
    /// 打开系统文件夹选择器选择音乐目录
    /// <para>仅在 Android 平台可用，选择后重新加载本地音乐</para>
    /// </summary>
    [RelayCommand]
    private async Task PickMusicFolder()
    {
#if ANDROID
        var uri = await CatClawMusic.UI.Platforms.Android.FolderPicker.PickFolderAsync();
        if (!string.IsNullOrEmpty(uri))
        {
            _hasLoadedLocal = false;
            await LoadLocalAsync();
        }
#endif
    }

    /// <summary>
    /// 加载本地音乐，支持缓存读取和增量扫描
    /// <para>
    /// 加载策略（按优先级）：
    /// <list type="number">
    ///   <item>检查文件夹权限有效性 → 若失效但有数据库缓存，展示缓存并提示重新授权</item>
    ///   <item>若非强制刷新且已加载过（_hasLoadedLocal=true 且 Songs 非空），直接返回</item>
    ///   <item>尝试从数据库缓存批量加载 → 分批（每批50首）添加到 Songs，模拟扫描进度</item>
    ///   <item>缓存为空且未加载过 → 检查是否有已保存的文件夹 URI，无则提示选择</item>
    ///   <item>有文件夹 URI 或强制刷新 → 进入 BackgroundScanAsync 后台扫描</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="forceReload">是否强制重新扫描，为 true 时跳过所有缓存直接进入后台扫描</param>
    public async Task LoadLocalAsync(bool forceReload = false)
    {
        // 第一步：验证已保存的文件夹 URI 是否仍然有效（权限是否过期）
        var validFolders = await Task.Run(() => FolderPicker.ValidateSavedFolders());
        if (validFolders == 0 && FolderPicker.GetSavedFolderUris().Count > 0)
        {
            // 权限已过期，尝试从数据库缓存加载
            var cachedSongs = await _musicLibrary.GetAllSongsAsync();
            if (cachedSongs.Count > 0)
            {
                // 后台批量填充 MediaStore ID（用于封面加载）
                _ = Task.Run(() => Platforms.Android.MediaStoreCoverHelper.BatchFillMediaStoreIds(cachedSongs));
                Songs.Clear();
                AddSongsBatch(cachedSongs);
                StatusText = $"🐱 共 {Songs.Count} 首歌曲（缓存 · 权限已过期）";
                _localSongsCache = Songs.ToList();
                _hasLoadedLocal = true;
                ShowPermissionPrompt = true;
                PermissionPromptText = "存储权限已过期，请重新选择音乐文件夹\n\n（使用系统文件管理器，无需额外权限）";
                IsLoading = false;
                return;
            }

            // 无缓存但保留数据库记录，不清空（等用户重新授权后可继续使用）
            Songs.Clear();
            StatusText = "未选择音乐文件夹";
            ShowPermissionPrompt = true;
            PermissionPromptText = "存储权限已过期，请重新选择音乐文件夹\n\n（使用系统文件管理器，无需额外权限）";
            IsLoading = false;
            return;
        }

        // 第二步：若非强制刷新且已加载过，直接返回（避免重复加载）
        if (!forceReload && _hasLoadedLocal && Songs.Count > 0)
        {
            return;
        }

        ShowPermissionPrompt = false; IsLoading = true;
        StatusText = "正在加载...";

        try
        {
            // 第三步：尝试从数据库缓存批量加载（非强制刷新时）
            if (!forceReload)
            {
                var cachedSongs = await _musicLibrary.GetAllSongsAsync();
                if (cachedSongs.Count > 0)
                {
                    _ = Task.Run(() => Platforms.Android.MediaStoreCoverHelper.BatchFillMediaStoreIds(cachedSongs));

                    Songs.Clear();
                    AddSongsBatch(cachedSongs);
                    _hasLoadedLocal = true;
                    _localSongsCache = Songs.ToList();
                    StatusText = $"🐱 共 {Songs.Count} 首歌曲";
                    IsLoading = false;
                    ScanCompleted?.Invoke(this, EventArgs.Empty);
                    return;
                }
            }

            // 第四步：缓存为空，检查是否有已保存的文件夹 URI
            if (!forceReload && !_hasLoadedLocal)
            {
                var savedUri = CatClawMusic.UI.Platforms.Android.FolderPicker.GetSavedFolderUri();
                if (string.IsNullOrEmpty(savedUri))
                {
                    // 无文件夹 URI，提示用户选择
                    PermissionPromptText = "点击下方按钮，选择手机上的音乐文件夹\n\n（使用系统文件管理器，无需额外权限）";
                    StatusText = "未选择音乐文件夹";
                    ShowPermissionPrompt = true;
                    IsLoading = false;
                    return;
                }
                // 有文件夹 URI，进入后台扫描
                _ = BackgroundScanAsync(false);
                return;
            }

            // 第五步：强制刷新，直接进入后台扫描
            if (forceReload)
            {
                _ = BackgroundScanAsync(forceReload);
            }
        }
        catch (Exception ex) { StatusText = $"加载出错: {ex.Message}"; IsLoading = false; }
    }

    /// <summary>
    /// 后台扫描音乐文件夹，增量入库并更新 UI
    /// <para>
    /// 扫描流程：
    /// <list type="number">
    ///   <item>初始化数据库，准备扫描</item>
    ///   <item>创建 MusicScanner 实例，设置批次回调用于实时更新 UI</item>
    ///   <item>调用 AndroidLocalScanner.ScanAsync 遍历文件夹，逐批发现歌曲</item>
    ///   <item>每批新歌曲通过 MusicScanner.AddSongAsync 增量写入数据库</item>
    ///   <item>扫描完成后调用 scanner.FlushAsync 刷新缓冲区</item>
    ///   <item>调用 RemoveStaleSongsAsync 清理数据库中已不存在于磁盘的歌曲</item>
    ///   <item>同步移除 UI 中对应的过期歌曲</item>
    ///   <item>标记 _hasLoadedLocal = true，保存缓存，触发 ScanCompleted 事件</item>
    /// </list>
    /// </para>
    /// <para>
    /// 去重机制：使用 scannedPaths（HashSet）防止同一文件被重复扫描，
    /// 使用 displayedPaths（HashSet，忽略大小写）防止同一歌曲被重复添加到 UI
    /// </para>
    /// </summary>
    /// <param name="forceReload">是否强制重新扫描（当前未区分，预留扩展）</param>
    private async Task BackgroundScanAsync(bool forceReload)
    {
        ScanDialogRequested?.Invoke(this, "🐱 正在扫描本地音乐");
        try
        {
            _dispatcher.Post(() => { StatusText = "正在准备扫描..."; IsScanning = true; ScanProgress = 0; ScanStatus = "遍历文件夹..."; });

            if (_database != null)
            {
                try { await _database.EnsureInitializedAsync().ConfigureAwait(false); } catch { }
            }

            // 去重集合：scannedPaths 用于数据库去重，displayedPaths 用于 UI 去重
            var scannedPaths = new HashSet<string>();
            var displayedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 创建扫描器，每批新歌曲实时添加到 UI
            var scanner = new MusicScanner(_database!, batch =>
            {
                // 过滤掉已显示过的歌曲（按文件路径去重）
                var songsToAdd = batch
                    .Where(s => displayedPaths.Add(s.FilePath))
                    .ToList();
                if (songsToAdd.Count > 0)
                {
                    _dispatcher.Post(() =>
                    {
                        AddSongsBatch(songsToAdd);
                        StatusText = $"🐱 已扫描 {Songs.Count} 首歌曲...";
                    });
                }
            });

            // 扫描进度回调，进度映射到 0-85% 区间（85-100% 留给后续清理步骤）
            var progress = new Progress<(int done, int total, string status)>(p =>
            {
                int pct = p.total > 0 ? (int)(85.0 * p.done / p.total) : 0;
                ReportProgress(pct, p.status);
            });

            // 在后台线程执行文件系统扫描，避免同步 I/O 阻塞 UI
            var customFolders = GetCustomFolders();
            await Task.Run(async () =>
            {
                await CatClawMusic.UI.Services.AndroidLocalScanner.ScanAsync(
                    customFolders, progress, async (batch) =>
                    {
                        var newSongs = batch.Where(s => scannedPaths.Add(s.FilePath)).ToList();
                        if (newSongs.Count == 0) return;

                        await scanner.AddSongsBatchAsync(newSongs);
                    });
            });

            // 刷新扫描器缓冲区，确保所有歌曲已写入数据库
            await scanner.FlushAsync().ConfigureAwait(false);

            // 清理已删除的歌曲：对比数据库记录与本次扫描路径，移除不存在的记录
            // 关键安全保护：若 scannedPaths 为空（扫描未发现任何文件），
            // 跳过清理以避免误删全部歌曲（例如 SAF 路径格式与已存路径不一致时）
            if (_database != null && scannedPaths.Count > 0)
            {
                try
                {
                    _dispatcher.Post(() => { ScanStatus = "清理已删除歌曲..."; ScanProgress = 90; });
                    var removed = await _database.RemoveStaleSongsAsync(CoreModels.SongSource.Local, scannedPaths).ConfigureAwait(false);
                    if (removed > 0)
                    {
                        // 同步移除 UI 中对应的过期歌曲，使用批量移除减少 CollectionChanged 通知
                        _dispatcher.Post(() => Songs.RemoveAll(s => !scannedPaths.Contains(s.FilePath)));
                    }
                }
                catch { }
            }

            // 扫描完成：从数据库全量重新加载，确保已有歌曲也正确显示（因为 InsertSongsBatchAsync 的
            // 回调只包含新插入的歌曲，已存在的歌曲只做 UPDATE 不会触发回调）
            if (_database != null)
            {
                try
                {
                    var allLocalSongs = await _database.GetSongsAsync().ConfigureAwait(false);
                    _dispatcher.Post(() =>
                    {
                        Songs.Clear();
                        AddSongsBatch(allLocalSongs);
                        ScanProgress = 100;
                        ScanStatus = "扫描完成";
                        IsScanning = false;
                        StatusText = $"🐱 共 {allLocalSongs.Count} 首歌曲";
                        _localSongsCache = Songs.ToList();
                        _hasLoadedLocal = true;
                        ScanCompleted?.Invoke(this, EventArgs.Empty);
                    });
                }
                catch
                {
                    _dispatcher.Post(() =>
                    {
                        ScanProgress = 100;
                        ScanStatus = "扫描完成";
                        IsScanning = false;
                        StatusText = $"🐱 共 {Songs.Count} 首歌曲";
                        _localSongsCache = Songs.ToList();
                        _hasLoadedLocal = true;
                        ScanCompleted?.Invoke(this, EventArgs.Empty);
                    });
                }
            }
            else
            {
                _dispatcher.Post(() =>
                {
                    ScanProgress = 100;
                    ScanStatus = "扫描完成";
                    IsScanning = false;
                    StatusText = $"🐱 共 {Songs.Count} 首歌曲";
                    _localSongsCache = Songs.ToList();
                    _hasLoadedLocal = true;
                    ScanCompleted?.Invoke(this, EventArgs.Empty);
                });
            }
        }
        catch (Exception ex)
        {
            _dispatcher.Post(() => { IsScanning = false; StatusText = $"扫描出错: {ex.Message}"; });
            ScanCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 在主线程更新扫描进度
    /// </summary>
    /// <param name="pct">进度百分比</param>
    /// <param name="status">进度状态描述</param>
    private void ReportProgress(int pct, string status)
    {
        _dispatcher.Post(() => { ScanProgress = pct; ScanStatus = status; });
    }

    /// <summary>
    /// 加载网络歌曲，支持缓存读取和强制刷新
    /// <para>
    /// 加载策略：
    /// <list type="number">
    ///   <item>若非强制刷新，先从数据库读取缓存 → 按启用协议过滤 → 按选中协议过滤 → 展示</item>
    ///   <item>缓存为空或强制刷新 → 遍历已启用的连接配置，逐个拉取远程歌曲</item>
    ///   <item>拉取完成后从数据库全量重新加载，避免新增与缓存重复</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="forceRefresh">是否强制从远程重新拉取</param>
    public async Task LoadNetworkAsync(bool forceRefresh = false)
    {
        ShowPermissionPrompt = false; IsLoading = true;
        StatusText = "正在加载...";

        try
        {
            if (_networkMusic == null) { StatusText = "网络服务未就绪"; IsLoading = false; return; }

            var allProfiles = await _networkMusic.GetProfilesAsync();
            var hasEnabled = allProfiles.Any(p => p.IsEnabled);
            if (!hasEnabled)
            {
                Songs.Clear();
                _networkSongsCache.Clear();
                _hasLoadedNetwork = true;
                StatusText = "未启用网络连接";
                IsLoading = false;
                return;
            }

            if (_database != null && !forceRefresh)
            {
                var cached = await Task.Run(async () =>
                {
                    await _database.EnsureInitializedAsync();
                    return await _database.GetCachedNetworkSongsAsync();
                });
                if (cached.Count > 0)
                {
                    cached = await FilterByEnabledProtocolsAsync(cached);
                    Songs.Clear();
                    var filtered = FilterSongsByProtocol(cached);
                    AddSongsBatch(filtered);
                    StatusText = $"☁️ 共 {filtered.Count} 首网络歌曲";
                    _networkSongsCache = Songs.ToList();
                    _hasLoadedNetwork = true;
                    IsLoading = false;
                    return;
                }
            }

            ScanDialogRequested?.Invoke(this, "☁️ 正在扫描网络音乐");

            var enabled = allProfiles.Where(p => p.IsEnabled).ToList();

            if (_selectedProtocolIndex < ProtocolTypes.Count)
            {
                var selectedProtocol = ProtocolTypes[_selectedProtocolIndex];
                enabled = enabled.Where(p => p.Protocol == selectedProtocol).ToList();
            }

            if (enabled.Count == 0) { StatusText = "请先在设置中配置网络连接"; IsLoading = false; return; }

            if (forceRefresh || !Songs.Any())
            {
                if (forceRefresh) IsScanning = true;
                ScanProgress = 0;
                Songs.Clear();
            }

            var all = new List<CoreModels.Song>();
            foreach (var p in enabled)
            {
                var idx = enabled.IndexOf(p);
                ReportProgress(5 + idx * 20, $"连接 {p.Name}...");
                StatusText = $"正在连接 {p.Name}...";
                try
                {
                    var progress = new Progress<(int done, int total, string status)>(p =>
                    {
                        int pct = p.total > 0 ? 10 + (int)(80.0 * p.done / p.total) : 10;
                        ReportProgress(pct, p.status);
                    });

                    var scanBuffer = new List<CoreModels.Song>();
                    var scanBufferLock = new object();
                    var scanBufferTimer = new System.Timers.Timer(200);
                    scanBufferTimer.Elapsed += (_, _) =>
                    {
                        List<CoreModels.Song> toAdd;
                        lock (scanBufferLock)
                        {
                            if (scanBuffer.Count == 0) return;
                            toAdd = scanBuffer.ToList();
                            scanBuffer.Clear();
                        }
                        _dispatcher.Post(() =>
                        {
                            AddSongsBatch(toAdd);
                            StatusText = $"☁️ 正在拉取... 已发现 {Songs.Count} 首歌曲";
                        });
                    };
                    scanBufferTimer.Start();

                    var scanned = await Task.Run(async () => await _networkMusic.ScanAsync(p, progress, (batch) =>
                    {
                        if (_database != null)
                        {
                            try { foreach (var s in batch) _database.SaveSongAsync(s).GetAwaiter().GetResult(); } catch { }
                        }
                        var filtered = FilterSongsByProtocol(batch);
                        lock (scanBufferLock)
                            scanBuffer.AddRange(filtered);
                    }));

                    scanBufferTimer.Stop();
                    scanBufferTimer.Dispose();

                    if (scanBuffer.Count > 0)
                    {
                        _dispatcher.Post(() =>
                        {
                            AddSongsBatch(scanBuffer);
                            StatusText = $"☁️ 正在拉取... 已发现 {Songs.Count} 首歌曲";
                        });
                    }

                    all.AddRange(scanned);
                }
                catch { }
            }

            if (_database != null)
            {
                await Task.Run(async () => await _database.EnsureInitializedAsync());
                if (Songs.Count == 0)
                {
                    var cachedSongs = await Task.Run(async () => await _database.GetCachedNetworkSongsAsync());
                    cachedSongs = await FilterByEnabledProtocolsAsync(cachedSongs);
                    var cachedFiltered = FilterSongsByProtocol(cachedSongs);
                    AddSongsBatch(cachedFiltered);
                }
                _networkSongsCache = Songs.ToList();
                _hasLoadedNetwork = true;
            }

            StatusText = Songs.Count > 0 ? $"☁️ 共 {Songs.Count} 首网络歌曲" : "连接成功但未找到歌曲";

            if (forceRefresh)
            {
                ReportProgress(100, "扫描完成");
                _dispatcher.Post(async () => { await Task.Delay(1500); IsScanning = false; });
            }

            if (forceRefresh)
                ScanCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusText = $"连接失败: {ex.Message}";
            if (forceRefresh) ScanCompleted?.Invoke(this, EventArgs.Empty);
        }
        finally { IsLoading = false; if (!forceRefresh) IsScanning = false; }
    }

    /// <summary>
    /// 根据当前选择的协议类型过滤歌曲列表
    /// <para>若选中了特定协议，仅保留该协议的歌曲；否则返回全部</para>
    /// </summary>
    /// <param name="songs">待过滤的歌曲列表</param>
    /// <returns>过滤后的歌曲列表</returns>
    private List<CoreModels.Song> FilterSongsByProtocol(List<CoreModels.Song> songs)
    {
        if (_selectedProtocolIndex < ProtocolTypes.Count)
        {
            var selectedProtocol = ProtocolTypes[_selectedProtocolIndex];
            songs = songs.Where(s => s.Protocol == selectedProtocol).ToList();
        }
        return songs;
    }

    /// <summary>
    /// 过滤掉已关闭协议的歌曲（用于缓存加载等场景）
    /// <para>从数据库获取当前已启用的协议列表，仅保留属于已启用协议的歌曲</para>
    /// </summary>
    /// <param name="songs">待过滤的歌曲列表</param>
    /// <returns>仅包含已启用协议的歌曲列表</returns>
    private async Task<List<CoreModels.Song>> FilterByEnabledProtocolsAsync(List<CoreModels.Song> songs)
    {
        if (_database == null) return songs;
        var enabledProtocols = await _database.GetEnabledProtocolsAsync();
        return _database.FilterByEnabledProtocols(songs, enabledProtocols);
    }

    /// <summary>
    /// 获取用户自定义的音乐文件夹列表
    /// <para>合并 SAF URI 和本地文件路径</para>
    /// </summary>
    /// <returns>文件夹 URI/路径列表，或 null</returns>
    private List<string>? GetCustomFolders()
    {
        var safUris = CatClawMusic.UI.Platforms.Android.FolderPicker.GetSavedFolderUris();
        var localPaths = CatClawMusic.UI.Services.ScanSettings.GetLocalFolderPaths();

        if (safUris.Count == 0 && localPaths.Count == 0)
            return null;

        var result = new List<string>(safUris);
        result.AddRange(localPaths);
        return result;
    }

    /// <summary>
    /// 协议选择变化时自动持久化到 SharedPreferences，确保下次启动恢复选择
    /// <para>此方法由 CommunityToolkit.Mvvm 的源生成器在 SelectedProtocolIndex 属性变更时自动调用</para>
    /// </summary>
    /// <param name="value">新的协议索引值</param>
    partial void OnSelectedProtocolIndexChanged(int value)
    {
#if ANDROID
        try
        {
            var ctx = global::Android.App.Application.Context;
            var prefs = ctx.GetSharedPreferences(PrefKey, FileCreationMode.Private);
            prefs.Edit().PutInt(PrefProtocolIndex, value).Apply();
        }
        catch { }
#endif
    }

    /// <summary>
    /// 搜索关键字变化时通知 UI 刷新过滤后的歌曲列表
    /// <para>此方法由 CommunityToolkit.Mvvm 的源生成器在 SearchQuery 属性变更时自动调用</para>
    /// </summary>
    /// <param name="value">新的搜索关键字</param>
    partial void OnSearchQueryChanged(string value) => OnPropertyChanged(nameof(FilteredSongs));
}
