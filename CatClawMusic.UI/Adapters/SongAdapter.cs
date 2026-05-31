using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.UI.Platforms.Android;
using System.Collections.Concurrent;

namespace CatClawMusic.UI.Adapters;

/// <summary>
/// 歌曲列表的 RecyclerView 适配器，负责歌曲数据的展示、封面加载与播放状态管理。
/// <para>
/// 核心机制说明：
/// <list type="bullet">
///   <item><b>封面加载优先级</b>：内存缓存(LruCache) → 磁盘缓存(本地文件) → MediaStore(本地歌曲) → 网络下载(远程歌曲) → 插件扩展(ICoverProviderPlugin)</item>
///   <item><b>DiffUtil</b>：使用 SongDiffCallback 对新旧列表进行差异计算，仅更新发生变化的条目，避免全量刷新导致的闪烁和性能损耗</item>
///   <item><b>LruCache</b>：基于 Android LruCache 实现的 Bitmap 内存缓存，容量为应用最大可用内存的 1/8，以 KB 为单位计量，淘汰时自动回收 Bitmap</item>
///   <item><b>SemaphoreSlim</b>：并发信号量（最大4个并发），限制同时进行的网络封面下载数量，防止大量并发请求导致内存溢出或网络拥塞</item>
///   <item><b>ScrollListener</b>：滚动监听器，在滚动过程中暂停网络封面加载，滚动停止后补加载可见条目的封面</item>
///   <item><b>CancellationTokenSource</b>：每个 ViewHolder 绑定时创建新的 CTS，当 ViewHolder 被回收或重新绑定时取消上一次的封面加载任务，防止图片错位</item>
/// </list>
/// </para>
/// </summary>
public class SongAdapter : RecyclerView.Adapter
{
    /// <summary>
    /// 当前展示的歌曲列表
    /// </summary>
    private List<Song> _songs = new();
    private readonly Dictionary<int, int> _songIdToIndex = new();

    /// <summary>
    /// 网络音乐服务实例，用于获取远程封面和连接配置。可为 null（纯本地模式）
    /// </summary>
    private readonly INetworkMusicService? _networkMusic;

    /// <summary>
    /// 缓存的 Navidrome 协议连接配置，避免重复查询
    /// </summary>
    private ConnectionProfile? _cachedNavidromeProfile;

    /// <summary>
    /// 缓存的 WebDAV 协议连接配置，避免重复查询
    /// </summary>
    private ConnectionProfile? _cachedWebDavProfile;

    /// <summary>
    /// 缓存的 SMB 协议连接配置，避免重复查询
    /// </summary>
    private ConnectionProfile? _cachedSmbProfile;

    /// <summary>
    /// 标记是否已经查询过连接配置，防止多次异步调用时重复查询
    /// </summary>
    private bool _profilesLookedUp;

    /// <summary>
    /// 当前正在播放的歌曲 ID，用于高亮显示正在播放的歌曲标题
    /// </summary>
    private int _currentPlayingSongId = -1;

    /// <summary>
    /// 当前是否处于播放状态，用于控制播放动画图标的显示
    /// </summary>
    private bool _isPlaying = false;

    private bool _multiSelectMode;
    private readonly HashSet<int> _selectedSongIds = new();

    /// <summary>
    /// 标记列表是否正在滚动（volatile 保证多线程可见性）。
    /// 滚动期间暂停网络封面加载，停止后补加载。
    /// </summary>
    private volatile bool _isScrolling;

    /// <summary>
    /// 正在加载中的封面任务字典（静态共享）。
    /// Key 为 "song_{songId}"，Value 为对应的加载 Task。
    /// 用于防止同一首歌曲的封面被重复加载。
    /// </summary>
    private static readonly ConcurrentDictionary<string, Task> _loadingCovers = new();

    /// <summary>
    /// 封面加载并发信号量，初始计数和最大并发数均为 4。
    /// 通过信号量限制同时进行的网络封面下载数量，避免内存溢出和网络拥塞。
    /// </summary>
    private static readonly SemaphoreSlim _coverLoadSemaphore = new(4, 4);

    /// <summary>
    /// 主线程 Handler，用于将封面加载结果从后台线程投递到主线程更新 UI
    /// </summary>
    private static readonly Handler _mainHandler = new(Looper.MainLooper!);

    /// <summary>
    /// Bitmap 内存缓存实例（静态共享），所有 SongAdapter 实例共用同一缓存
    /// </summary>
    private static readonly BitmapLruCache _bitmapCache;

    /// <summary>
    /// 静态构造函数，初始化 Bitmap 内存缓存。
    /// 缓存容量设为应用最大可用内存的 1/8（以 KB 为单位），
    /// 这是 Android 推荐的图片缓存容量比例，在内存紧张时系统会自动回收。
    /// </summary>
    static SongAdapter()
    {
        var maxMemory = (int)(Java.Lang.Runtime.GetRuntime()!.MaxMemory() / 1024);
        var cacheSize = maxMemory / 4;
        _bitmapCache = new BitmapLruCache(cacheSize);
    }

    /// <summary>
    /// 基于 Android LruCache 的 Bitmap 内存缓存实现。
    /// <para>
    /// 核心机制：
    /// <list type="bullet">
    ///   <item>以 KB 为单位计量每个 Bitmap 的大小，而非默认的条目数量</item>
    ///   <item>当缓存总大小超过设定容量时，自动淘汰最近最少使用的条目</item>
    ///   <item>条目被淘汰时不再调用 Bitmap.Recycle()，避免 ImageView 仍在引用时
    ///         触发 "Canvas: trying to use a recycled bitmap" 崩溃。
    ///         Android 8.0+ 的 Bitmap 像素数据在 Java 堆中由 GC 管理；
    ///         API 24-25 的 Bitmap finalizer 也会释放原生内存</item>
    /// </list>
    /// </para>
    /// </summary>
    private class BitmapLruCache : LruCache
    {
        /// <summary>
        /// 初始化 BitmapLruCache 实例
        /// </summary>
        /// <param name="size">缓存最大容量（KB）</param>
        public BitmapLruCache(int size) : base(size) { }

