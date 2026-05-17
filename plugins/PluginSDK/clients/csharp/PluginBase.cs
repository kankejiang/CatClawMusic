using System.Text.Json;

namespace CatClawMusic.PluginSDK;

/// <summary>
/// 插件基类，所有语言 SDK 的基础抽象。
/// C# 插件直接继承此类并覆写对应方法。
/// </summary>
public abstract class PluginBase
{
    private readonly HttpClient _client = new();
    private int _requestIdCounter;
    private string _hostUrl = "http://127.0.0.1:18999";
    private CancellationTokenSource? _cts;

    public PluginManifest Manifest { get; }
    public bool IsRunning { get; private set; }

    protected HttpClient HttpClient => _client;
    protected CancellationToken CancellationToken => _cts?.Token ?? CancellationToken.None;

    protected PluginBase(PluginManifest manifest)
    {
        Manifest = manifest;
    }

    public virtual Task InitializeAsync() => Task.CompletedTask;
    public virtual Task ShutdownAsync() => Task.CompletedTask;

    public virtual Task<LrcLyricsResult?> GetLyricsAsync(SongInfo song) => Task.FromResult<LrcLyricsResult?>(null);
    public virtual Task<byte[]?> GetCoverAsync(SongInfo song) => Task.FromResult<byte[]?>(null);
    public virtual Task<List<RemoteFileInfo>> ListFilesAsync(string path) => Task.FromResult(new List<RemoteFileInfo>());
    public virtual Task<Stream?> OpenReadAsync(string filePath) => Task.FromResult<Stream?>(null);
    public virtual Task<bool> TestConnectionAsync(ConnectionProfileInfo profile) => Task.FromResult(false);
    public virtual Task<float[]> ProcessSamplesAsync(float[] samples, int sampleRate, int channels) => Task.FromResult(samples);
    public virtual Task<List<MenuItemInfo>> GetMenuItemsAsync(SongInfo song) => Task.FromResult(new List<MenuItemInfo>());
    public virtual Task OnMenuItemClickedAsync(int itemId, SongInfo song) => Task.CompletedTask;

    public Task StartAsync(string hostUrl, CancellationToken cancellationToken = default)
    {
        _hostUrl = hostUrl;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsRunning = true;
        return RegisterAsync();
    }

    public async Task StopAsync()
    {
        IsRunning = false;
        _cts?.Cancel();
        await UnregisterAsync();
        await ShutdownAsync();
    }

    protected async Task<T?> CallHostAsync<T>(string method, object? @params = null)
    {
        var reqId = $"req-{Interlocked.Increment(ref _requestIdCounter)}";
        var requestBody = new
        {
            id = reqId,
            method,
            @params
        };

        var json = JsonSerializer.Serialize(requestBody, ProtocolSerializer.Options);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await _client.PostAsync($"{_hostUrl}/invoke", content, CancellationToken);
        response.EnsureSuccessStatusCode();
        var responseJson = await response.Content.ReadAsStringAsync(CancellationToken);
        var result = JsonSerializer.Deserialize<ProtocolResponse>(responseJson, ProtocolSerializer.Options);

        if (result?.Error != null)
            throw new PluginException(result.Error.Code, result.Error.Message);

        if (result?.Result == null)
            return default;

        var resultStr = result!.Result!.Value.GetRawText();
        return JsonSerializer.Deserialize<T>(resultStr, ProtocolSerializer.Options);
    }

    private async Task RegisterAsync()
    {
        var json = JsonSerializer.Serialize(Manifest, ProtocolSerializer.Options);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        await _client.PostAsync($"{_hostUrl}/plugins/register", content, CancellationToken);
    }

    private async Task UnregisterAsync()
    {
        try
        {
            await _client.PostAsync($"{_hostUrl}/plugins/unregister", null, CancellationToken);
        }
        catch { }
    }
}

/// <summary>插件异常</summary>
public class PluginException : Exception
{
    public int ErrorCode { get; }

    public PluginException(int code, string message) : base(message)
    {
        ErrorCode = code;
    }
}
