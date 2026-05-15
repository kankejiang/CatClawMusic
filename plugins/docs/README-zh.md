# 猫爪音乐 (CatClawMusic) 插件 SDK 开发指南

## 概述

猫爪音乐支持通过插件系统扩展应用功能。你可以编写插件来实现：

- **歌词提供者** — 从自定义源获取歌词
- **封面提供者** — 从自定义源获取专辑封面
- **协议提供者** — 添加新的网络协议支持（如 SMB、FTP）
- **音频增强器** — 实时音频处理（均衡器、音效）

插件为 .NET 9 类库（DLL），通过应用程序中的「插件管理」页面从网络安装。

---

## 快速开始

### 1. 创建项目

```xml
<!-- CatClawMusic.MyPlugin.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CatClawMusic.Core\CatClawMusic.Core.csproj" />
  </ItemGroup>
</Project>
```

### 2. 实现插件接口

选择你要实现的接口类型：

| 接口 | 用途 | 核心方法 |
|------|------|----------|
| `ILyricsProviderPlugin` | 歌词获取 | `GetLyricsAsync(Song)` |
| `ICoverProviderPlugin` | 封面获取 | `GetCoverAsync(Song)` |
| `IProtocolProviderPlugin` | 网络协议 | `ListFilesAsync`, `OpenReadAsync`, `TestConnectionAsync` |
| `IAudioEnhancerPlugin` | 音频处理 | `ProcessSamples(float[], int, int)` |

### 3. 编译并发布

```bash
dotnet build CatClawMusic.MyPlugin.csproj -c Release
```

将生成的 `CatClawMusic.MyPlugin.dll` 上传到 GitHub Releases 或其他可直链访问的位置。

### 4. 在应用中安装

打开「设置 → 插件管理」→ 点击「从网络安装插件」→ 输入 DLL 文件的直链地址 → 安装。

---

## 核心接口详解

### IPlugin（基础接口）

所有插件必须实现此接口。

```csharp
public interface IPlugin
{
    string PluginId { get; }           // 唯一标识符，如 "my.lyrics.provider"
    string Name { get; }               // 显示名称，如 "QQ音乐歌词"
    string Version { get; }            // 版本号，如 "1.0.0"
    string Author { get; }             // 作者名
    string Description { get; }        // 插件描述
    List<string> Capabilities { get; } // 功能列表，在插件卡片展开时显示
    Task InitializeAsync();            // 初始化（启用时调用）
    Task ShutdownAsync();              // 关闭（禁用/卸载时调用）
}
```

**字段说明：**

| 字段 | 类型 | 说明 |
|------|------|------|
| `PluginId` | `string` | **必须唯一**。格式建议 `{分类}.{名称}`，如 `lyrics.qqmusic`。用于持久化标识 |
| `Name` | `string` | 用户可见的插件名称，在插件管理页面显示 |
| `Version` | `string` | 语义化版本号，建议遵循 SemVer 规范 |
| `Author` | `string` | 插件作者或组织名称 |
| `Description` | `string` | 简要描述插件功能，建议 30 字以内 |
| `Capabilities` | `List<string>` | **可选但推荐**。功能列表，点击卡片展开时显示 |
| `InitializeAsync` | `Task` | 插件启用时调用，可在此处初始化资源（HTTP 客户端、数据库连接等） |
| `ShutdownAsync` | `Task` | 插件禁用/卸载时调用，在此处释放资源 |

### ILyricsProviderPlugin（歌词提供者）

```csharp
public interface ILyricsProviderPlugin : IPlugin
{
    Task<LrcLyrics?> GetLyricsAsync(Song song);
    bool IsAvailable { get; }
}
```

**`GetLyricsAsync(Song song)`**
- 参数 `song`：包含歌曲信息的 Song 对象（标题、艺术家、专辑、文件路径等）
- 返回：`LrcLyrics` 对象，或 `null`（未找到歌词时）
- 多个歌词插件会按注册顺序依次尝试，第一个返回非 null 结果即采用

**`IsAvailable`**
- 返回插件当前是否可用（如网络连接状态）

### ICoverProviderPlugin（封面提供者）

```csharp
public interface ICoverProviderPlugin : IPlugin
{
    Task<byte[]?> GetCoverAsync(Song song);
    bool IsAvailable { get; }
}
```

**`GetCoverAsync(Song song)`**
- 返回：封面图片的字节数组（JPEG/PNG 格式），或 `null`
- 应用会自动缓存返回的封面数据

### IProtocolProviderPlugin（协议提供者）

```csharp
public interface IProtocolProviderPlugin : IPlugin
{
    string ProtocolName { get; }
    Task<List<RemoteFile>> ListFilesAsync(string path);
    Task<Stream> OpenReadAsync(string filePath);
    Task<bool> TestConnectionAsync(ConnectionProfile profile);
}
```

**`ProtocolName`**
- 协议标识名，如 "SMB"、"FTP"、"Dropbox"

**`ListFilesAsync(string path)`**
- 列出指定路径下的文件和目录

**`OpenReadAsync(string filePath)`**
- 打开远程文件的只读流，用于播放远程音乐

**`TestConnectionAsync(ConnectionProfile profile)`**
- 测试服务器连接是否成功
- `ConnectionProfile` 包含主机、端口、用户名、密码等连接信息

### IAudioEnhancerPlugin（音频增强器）

```csharp
public interface IAudioEnhancerPlugin : IPlugin
{
    bool IsEnabled { get; set; }
    float[] ProcessSamples(float[] samples, int sampleRate, int channels);
    void Reset();
}
```

**`ProcessSamples(float[] samples, int sampleRate, int channels)`**
- 实时处理音频采样数据
- `samples`：PCM 浮点采样数据（范围 [-1.0, 1.0]）
- `sampleRate`：采样率（如 44100）
- `channels`：声道数（1 = 单声道，2 = 立体声）
- 返回：处理后的采样数据

