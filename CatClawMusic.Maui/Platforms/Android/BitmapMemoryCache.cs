using Android.Graphics;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>
/// Android Bitmap 内存缓存：按文件路径缓存解码后的 Bitmap，
/// 避免 CollectionView 滑动时反复解码同一封面图片造成 GC 压力。
/// 使用 LRU 算法限制总占用大小为 32MB。
/// </summary>
internal static class BitmapMemoryCache
{
    // 32MB：64MB 的 Bitmap 全是 Java 堆对象（JNI 全局引用），常驻过多会加剧
    // Mono GC bridge 的 Runtime.gc() 频率（每 500ms 一次的 Explicit GC 风暴）。
    // 300px 封面约 360KB/张，32MB ≈ 90 张，足够覆盖列表滚动窗口。
    private const long MaxSizeBytes = 32L * 1024 * 1024;
    private static readonly object _lock = new();
    private static readonly LinkedList<string> _lruList = new();
    private static readonly Dictionary<string, Entry> _cache = new();
    private static long _totalSize;

    private struct Entry
    {
        public Bitmap Bitmap { get; set; }
        public long Size { get; set; }
        public LinkedListNode<string> Node { get; set; }
    }

    /// <summary>从缓存中获取 Bitmap，命中时将其移到 LRU 队首</summary>
    public static Bitmap? Get(string key)
    {
        lock (_lock)
        {
            if (!_cache.TryGetValue(key, out var entry)) return null;
            _lruList.Remove(entry.Node);
            _lruList.AddFirst(entry.Node);
            return entry.Bitmap;
        }
    }

    /// <summary>将 Bitmap 加入缓存，超限时按 LRU 策略驱逐旧条目</summary>
    public static void Put(string key, Bitmap bitmap)
    {
        var size = bitmap.ByteCount;
        lock (_lock)
        {
            // 已存在则先移除旧条目
            if (_cache.TryGetValue(key, out var existing))
            {
                _lruList.Remove(existing.Node);
                _cache.Remove(key);
                _totalSize -= existing.Size;
            }

            var node = _lruList.AddFirst(key);
            _cache[key] = new Entry { Bitmap = bitmap, Size = size, Node = node };
            _totalSize += size;

            // 超限时从队尾驱逐（不 recycle，bitmap 可能仍被 ImageView 引用，由 GC 回收）
            while (_totalSize > MaxSizeBytes && _lruList.Count > 0)
            {
                var lastKey = _lruList.Last!.Value;
                _lruList.RemoveLast();
                if (_cache.TryGetValue(lastKey, out var evict))
                {
                    _cache.Remove(lastKey);
                    _totalSize -= evict.Size;
                }
            }
        }
    }

    /// <summary>清空缓存（不 recycle bitmap）</summary>
    public static void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
            _lruList.Clear();
            _totalSize = 0;
        }
    }

    /// <summary>渐进式驱逐：按 LRU 从队尾驱逐，使总占用降至上限的 keepFraction。
    /// 用于 Android OnTrimMemory 内存裁剪——全量 Clear 会导致回前台时所有封面
    /// 重新解码（解码风暴 + GC 压力），渐进驱逐保留热数据。
    /// 目标基于 MaxSizeBytes 计算而非当前 _totalSize，保证多次调用幂等
    /// （OnLowMemory 与 OnTrimMemory(Critical) 先后触发时不会连续腰斩）。</summary>
    /// <param name="keepFraction">保留比例（0~1），默认保留 50%</param>
    public static void Trim(double keepFraction = 0.5)
    {
        lock (_lock)
        {
            var targetSize = (long)(MaxSizeBytes * Math.Clamp(keepFraction, 0, 1));
            while (_totalSize > targetSize && _lruList.Count > 0)
            {
                var lastKey = _lruList.Last!.Value;
                _lruList.RemoveLast();
                if (_cache.TryGetValue(lastKey, out var evict))
                {
                    _cache.Remove(lastKey);
                    _totalSize -= evict.Size;
                }
            }
        }
    }
}
