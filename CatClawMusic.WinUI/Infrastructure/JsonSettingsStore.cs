using System.Text.Json;

namespace CatClawMusic.WinUI.Infrastructure;

/// <summary>
/// 轻量键值设置存储（替代 MAUI 的 <c>Preferences.Default</c>）。
/// 单文件 JSON，进程内加锁；设置量小、写入不频繁，够用且便于用户直接查看/备份。
/// </summary>
public sealed class JsonSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly object _gate = new();
    private readonly Dictionary<string, JsonElement> _values;

    public JsonSettingsStore(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _values = Load(path);
    }

    /// <summary>读取设置；键不存在或反序列化失败时返回 <paramref name="fallback"/>。</summary>
    public T Get<T>(string key, T fallback)
    {
        lock (_gate)
        {
            if (!_values.TryGetValue(key, out var element))
                return fallback;

            try
            {
                return element.Deserialize<T>(SerializerOptions) ?? fallback;
            }
            catch (JsonException)
            {
                return fallback;
            }
        }
    }

    /// <summary>写入设置并立即落盘。</summary>
    public void Set<T>(string key, T value)
    {
        lock (_gate)
        {
            _values[key] = JsonSerializer.SerializeToElement(value, SerializerOptions);
            Save();
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_values, SerializerOptions));
        }
        catch (IOException)
        {
            // 设置写失败不应影响使用：内存值仍然生效，下次写入再试。
        }
    }

    private static Dictionary<string, JsonElement> Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new Dictionary<string, JsonElement>(StringComparer.Ordinal);

            var json = File.ReadAllText(path);
            var map = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, SerializerOptions);
            return map is null
                ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                : new Dictionary<string, JsonElement>(map, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }
    }
}
