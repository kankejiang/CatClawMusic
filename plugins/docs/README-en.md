# CatClawMusic Plugin SDK — API Reference

## Overview

CatClawMusic supports plugin-based extensibility via .NET 9 class libraries. Plugins are dynamically loaded at runtime and can be installed from network URLs through the in-app Plugin Management page.

### Supported Plugin Types

| Interface | Purpose |
|-----------|---------|
| `IPlugin` | Base interface for all plugins |
| `ILyricsProviderPlugin` | Fetch synchronized lyrics from external sources |
| `ICoverProviderPlugin` | Fetch album artwork from external sources |
| `IProtocolProviderPlugin` | Add custom network protocol support |
| `IAudioEnhancerPlugin` | Real-time audio processing (EQ, effects) |

---

## Plugin Deployment

### Assembly Structure

- **Format**: .NET 9 class library (`.dll`)
- **No native dependencies** required (pure managed code)
- Plugin assemblies are loaded via `AssemblyLoadContext` with `isCollectible: true`

### Installation Flow

```
User enters URL → HTTP download → AssemblyLoadContext loads DLL
→ Reflection discovers IPlugin types → Instance created → InitializeAsync() called
→ Plugin registered in manager → persisted to installed.json
```

### Uninstallation Flow

```
User confirms uninstall → ShutdownAsync() called → AssemblyLoadContext unloaded
→ DLL file deleted → Plugin removed from list → installed.json updated
```

---

## API Reference

### `IPlugin` Interface

Base interface that all plugins must implement.

**Namespace**: `CatClawMusic.Core.Interfaces`

```csharp
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
```

| Property | Type | Description |
|----------|------|-------------|
| `PluginId` | `string` | Globally unique identifier. Recommended format: `{category}.{name}`, e.g. `lyrics.qqmusic` |
| `Name` | `string` | Human-readable display name shown in the plugin management UI |
| `Version` | `string` | Semantic version string, e.g. `"1.0.0"` |
| `Author` | `string` | Plugin author or organization name |
| `Description` | `string` | Short description shown on the plugin card |
| `Capabilities` | `List<string>` | Feature list displayed when the plugin card is expanded |

| Method | Returns | Description |
|--------|---------|-------------|
| `InitializeAsync()` | `Task` | Called when the plugin is enabled. Initialize HTTP clients, DB connections, etc. here |
| `ShutdownAsync()` | `Task` | Called when the plugin is disabled or uninstalled. Clean up resources here |

---

### `ILyricsProviderPlugin` Interface

**Namespace**: `CatClawMusic.Core.Interfaces`

```csharp
public interface ILyricsProviderPlugin : IPlugin
{
    Task<LrcLyrics?> GetLyricsAsync(Song song);
    bool IsAvailable { get; }
}
```

| Member | Type | Description |
|--------|------|-------------|
| `GetLyricsAsync(Song)` | `Task<LrcLyrics?>` | Return `LrcLyrics` object with parsed timeline, or `null` if not found |
| `IsAvailable` | `bool` | Whether the lyrics service is currently reachable |

**Fallback chain**: Multiple lyrics plugins are tried in registration order. The first non-null result is used.

---

### `ICoverProviderPlugin` Interface

**Namespace**: `CatClawMusic.Core.Interfaces`

```csharp
public interface ICoverProviderPlugin : IPlugin
{
    Task<byte[]?> GetCoverAsync(Song song);
    bool IsAvailable { get; }
}
```

| Member | Type | Description |
|--------|------|-------------|
| `GetCoverAsync(Song)` | `Task<byte[]?>` | Return cover image bytes (JPEG/PNG format), or `null` if not found |
| `IsAvailable` | `bool` | Whether the cover service is currently reachable |

**Caching**: Returned cover data is automatically cached by the application.

---

### `IProtocolProviderPlugin` Interface

**Namespace**: `CatClawMusic.Core.Interfaces`

