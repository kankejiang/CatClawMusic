using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.JavaPlugin;

/// <summary>
/// Java 插件 C# 适配器 - 将 IKVM 编译后的 Java 类桥接到 IPlugin 接口。
/// 编译时引用 IKVM 生成的 DLL，运行时通过反射或直接引用调用 Java 方法。
/// </summary>
public class JavaPluginAdapter : ILyricsProviderPlugin, ICoverProviderPlugin, IMenuContributorPlugin
{
    private readonly dynamic _javaInstance;

    public JavaPluginAdapter()
    {
        // 通过 IKVM 编译后，Java 类成为普通 .NET 类
        _javaInstance = Activator.CreateInstance(Type.GetType("MyPlugin")!)!;
    }

    public string PluginId => "java.plugin";
    public string Name => "Java 插件";
    public string Version => _javaInstance.getVersion();
    public string Author => "Java Developer";
    public string Description => "通过 IKVM.NET 编译的 Java 插件";
    public bool IsAvailable => true;
    public List<string> Capabilities => new() { "歌词搜索: Java (IKVM.NET)", "封面搜索: Java (IKVM.NET)", "菜单扩展: Java (IKVM.NET)" };

    public Task InitializeAsync() => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;

    public async Task<LrcLyrics?> GetLyricsAsync(Song song)
    {
        try
        {
            var json = _javaInstance.getLyricsJson(song.Title, song.Artist);
            var result = JsonSerializer.Deserialize<JavaLyricsResult>(json);
            if (result == null || result.Lines.Count == 0) return null;

            return new LrcLyrics
            {
                Metadata = new LrcMetadata
                {
                    Title = result.Metadata?.Title ?? song.Title,
                    Artist = result.Metadata?.Artist ?? song.Artist
                },
                Lines = ConvertLines(result.Lines)
            };
        }
        catch { return null; }
    }

    public async Task<byte[]?> GetCoverAsync(Song song)
    {
        try
        {
            var bytes = _javaInstance.getCover(song.Title, song.Artist);
            return bytes as byte[];
        }
        catch { return null; }
    }

    public List<MenuItemEntry> GetMenuItems(Song song)
    {
        try
        {
            var items = _javaInstance.getMenuItems(song.Title, song.Artist) as string[];
            if (items == null) return new();
            return items.Select(s =>
            {
                var parts = s.Split('|', 2);
                return new MenuItemEntry(int.Parse(parts[0]), parts.Length > 1 ? parts[1] : parts[0]);
            }).ToList();
        }
        catch { return new(); }
    }

    public Task OnMenuItemClicked(int itemId, Song song, object fragment) => Task.CompletedTask;

    private static List<LrcLyricLine> ConvertLines(List<JavaLyricLine> lines)
    {
        var result = new List<LrcLyricLine>();
        foreach (var l in lines)
            result.Add(new LrcLyricLine { Timestamp = ParseTimestamp(l.Timestamp), Text = l.Text });
        return result;
    }

    private static TimeSpan ParseTimestamp(string ts)
    {
        var parts = ts.Split(':', '.');
        return new TimeSpan(0, 0, int.Parse(parts[0]), int.Parse(parts[1]),
            parts.Length > 2 ? int.Parse(parts[2].PadRight(3, '0')) : 0);
    }
}

// JSON 反序列化辅助模型
public class JavaLyricsResult
{
    public JavaLyricsMeta? Metadata { get; set; }
    public List<JavaLyricLine> Lines { get; set; } = new();
}

public class JavaLyricsMeta { public string? Title { get; set; } public string? Artist { get; set; } }
public class JavaLyricLine { public string Timestamp { get; set; } = ""; public string Text { get; set; } = ""; }
