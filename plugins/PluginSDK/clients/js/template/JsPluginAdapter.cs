using System.Reflection;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using Jint;

namespace CatClawMusic.JsPlugin;

/// <summary>
/// JavaScript 插件 C# 适配器 - 通过 Jint 引擎执行嵌入的 .js 源文件。
/// .js 文件作为 EmbeddedResource 保存在 DLL 中，运行时由 Jint 解释执行。
/// </summary>
public class JsPluginAdapter : ILyricsProviderPlugin, IMenuContributorPlugin
{
    private readonly Engine _engine;
    private readonly object _plugin;

    public JsPluginAdapter()
    {
        _engine = new Engine(cfg => cfg
            .AllowClr()
            .CatchClrExceptions());

        // 注入 console.log 支持
        _engine.SetValue("console", new { log = new Action<object>(o => System.Console.WriteLine($"[JS] {o}")) });

        // 读取嵌入的 my_plugin.js 并执行
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("my_plugin.js"));
        using var stream = assembly.GetManifestResourceStream(resourceName);
        using var reader = new StreamReader(stream!);
        var code = reader.ReadToEnd();

        _engine.Execute(code);
        _plugin = _engine.Evaluate("plugin").ToObject()!;
    }

    public string PluginId => "js.plugin";
    public string Name => "JavaScript 插件";
    public string Version => Invoke<string>("version") ?? "1.0.0";
    public string Author => "JS Developer";
    public string Description => "通过 Jint 引擎运行的 JavaScript 插件";
    public bool IsAvailable => true;
    public List<string> Capabilities => new() { "歌词搜索: JavaScript (Jint)", "菜单扩展: JavaScript (Jint)" };

    public Task InitializeAsync() => Task.CompletedTask;
    public Task ShutdownAsync()
    {
        _engine.Dispose();
        return Task.CompletedTask;
    }

    public async Task<LrcLyrics?> GetLyricsAsync(Song song)
    {
        try
        {
            var result = _engine.Invoke("plugin.getLyrics", song.Title ?? "", song.Artist ?? "");
            if (result.IsNull() || result.IsUndefined()) return null;

            var obj = result.AsObject();
            var meta = obj.Get("metadata").AsObject();
            var lines = obj.Get("lines").AsArray();

            var lrcLines = new List<LrcLyricLine>();
            foreach (var l in lines)
            {
                var lo = l.AsObject();
                lrcLines.Add(new LrcLyricLine
                {
                    Timestamp = ParseTs(lo.Get("timestamp").AsString()),
                    Text = lo.Get("text").AsString()
                });
            }
            if (lrcLines.Count == 0) return null;

            return new LrcLyrics
            {
                Metadata = new LrcMetadata
                {
                    Title = meta.Get("title").AsString(),
                    Artist = meta.Get("artist").AsString()
                },
                Lines = lrcLines
            };
        }
        catch { return null; }
    }

    public List<MenuItemEntry> GetMenuItems(Song song)
    {
        try
        {
            var items = _engine.Invoke("plugin.getMenuItems", song.Title ?? "", song.Artist ?? "");
            var result = new List<MenuItemEntry>();
            foreach (var item in items.AsArray())
            {
                var s = item.AsString();
                var parts = s.Split('|', 2);
                result.Add(new MenuItemEntry(int.Parse(parts[0]), parts.Length > 1 ? parts[1] : parts[0]));
            }
            return result;
        }
        catch { return new(); }
    }

    public Task OnMenuItemClicked(int itemId, Song song, object fragment)
    {
        _engine.Invoke("plugin.onMenuClicked", itemId, song.Title ?? "", song.Artist ?? "");
        return Task.CompletedTask;
    }

    private T? Invoke<T>(string property) where T : class
    {
        return _engine.Evaluate("plugin." + property).ToObject() as T;
    }

    private static TimeSpan ParseTs(string ts)
    {
        var parts = ts.Split(':', '.');
        return new TimeSpan(0, 0, int.Parse(parts[0]), int.Parse(parts[1]),
            parts.Length > 2 ? int.Parse(parts[2].PadRight(3, '0')) : 0);
    }
}