        /// <summary>
        /// 计算缓存条目的大小（以 KB 为单位）。
        /// 覆盖 LruCache 默认的按条目计数方式，改为按 Bitmap 实际占用内存大小计量，
        /// 使缓存容量控制更精确。
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="value">缓存值（Bitmap 的 Java 包装）</param>
        /// <returns>Bitmap 占用内存大小（KB），若非 Bitmap 则返回 0</returns>
        protected override int SizeOf(Java.Lang.Object? key, Java.Lang.Object? value)
        {
            if (value is Bitmap b)
                return b.ByteCount / 1024;
            var bitmap = value?.JavaCast<Bitmap>();
            if (bitmap == null) return 0;
            return bitmap.ByteCount / 1024;
        }

        /// <summary>
        /// 条目被移除时的回调。当条目因缓存淘汰(evicted)或被替换时触发。
        /// 自动回收被移除的 Bitmap 原生内存，防止内存泄漏。
        /// </summary>
        /// <param name="evicted">是否因缓存空间不足被淘汰（true=淘汰，false=被替换）</param>
        /// <param name="key">被移除条目的键</param>
        /// <param name="oldValue">被移除条目的旧值</param>
        /// <param name="newValue">替换的新值（仅替换时有值）</param>
        protected override void EntryRemoved(bool evicted, Java.Lang.Object? key, Java.Lang.Object? oldValue, Java.Lang.Object? newValue)
        {
            /* 不再主动 Recycle：LruCache 淘汰条目时，ImageView 可能仍持有该 Bitmap 引用，
             * 调用 Recycle() 会导致 "trying to use a recycled bitmap" 崩溃。
             * 交由 GC 回收：Android 8.0+ Bitmap 像素在 Java 堆中由 GC 自动管理；
             * 低版本 API 的 Bitmap finalizer 也会释放原生内存 */
        }

        /// <summary>
        /// 从缓存中获取 Bitmap。处理了 Java 侧类型转换的兼容性问题。
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <returns>缓存中的 Bitmap，若不存在则返回 null</returns>
        public Bitmap? GetBitmap(string key)
        {
            var val = Get(key);
            if (val == null) return null;
            if (val is Bitmap b) return b;
            return val.JavaCast<Bitmap>();
        }

        /// <summary>
        /// 将 Bitmap 存入缓存
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="bitmap">要缓存的 Bitmap</param>
        public void PutBitmap(string key, Bitmap bitmap) => Put(key, bitmap);
    }

    /// <summary>
    /// 歌曲单击事件，当用户点击某首歌曲时触发
    /// </summary>
    public event EventHandler<Song>? SongClicked;

    /// <summary>
    /// 歌曲长按事件，当用户长按某首歌曲时触发（通常用于弹出上下文菜单）
    /// </summary>
    public event EventHandler<Song>? SongLongClicked;

    public event EventHandler? SelectionChanged;

    /// <summary>
    /// 最后一次长按操作的锚点 View，用于定位弹出菜单的位置
    /// </summary>
    public View? LastLongClickedView { get; private set; }

    /// <summary>
    /// 构造函数，初始化 SongAdapter 实例
    /// </summary>
    /// <param name="networkMusic">网络音乐服务实例（可选），传入 null 时为纯本地模式</param>
    public SongAdapter(INetworkMusicService? networkMusic = null)
    {
        _networkMusic = networkMusic;
    }

    /// <summary>
    /// 设置列表滚动状态。滚动期间暂停网络封面加载，停止后由 ScrollListener 补加载。
    /// </summary>
    /// <param name="scrolling">是否正在滚动</param>
    public void SetScrolling(bool scrolling) => _isScrolling = scrolling;

    public void SetMultiSelectMode(bool enabled)
    {
        _multiSelectMode = enabled;
        if (!enabled) _selectedSongIds.Clear();
        NotifyDataSetChanged();
    }

    private bool _customSortMode;

    public void SetCustomSortMode(bool enabled)
    {
        _customSortMode = enabled;
        NotifyDataSetChanged();
    }