**`Reset()`**
- 切换歌曲时调用，重置处理状态

---

## 数据模型参考

### Song（歌曲信息）

```csharp
public class Song
{
    public int Id { get; set; }           // 唯一 ID
    public string Title { get; set; }     // 标题
    public string Artist { get; set; }    // 艺术家（运行时字段）
    public string Album { get; set; }     // 专辑（运行时字段）
    public int Duration { get; set; }     // 时长（毫秒）
    public string FilePath { get; set; }  // 文件路径
    public long FileSize { get; set; }    // 文件大小（字节）
    public int Bitrate { get; set; }      // 比特率（bps）
    public int Year { get; set; }         // 发行年份
    public string? Genre { get; set; }    // 流派
    public string? CoverArtPath { get; set; } // 封面路径
    public string? LyricsPath { get; set; }   // 歌词路径
}
```

### LrcLyrics（歌词）

```csharp
public class LrcLyrics
{
    public LrcMetadata Metadata { get; set; }     // 元数据（ti, ar, al）
    public List<LrcLyricLine> Lines { get; set; } // 歌词行
}

public class LrcLyricLine
{
    public TimeSpan Timestamp { get; set; } // 时间戳
    public string Text { get; set; }        // 歌词文本
}
```

### ConnectionProfile（连接配置）

```csharp
public class ConnectionProfile
{
    public int Id { get; set; }
    public string Name { get; set; }        // 显示名称
    public ProtocolType Protocol { get; set; } // 协议类型
    public string Host { get; set; }        // 主机地址
    public int Port { get; set; }           // 端口
    public string UserName { get; set; }    // 用户名
    public string Password { get; set; }    // 密码/Token
    public string BasePath { get; set; }    // 基础路径
    public bool IsEnabled { get; set; }     // 是否启用
}
```

### RemoteFile（远程文件）

```csharp
public class RemoteFile
{
    public string Name { get; set; }        // 文件名
    public string Path { get; set; }        // 完整路径
    public bool IsDirectory { get; set; }   // 是否目录
    public long Size { get; set; }          // 文件大小
    public long LastModified { get; set; }  // 最后修改时间
}
```

---

## 插件生命周期

```
创建实例 → InitializeAsync() → [运行中] → ShutdownAsync() → 销毁
                                       ↑ 禁用/卸载时触发
```

1. **加载**：应用启动或安装完成后，通过反射创建插件实例
2. **初始化**：调用 `InitializeAsync()`，失败则自动禁用
3. **运行**：插件根据接口定义提供功能
4. **关闭**：用户禁用或卸载插件时调用 `ShutdownAsync()`
5. **卸载**：删除 DLL 文件并清理持久化记录

---

## 示例：完整歌词插件

```csharp
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace MyLyricsPlugin;

public class NeteaseLyricsPlugin : ILyricsProviderPlugin
{
    private readonly HttpClient _client = new();

    public string PluginId => "lyrics.netease";
    public string Name => "网易云歌词";
    public string Version => "1.0.0";
    public string Author => "YourName";
    public string Description => "从网易云音乐获取歌词";

    public bool IsAvailable => true;

    public List<string> Capabilities => new()
    {
        "歌词搜索: 通过歌曲信息匹配网易云歌词",
        "LRC解析: 自动解析[mm:ss.xx]格式时间轴",
        "翻译支持: 获取双语歌词（如有）"
    };

    public Task InitializeAsync() => Task.CompletedTask;

    public Task ShutdownAsync() { _client.Dispose(); return Task.CompletedTask; }

    public async Task<LrcLyrics?> GetLyricsAsync(Song song)
    {
        // 构建搜索请求
        var url = $"https://api.example.com/lyrics?title={song.Title}&artist={song.Artist}";
        var response = await _client.GetStringAsync(url);

        // 解析 LRC 格式歌词
        return ParseLrc(response);
    }

    private static LrcLyrics ParseLrc(string lrcText)
    {
        var result = new LrcLyrics();
        foreach (var line in lrcText.Split('\n'))
        {
            // 解析 [mm:ss.xx]文本...
        }
        return result;
    }
}
```

---

## 发布与分发

### 1. 编译插件

```bash
dotnet build -c Release
```

输出文件在 `bin/Release/net9.0/{AssemblyName}.dll`

### 2. 上传到 GitHub Releases

1. 在 GitHub 仓库创建 Release
2. 上传编译好的 `.dll` 文件
3. 复制直链下载地址（Raw URL 或 Release Asset URL）

### 3. 用户安装

用户在应用的「插件管理」页面点击「从网络安装插件」，粘贴 `.dll` 直链地址即可安装。

---

## 最佳实践

1. **PluginId 命名**：使用 `{分类}.{名称}` 格式，确保全局唯一
2. **异常处理**：在 `GetLyricsAsync` 等方法内部捕获异常，返回 `null` 而非抛出
3. **异步编程**：网络请求使用 `HttpClient` 的异步方法，避免阻塞 UI 线程
4. **资源释放**：在 `ShutdownAsync()` 中释放 `HttpClient`、文件句柄等资源
5. **体积控制**：插件 DLL 尽量精简，避免包含不必要的依赖
6. **Capabilities 描述**：使用清晰的中文描述功能，方便用户了解插件用途

---

## 目录结构参考

```
plugins/
├── samples/
│   └── SamplePlugin/              # 示例插件
│       ├── CatClawMusic.SamplePlugin.csproj
│       └── SamplePlugins.cs       # 四种插件类型的完整示例
└── docs/
    ├── README-zh.md               # 中文开发文档（本文档）
    └── README-en.md               # English API Reference
```
