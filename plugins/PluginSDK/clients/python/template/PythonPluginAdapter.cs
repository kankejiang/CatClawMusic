using System.Reflection;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using IronPython.Hosting;
using Microsoft.Scripting.Hosting;

namespace CatClawMusic.PythonPlugin;

/// <summary>
/// Python 插件 C# 适配器 - 通过 IronPython 引擎执行嵌入的 .py 源文件。
/// .py 文件作为 EmbeddedResource 保存在 DLL 中，运行时由 IronPython 解析执行。
/// </summary>
public class PythonPluginAdapter : ILyricsProviderPlugin, IMenuContributorPlugin
{
    private readonly ScriptEngine _engine;
    private readonly ScriptScope _scope;
    private readonly dynamic _pyPlugin;

    public PythonPluginAdapter()
    {
        _engine = Python.CreateEngine();
        _scope = _engine.CreateScope();

        // 读取嵌入的 my_plugin.py 并执行
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("my_plugin.py"));
        using var stream = assembly.GetManifestResourceStream(resourceName);
        using var reader = new StreamReader(stream!);
        var code = reader.ReadToEnd();

        _engine.Execute(code, _scope);
        _pyPlugin = _scope.GetVariable("plugin");
    }

    public string PluginId => "python.plugin";
    public string Name => "Python 插件";
    public string Version => ((string)_pyPlugin.get_version()) ?? "1.0.0";
    public string Author => "Python Developer";
    public string Description => "通过 IronPython 运行的 Python 插件";
    public bool IsAvailable => true;
    public List<string> Capabilities => new() { "歌词搜索: Python (IronPython)", "菜单扩展: Python (IronPython)" };

    public Task InitializeAsync() => Task.CompletedTask;
    public Task ShutdownAsync()
    {
        _engine.Runtime.Shutdown();
        return Task.CompletedTask;
    }

    public async Task<LrcLyrics?> GetLyricsAsync(Song song)
    {
        try
        {
            var result = _pyPlugin.get_lyrics(song.Title ?? "", song.Artist ?? "");
            if (result == null) return null;

            var metadata = result.metadata;
            var lines = result.lines;

            var lrcLines = new List<LrcLyricLine>();
            foreach (var l in lines)
            {
                lrcLines.Add(new LrcLyricLine
                {
                    Timestamp = ParseTs((string)l.timestamp),
                    Text = (string)l.text
                });
            }
            if (lrcLines.Count == 0) return null;

            return new LrcLyrics
            {
                Metadata = new LrcMetadata { Title = metadata.title, Artist = metadata.artist },
                Lines = lrcLines
            };
        }
        catch { return null; }
    }

    public List<MenuItemEntry> GetMenuItems(Song song)
    {
        try
        {
            var items = _pyPlugin.get_menu_items(song.Title ?? "", song.Artist ?? "");
            var result = new List<MenuItemEntry>();
            foreach (string s in items)
            {
                var parts = s.Split('|', 2);
                result.Add(new MenuItemEntry(int.Parse(parts[0]), parts.Length > 1 ? parts[1] : parts[0]));
            }
            return result;
        }
        catch { return new(); }
    }

    public Task OnMenuItemClicked(int itemId, Song song, object fragment)
    {
        _pyPlugin.on_menu_clicked(itemId, song.Title ?? "", song.Artist ?? "");
        return Task.CompletedTask;
    }

    private static TimeSpan ParseTs(string ts)
    {
        var parts = ts.Split(':', '.');
        return new TimeSpan(0, 0, int.Parse(parts[0]), int.Parse(parts[1]),
            parts.Length > 2 ? int.Parse(parts[2].PadRight(3, '0')) : 0);
    }
}