    public void ToggleSelection(int songId)
    {
        if (_selectedSongIds.Contains(songId))
            _selectedSongIds.Remove(songId);
        else
            _selectedSongIds.Add(songId);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public HashSet<int> GetSelectedSongIds() => _selectedSongIds;

    /// <summary>
    /// 使用 DiffUtil 差异计算更新歌曲列表。
    /// <para>
    /// DiffUtil 会对比新旧列表，仅对发生变化的条目发送增量更新通知，
    /// 避免全量 NotifyDataSetChanged() 导致的闪烁和滚动位置重置问题。
    /// </para>
    /// </summary>
    /// <param name="songs">新的歌曲列表</param>
    public void UpdateSongs(IEnumerable<Song> songs)
    {
        var oldList = _songs;
        List<Song> newList;
        if (songs is List<Song> list)
            newList = list;
        else
            newList = songs.ToList();
        _songs = newList;
        _songIdToIndex.Clear();
        for (int i = 0; i < newList.Count; i++)
            _songIdToIndex[newList[i].Id] = i;
        var diffResult = DiffUtil.CalculateDiff(new SongDiffCallback(oldList, newList));
        diffResult.DispatchUpdatesTo(this);
    }

    /// <summary>
    /// 在列表末尾追加一批歌曲，并发送范围插入通知
    /// </summary>
    /// <param name="songs">要追加的歌曲列表</param>
    public void AddRange(IList<Song> songs)
    {
        if (songs.Count == 0) return;
        int startPos = _songs.Count;
        _songs.AddRange(songs);
        for (int i = 0; i < songs.Count; i++)
            _songIdToIndex[songs[i].Id] = startPos + i;
        NotifyItemRangeInserted(startPos, songs.Count);
    }

    /// <summary>
    /// 清空歌曲列表，并发送范围移除通知
    /// </summary>
    public void Clear()
    {
        int count = _songs.Count;
        _songs.Clear();
        _songIdToIndex.Clear();
        if (count > 0) NotifyItemRangeRemoved(0, count);
    }

    /// <summary>
    /// 获取列表中的歌曲总数
    /// </summary>
    public override int ItemCount => _songs.Count;

    /// <summary>
    /// 获取指定位置歌曲的唯一标识（Song.Id）。
    /// 由于 HasStableIds = true，RecyclerView 会利用此 ID 优化动画和复用逻辑。
    /// </summary>
    /// <param name="position">列表位置</param>
    /// <returns>歌曲的唯一 ID</returns>
    public override long GetItemId(int position)
    {
        return _songs[position].Id;
    }

    internal int GetSongPosition(int songId)
    {
        return _songIdToIndex.TryGetValue(songId, out var idx) ? idx : -1;
    }

    /// <summary>
    /// 创建歌曲列表项的 ViewHolder，加载 item_song 布局并绑定点击/长按事件
    /// </summary>
    /// <param name="parent">父容器</param>
    /// <param name="viewType">视图类型（本适配器仅有一种类型）</param>
    /// <returns>SongViewHolder 实例</returns>
    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var view = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_song, parent, false)!;
        return new SongViewHolder(view, OnSongClick, OnSongLongClick);
    }

    /// <summary>
    /// 绑定歌曲数据到 ViewHolder，触发封面加载和播放状态更新
    /// </summary>
    /// <param name="holder">ViewHolder 实例</param>
    /// <param name="position">列表位置</param>
    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        ((SongViewHolder)holder).Bind(_songs[position], this);
    }

    /// <summary>
    /// 当 ViewHolder 被回收时取消正在进行的封面加载任务，防止图片错位和资源浪费
    /// </summary>
    /// <param name="holder">被回收的 ViewHolder</param>
    public override void OnViewRecycled(Java.Lang.Object holder)
    {
        ((SongViewHolder)holder).CancelLoad();
        base.OnViewRecycled(holder);
    }

    /// <summary>
    /// 歌曲点击事件处理器，触发 SongClicked 事件
    /// </summary>
    /// <param name="position">被点击歌曲的位置</param>
    private void OnSongClick(int position)
    {
        if (position >= 0 && position < _songs.Count)
            SongClicked?.Invoke(this, _songs[position]);
    }

    /// <summary>
    /// 歌曲长按事件处理器，记录锚点 View 并触发 SongLongClicked 事件
    /// </summary>
    /// <param name="position">被长按歌曲的位置</param>
    /// <param name="anchor">长按的锚点 View，用于定位弹出菜单</param>
    private void OnSongLongClick(int position, View anchor)
    {
        if (_customSortMode) return;
        if (position >= 0 && position < _songs.Count)
        {
            LastLongClickedView = anchor;
            SongLongClicked?.Invoke(this, _songs[position]);
        }
    }

    /// <summary>
    /// 更新当前播放状态，高亮正在播放的歌曲标题并显示播放动画。
    /// <para>
    /// 仅当播放歌曲 ID 发生变化时才遍历列表刷新相关条目，
    /// 避免不必要的 UI 更新。
    /// </para>
    /// </summary>
    /// <param name="currentSongId">当前播放歌曲的 ID</param>
    /// <param name="isPlaying">是否正在播放</param>
    public void UpdatePlayState(int currentSongId, bool isPlaying)
    {
        var oldId = _currentPlayingSongId;
        _currentPlayingSongId = currentSongId;
        _isPlaying = isPlaying;

        if (oldId == currentSongId) return;

        for (int i = 0; i < _songs.Count; i++)
        {
            if (_songs[i].Id == oldId || _songs[i].Id == currentSongId)
                NotifyItemChanged(i);
        }
    }

    /// <summary>
    /// 异步获取指定协议类型的网络连接配置。
    /// <para>
    /// 采用懒加载 + 缓存策略：首次调用时从网络服务查询所有配置并缓存，
    /// 后续调用直接返回缓存结果，避免重复网络请求。
    /// 通过 _profilesLookedUp 标志确保只查询一次。
    /// </para>
    /// </summary>
    /// <param name="protocol">协议类型（Navidrome / WebDAV / SMB）</param>
    /// <returns>匹配的连接配置，未找到或查询失败时返回 null</returns>
    internal async Task<ConnectionProfile?> GetNetworkProfileAsync(ProtocolType protocol)
    {
        if (protocol == ProtocolType.Navidrome && _cachedNavidromeProfile != null) return _cachedNavidromeProfile;
        if (protocol == ProtocolType.WebDAV && _cachedWebDavProfile != null) return _cachedWebDavProfile;
        if (protocol == ProtocolType.SMB && _cachedSmbProfile != null) return _cachedSmbProfile;
        if (_networkMusic == null || _profilesLookedUp) return null;
        _profilesLookedUp = true;
        try
        {
            var profiles = await _networkMusic.GetProfilesAsync();
            _cachedNavidromeProfile = profiles.FirstOrDefault(p => p.Protocol == ProtocolType.Navidrome && p.IsEnabled);
            _cachedWebDavProfile = profiles.FirstOrDefault(p => p.Protocol == ProtocolType.WebDAV && p.IsEnabled);
            _cachedSmbProfile = profiles.FirstOrDefault(p => p.Protocol == ProtocolType.SMB && p.IsEnabled);
            return protocol == ProtocolType.Navidrome ? _cachedNavidromeProfile
                : protocol == ProtocolType.SMB ? _cachedSmbProfile : _cachedWebDavProfile;
        }
        catch { return null; }
    }

    /// <summary>
    /// 解码图片文件为采样后的 Bitmap，通过降采样减少内存占用。
    /// <para>
    /// 采样流程：
    /// <list type="number">
    ///   <item>第一次解码（InJustDecodeBounds=true）仅读取图片尺寸，不分配像素内存</item>
    ///   <item>根据目标尺寸计算采样率 InSampleSize（2 的幂次）</item>
    ///   <item>第二次解码使用采样率加载缩小后的图片，并使用 Rgb565 配置（每像素2字节）进一步减少内存</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="path">图片文件路径</param>
    /// <param name="reqWidth">目标宽度（像素）</param>
    /// <param name="reqHeight">目标高度（像素）</param>
    /// <returns>采样后的 Bitmap，解码失败返回 null</returns>
    private static Bitmap? DecodeSampledBitmap(string path, int reqWidth, int reqHeight)
    {
        try
        {
            var options = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeFile(path, options);

            var imageHeight = options.OutHeight;
            var imageWidth = options.OutWidth;
            if (imageWidth <= 0 || imageHeight <= 0) return null;

            int inSampleSize = 1;
            if (imageHeight > reqHeight || imageWidth > reqWidth)
            {
                var halfHeight = imageHeight / 2;
                var halfWidth = imageWidth / 2;
                while ((halfHeight / inSampleSize) >= reqHeight && (halfWidth / inSampleSize) >= reqWidth)
                    inSampleSize *= 2;
            }

            options.InJustDecodeBounds = false;
            options.InSampleSize = inSampleSize;
            options.InPreferredConfig = Bitmap.Config.Rgb565;
            return BitmapFactory.DecodeFile(path, options);
        }
        catch { return null; }
    }

    /// <summary>
    /// 歌曲列表差异计算回调，用于 DiffUtil 对比新旧列表的差异。
    /// <para>
    /// DiffUtil 算法基于 Eugene W. Myers 的差异算法，时间复杂度 O(N)，
    /// 通过 AreItemsTheSame 判断是否为同一首歌（按 ID），
    /// 通过 AreContentsTheSame 判断内容是否发生变化（标题、艺术家、专辑、来源），
    /// 从而实现最小化增量更新。
    /// </para>
    /// </summary>
    private class SongDiffCallback : DiffUtil.Callback
    {
        private readonly List<Song> _oldList;
        private readonly List<Song> _newList;

        /// <summary>
        /// 创建差异计算回调
        /// </summary>
        /// <param name="oldList">旧歌曲列表</param>
        /// <param name="newList">新歌曲列表</param>
        public SongDiffCallback(List<Song> oldList, List<Song> newList)
        {
            _oldList = oldList;
            _newList = newList;
        }

        /// <summary>
        /// 旧列表大小
        /// </summary>
        public override int OldListSize => _oldList.Count;

        /// <summary>
        /// 新列表大小
        /// </summary>
        public override int NewListSize => _newList.Count;

        /// <summary>
        /// 判断两个位置上的条目是否代表同一个项目（按 Song.Id 比较）。
        /// 这是 DiffUtil 判断条目是否被移动、删除或新增的依据。
        /// </summary>
        /// <param name="oldItemPosition">旧列表位置</param>
        /// <param name="newItemPosition">新列表位置</param>
        /// <returns>ID 相同返回 true</returns>
        public override bool AreItemsTheSame(int oldItemPosition, int newItemPosition)
            => _oldList[oldItemPosition].Id == _newList[newItemPosition].Id;

        /// <summary>
        /// 判断两个位置上的条目内容是否相同。
        /// 当 AreItemsTheSame 返回 true 时调用，比较标题、艺术家、专辑和来源。
        /// 仅当内容发生变化时才触发条目的变更动画。
        /// </summary>
        /// <param name="oldItemPosition">旧列表位置</param>
        /// <param name="newItemPosition">新列表位置</param>
        /// <returns>内容完全相同返回 true</returns>
        public override bool AreContentsTheSame(int oldItemPosition, int newItemPosition)
        {
            var old = _oldList[oldItemPosition];
            var @new = _newList[newItemPosition];
            return old.Title == @new.Title
                && old.Artist == @new.Artist
                && old.Album == @new.Album
                && old.Source == @new.Source;
        }
    }

    /// <summary>
    /// 歌曲列表项的 ViewHolder，负责歌曲信息的展示和封面加载。
    /// <para>
    /// 封面加载优先级（在 Bind 方法中依次尝试）：
    /// <list type="number">
    ///   <item><b>内存缓存</b>：从 BitmapLruCache 中直接获取，命中时立即显示，无异步开销</item>
    ///   <item><b>磁盘缓存</b>：检查本地缓存文件是否存在，存在则异步解码并显示</item>
    ///   <item><b>MediaStore</b>（仅本地歌曲）：通过 Android MediaStore API 获取系统媒体库中的封面</item>
    ///   <item><b>网络下载</b>（仅远程歌曲，且非滚动状态）：通过 SemaphoreSlim 限流后从网络获取封面</item>
    ///   <item><b>插件扩展</b>：当以上方式均未获取到封面时，尝试通过 ICoverProviderPlugin 插件获取</item>
    /// </list>
    /// </para>
    /// <para>
    /// 防错位机制：每次 Bind 时创建新的 CancellationTokenSource，CancelLoad 时取消上一次任务，
    /// 确保 ViewHolder 复用后旧任务不会覆盖新数据。
    /// </para>
    /// </summary>
    private class SongViewHolder : RecyclerView.ViewHolder
    {
        private readonly TextView _title, _artist, _album;
        private readonly ImageView _cover;
        private readonly Helpers.WaveformView _pauseIcon;
        private readonly CheckBox _checkbox;
        private readonly View _cardView;

        /// <summary>
        /// 当前 ViewHolder 绑定的歌曲 ID，用于判断封面加载完成时是否仍对应当前歌曲（防错位）
        /// </summary>
        private int _boundSongId;

        /// <summary>
        /// 封面加载任务的取消令牌源，ViewHolder 被回收或重新绑定时取消进行中的加载任务
        /// </summary>
        private CancellationTokenSource? _coverCts;

        /// <summary>
        /// 当前已加载封面的缓存键，避免重复加载同一封面
        /// </summary>
        private string? _loadedCoverKey;

        private EventHandler? _checkboxClickHandler;

        /// <summary>
        /// 缓存的非播放状态标题颜色，从主题属性解析一次后复用，避免每次 Bind 都解析主题
        /// </summary>
        private static Color? _cachedTitleColor;
        private static Color? _cachedPlayingColor;

        /// <summary>
        /// 创建 SongViewHolder 实例，初始化视图引用和事件绑定
        /// </summary>
        /// <param name="view">列表项根视图</param>
        /// <param name="onClick">单击回调</param>
        /// <param name="onLongClick">长按回调（可为 null）</param>
        public SongViewHolder(View view, Action<int> onClick, Action<int, View>? onLongClick) : base(view)
        {
            _title = view.FindViewById<TextView>(Resource.Id.song_title)!;
            _artist = view.FindViewById<TextView>(Resource.Id.song_artist)!;
            _album = view.FindViewById<TextView>(Resource.Id.song_album)!;
            _cover = view.FindViewById<ImageView>(Resource.Id.song_cover)!;
            _pauseIcon = view.FindViewById<Helpers.WaveformView>(Resource.Id.playing_pause_icon)!;
            _checkbox = view.FindViewById<CheckBox>(Resource.Id.checkbox_select)!;
            _title.ImportantForAutofill = ImportantForAutofill.No;
            _artist.ImportantForAutofill = ImportantForAutofill.No;
            _album.ImportantForAutofill = ImportantForAutofill.No;
            view.Click += (s, e) => onClick(BindingAdapterPosition);
            if (onLongClick != null)
                view.LongClick += (s, e) => { onLongClick(BindingAdapterPosition, (View)s!); };
        }

        /// <summary>
        /// 将歌曲数据绑定到 ViewHolder，按优先级加载封面并更新播放状态显示。
        /// <para>
        /// 封面加载优先级：
        /// <list type="number">
        ///   <item>内存缓存（LruCache） → 直接显示</item>
        ///   <item>磁盘缓存文件 → 异步解码显示</item>
        ///   <item>MediaStore（本地歌曲）→ 异步查询显示</item>
        ///   <item>网络下载（远程歌曲，非滚动时）→ 限流下载显示</item>
        /// </list>
        /// 每次绑定都会取消上一次的封面加载任务（防错位）。
        /// </para>
        /// </summary>
        /// <param name="song">要绑定的歌曲数据</param>
        /// <param name="adapter">所属的 SongAdapter 实例</param>
        public void Bind(Song song, SongAdapter adapter)
        {
            _title.Text = song.Title ?? "未知歌曲";
            _artist.Text = string.IsNullOrEmpty(song.Artist) ? "未知艺术家" : song.Artist;
            _album.Text = song.Album ?? "";
            _boundSongId = song.Id;

            if (adapter._multiSelectMode)
            {
                _checkbox.Visibility = ViewStates.Visible;
                _checkbox.Checked = adapter._selectedSongIds.Contains(song.Id);
                _checkbox.Click -= _checkboxClickHandler;
                _checkboxClickHandler = (s, e) =>
                {
                    adapter.ToggleSelection(song.Id);
                    _checkbox.Checked = adapter._selectedSongIds.Contains(song.Id);
                };
                _checkbox.Click += _checkboxClickHandler;
            }
            else
            {
                _checkbox.Visibility = ViewStates.Gone;
            }

            if (song.Id == adapter._currentPlayingSongId)
            {
                _cachedPlayingColor ??= Color.ParseColor("#9B7ED8");
                _title.SetTextColor(_cachedPlayingColor.Value);
                _pauseIcon.SetPlaying(adapter._isPlaying);
            }
            else
            {
                if (_cachedTitleColor == null)
                {
                    var typedValue = new Android.Util.TypedValue();
                    _cachedTitleColor = ItemView.Context?.Theme?.ResolveAttribute(Resource.Attribute.catClawTextPrimary, typedValue, true) == true
                        ? new Color(typedValue.Data)
                        : Color.Black;
                }
                _title.SetTextColor(_cachedTitleColor.Value);
                _pauseIcon.SetPlaying(false);
            }

            _coverCts?.Cancel();
            _coverCts?.Dispose();
            _coverCts = new CancellationTokenSource();
            var ct = _coverCts.Token;

            var coverKey = $"cover_{song.Id}";
            var cachedBitmap = _bitmapCache.GetBitmap(coverKey);
            if (cachedBitmap != null && !cachedBitmap.IsRecycled)
            {
                _cover.SetImageBitmap(cachedBitmap);
                _loadedCoverKey = coverKey;
                return;
            }

            var coverPath = GetCoverCachedPath(song.Id);
            if (System.IO.File.Exists(coverPath))
            {
                if (_loadedCoverKey != coverKey)
                {
                    _loadedCoverKey = coverKey;
                    _ = LoadCachedCoverAsync(coverPath, coverKey, song.Id, adapter, ct);
                }
            }
            else if (song.Source == SongSource.Local)
            {
                _loadedCoverKey = null;
                _ = LoadMediaStoreCoverAsync(song, adapter, ct);
            }
            else
            {
                _loadedCoverKey = null;
                _cover.SetImageResource(Resource.Drawable.cover_default);

                if (!adapter._isScrolling)
                {
                    var loadKey = $"song_{song.Id}";
                    if (!_loadingCovers.TryGetValue(loadKey, out var existingTask) || existingTask.IsCompleted)
                    {
                        var loadTask = LoadCoverWithThrottleAsync(song, adapter, ct);
                        _loadingCovers[loadKey] = loadTask;
                        _ = loadTask.ContinueWith(_ => _loadingCovers.TryRemove(loadKey, out _));
                    }
                }
            }
        }

        /// <summary>
        /// 从磁盘缓存文件异步加载封面图片。
        /// <para>
        /// 在后台线程解码图片（DecodeSampledBitmap），解码完成后通过主线程 Handler 更新 UI。
        /// 如果解码成功且当前 ViewHolder 仍绑定同一首歌，直接显示；
        /// 否则将 Bitmap 存入缓存并通知对应位置的条目刷新。
        /// 如果解码失败，删除无效的缓存文件。
        /// </para>
        /// </summary>
        /// <param name="coverPath">磁盘缓存文件路径</param>
        /// <param name="coverKey">内存缓存键</param>
        /// <param name="songId">歌曲 ID</param>
        /// <param name="adapter">所属适配器</param>
        /// <param name="ct">取消令牌</param>
        private async Task LoadCachedCoverAsync(string coverPath, string coverKey, int songId, SongAdapter adapter, CancellationToken ct)
        {
            var bitmap = await Task.Run(() => DecodeSampledBitmap(coverPath, 120, 120), ct);
            ct.ThrowIfCancellationRequested();
            _mainHandler.Post(() =>
            {
                if (bitmap != null)
                {
                    _bitmapCache.PutBitmap(coverKey, bitmap);
                    if (_boundSongId == songId)
                    {
                        _cover.SetImageBitmap(bitmap);
                    }
                    else
                    {
                        var pos = adapter.GetSongPosition(songId);
                        if (pos >= 0)
                            try { adapter.NotifyItemChanged(pos); } catch { }
                    }
                }
                else
                {
                    if (_boundSongId == songId)
                        _cover.SetImageResource(Resource.Drawable.cover_default);
                    try { System.IO.File.Delete(coverPath); } catch { }
                }
            });
        }

        /// <summary>
        /// 通过 Android MediaStore 异步加载本地歌曲的封面。
        /// <para>
        /// 两种加载路径：
        /// <list type="number">
        ///   <item>若 MediaStoreId 无效但有文件路径，先通过文件路径查询 MediaStore 获取 ID，再加载封面</item>
        ///   <item>若 MediaStoreId 有效，直接通过 ID 加载封面</item>
        /// </list>
        /// 如果 MediaStore 中未找到封面，且当前非滚动状态，会回退到网络/插件加载。
        /// </para>
        /// </summary>
        /// <param name="song">歌曲数据</param>
        /// <param name="adapter">所属适配器</param>
        /// <param name="ct">取消令牌</param>
        private async Task LoadMediaStoreCoverAsync(Song song, SongAdapter adapter, CancellationToken ct)
        {
            long msId = song.MediaStoreId;
            if (msId <= 0 && !string.IsNullOrEmpty(song.FilePath))
            {
                if (song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
                {
                    if (!adapter._isScrolling)
                    {
                        var loadKey = $"song_{song.Id}";
                        if (!_loadingCovers.TryGetValue(loadKey, out var existingTask) || existingTask.IsCompleted)
                        {
                            var loadTask = LoadCoverWithThrottleAsync(song, adapter, ct);
                            _loadingCovers[loadKey] = loadTask;
                            _ = loadTask.ContinueWith(_ => _loadingCovers.TryRemove(loadKey, out _));
                        }
                    }
                    return;
                }

                var (bitmap0, foundId) = await Task.Run(() => MediaStoreCoverHelper.LoadCoverByFilePath(song.FilePath, 120), ct);
                if (foundId > 0) song.MediaStoreId = foundId;
                ct.ThrowIfCancellationRequested();
                _mainHandler.Post(() =>
                {
                    if (bitmap0 != null)
                    {
                        var coverKey0 = $"cover_{song.Id}";
                        _bitmapCache.PutBitmap(coverKey0, bitmap0);
                        if (_boundSongId == song.Id)
                        {
                            _cover.SetImageBitmap(bitmap0);
                            _loadedCoverKey = coverKey0;
                        }
                        else
                        {
                            var pos = adapter.GetSongPosition(song.Id);
                            if (pos >= 0)
                                try { adapter.NotifyItemChanged(pos); } catch { }
                        }
                    }
                    else if (_boundSongId == song.Id)
                    {
                        _cover.SetImageResource(Resource.Drawable.cover_default);
                    }
                });
                return;
            }

            if (msId <= 0)
            {
                if (_boundSongId == song.Id)
                    _cover.SetImageResource(Resource.Drawable.cover_default);
                return;
            }

            var bitmap = await Task.Run(() => MediaStoreCoverHelper.LoadCoverFromMediaStore(msId, 120), ct);
            ct.ThrowIfCancellationRequested();
            _mainHandler.Post(() =>
            {
                if (bitmap != null)
                {
                    var coverKey = $"cover_{song.Id}";
                    _bitmapCache.PutBitmap(coverKey, bitmap);
                    if (_boundSongId == song.Id)
                    {
                        _cover.SetImageBitmap(bitmap);
                        _loadedCoverKey = coverKey;
                    }
                    else
                    {
                        var pos = adapter.GetSongPosition(song.Id);
                        if (pos >= 0)
                            try { adapter.NotifyItemChanged(pos); } catch { }
                    }
                }
                else
                {
                    if (_boundSongId == song.Id)
                        _cover.SetImageResource(Resource.Drawable.cover_default);
                    if (!adapter._isScrolling)
                    {
                        var loadKey = $"song_{song.Id}";
                        if (!_loadingCovers.TryGetValue(loadKey, out var existingTask) || existingTask.IsCompleted)
                        {
                            var loadTask = LoadCoverWithThrottleAsync(song, adapter, ct);
                            _loadingCovers[loadKey] = loadTask;
                            _ = loadTask.ContinueWith(_ => _loadingCovers.TryRemove(loadKey, out _));
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 取消当前进行中的封面加载任务。
        /// 在 ViewHolder 被回收或重新绑定时调用，防止图片错位和资源浪费。
        /// </summary>
        public void CancelLoad()
        {
            _coverCts?.Cancel();
            _coverCts?.Dispose();
            _coverCts = null;
        }

        /// <summary>
        /// 获取封面磁盘缓存文件路径。
        /// 缓存目录为应用缓存目录下的 "covers" 子目录，文件名格式为 "cover_{songId}.jpg"。
        /// </summary>
        /// <param name="songId">歌曲 ID</param>
        /// <returns>缓存文件的完整路径</returns>
        private static string GetCoverCachedPath(int songId)
        {
            var cacheDir = System.IO.Path.Combine(
                Application.Context.CacheDir!.AbsolutePath, "covers");
            return System.IO.Path.Combine(cacheDir, $"cover_{songId}.jpg");
        }

        /// <summary>
        /// 带限流的封面加载方法，通过 SemaphoreSlim 控制最大 4 个并发下载。
        /// <para>
        /// 加载流程（按歌曲来源区分）：
        /// <list type="bullet">
        ///   <item><b>WebDAV/SMB 远程歌曲</b>：通过网络服务下载封面字节流（5秒超时），
        ///   自动识别 Navidrome 协议并选择对应的连接配置</item>
        ///   <item><b>本地歌曲（MediaStoreId 有效）</b>：从 MediaStore 加载封面</item>
        ///   <item><b>本地歌曲（content:// URI）</b>：通过 MediaMetadataRetriever 提取嵌入封面</item>
        ///   <item><b>本地歌曲（文件路径）</b>：通过 TagReader 提取嵌入封面</item>
        ///   <item><b>插件兜底</b>：以上方式均未获取到封面时，遍历所有启用的 ICoverProviderPlugin 尝试获取</item>
        /// </list>
        /// </para>
        /// <para>
        /// 获取到封面字节数据后，会写入磁盘缓存并解码为 Bitmap 显示。
        /// SemaphoreSlim 在 finally 块中释放，确保不会发生信号量泄漏。
        /// </para>
        /// </summary>
        /// <param name="song">歌曲数据</param>
        /// <param name="adapter">所属适配器</param>
        /// <param name="ct">取消令牌</param>
        private async Task LoadCoverWithThrottleAsync(Song song, SongAdapter adapter, CancellationToken ct)
        {
            await _coverLoadSemaphore.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                byte[]? coverBytes = null;

                if (song.Source == SongSource.WebDAV || song.Source == SongSource.SMB)
                {
                    var coverId = song.CoverArtPath ?? song.RemoteId;
                    if (!string.IsNullOrEmpty(coverId))
                    {
                        try
                        {
                            var isNavidrome = !string.IsNullOrEmpty(song.FilePath) && song.FilePath.Contains("stream.view?id=");
                            var protocol = isNavidrome ? ProtocolType.Navidrome
                                : song.Source == SongSource.SMB ? ProtocolType.SMB : ProtocolType.WebDAV;
                            var profile = await adapter.GetNetworkProfileAsync(protocol);
                            if (profile != null && adapter._networkMusic != null)
                            {
                                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                                cts.CancelAfter(TimeSpan.FromSeconds(5));
                                var stream = await adapter._networkMusic.GetCoverAsync(coverId, profile);
                                if (stream != null)
                                {
                                    using var ms = new MemoryStream();
                                    await stream.CopyToAsync(ms, cts.Token);
                                    cts.Token.ThrowIfCancellationRequested();
                                    coverBytes = ms.ToArray();
                                }
                            }
                        }
                        catch (System.OperationCanceledException) { }
                        catch { }
                    }
                }
                else
                {
                    if (song.MediaStoreId > 0)
                    {
                        var msBitmap = MediaStoreCoverHelper.LoadCoverFromMediaStore(song.MediaStoreId, 120);
                        if (msBitmap != null)
                        {
                            var coverKey2 = $"cover_{song.Id}";
                            _bitmapCache.PutBitmap(coverKey2, msBitmap);
                            _mainHandler.Post(() =>
                            {
                                if (_boundSongId == song.Id)
                                {
                                    _cover.SetImageBitmap(msBitmap);
                                    _loadedCoverKey = coverKey2;
                                }
                                else
                                {
                                    /* 不再 Recycle：msBitmap 已在缓存中，可能被其他 ViewHolder 引用，
                                     * 主动 Recycle 会导致 "trying to use a recycled bitmap" 崩溃 */
                                    var pos = adapter.GetSongPosition(song.Id);
                                    if (pos >= 0)
                                        try { adapter.NotifyItemChanged(pos); } catch { }
                                }
                            });
                            return;
                        }
                    }

                    if (song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
                    {
                        coverBytes = await Task.Run(() =>
                        {
                            try
                            {
                                var retriever = new Android.Media.MediaMetadataRetriever();
                                try
                                {
                                    retriever.SetDataSource(Application.Context,
                                        Android.Net.Uri.Parse(song.FilePath));
                                    var embedded = retriever.GetEmbeddedPicture();
                                    return embedded != null && embedded.Length > 0 ? embedded : null;
                                }
                                finally { retriever.Release(); }
                            }
                            catch { return null; }
                        }, ct);
                    }
                    else if (System.IO.File.Exists(song.FilePath))
                    {
                        coverBytes = await Task.Run(() =>
                            CatClawMusic.Core.Services.TagReader.ExtractCoverArt(song.FilePath), ct);
                    }
                }

                if (coverBytes == null)
                {
                    try
                    {
                        var pluginManager = MainApplication.Services.GetService(typeof(IPluginManager)) as IPluginManager;
                        if (pluginManager != null)
                        {
                            var coverProviders = pluginManager.GetEnabledPlugins<ICoverProviderPlugin>();
                            foreach (var provider in coverProviders)
                            {
                                ct.ThrowIfCancellationRequested();
                                try
                                {
                                    if (!provider.IsAvailable) continue;
                                    coverBytes = await provider.GetCoverAsync(song);
                                    if (coverBytes != null) break;
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }

                ct.ThrowIfCancellationRequested();
                if (coverBytes != null)
                {
                    var coverPath = GetCoverCachedPath(song.Id);
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(coverPath)!);
                    await System.IO.File.WriteAllBytesAsync(coverPath, coverBytes, ct);

                    var coverKey = $"cover_{song.Id}";
                    var bitmap = await Task.Run(() => DecodeSampledBitmap(coverPath, 120, 120), ct);
                    ct.ThrowIfCancellationRequested();

                    _mainHandler.Post(() =>
                    {
                        if (bitmap != null)
                        {
                            _bitmapCache.PutBitmap(coverKey, bitmap);
                            if (_boundSongId == song.Id)
                            {
                                _cover.SetImageBitmap(bitmap);
                                _loadedCoverKey = coverKey;
                            }
                            else
                            {
                                var pos = adapter.GetSongPosition(song.Id);
                                if (pos >= 0)
                                    try { adapter.NotifyItemChanged(pos); } catch { }
                            }
                        }
                        else
                        {
                            if (_boundSongId == song.Id)
                            {
                                _cover.SetImageResource(Resource.Drawable.cover_default);
                                try { System.IO.File.Delete(coverPath); } catch { }
                            }
                        }
                    });
                }
            }
            catch (System.OperationCanceledException) { }
            catch { }
            finally
            {
                _coverLoadSemaphore.Release();
            }
        }
    }

    /// <summary>
    /// RecyclerView 滚动状态监听器，实现滚动期间暂停网络封面加载、停止后补加载的优化策略。
    /// <para>
    /// 核心机制：
    /// <list type="bullet">
    ///   <item>滚动开始时（newState != 0）设置 _isScrolling = true，Bind 方法中跳过网络封面加载</item>
    ///   <item>滚动停止时（newState == 0）设置 _isScrolling = false，遍历当前可见条目，
    ///   对缺少封面的条目重新触发 Bind 以补加载封面</item>
    ///   <item>补加载时检查内存缓存和磁盘缓存，避免重复加载已有封面的条目</item>
    /// </list>
    /// 这种策略在快速滑动时显著减少网络请求和图片解码开销，提升滚动流畅度。
    /// </para>
    /// </summary>
    public class ScrollListener : RecyclerView.OnScrollListener
    {
        private readonly SongAdapter _adapter;

        /// <summary>
        /// 创建滚动监听器
        /// </summary>
        /// <param name="adapter">关联的 SongAdapter 实例</param>
        public ScrollListener(SongAdapter adapter)
        {
            _adapter = adapter;
        }

        /// <summary>
        /// 滚动状态变化回调。
        /// <para>
        /// newState 取值：
        /// <list type="bullet">
        ///   <item>SCROLL_STATE_IDLE (0)：空闲/停止滚动</item>
        ///   <item>SCROLL_STATE_DRAGGING (1)：用户手指拖动</item>
        ///   <item>SCROLL_STATE_SETTLING (2)：手指离开后的惯性滚动</item>
        /// </list>
        /// 仅在停止滚动（newState == 0）时补加载可见条目的封面。
        /// </para>
        /// </summary>
        /// <param name="recyclerView">RecyclerView 实例</param>
        /// <param name="newState">新的滚动状态</param>
        public override void OnScrollStateChanged(RecyclerView recyclerView, int newState)
        {
            _adapter.SetScrolling(newState != 0);
            if (newState == 0)
            {
                recyclerView.Post(() =>
                {
                    var lm = recyclerView.GetLayoutManager() as LinearLayoutManager;
                    if (lm == null) return;
                    int first = lm.FindFirstVisibleItemPosition();
                    int last = lm.FindLastVisibleItemPosition();
                    for (int i = first; i <= last; i++)
                    {
                        var vh = recyclerView.FindViewHolderForAdapterPosition(i);
                        if (vh is SongViewHolder svh && svh.BindingAdapterPosition == i)
                        {
                            if (i < _adapter._songs.Count)
                            {
                                var song = _adapter._songs[i];
                                var coverKey = $"cover_{song.Id}";
                                var cachedBitmap = _bitmapCache.GetBitmap(coverKey);
                                if (cachedBitmap != null && !cachedBitmap.IsRecycled) continue;

                                var coverPath = GetCoverCachedPathStatic(song.Id);
                                if (!System.IO.File.Exists(coverPath))
                                {
                                    var loadKey = $"song_{song.Id}";
                                    if (!_loadingCovers.TryGetValue(loadKey, out var et) || et.IsCompleted)
                                        svh.Bind(song, _adapter);
                                }
                                else
                                {
                                    svh.Bind(song, _adapter);
                                }
                            }
                        }
                    }
                });
            }
        }

        /// <summary>
        /// 获取封面磁盘缓存文件路径（静态版本，供 ScrollListener 调用）
        /// </summary>
        /// <param name="songId">歌曲 ID</param>
        /// <returns>缓存文件的完整路径</returns>
        private static string GetCoverCachedPathStatic(int songId)
        {
            var cacheDir = System.IO.Path.Combine(
                Application.Context.CacheDir!.AbsolutePath, "covers");
            return System.IO.Path.Combine(cacheDir, $"cover_{songId}.jpg");
        }
    }
}
