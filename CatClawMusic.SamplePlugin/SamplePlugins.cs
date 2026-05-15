using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.SamplePlugin;

public class SampleLyricsPlugin : ILyricsProviderPlugin
{
    public string PluginId => "sample.lyrics";
    public string Name => "示例歌词插件";
    public string Version => "1.0.0";
    public string Author => "CatClawMusic";
    public string Description => "这是一个示例歌词插件，展示了插件SDK的所有可用功能。从本地lrc文件或嵌入标签读取歌词。";
    public bool IsAvailable => true;

    public List<string> Capabilities => new()
    {
        "歌词搜索: 从本地.lrc文件匹配歌词",
        "嵌入标签读取: 从音频文件嵌入标签提取歌词",
        "时间轴解析: 支持[mm:ss.xx]格式LRC时间轴"
    };

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        return Task.CompletedTask;
    }

    public async Task<LrcLyrics?> GetLyricsAsync(Song song)
    {
        await Task.CompletedTask;
        return null;
    }
}

public class SampleProtocolPlugin : IProtocolProviderPlugin
{
    public string PluginId => "sample.protocol";
    public string Name => "示例协议插件";
    public string Version => "1.0.0";
    public string Author => "CatClawMusic";
    public string Description => "这是一个示例协议插件，演示如何实现自定义网络协议支持。";
    public string ProtocolName => "SampleProtocol";

    public List<string> Capabilities => new()
    {
        "协议注册: 注册自定义网络协议名称",
        "文件列表: 列出远程目录文件",
        "流式读取: 通过Stream打开远程文件",
        "连接测试: 测试远程服务器连接状态"
    };

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        return Task.CompletedTask;
    }

    public Task<List<RemoteFile>> ListFilesAsync(string path)
    {
        return Task.FromResult(new List<RemoteFile>());
    }

    public Task<Stream> OpenReadAsync(string filePath)
    {
        return Task.FromResult(Stream.Null);
    }

    public Task<bool> TestConnectionAsync(ConnectionProfile profile)
    {
        return Task.FromResult(false);
    }
}

public class SampleCoverPlugin : ICoverProviderPlugin
{
    public string PluginId => "sample.cover";
    public string Name => "示例封面插件";
    public string Version => "1.0.0";
    public string Author => "CatClawMusic";
    public string Description => "这是一个示例封面插件，演示如何实现封面获取功能。可从网络API匹配专辑封面。";
    public bool IsAvailable => true;

    public List<string> Capabilities => new()
    {
        "封面搜索: 通过歌曲信息搜索匹配封面",
        "图片缓存: 下载并缓存封面图片到本地",
        "多源支持: 支持多个封面API源"
    };

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        return Task.CompletedTask;
    }

    public async Task<byte[]?> GetCoverAsync(Song song)
    {
        await Task.CompletedTask;
        return null;
    }
}

public class SampleAudioEnhancerPlugin : IAudioEnhancerPlugin
{
    public string PluginId => "sample.enhancer";
    public string Name => "示例音效插件";
    public string Version => "1.0.0";
    public string Author => "CatClawMusic";
    public string Description => "这是一个示例音频增强插件，演示如何实现实时音频处理。支持均衡器和音效处理。";
    public bool IsEnabled { get; set; } = true;

    public List<string> Capabilities => new()
    {
        "均衡器: 多频段增益调节",
        "音频处理: 实时采样数据处理",
        "状态重置: 切换歌曲时重置处理状态"
    };

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        return Task.CompletedTask;
    }

    public float[] ProcessSamples(float[] samples, int sampleRate, int channels)
    {
        return samples;
    }

    public void Reset()
    {
    }
}
