namespace CatClawMusic.Data;

/// <summary>随机抽样工具：partial Fisher-Yates，O(n) 拷贝 + O(k) 交换，
/// 替代 OrderBy(random.Next()).Take(k) 的 O(n log n) 全量排序与随机键分配。</summary>
public static class RandomSampler
{
    /// <summary>从 source 随机抽取最多 count 个不重复元素；不修改 source（可能是共享缓存列表）</summary>
    public static List<T> Sample<T>(IReadOnlyList<T> source, int count, Random? random = null)
    {
        random ??= Random.Shared;
        if (count <= 0 || source.Count == 0) return new List<T>();
        if (count >= source.Count) return source.ToList();

        // 拷贝一份，只对前 k 个位置做 Fisher-Yates 交换
        var pool = new List<T>(source);
        for (int i = 0; i < count; i++)
        {
            var j = random.Next(i, pool.Count);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        var result = new List<T>(count);
        for (int i = 0; i < count; i++) result.Add(pool[i]);
        return result;
    }
}
