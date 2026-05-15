using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Core.Models;

public class PluginInfo
{
    public string PluginTypeId { get; set; } = string.Empty;
    public IPlugin Plugin { get; set; } = null!;
    public bool IsEnabled { get; set; } = true;
    public string DisplayName => Plugin.Name;
    public string Version => Plugin.Version;
    public string Author => Plugin.Author;
    public string Description { get; set; } = string.Empty;
    public List<string> Capabilities => Plugin.Capabilities;
    public PluginCategory Category { get; set; }
    public string IconEmoji { get; set; } = "🧩";
    public PluginSource Source { get; set; } = PluginSource.BuiltIn;
    public string? AssemblyPath { get; set; }
    public bool CanUninstall => Source == PluginSource.Installed;
    public string? InstallUrl { get; set; }
}

public enum PluginCategory
{
    LyricsProvider,
    ProtocolProvider,
    CoverProvider,
    AudioEnhancer,
    Other
}

public enum PluginSource
{
    BuiltIn,
    Installed
}
