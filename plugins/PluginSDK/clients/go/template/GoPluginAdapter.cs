using System.Runtime.InteropServices;
using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.GoPlugin;

/// <summary>
/// Go 插件 C# 适配器 - 通过 P/Invoke 调用 Go 编译的原生库 (cgo c-shared)。
///
/// Go 代码通过 <c>go build -buildmode=c-shared</c> 编译为原生共享库 (.so/.dll),
/// 导出 C ABI 函数。本适配器通过 <see cref="DllImport"/> 调用这些函数。
///
/// 编译产物: .NET DLL (含 P/Invoke) + 原生 .so 文件 → 一起打包为 .ccp
/// </summary>
public class GoPluginAdapter : ILyricsProviderPlugin, IMenuContributorPlugin
{
    private readonly string _pluginName;
    private readonly string _pluginVersion;

    public GoPluginAdapter()
    {
        _pluginVersion = GetStringAndFree(NativeGo.GetPluginVersion()) ?? "1.0.0";
        _pluginName = "Go 插件";
    }

    public string PluginId => "go.plugin";
    public string Name => _pluginName;
    public string Version => _pluginVersion;
    public string Author => "Go Developer";
    public string Description => "通过 cgo c-shared 编译的 Go 语言插件";
    public bool IsAvailable => true;
    public List<string> Capabilities => new()
    {
        "歌词搜索: Go (cgo c-shared)",
        "菜单扩展: Go (cgo c-shared)"
    };

    // ── 生命周期 ────────────────────────────────────────

    public Task InitializeAsync() => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;

    // ── 歌词搜索 ────────────────────────────────────────

    public Task<LrcLyrics?> GetLyricsAsync(Song song)
    {
        return Task.FromResult(GetLyricsImpl(song));
    }

    private LrcLyrics? GetLyricsImpl(Song song)
    {
        try
        {
            var titlePtr = Marshal.StringToCoTaskMemUTF8(song.Title ?? "");
            var artistPtr = Marshal.StringToCoTaskMemUTF8(song.Artist ?? "");
            try
            {
                var json = GetStringAndFree(NativeGo.GetLyricsJson(titlePtr, artistPtr));
                if (string.IsNullOrEmpty(json) || json == "null")
                    return null;

                var result = JsonSerializer.Deserialize<GoLyricsResult>(json);
                if (result == null || result.Lines.Count == 0) return null;

                return new LrcLyrics
                {
                    Metadata = new LrcMetadata
                    {
                        Title = result.Metadata.Title ?? song.Title,
                        Artist = result.Metadata.Artist ?? song.Artist
                    },
                    Lines = result.Lines.Select(l => new LrcLyricLine
                    {
                        Timestamp = ParseTimestamp(l.Timestamp),
                        Text = l.Text
                    }).ToList()
                };
            }
            finally
            {
                Marshal.FreeCoTaskMem(titlePtr);
                Marshal.FreeCoTaskMem(artistPtr);
            }
        }
        catch { return null; }
    }

    // ── 菜单扩展 ────────────────────────────────────────

    public List<MenuItemEntry> GetMenuItems(Song song)
    {
        try
        {
            var titlePtr = Marshal.StringToCoTaskMemUTF8(song.Title ?? "");
            var artistPtr = Marshal.StringToCoTaskMemUTF8(song.Artist ?? "");
            try
            {
                var json = GetStringAndFree(NativeGo.GetMenuItemsJson(titlePtr, artistPtr));
                if (string.IsNullOrEmpty(json) || json == "null")
                    return new();

                var items = JsonSerializer.Deserialize<string[]>(json);
                if (items == null) return new();

                return items.Select(s =>
                {
                    var parts = s.Split('|', 2);
                    return new MenuItemEntry(
                        int.Parse(parts[0]),
                        parts.Length > 1 ? parts[1] : parts[0]);
                }).ToList();
            }
            finally
            {
                Marshal.FreeCoTaskMem(titlePtr);
                Marshal.FreeCoTaskMem(artistPtr);
            }
        }
        catch { return new(); }
    }

    public Task OnMenuItemClicked(int itemId, Song song, object fragment)
        => Task.CompletedTask;

    // ── 辅助 ────────────────────────────────────────────

    private static string? GetStringAndFree(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return null;
        var str = Marshal.PtrToStringUTF8(ptr);
        NativeGo.FreeString(ptr);
        return str;
    }

    private static TimeSpan ParseTimestamp(string ts)
    {
        var parts = ts.Split(':', '.');
        return new TimeSpan(0, 0,
            int.Parse(parts[0]),
            int.Parse(parts[1]),
            parts.Length > 2 ? int.Parse(parts[2].PadRight(3, '0')) : 0);
    }
}

// ── P/Invoke 声明 ─────────────────────────────────────

internal static class NativeGo
{
    // Android 平台加载 libgoplugin.so
    // Windows 开发时加载 goplugin.dll
    // 实际部署时 .so 文件放在 APK lib/ 目录或通过 DllImport 自动解析

    private const string DllName =
#if ANDROID
        "libgoplugin.so";
#elif WINDOWS
        "goplugin.dll";
#else
        "libgoplugin.so";
#endif

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr GetPluginVersion();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr GetLyricsJson(IntPtr title, IntPtr artist);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr GetMenuItemsJson(IntPtr title, IntPtr artist);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void FreeString(IntPtr s);
}

// ── JSON 反序列化模型 ──────────────────────────────────

public class GoLyricsResult
{
    public GoLyricMeta Metadata { get; set; } = new();
    public List<GoLyricLine> Lines { get; set; } = new();
}

public class GoLyricMeta
{
    public string? Title { get; set; }
    public string? Artist { get; set; }
}

public class GoLyricLine
{
    public string Timestamp { get; set; } = "";
    public string Text { get; set; } = "";
}
