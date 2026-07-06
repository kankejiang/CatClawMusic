using Android.Graphics;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>
/// Android Bitmap 内存缓存：按文件路径缓存解码后的 Bitmap，
/// 避免 CollectionView 滑动时反复解码同一封面图片造成 GC 压力。
/// 使用 LRU 算法限制总占用大小为 64MB。
/// </summary>
internal static class BitmapMemoryCache
{
    private const long MaxSizeBytes = 64L * 1024 * 1024;
    private static readonly object _lock = new();
    private static readonly LinkedList<string> _lruList = new();
    private static readonly Dictionary<string, Entry> _cache = new();
    private static long _totalSize;

    private struct Entry
    {
        public Bitmap Bitmap;
        public long Size;
        public LinkedListNode<string> Node;
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
}
