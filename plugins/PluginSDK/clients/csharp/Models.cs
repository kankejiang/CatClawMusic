using System.Text.Json;
using System.Text.Json.Serialization;

namespace CatClawMusic.PluginSDK;

/// <summary>插件基础类型枚举</summary>
public enum PluginType
{
    LyricsProvider,
    CoverProvider,
    ProtocolProvider,
    AudioEnhancer,
    MenuContributor,
    Other
}

/// <summary>插件清单，描述插件的元数据</summary>
public class PluginManifest
{
    [JsonPropertyName("pluginId")]
    public string PluginId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "csharp";

    [JsonPropertyName("entryPoint")]
    public string EntryPoint { get; set; } = "";

    [JsonPropertyName("capabilities")]
    public List<string> Capabilities { get; set; } = new();

    [JsonPropertyName("pluginType")]
    public string PluginType { get; set; } = "Other";
}

/// <summary>歌曲模型</summary>
public class SongInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = "";

    [JsonPropertyName("album")]
    public string Album { get; set; } = "";

    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = "";

    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    [JsonPropertyName("bitrate")]
    public int Bitrate { get; set; }

    [JsonPropertyName("year")]
    public int Year { get; set; }

    [JsonPropertyName("genre")]
    public string? Genre { get; set; }

    [JsonPropertyName("coverArtPath")]
    public string? CoverArtPath { get; set; }

    [JsonPropertyName("lyricsPath")]
    public string? LyricsPath { get; set; }
}

/// <summary>歌词模型</summary>
public class LrcLyricsResult
{
    [JsonPropertyName("metadata")]
    public LrcMetadata Meta { get; set; } = new();

    [JsonPropertyName("lines")]
    public List<LrcLine> Lines { get; set; } = new();
}

public class LrcMetadata
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = "";

    [JsonPropertyName("album")]
    public string Album { get; set; } = "";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("maker")]
    public string Maker { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";
}

public class LrcLine
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}

/// <summary>远程文件模型</summary>
public class RemoteFileInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("isDirectory")]
    public bool IsDirectory { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("lastModified")]
    public long LastModified { get; set; }
}

/// <summary>连接配置</summary>
public class ConnectionProfileInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = "";

    [JsonPropertyName("host")]
    public string Host { get; set; } = "";

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("userName")]
    public string UserName { get; set; } = "";

    [JsonPropertyName("password")]
    public string Password { get; set; } = "";

    [JsonPropertyName("basePath")]
    public string BasePath { get; set; } = "";

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; }
}

/// <summary>菜单项</summary>
public class MenuItemInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    public MenuItemInfo() { }

    public MenuItemInfo(int id, string title)
    {
        Id = id;
        Title = title;
    }
}

/// <summary>协议请求消息</summary>
public class ProtocolRequest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }
}

/// <summary>协议响应消息</summary>
public class ProtocolResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }

    [JsonPropertyName("error")]
    public ProtocolError? Error { get; set; }
}

public class ProtocolError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

/// <summary>JSON 协议序列化配置</summary>
public static class ProtocolSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
