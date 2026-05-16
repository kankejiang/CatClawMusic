using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Interfaces;

public interface IPlugin
{
    string PluginId { get; }
    string Name { get; }
    string Version { get; }
    string Author { get; }
    string Description { get; }
    List<string> Capabilities { get; }
    Task InitializeAsync();
    Task ShutdownAsync();
}

public interface ILyricsProviderPlugin : IPlugin
{
    Task<LrcLyrics?> GetLyricsAsync(Song song);
    bool IsAvailable { get; }
}

public interface IProtocolProviderPlugin : IPlugin
{
    string ProtocolName { get; }
    Task<List<RemoteFile>> ListFilesAsync(string path);
    Task<Stream> OpenReadAsync(string filePath);
    Task<bool> TestConnectionAsync(ConnectionProfile profile);
}

public interface ICoverProviderPlugin : IPlugin
{
    Task<byte[]?> GetCoverAsync(Song song);
    bool IsAvailable { get; }
}

public interface IAudioEnhancerPlugin : IPlugin
{
    bool IsEnabled { get; set; }
    float[] ProcessSamples(float[] samples, int sampleRate, int channels);
    void Reset();
}

public interface IMenuContributorPlugin : IPlugin
{
    List<MenuItemEntry> GetMenuItems(Song song);
    Task OnMenuItemClicked(int itemId, Song song, object fragment);
}

public class MenuItemEntry
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;

    public MenuItemEntry() { }

    public MenuItemEntry(int id, string title)
    {
        Id = id;
        Title = title;
    }
}