```csharp
public interface IProtocolProviderPlugin : IPlugin
{
    string ProtocolName { get; }
    Task<List<RemoteFile>> ListFilesAsync(string path);
    Task<Stream> OpenReadAsync(string filePath);
    Task<bool> TestConnectionAsync(ConnectionProfile profile);
}
```

| Member | Type | Description |
|--------|------|-------------|
| `ProtocolName` | `string` | Protocol identifier, e.g. `"SMB"`, `"FTP"`, `"Dropbox"` |
| `ListFilesAsync(string)` | `Task<List<RemoteFile>>` | List files and directories at the given path |
| `OpenReadAsync(string)` | `Task<Stream>` | Open a read-only stream for remote file playback |
| `TestConnectionAsync(ConnectionProfile)` | `Task<bool>` | Test connectivity with the given profile parameters |

---

### `IAudioEnhancerPlugin` Interface

**Namespace**: `CatClawMusic.Core.Interfaces`

```csharp
public interface IAudioEnhancerPlugin : IPlugin
{
    bool IsEnabled { get; set; }
    float[] ProcessSamples(float[] samples, int sampleRate, int channels);
    void Reset();
}
```

| Member | Type | Description |
|--------|------|-------------|
| `IsEnabled` | `bool` | Toggle to bypass the enhancer without disabling the plugin |
| `ProcessSamples(float[], int, int)` | `float[]` | Process PCM float samples. Input/output range: `[-1.0, 1.0]` |
| `Reset()` | `void` | Called when track changes. Reset all processing state |

**Parameters for ProcessSamples**:
- `samples`: Interleaved PCM float samples
- `sampleRate`: Sample rate in Hz (e.g. 44100)
- `channels`: Channel count (1 = mono, 2 = stereo)

---

## Data Models

### `Song`

**Namespace**: `CatClawMusic.Core.Models`

```csharp
public class Song
{
    int Id              // Unique database ID
    string Title        // Song title
    string Artist       // Artist name (runtime field, not stored)
    string Album        // Album name (runtime field, not stored)
    int Duration        // Duration in milliseconds
    string FilePath     // File path or remote URI
    long FileSize       // File size in bytes
    int Bitrate         // Bitrate in bps
    int Year            // Release year
    string? Genre       // Genre
    string? CoverArtPath // Local cover cache path
    string? LyricsPath  // Local lyrics file path
}
```

### `LrcLyrics`

**Namespace**: `CatClawMusic.Core.Models`

```csharp
public class LrcLyrics
{
    LrcMetadata Metadata { get; set; }
    List<LrcLyricLine> Lines { get; set; }
}

public class LrcLyricLine
{
    TimeSpan Timestamp { get; set; }
    string Text { get; set; }
}
```

### `ConnectionProfile`

**Namespace**: `CatClawMusic.Core.Models`

```csharp
public class ConnectionProfile
{
    int Id
    string Name            // Display name
    ProtocolType Protocol  // WebDAV, Navidrome, SMB, DLNA, FTP, NFS
    string Host            // Server hostname or IP
    int Port               // Connection port
    string UserName        // Username
    string Password        // Password or API token
    string BasePath        // Base directory path
    bool IsEnabled         // Whether this profile is active
}
```

### `RemoteFile`

**Namespace**: `CatClawMusic.Core`

```csharp
public class RemoteFile
{
    string Name
    string Path
    bool IsDirectory
    long Size
    long LastModified
}
```

---

## Project Setup

### Minimal .csproj

```xml
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

### Build

```bash
dotnet build -c Release
```

Output: `bin/Release/net9.0/{AssemblyName}.dll`

---

## Best Practices

- **PluginId**: Use `{category}.{name}` format to ensure global uniqueness
- **Error Handling**: Catch exceptions internally and return `null` — never throw from plugin methods
- **Async**: Use `HttpClient` async methods; avoid blocking the UI thread
- **Cleanup**: Dispose `HttpClient`, file handles, and other resources in `ShutdownAsync()`
- **Size**: Keep the plugin DLL lean; avoid large NuGet dependencies
- **Capabilities**: Write clear, user-facing capability descriptions
