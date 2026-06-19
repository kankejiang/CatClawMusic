# 猫爪音乐 (CatClawMusic) Code Wiki

## 目录

1. [项目概述](#1-项目概述)
2. [项目结构](#2-项目结构)
3. [核心模型（Models）](#3-核心模型models)
4. [接口与抽象（Interfaces）](#4-接口与抽象interfaces)
5. [核心服务（Core Services）](#5-核心服务core-services)
6. [数据层（Data Layer）](#6-数据层data-layer)
7. [UI 层](#7-ui-层)
8. [依赖关系图](#8-依赖关系图)
9. [构建与运行](#9-构建与运行)
10. [关键功能实现](#10-关键功能实现)

---

## 1. 项目概述

### 1.1 项目简介

**猫爪音乐**是一款基于 .NET 10.0 和 Xamarin.Android 开发的 Android 原生音乐播放器应用。项目采用现代化的 MVVM 架构模式，结合依赖注入和分层架构设计，提供完整的音乐播放、管理和智能推荐功能。

### 1.2 技术栈

- **开发框架**: .NET 10.0.300 + Xamarin.Android
- **UI 框架**: AndroidX + Material Design 3
- **音频引擎**: ExoPlayer (Media3)
- **数据库**: SQLite (sqlite-net-pcl)
- **架构模式**: MVVM + 依赖注入
- **编程语言**: C# 12 (Preview)

### 1.3 主要功能

- **音乐播放**: 支持本地和网络音乐播放，多种播放模式（顺序、随机、单曲循环、列表循环）
- **歌词显示**: 支持 LRC/TTML 格式，逐字高亮歌词
- **网络音乐**: 支持 WebDAV、SMB/CIFS、Navidrome/Subsonic 协议
- **智能推荐**: 基于 AI 的音乐推荐和探索功能
- **音效系统**: 7 段 EQ、压缩器、混响、立体声扩展、磁带饱和、去齿音、限幅器
- **插件系统**: 支持封面提供者、歌词提供者、菜单扩展等插件
- **数据抓取**: 从网易云、QQ 音乐、豆瓣、百度百科、AI 搜索获取艺术家元数据
- **主题系统**: Material You 动态取色，支持深色模式

---

## 2. 项目结构

### 2.1 解决方案结构

```
CatClawMusic.sln
├── CatClawMusic.Core          # 核心业务层（模型、接口、服务）
├── CatClawMusic.Data          # 数据访问层（数据库、网络服务、数据抓取）
├── CatClawMusic.UI            # 用户界面层（Android UI、ViewModels）
└── CatClawMusic.Core.Tests    # 单元测试项目
```

### 2.2 项目职责

#### CatClawMusic.Core（核心层）

**职责**: 定义业务模型、服务接口和核心业务逻辑

**目录结构**:
```
CatClawMusic.Core/
├── Models/                    # 数据模型（Song, Artist, Album, Playlist 等）
├── Interfaces/                # 服务接口定义（IAudioPlayerService, IMusicLibraryService 等）
└── Services/                  # 核心服务实现（PlayQueue, LyricsService, PluginManager）
    └── AI/                    # AI 相关服务（AgentService, LlmClient, AgentTools）
```

**依赖**:
- TagLibSharp 2.3.0（音频标签读取）
- sqlite-net-pcl 1.9.172（SQLite 数据库）

#### CatClawMusic.Data（数据层）

**职责**: 数据持久化、网络服务集成、元数据抓取

**目录结构**:
```
CatClawMusic.Data/
├── MusicDatabase.cs           # SQLite 数据库操作
├── MusicLibraryService.cs     # 音乐库管理服务
├── MusicScanner.cs            # 音乐扫描器
├── NetworkMusicService.cs     # 网络音乐服务
├── WebDavService.cs           # WebDAV 文件服务
├── SmbService.cs              # SMB 文件服务
├── SubsonicService.cs         # Subsonic/Navidrome API 客户端
├── BackupService.cs           # 备份与恢复服务
├── ExploreDataService.cs      # 探索页面数据服务
├── *Scraper.cs                # 各种元数据抓取器
└── IArtistMetadataScraper.cs  # 抓取器接口
```

**依赖**:
- 项目引用: CatClawMusic.Core
- SMBLibrary 1.5.2（SMB 协议支持）

#### CatClawMusic.UI（UI 层）

**职责**: Android 用户界面、ViewModel、平台特定实现

**目录结构**:
```
CatClawMusic.UI/
├── MainActivity.cs            # 主 Activity
├── MainApplication.cs         # 应用入口，DI 容器配置
├── SplashActivity.cs          # 启动画面
├── ViewModels/                # MVVM ViewModel（9 个）
├── Fragments/                 # UI 片段（35 个）
├── Adapters/                  # RecyclerView 适配器（13 个）
├── Services/                  # UI 层服务（22 个）
│   └── Effects/               # 音频效果处理器（8 个）
├── Helpers/                   # 辅助类和自定义控件（14 个）
└── Platforms/Android/         # Android 平台特定实现（7 个）
```

**依赖**:
- 项目引用: CatClawMusic.Core, CatClawMusic.Data
- Xamarin.AndroidX.Media3.ExoPlayer 1.10.1
- CommunityToolkit.Mvvm 8.4.2
- Xamarin.Google.Android.Material 1.14.0
- Microsoft.Extensions.DependencyInjection 9.0.0

---

## 3. 核心模型（Models）

### 3.1 Song（歌曲）

**文件**: `CatClawMusic.Core/Models/Song.cs`

**职责**: 表示一首歌曲的完整信息

**关键属性**:
- `Id` (int): 主键，自增
- `Title` (string): 歌曲标题
- `ArtistId` (int): 艺术家 ID（外键）
- `AlbumId` (int): 专辑 ID（外键）
- `Duration` (int): 时长（毫秒）
- `FilePath` (string): 文件路径
- `FileSize` (long): 文件大小（字节）
- `Bitrate` (int): 比特率（kbps）
- `TrackNumber` (int): 专辑内曲目序号
- `Year` (int): 发行年份
- `Genre` (string?): 流派
- `CoverArtPath` (string?): 专辑封面路径
- `LyricsPath` (string?): 歌词文件路径
- `Source` (SongSource): 歌曲来源（Local/WebDAV/SMB/Cache）
- `Protocol` (ProtocolType): 远程协议类型
- `RemoteId` (string?): 远程歌曲唯一标识

**运行时属性**（不持久化）:
- `Artist` (string): 艺术家名称
- `AllArtists` (string): 全部艺术家名称（用 " / " 分隔）
- `Album` (string): 专辑名称
- `PlayCount` (int): 播放次数
- `IsAlsoOnNetwork` (bool): 本地歌曲是否同时存在于网络
- `IsAlsoLocal` (bool): 网络歌曲是否同时存在于本地

### 3.2 Artist（艺术家）

**文件**: `CatClawMusic.Core/Models/Artist.cs`

**职责**: 表示艺术家信息

**关键属性**:
- `Id` (int): 主键 ID
- `Name` (string): 艺术家名称
- `Cover` (string?): 封面 URL 或路径
- `Gender` (string?): 性别
- `Birthday` (string?): 生日
- `Region` (string?): 国籍/地区
- `Description` (string?): 简介

### 3.3 Album（专辑）

**文件**: `CatClawMusic.Core/Models/Album.cs`

**职责**: 表示专辑信息

**关键属性**:
- `Id` (int): 主键，自增
- `Title` (string): 专辑标题
- `Name` (string): 专辑名称（旧字段兼容）
- `Artist` (string): 艺术家名称
- `CoverArtPath` (string?): 封面路径
- `SongCount` (int): 歌曲数量
- `Year` (int?): 发行年份
- `ArtistId` (int): 艺术家 ID（外键）

### 3.4 Playlist（播放列表）

**文件**: `CatClawMusic.Core/Models/Playlist.cs`

**职责**: 表示播放列表

**关键属性**:
- `Id` (int): 主键，自增
- `Name` (string): 播放列表名称
- `CreatedAt` (long): 创建时间（Unix 时间戳）
- `UpdatedAt` (long): 更新时间（Unix 时间戳）
- `SongCount` (int): 歌曲数量
- `IsSystem` (bool): 是否系统播放列表
- `CoverSongId` (int): 封面歌曲 ID（运行时）

### 3.5 SongArtist（歌曲-艺术家关联）

**文件**: `CatClawMusic.Core/Models/SongArtist.cs`

**职责**: 歌曲与艺术家的多对多关联表

**关键属性**:
- `Id` (int): 主键，自增
- `SongId` (int): 歌曲 ID（外键）
- `ArtistId` (int): 艺术家 ID（外键）

### 3.6 PlaylistSong（播放列表-歌曲关联）

**文件**: `CatClawMusic.Core/Models/PlaylistSong.cs`

**职责**: 播放列表与歌曲的多对多关联表

**关键属性**:
- `Id` (int): 主键，自增
- `PlaylistId` (int): 播放列表 ID（外键）
- `SongId` (int): 歌曲 ID（外键）
- `Position` (int): 在播放列表中的排序位置

### 3.7 Lyric（歌词）

**文件**: `CatClawMusic.Core/Models/Lyric.cs`

**职责**: 歌词缓存模型

**关键属性**:
- `SongId` (int): 关联的歌曲 ID（主键）
- `LrcPath` (string?): 外部 LRC 文件路径
- `Content` (string?): 歌词内容文本

### 3.8 PlayHistory（播放历史）

**文件**: `CatClawMusic.Core/Models/PlayHistory.cs`

**职责**: 播放历史记录

**关键属性**:
- `Id` (int): 自增主键
- `SongId` (int): 关联的歌曲 ID
- `PlayedAt` (long): 最近播放时间（Unix 时间戳）
- `PlayCount` (int): 播放次数

### 3.9 Favorite（收藏）

**文件**: `CatClawMusic.Core/Models/Favorite.cs`

**职责**: 收藏记录

**关键属性**:
- `SongId` (int): 关联的歌曲 ID（主键）
- `AddedAt` (long): 收藏时间（Unix 时间戳）

### 3.10 CachedSong（缓存歌曲）

**文件**: `CatClawMusic.Core/Models/CachedSong.cs`

**职责**: 缓存歌曲信息

**关键属性**:
- `Id` (int): 主键，自增
- `SongId` (int): 关联的歌曲 ID
- `LocalPath` (string): 缓存本地路径
- `CachedAt` (long): 缓存时间（Unix 时间戳）
- `FileSize` (long): 缓存文件大小（字节）

### 3.11 ConnectionProfile（连接配置）

**文件**: `CatClawMusic.Core/Models/ConnectionProfile.cs`

**职责**: 网络音乐服务连接配置

**关键属性**:
- `Id` (int): 主键，自增
- `Name` (string): 显示名称
- `Protocol` (ProtocolType): 协议类型（WebDAV/Navidrome/SMB）
- `Host` (string): 主机地址
- `Port` (int): 端口
- `UserName` (string): 用户名
- `Password` (string): 密码 / Token
- `BasePath` (string): 基础路径
- `IsEnabled` (bool): 是否启用
- `ApiVersion` (string): Subsonic API 版本
- `ClientName` (string): 客户端名称
- `UseHttps` (bool): 是否使用 HTTPS

### 3.12 PluginInfo（插件信息）

**文件**: `CatClawMusic.Core/Models/PluginInfo.cs`

**职责**: 插件元数据和状态

**关键属性**:
- `PluginTypeId` (string): 插件类型唯一标识
- `Plugin` (IPlugin): 插件实例
- `SubPlugins` (List<IPlugin>): 子插件列表
- `IsEnabled` (bool): 是否已启用
- `DisplayNameOverride` (string?): 显示名称覆盖值
- `DisplayName` (string): 显示名称（只读）
- `Version` (string): 版本号（只读）
- `Author` (string): 作者（只读）
- `Description` (string): 描述文本
- `Capabilities` (List<string>): 能力列表（只读）
- `Category` (PluginCategory): 插件分类
- `IconEmoji` (string): 分类图标 Emoji

### 3.13 ChatMessage（AI 对话消息）

**文件**: `CatClawMusic.Core/Models/ChatMessage.cs`

**职责**: AI 对话消息模型

**关键属性**:
- `Role` (string): 角色（user/assistant/tool）
- `Content` (string): 内容
- `ToolCalls` (List<ToolCall>?): 工具调用列表
- `ToolCallId` (string?): 工具调用 ID
- `Name` (string?): 名称
- `Songs` (List<Song>?): 关联的歌曲列表

### 3.14 Lyrics（歌词结构）

**文件**: `CatClawMusic.Core/Models/Lyrics.cs`

**职责**: 歌词解析结果

**类**: `LrcLyrics`
- `Metadata` (LrcMetadata): 歌词元数据（ti、ar、al 等）
- `Lines` (List<LrcLyricLine>): 歌词行列表
- `HasPerLineAlignment` (bool): 是否有按行对齐方式

---

## 4. 接口与抽象（Interfaces）

### 4.1 IAudioPlayerService（音频播放服务）

**文件**: `CatClawMusic.Core/Interfaces/IAudioPlayerService.cs`

**职责**: 定义音频播放的核心操作

**方法**:
- `Task PlayAsync(string filePathOrUrl)`: 播放指定歌曲
- `Task PrepareWithoutPlayAsync(string filePathOrUrl)`: 仅准备播放（不自动播放）
- `Task ResumeAsync()`: 从暂停状态恢复播放
- `Task PauseAsync()`: 暂停播放
- `Task StopAsync()`: 停止播放
- `Task SeekAsync(TimeSpan position)`: 跳转到指定位置

**属性**:
- `bool IsPlaying`: 是否正在播放
- `string? CurrentSongFilePath`: 当前播放的歌曲文件路径
- `int AudioSessionId`: 音频会话 ID（用于 Visualizer 绑定）
- `TimeSpan CurrentPosition`: 当前播放位置
- `long RealtimePositionMs`: 直接从播放器读取实时位置（毫秒）
- `TimeSpan Duration`: 歌曲总时长
- `int Volume`: 音量（0-100）

**事件**:
- `event Action<byte[]>? PcmDataAvailable`: PCM 数据回调（原始音频字节，供频谱分析用）
- `event EventHandler<PlaybackStateChangedEventArgs> StateChanged`: 播放状态改变事件
- `event EventHandler<TimeSpan> PositionChanged`: 播放位置改变事件

### 4.2 IMusicLibraryService（音乐库服务）

**文件**: `CatClawMusic.Core/Interfaces/IMusicLibraryService.cs`

**职责**: 音乐库管理核心接口

**主要方法**:
- `Task<List<Song>> ScanLocalAsync(List<string>? customFolders = null)`: 扫描本地音乐
- `Task<List<Song>> ImportSongsAsync(List<Song> songs)`: 导入预扫描歌曲列表
- `Task<List<Song>> ScanNetworkAsync(ConnectionProfile profile)`: 扫描网络音乐
- `Task<List<Song>> SearchAsync(string keyword)`: 搜索歌曲（本地 + 网络）
- `Task<Stream?> GetAlbumCoverAsync(Song song)`: 获取专辑封面
- `Task<List<Song>> GetAllSongsAsync()`: 获取所有歌曲
- `Task<List<Song>> GetMergedSongsAsync()`: 获取去重后的全部歌曲
- `Task<int> GetMergedSongCountAsync()`: 获取合并歌曲数量
- `Task<int> GetFavoriteSongCountAsync()`: 获取收藏歌曲数量
- `Task<int> GetRecentSongCountAsync()`: 获取最近播放歌曲数量
- `Task<List<Song>> GetSongsByArtistAsync(int artistId)`: 按艺术家获取歌曲
- `Task<List<Song>> GetSongsByAlbumAsync(int albumId)`: 按专辑获取歌曲
- `Task<List<Playlist>> GetAllPlaylistsAsync()`: 获取所有播放列表
- `Task<List<Song>> GetPlaylistSongsAsync(int playlistId)`: 获取歌单中的歌曲
- `Task<int> CreatePlaylistAsync(string name)`: 创建播放列表
- `Task AddSongsToPlaylistAsync(int playlistId, List<int> songIds)`: 添加歌曲到播放列表
- `Task RemoveSongsFromPlaylistAsync(int playlistId, List<int> songIds)`: 从播放列表移除歌曲
- `Task DeletePlaylistAsync(int playlistId)`: 删除播放列表
- `Task<List<Artist>> GetAllArtistsAsync()`: 获取所有艺术家
- `Task<List<Album>> GetAllAlbumsAsync()`: 获取所有专辑
- `Task<Artist?> GetArtistAsync(int artistId)`: 获取艺术家详情
- `Task<Album?> GetAlbumAsync(int albumId)`: 获取专辑详情
- `Task<List<Song>> GetRecentSongsAsync(int limit = 50)`: 获取最近播放的歌曲
- `Task<List<Song>> GetFavoriteSongsAsync(int limit = 50)`: 获取收藏的歌曲
- `Task ToggleFavoriteAsync(int songId, bool favorite)`: 切换收藏状态
- `Task RecordPlayAsync(int songId)`: 记录播放
- `Task<List<Song>> GetListeningStatsAsync(int topN = 10)`: 获取播放统计

### 4.3 ILyricsService（歌词服务）

**文件**: `CatClawMusic.Core/Interfaces/ILyricsService.cs`

**职责**: 歌词解析和获取

**方法**:
- `Task<LrcLyrics?> GetLyricsAsync(Song song)`: 获取歌词（优先本地，失败后尝试网络提供者）
- `Task<LrcLyrics?> GetLocalLyricsAsync(Song song, bool skipEmbedded = false, bool preferEmbedded = false)`: 从本地文件获取歌词
- `LrcLyrics? ParseLrc(string lrcContent)`: 解析 LRC 格式字符串
- `LrcLyrics? ParseTtml(string ttmlContent)`: 解析 TTML 格式歌词
- `LrcLyrics? ParseTtmlFromFile(string filePath)`: 从文件解析 TTML 格式
- `Task<LrcLyrics?> ParseTtmlFromFileAsync(string filePath)`: 异步从文件解析 TTML 格式
- `int GetCurrentLyricIndex(LrcLyrics? lyrics, TimeSpan position)`: 根据播放位置获取当前歌词行索引
- `int GetCurrentWordIndex(LrcLyricLine? line, TimeSpan position)`: 根据播放位置获取当前行内的逐字歌词索引

### 4.4 INetworkMusicService（网络音乐服务）

**文件**: `CatClawMusic.Core/Interfaces/INetworkMusicService.cs`

**职责**: 网络音乐服务接口

**方法**:
- `Task<List<ConnectionProfile>> GetProfilesAsync()`: 获取已配置的连接列表
- `Task<List<Song>> ScanAsync(ConnectionProfile profile, IProgress<(int done, int total, string status)>? progress = null, Action<List<Song>>? songBatchCallback = null)`: 扫描网络音乐库
- `Task<List<Song>> SearchAsync(string keyword, ConnectionProfile profile)`: 搜索网络歌曲
- `Task<Stream?> GetCoverAsync(string songId, ConnectionProfile profile)`: 获取专辑封面流
- `Task<string> GetStreamUrlAsync(Song song, ConnectionProfile profile)`: 获取流媒体 URL
- `Task<string?> GetLyricsAsync(string remotePath, ConnectionProfile profile)`: 获取远程歌词文本
- `Task<Song?> FetchSongMetadataAsync(Song song, ConnectionProfile profile)`: 按需获取网络歌曲元数据

### 4.5 INetworkFileService（网络文件服务）

**文件**: `CatClawMusic.Core/Interfaces/INetworkFileService.cs`

**职责**: 网络文件访问接口（WebDAV/SMB）

**方法**:
- `Task<List<RemoteFile>> ListFilesAsync(string path)`: 列出指定路径下的文件（仅当前目录）
- `Task<List<RemoteFile>> ListAllFilesAsync(string path)`: 递归列出所有文件
- `Task<Stream> OpenReadAsync(string filePath)`: 打开文件流（用于读取）
- `Task<byte[]> OpenReadRangeAsync(string filePath, long offset, long length)`: 读取文件指定范围
- `Task<(bool Success, string Message)> TestConnectionAsync(ConnectionProfile profile)`: 测试连接
- `Task<RemoteFile?> GetFileInfoAsync(string filePath)`: 获取文件信息
- `Task<(bool Success, string Message)> UploadFileAsync(string remotePath, byte[] content, string? contentType = null)`: 上传文件到远程路径
- `void Configure(ConnectionProfile profile)`: 配置连接（初始化 HttpClient）

### 4.6 ISubsonicService（Subsonic 服务）

**文件**: `CatClawMusic.Core/Interfaces/ISubsonicService.cs`

**职责**: Subsonic / Navidrome API 服务接口

**方法**:
- `Task<(bool Success, string Message)> PingAsync(ConnectionProfile profile)`: 测试连接（ping）
- `Task<List<Song>> SearchAsync(string query, ConnectionProfile profile)`: 搜索歌曲/艺术家/专辑
- `Task<List<Song>> GetSongsAsync(ConnectionProfile profile, IProgress<(int done, int total, string status)>? progress = null, Func<List<Song>, Task>? songCallback = null)`: 浏览音乐库
- `Task<List<Album>> GetAlbumsAsync(ConnectionProfile profile)`: 获取专辑列表
- `string GetStreamUrl(string songId, ConnectionProfile profile)`: 获取歌曲流 URL
- `string GetCoverArtUrl(string coverArtId, ConnectionProfile profile)`: 获取封面图 URL
- `Task<byte[]?> GetCoverArtAsync(string coverArtId, ConnectionProfile profile)`: 下载封面图字节
- `Task<string?> GetLyricsAsync(string songId, ConnectionProfile profile)`: 获取歌词
- `Task<Song?> GetSongAsync(string songId, ConnectionProfile profile)`: 获取单首歌曲完整元数据

### 4.7 IPlugin（插件接口）

**文件**: `CatClawMusic.Core/Interfaces/IPlugin.cs`

**职责**: 插件基础接口

**属性**:
- `string PluginId`: 插件唯一标识
- `string Name`: 插件名称
- `string Version`: 版本号
- `string Author`: 作者
- `string Description`: 描述信息
- `List<string> Capabilities`: 能力列表

**方法**:
- `Task InitializeAsync()`: 初始化插件
- `Task ShutdownAsync()`: 关闭插件

**扩展接口**:
- `ICoverProviderPlugin`: 封面提供者插件
  - `Task<byte[]?> GetCoverAsync(Song song)`: 获取指定歌曲的封面图片
  - `bool IsAvailable`: 封面服务是否可用
- `IAudioEnhancerPlugin`: 音频增强插件
  - `bool IsEnabled`: 是否启用增强效果
  - `float[] ProcessSamples(float[] samples, int sampleRate, int channels)`: 处理音频采样数据
  - `void Reset()`: 重置增强器状态
- `IMenuContributorPlugin`: 菜单扩展插件
  - `List<MenuItemEntry> GetMenuItems(Song song)`: 获取菜单项列表
  - `Task OnMenuItemClicked(int itemId, Song song, object fragment)`: 菜单项点击回调

### 4.8 IPluginManager（插件管理器）

**文件**: `CatClawMusic.Core/Interfaces/IPluginManager.cs`

**职责**: 插件管理器接口

**方法**:
- `List<PluginInfo> GetAllPlugins()`: 获取所有插件信息
- `List<T> GetEnabledPlugins<T>() where T : IPlugin`: 获取指定类型的所有已启用插件实例
- `bool IsPluginEnabled(string pluginTypeId)`: 判断指定插件是否已启用
- `void SetPluginEnabled(string pluginTypeId, bool enabled)`: 设置插件的启用状态
- `Task InitializeAllAsync()`: 初始化所有已启用的插件
- `Task ShutdownAllAsync()`: 关闭所有已启用的插件
- `Task<PluginInfo?> InstallFromLocalFileAsync(string filePath, IProgress<(string, int)>? progress = null)`: 从本地文件安装插件
- `Task<PluginInfo?> InstallFromGitHubAsync(string repoUrl, IProgress<(string, int)>? progress = null)`: 从 GitHub Release 安装插件
- `Task<bool> UninstallPluginAsync(string pluginTypeId)`: 卸载指定插件

### 4.9 IAgentService（AI 代理服务）

**文件**: `CatClawMusic.Core/Interfaces/IAgentService.cs`

**职责**: AI 代理服务接口体系

**接口**:
- `ILlmClient`: LLM 客户端接口
  - `Task<LlmResponse> ChatAsync(List<ChatMessage> messages, List<ToolDefinition>? tools = null, CancellationToken ct = default)`: 与 LLM 对话
  - `Task<bool> TestConnectionAsync()`: 测试连接
  - `Task<List<string>> GetModelsAsync()`: 获取可用模型列表
- `IAgentTool`: AI 工具接口
  - `string Name`: 工具名称
  - `string Description`: 工具描述
  - `ToolDefinition GetDefinition()`: 获取工具定义
  - `Task<string> ExecuteAsync(string arguments)`: 执行工具
- `IAgentService`: 代理服务接口
  - `Task<ChatMessage> SendMessageAsync(string userMessage, Action<ChatMessage>? onPartialMessage = null, CancellationToken ct = default)`: 发送消息
  - `void ClearConversation()`: 清空对话
  - `List<ChatMessage> GetConversationHistory()`: 获取对话历史
  - `bool IsConfigured`: 是否已配置
  - `BuiltinAgent GetCurrentAgent()`: 获取当前代理
  - `void SetCurrentAgent(string agentId)`: 设置当前代理
- `IAgentConfigStorage`: 配置存储接口
  - `string? GetString(string key, string? defaultValue = null)`: 获取字符串配置
  - `void SetString(string key, string value)`: 设置字符串配置
  - `int GetInt(string key, int defaultValue = 0)`: 获取整数配置
  - `void SetInt(string key, int value)`: 设置整数配置
  - `float GetFloat(string key, float defaultValue = 0f)`: 获取浮点数配置
  - `void SetFloat(string key, float value)`: 设置浮点数配置
  - `bool GetBool(string key, bool defaultValue = false)`: 获取布尔配置
  - `void SetBool(string key, bool value)`: 设置布尔配置

### 4.10 其他接口

#### INavigationService（导航服务）

**文件**: `CatClawMusic.Core/Interfaces/INavigationService.cs`

**方法**:
- `void PushFragment(string route, Dictionary<string, object>? parameters = null)`: 推入全屏 Fragment
- `void GoBack()`: 返回上一页
- `void SwitchTab(int tabIndex)`: 切换 Tab

#### ILogService（日志服务）

**文件**: `CatClawMusic.Core/Interfaces/ILogService.cs`

**方法**:
- `void Info(string tag, string message)`: 输出信息级别日志
- `void Warn(string tag, string message)`: 输出警告级别日志
- `void Error(string tag, string message)`: 输出错误级别日志

#### IDialogService（对话框服务）

**文件**: `CatClawMusic.Core/Interfaces/IDialogService.cs`

**方法**:
- `Task ShowAlertAsync(string title, string message, string buttonText = "确定")`: 显示警告对话框
- `Task<bool> ShowConfirmAsync(string title, string message, string acceptText = "确定", string cancelText = "取消")`: 显示确认对话框

#### IPermissionService（权限服务）

**文件**: `CatClawMusic.Core/Interfaces/IPermissionService.cs`

**方法**:
- `Task<bool> CheckStoragePermissionAsync()`: 检查存储/媒体权限是否已授予
- `Task<bool> RequestStoragePermissionAsync()`: 请求存储/媒体权限
- `Task<bool> CheckManageStoragePermissionAsync()`: 检查全文件管理权限
- `Task<bool> RequestManageStoragePermissionAsync()`: 请求全文件管理权限
- `string GetPermissionStatus()`: 获取权限状态描述
- `bool IsPermanentlyDenied()`: 权限是否被永久拒绝
- `void OpenAppSettings()`: 打开应用系统设置页面
- `Task<bool> CheckOverlayPermissionAsync()`: 检查悬浮窗权限是否已授予
- `Task<bool> RequestOverlayPermissionAsync()`: 请求悬浮窗权限

#### IThemeService（主题服务）

**文件**: `CatClawMusic.Core/Interfaces/IThemeService.cs`

**职责**: 主题管理（深色模式、Material You）

#### IMainThreadDispatcher（主线程调度器）

**文件**: `CatClawMusic.Core/Interfaces/IMainThreadDispatcher.cs`

**职责**: 主线程调度

---

## 5. 核心服务（Core Services）

### 5.1 PlayQueue（播放队列）

**文件**: `CatClawMusic.Core/Services/PlayQueue.cs`

**职责**: 管理播放队列，支持多种播放模式

**关键成员**:
- `_originalList` (List<Song>): 原始播放列表（顺序模式使用）
- `_shuffledList` (List<Song>): 洗牌后的播放列表（随机模式使用）
- `_currentIndex` (int): 当前播放索引
- `_history` (Stack<int>): 播放历史记录栈（支持"上一曲"）
- `_playMode` (PlayMode): 当前播放模式
- `_songIdToIndex` (Dictionary<int, int>): 歌曲 ID 到索引的映射（O(1) 查找）

**属性**:
- `PlayMode PlayMode`: 当前播放模式
- `Song? CurrentSong`: 当前歌曲

**方法**:
- `void SetSongs(IEnumerable<Song> songs)`: 设置播放列表
- `void EnableShuffle()`: 开启随机播放（洗牌）
- `Song? Next()`: 下一首
- `Song? Previous()`: 上一曲
- `void SelectSong(int songId)`: 用户手动选择某首歌
- `void AddToEnd(Song song)`: 添加到队列末尾
- `Song? PeekNext()`: 预览下一首（不改变队列状态）
- `List<Song> GetUpcomingSongs(int count = 3)`: 获取接下来 N 首预播歌曲
- `IReadOnlyList<Song> GetSongs()`: 获取当前播放列表

**播放模式枚举** (PlayMode):
- `Sequential`: 顺序播放
- `Shuffle`: 随机播放
- `SingleRepeat`: 单曲循环
- `ListRepeat`: 列表循环

**辅助类** (ShuffleService):
- `static List<T> Shuffle<T>(IList<T> list)`: Fisher-Yates 洗牌算法

### 5.2 LyricsService（歌词服务）

**文件**: `CatClawMusic.Core/Services/LyricsService.cs`

**职责**: 歌词解析和获取，支持 LRC 和 TTML 格式

**实现接口**: `ILyricsService`

**关键特性**:
- 支持 LRC 格式解析（时间标签 + 文本）
- 支持 TTML 格式解析（逐字歌词）
- 支持嵌入式歌词（从音频文件中提取）
- 支持外部歌词文件（.lrc/.ttml）
- 支持插件扩展（ICoverProviderPlugin）
- 根据播放位置计算当前歌词行和词

**依赖**:
- `IPluginManager?`: 插件管理器（可选，由 UI 层设置）

### 5.3 TagReader（标签读取器）

**文件**: `CatClawMusic.Core/Services/TagReader.cs`

**职责**: 从音频文件中读取元数据

**方法**:
- `static Song? ReadFromStream(Stream stream, string uri, string displayName, long fileSize)`: 从 Content URI Stream 读取歌曲信息（SAF 路径用）
- `static Song? ReadSongInfo(string filePath)`: 从音频文件读取歌曲信息

**依赖**:
- TagLibSharp 库：读取音频标签（ID3、Vorbis 等）

### 5.4 PluginManager（插件管理器）

**文件**: `CatClawMusic.Core/Services/PluginManager.cs`

**职责**: 管理插件的生命周期（加载、安装、卸载、启用/禁用）

**实现接口**: `IPluginManager`

**关键特性**:
- 支持从本地文件安装插件
- 支持从 GitHub Release 安装插件
- 使用反射适配器模式统一不同插件接口
- 插件初始化采用 fire-and-forget 模式，异常不影响主应用

**内部适配器类**:
- `BasicPluginAdapter`: 基础插件适配器
- `CoverProviderAdapter`: 封面提供者适配器
- `LyricsProviderAdapter`: 歌词提供者适配器
- `MenuContributorAdapter`: 菜单扩展适配器
- `ProtocolProviderAdapter`: 协议提供者适配器
- `AudioEnhancerAdapter`: 音频增强适配器

### 5.5 MusicUtility（音乐工具类）

**文件**: `CatClawMusic.Core/Services/MusicUtility.cs`

**职责**: 音乐相关的静态工具方法

**关键成员**:
- `HashSet<string> KnownBandNames`: 已知乐队/组合名称（包含分隔符但不应被拆分的艺术家名）
- `string[] AudioExtensions`: 支持的音频文件扩展名列表

**主要功能**:
- 拆分多艺术家名称（支持 "、" "/" "&" "feat." 等分隔符）
- 识别已知乐队名称避免错误拆分

### 5.6 AI 服务（AgentService）

**文件**: `CatClawMusic.Core/Services/AI/AgentService.cs`

**职责**: AI 代理服务，管理对话历史和工具调用

**实现接口**: `IAgentService`

**关键成员**:
- `_llmClient` (ILlmClient): LLM 客户端
- `_tools` (IEnumerable<IAgentTool>): 工具集合
- `_conversationHistory` (List<ChatMessage>): 对话历史
- `_logService` (ILogService): 日志服务
- `_musicLibrary` (IMusicLibraryService?): 音乐库服务（可选）
- `_currentAgentId` (string): 当前代理 ID
- `_staticConfigStorage` (IAgentConfigStorage?): 静态配置存储实例

**方法**:
- `static void Initialize(IAgentConfigStorage configStorage)`: 初始化静态配置存储
- `static LlmProviderInfo[] GetProviders()`: 获取所有可用的 LLM 提供商
- 实现 `IAgentService` 接口的所有方法

**支持的 LLM 提供商**:
- OpenAI
- Anthropic Claude
- Google Gemini
- 自定义 OpenAI 兼容 API

### 5.7 OpenAiCompatibleLlmClient（LLM 客户端）

**文件**: `CatClawMusic.Core/Services/AI/OpenAiCompatibleLlmClient.cs`

**职责**: OpenAI 兼容的 LLM 客户端实现

**实现接口**: `ILlmClient`

**关键成员**:
- `_httpClient` (HttpClient): HTTP 客户端
- `_configProvider` (Func<LlmConfig>): 配置提供器
- `_fallbackConfigsProvider` (Func<List<LlmConfig>>?): 备用配置提供器

**方法**:
- `List<LlmConfig> GetFallbackConfigs()`: 获取所有可用的退回配置
- `static LlmResponse ParseResponse(string responseBody)`: 解析 API 响应
- 实现 `ILlmClient` 接口的所有方法

**特性**:
- 支持多个 LLM 提供商
- 支持自动故障转移到备用配置
- 支持工具调用（Function Calling）

### 5.8 AgentTools（AI 工具集）

**文件**: `CatClawMusic.Core/Services/AI/AgentTools.cs`

**职责**: 提供 AI 代理可调用的工具集

**工具列表**:

| 工具类 | 功能 |
|--------|------|
| `SearchMusicTool` | 搜索音乐库中的歌曲 |
| `AddSongToPlaylistTool` | 将歌曲添加到指定歌单中 |
| `RemoveSongFromPlaylistTool` | 从指定歌单中移除歌曲 |
| `ListPlaylistsTool` | 获取用户所有播放列表 |
| `GetPlaylistSongsTool` | 获取指定歌单中的歌曲列表 |
| `DeletePlaylistTool` | 删除指定的播放列表 |
| `PlaySongTool` | 播放指定歌曲 |
| `ControlPlaybackTool` | 控制音乐播放（暂停、恢复、下一首、上一首、停止、调节音量、跳转） |
| `GetCurrentSongTool` | 获取当前正在播放的歌曲信息 |
| `GetPlayQueueTool` | 获取当前播放队列信息 |
| `ToggleFavoriteTool` | 收藏或取消收藏一首歌曲 |
| `GetFavoriteSongsTool` | 获取收藏的歌曲列表 |
| `GetRecentSongsTool` | 获取最近播放的歌曲列表 |
| `GetListeningStatsTool` | 获取播放统计数据 |
| `AddToPlayQueueTool` | 将歌曲添加到播放队列 |
| `ClearPlayQueueTool` | 清空播放队列并停止播放 |

**依赖**: 所有工具类都实现 `IAgentTool` 接口，依赖 `IMusicLibraryService`, `IAudioPlayerService`, `PlayQueue` 等服务

---

## 6. 数据层（Data Layer）

### 6.1 MusicDatabase（数据库）

**文件**: `CatClawMusic.Data/MusicDatabase.cs`

**职责**: SQLite 数据库操作，管理所有持久化数据

**关键成员**:
- `_database` (SQLiteAsyncConnection): SQLite 异步数据库连接
- `_isInitialized` (bool): 数据库是否已完成初始化
- `_initSemaphore` (SemaphoreSlim): 初始化信号量，确保并发安全
- `ExtractArtistNameCallback` (Func<string, string?>?): 从文件路径提取艺术家名称的回调

**数据库表**:
- `Song`: 歌曲表
- `Artist`: 艺术家表
- `Album`: 专辑表
- `Playlist`: 播放列表表
- `PlaylistSong`: 播放列表-歌曲关联表
- `SongArtist`: 歌曲-艺术家关联表
- `Lyric`: 歌词缓存表
- `PlayHistory`: 播放历史表
- `Favorite`: 收藏表
- `CachedSong`: 缓存歌曲表
- `ConnectionProfile`: 连接配置表

**主要方法**:

**初始化**:
- `Task EnsureInitializedAsync()`: 确保数据库已初始化（创建表、索引、迁移）
- `private Task CreateIndexesAsync()`: 创建数据库查询索引

**歌曲操作**:
- `Task<List<Song>> GetSongsAsync()`: 获取所有本地歌曲
- `Task<int> GetLocalSongCountAsync()`: 获取本地歌曲数量
- `Task<Song?> GetSongByIdAsync(int id)`: 根据 ID 获取单首歌曲
- `Task<List<Song>> SearchSongsAsync(string keyword)`: 数据库层面搜索歌曲
- `Task<List<Song>> GetSongsByArtistAsync(string artist)`: 按艺术家获取歌曲
- `Task<int> DeleteSongAsync(Song song)`: 删除指定歌曲
- `Task InsertSongAsync(Song song)`: 插入单首歌曲
- `Task SaveSongArtistsBatchAsync(List<(int SongId, List<int> ArtistIds)> entries)`: 批量保存歌曲-艺术家关联
- `Task<Dictionary<int, string>> GetAllArtistsForSongsAsync(IEnumerable<int> songIds)`: 批量获取歌曲的所有艺术家
- `Task ClearLocalSongsAsync()`: 清除本地歌曲

**艺术家操作**:
- `Task<List<Artist>> GetAllArtistsAsync()`: 获取所有艺术家
- `Task<int> EnsureArtistAsync(string name)`: 确保艺术家存在，返回 ID
- `Task UpdateArtistAsync(Artist artist)`: 更新艺术家信息
- `Task CleanupOrphanedArtistsAndAlbumsAsync()`: 清理孤立的艺术家和专辑

**专辑操作**:
- `Task<List<Album>> GetAllAlbumsAsync()`: 获取所有专辑
- `Task RepairAlbumAssociationsAsync()`: 修复专辑关联

**播放历史与收藏**:
- `Task RecordPlayAsync(int songId)`: 记录播放历史
- `Task SetFavoriteAsync(int songId, bool isFav)`: 设置收藏状态

**歌词操作**:
- `Task SaveLyricAsync(int songId, string? lrcPath, string? content)`: 保存歌词信息

**播放列表操作**:
- `Task<List<Playlist>> GetAllPlaylistsAsync()`: 获取所有播放列表
- `Task<int> CreatePlaylistAsync(string name)`: 创建播放列表
- `Task UpdatePlaylistAsync(Playlist playlist)`: 更新播放列表
- `Task DeletePlaylistAsync(int playlistId)`: 删除播放列表
- `Task AddSongToPlaylistAsync(int playlistId, int songId)`: 向播放列表添加歌曲
- `Task RemoveSongFromPlaylistAsync(int playlistId, int songId)`: 从播放列表移除歌曲
- `Task<List<Song>> GetPlaylistSongsAsync(int playlistId)`: 获取播放列表中的所有歌曲
- `Task UpdateSongPositionAsync(int playlistId, int songId, int newPosition)`: 更新歌曲位置
- `Task UpdatePlaylistOrderAsync(int playlistId, List<int> orderedSongIds)`: 批量更新播放顺序

**缓存歌曲操作**:
- `Task SaveCachedSongAsync(CachedSong cachedSong)`: 保存缓存歌曲
- `Task DeleteCachedSongAsync(int songId)`: 删除缓存歌曲
- `Task ReplaceNetworkSongsAsync(List<Song> songs)`: 替换网络歌曲
- `Task ClearCachedNetworkSongsAsync()`: 清除缓存的网络歌曲

**连接配置**:
- `Task<List<ConnectionProfile>> GetConnectionProfilesAsync()`: 获取所有连接配置

**关键业务逻辑**:
- 使用 SQLite-net 进行数据持久化
- 支持多艺术家关联（SongArtists 多对多表）
- 动态批次大小和内存缓存优化批量插入性能
- 支持数据库迁移（添加新列）
- 提供完整的 CRUD 操作和复杂查询

### 6.2 MusicLibraryService（音乐库服务）

**文件**: `CatClawMusic.Data/MusicLibraryService.cs`

**职责**: 统一管理本地和网络音乐库操作

**实现接口**: `IMusicLibraryService`

**依赖**:
- `MusicDatabase`: 数据库操作实例
- `INetworkMusicService?`: 网络音乐服务（可选）
- `TagReader`: 读取音频标签
- `MusicUtility`: 音乐工具类

**主要方法**:
- `Task<List<Song>> ScanLocalAsync(List<string>? customFolders = null)`: 扫描本地文件夹中的音乐
- `Task<List<Song>> ScanNetworkAsync(ConnectionProfile profile)`: 扫描网络音乐源
- `Task<List<Song>> SearchSongsAsync(string keyword)`: 搜索歌曲
- `Task<List<Song>> GetSongsByArtistAsync(string artist)`: 按艺术家获取歌曲
- `Task<List<Song>> GetSongsByAlbumAsync(string album)`: 按专辑获取歌曲
- `Task<List<Album>> GetAllAlbumsAsync()`: 获取所有专辑
- `Task<int> GetMergedSongCountAsync()`: 获取合并后的歌曲数量
- `Task<int> GetFavoriteSongCountAsync()`: 获取收藏歌曲数量
- `Task<int> GetRecentSongCountAsync()`: 获取最近播放歌曲数量
- 播放列表相关方法（委托给 `_db`）
- 缓存歌曲相关方法

**关键业务逻辑**:
- 本地扫描支持递归目录扫描和去重
- 使用 `TagReader` 读取音频文件元数据
- 委托数据库操作给 `MusicDatabase`

### 6.3 MusicScanner（音乐扫描器）

**文件**: `CatClawMusic.Data/MusicScanner.cs`

**职责**: 批量入库歌曲，优化性能

**关键成员**:
- `_db` (MusicDatabase): 数据库访问实例
- `_batchCallback` (Action<List<Song>>?): 批次完成后的回调函数
- `_pending` (List<Song>): 当前待刷写的歌曲缓冲区
- `_totalInserted` (int): 累计成功插入的歌曲总数
- `_artistCache` (Dictionary<string, int>): 艺术家名称到 ID 的内存缓存
- `_albumCache` (Dictionary<(string album, int artistId), int>): 专辑到 ID 的内存缓存
- `_batchFilePaths` (HashSet<string>): 当前批次内已处理的文件路径集合
- `_batchRemoteIds` (HashSet<string>): 当前批次内已处理的远程 ID 集合

**方法**:
- `Task AddSongsBatchAsync(IEnumerable<Song> songs)`: 批量添加歌曲到待处理缓冲区
- `Task AddSongAsync(Song song)`: 添加单首歌曲到待处理缓冲区
- `Task FlushAsync()`: 将缓冲区中的所有歌曲批量写入数据库
- `private Task FlushBatchAsync(Song[] batch)`: 执行实际的批量写入
- `private Task LoadCachesAsync()`: 从数据库加载艺术家和专辑到内存缓存
- `private int GetBatchSize()`: 根据已插入数量计算动态批次大小

**关键业务逻辑**:
- 动态批次大小：初期少量提交以便快速反馈，后期大批量提交以提升吞吐
- 内存缓存：首次刷写时从数据库加载全部艺术家和专辑到字典中，后续查重直接走内存
- 批次内去重：使用 HashSet 在当前批次内对文件路径和远程 ID 进行去重
- 支持多艺术家拆分和关联
- 回调通知：每批入库完成后通过回调通知调用方

### 6.4 NetworkMusicService（网络音乐服务）

**文件**: `CatClawMusic.Data/NetworkMusicService.cs`

**职责**: 按协议类型分发扫描和搜索操作

**实现接口**: `INetworkMusicService`

**依赖**:
- `MusicDatabase`: 数据库操作实例
- `ISubsonicService`: Subsonic/Navidrome API 客户端
- `INetworkFileService`: WebDAV 文件服务
- `INetworkFileService`: SMB 文件服务
- `MusicScanner`: 音乐扫描器
- `TagReader`: 音频标签读取器

**关键成员**:
- `_db` (MusicDatabase): 数据库操作实例
- `_subsonic` (ISubsonicService): Subsonic 客户端
- `_webDav` (INetworkFileService): WebDAV 服务
- `_smb` (INetworkFileService): SMB 服务
- `ScanSemaphore` (SemaphoreSlim): 扫描信号量（最大并发 2）
- `MaxScanDepth` (const int): WebDAV 目录扫描最大递归深度（20）
- `AudioExtSet` (HashSet<string>): 支持的音频文件扩展名集合

**方法**:
- `Task<List<ConnectionProfile>> GetProfilesAsync()`: 获取所有连接配置
- `Task<List<Song>> ScanAsync(ConnectionProfile profile, IProgress<(int done, int total, string status)>? progress = null, Action<List<Song>>? songBatchCallback = null)`: 扫描网络音乐源
- `Task<List<Song>> SearchAsync(string keyword, ConnectionProfile profile)`: 搜索网络音乐
- `Task<string> GetStreamUrlAsync(Song song, ConnectionProfile profile)`: 获取歌曲流 URL
- `private Task<(List<Song> NewSongs, HashSet<string> AllFoundIds)> ScanWebDavAsync(...)`: 扫描 WebDAV 目录
- `private Task<(List<Song> NewSongs, HashSet<string> AllFoundIds)> ScanSmbAsync(...)`: 扫描 SMB 目录
- `private Task ScanSmbDirectoryAsync(...)`: 递归扫描 SMB 目录

**关键业务逻辑**:
- 按协议类型（Navidrome、WebDAV、SMB）分发扫描和搜索操作
- 支持并发扫描（最大 2 个并发）
- 支持增量回调和进度报告
- 自动清理已移除的网络歌曲
- 从音频文件中读取元数据（标题、艺术家、专辑等）

### 6.5 WebDavService（WebDAV 服务）

**文件**: `CatClawMusic.Data/WebDavService.cs`

**职责**: WebDAV 文件访问

**实现接口**: `INetworkFileService`, `IDisposable`

**依赖**:
- `HttpClient`: HTTP 客户端
- `ConnectionProfile`: 连接配置
- `RemoteFile`: 远程文件模型

**方法**:
- `Task<(bool Success, string Message)> TestConnectionAsync(ConnectionProfile profile)`: 测试连接
- `Task<List<RemoteFile>> ListFilesAsync(string path)`: 列出指定路径下的文件和目录
- `Task<List<RemoteFile>> ListAllFilesAsync(string path)`: 列出所有文件（深度 PROPFIND）
- `void Configure(ConnectionProfile profile)`: 配置并连接到 WebDAV 服务器
- `Task<Stream> OpenReadAsync(string filePath)`: 以流的方式读取远程文件
- `Task<byte[]> OpenReadRangeAsync(string filePath, long offset, long length)`: 读取指定范围的字节
- `Task<RemoteFile?> GetFileInfoAsync(string filePath)`: 获取远程文件信息
- `Task<(bool Success, string Message)> UploadFileAsync(string remotePath, byte[] content, string? contentType = null)`: 上传文件

**关键业务逻辑**:
- 使用 HTTP 协议实现 WebDAV 文件访问
- 支持 PROPFIND 请求获取文件列表
- 支持 Basic 认证
- 支持 HTTPS 和 HTTP

### 6.6 SmbService（SMB 服务）

**文件**: `CatClawMusic.Data/SmbService.cs`

**职责**: SMB/CIFS 文件访问

**实现接口**: `INetworkFileService`, `IDisposable`

**依赖**:
- `SMBLibrary`: SMB 协议库
- `SMB2Client`: SMB2 客户端
- `ConnectionProfile`: 连接配置
- `RemoteFile`: 远程文件模型

**方法**:
- `void Configure(ConnectionProfile profile)`: 配置并连接到 SMB 服务器
- `Task<(bool Success, List<string> Shares, string Message)> ListSharesAsync(ConnectionProfile profile)`: 列出可用的共享名
- `Task<(bool Success, string Message)> TestConnectionAsync(ConnectionProfile profile)`: 测试连接
- `Task<List<RemoteFile>> ListFilesAsync(string path)`: 列出指定路径下的文件和目录
- `Task<Stream> OpenReadAsync(string filePath)`: 以流的方式读取远程文件
- `Task<byte[]> OpenReadRangeAsync(string filePath, long offset, long length)`: 读取指定范围的字节
- `Task<RemoteFile?> GetFileInfoAsync(string filePath)`: 获取远程文件信息

**关键业务逻辑**:
- 使用 SMBLibrary 实现 SMB/CIFS 协议
- 支持 SMB2 协议
- 支持域名、用户名、密码认证
- 支持共享名连接
- 线程安全的连接管理

### 6.7 SubsonicService（Subsonic 服务）

**文件**: `CatClawMusic.Data/SubsonicService.cs`

**职责**: Subsonic/Navidrome API 客户端

**实现接口**: `ISubsonicService`

**依赖**:
- `HttpClient`: HTTP 客户端（超时 15 秒）
- `ConnectionProfile`: 连接配置
- `Song`, `Album`: 数据模型

**方法**:
- `Task<(bool Success, string Message)> PingAsync(ConnectionProfile profile)`: 测试连接（ping.view）
- `Task<List<Song>> SearchAsync(string query, ConnectionProfile profile)`: 搜索歌曲（search3 API）
- `Task<List<Song>> GetSongsAsync(ConnectionProfile profile, IProgress<(int done, int total, string status)>? progress = null, Func<List<Song>, Task>? songCallback = null)`: 获取所有歌曲
- `Task<List<Album>> GetAlbumsAsync(ConnectionProfile profile)`: 获取专辑列表
- `string GetStreamUrl(string songId, ConnectionProfile profile)`: 构建歌曲流播放 URL
- `string GetCoverArtUrl(string coverArtId, ConnectionProfile profile)`: 构建封面图 URL
- `Task<byte[]?> GetCoverArtAsync(string coverArtId, ConnectionProfile profile)`: 获取封面图
- `Task<string?> GetLyricsAsync(string songId, ConnectionProfile profile)`: 获取歌词
- `Task<Song?> GetSongAsync(string songId, ConnectionProfile profile)`: 根据 ID 获取单首歌曲

**关键业务逻辑**:
- 实现 Subsonic/Navidrome API 客户端
- 使用 salt + token 认证方式
- 支持 OpenSubsonic 结构化歌词
- 分批获取专辑和歌曲信息
- 支持增量回调

### 6.8 BackupService（备份服务）

**文件**: `CatClawMusic.Data/BackupService.cs`

**职责**: 数据备份与恢复

**依赖**:
- `MusicDatabase`: 数据库操作实例
- `IAgentConfigStorage`: AI 配置存储接口

**相关数据类**:
- `BackupData`: 备份数据结构（包含播放列表、播放历史、收藏、艺术家、LLM 配置等）
- `BackupItems`: 备份选项枚举（Flags）
- `ArtistBackupEntry`: 艺术家备份条目
- `PlaylistSongBackupEntry`: 歌单歌曲备份条目
- `PlayHistoryBackupEntry`: 播放记录备份条目
- `FavoriteBackupEntry`: 收藏备份条目

**方法**:
- `Task<string> BackupAsync(string externalStoragePath, BackupItems items = BackupItems.All)`: 执行备份
- `Task RestoreAsync(string filePath, BackupItems items = BackupItems.All)`: 从备份文件恢复数据
- `static string GetBackupDirectory(string externalStoragePath)`: 获取备份目录路径
- `static List<string> ListBackups(string externalStoragePath)`: 列出备份文件
- `static Task<BackupData?> ReadBackupInfoAsync(string filePath)`: 读取备份文件信息

**关键业务逻辑**:
- 支持选择性备份/恢复（播放列表、播放历史、收藏、艺术家元数据、AI 模型配置）
- 备份文件为 JSON 格式，包含歌曲标题和艺术家信息用于跨设备恢复
- 恢复时通过 Title+Artist 匹配本地歌曲

### 6.9 ExploreDataService（探索数据服务）

**文件**: `CatClawMusic.Data/ExploreDataService.cs`

**职责**: 为探索页面提供数据支持

**依赖**:
- `MusicDatabase`: 数据库操作实例
- `IMusicLibraryService`: 音乐库服务

**相关数据类**:
- `ArtistWithCount`: 艺术家及其歌曲数量
- `AlbumWithCount`: 专辑及其歌曲数量
- `DailyRecommendCache`: 每日推荐缓存

**方法**:
- `void SetSourceFilter(string filter)`: 设置来源筛选
- `Task<List<Song>> GetDailyRecommendAsync()`: 获取每日推荐（每天 0 点更新，随机 20 首）
- `Task<List<ArtistWithCount>> GetArtistsWithSongCountAsync()`: 获取所有艺术家及其歌曲数量
- `Task<List<AlbumWithCount>> GetAlbumsWithSongCountAsync()`: 获取所有专辑及其歌曲数量
- `Task<List<Song>> GetTopPlayedSongsAsync(int limit = 50)`: 获取最多播放的歌曲
- `Task<List<Song>> GetRecentlyAddedSongsAsync(int limit = 50)`: 获取最近 7 天内入库的歌曲
- `Task<List<Song>> GetSongsByArtistAsync(string artistName)`: 按艺术家获取歌曲
- `Task<List<Song>> GetSongsByAlbumAsync(string albumTitle)`: 按专辑获取歌曲

**关键业务逻辑**:
- 每日推荐支持内存缓存和磁盘缓存
- 支持来源筛选（全部、本地、网络）
- 通过 SongArtists 多对多表统计艺术家歌曲数量
- 从第一首歌曲获取封面信息用于列表显示

### 6.10 元数据抓取器

#### IArtistMetadataScraper（接口）

**文件**: `CatClawMusic.Data/IArtistMetadataScraper.cs`

**职责**: 定义艺术家元数据刮削器的统一接口

**属性**:
- `string SourceName`: 来源名称

**方法**:
- `Task<List<ArtistSearchResult>> SearchArtistsAsync(string name, int limit = 10)`: 搜索艺术家
- `Task<string?> DownloadAndCacheArtistCoverAsync(string coverUrl, string artistName)`: 下载并缓存艺术家封面

**ArtistSearchResult 属性**:
- `Source`, `Id`, `Name`, `CoverUrl`, `Alias`, `Gender`, `Birthday`, `Region`, `Description`, `RealName`, `Nickname`, `Ethnicity`, `BirthPlace`, `Education`, `Zodiac`, `Height`, `Agency`, `RepresentativeWorks`, `Occupation`, `ExtraInfo`

#### NetEaseMusicScraper（网易云音乐）

**文件**: `CatClawMusic.Data/NetEaseMusicScraper.cs`

**职责**: 从网易云音乐 API 获取艺术家和专辑信息

**特性**:
- 支持批量刮削艺术家封面
- 优先使用本地缓存

#### MultiSourcePhotoScraper（QQ 音乐）

**文件**: `CatClawMusic.Data/MultiSourcePhotoScraper.cs`

**职责**: 从 QQ 音乐获取艺术家元数据

**特性**:
- 华语艺术家覆盖最好

#### DoubanScraper（豆瓣）

**文件**: `CatClawMusic.Data/DoubanScraper.cs`

**职责**: 从豆瓣音乐人/歌手页面获取信息

**特性**:
- 中文简介质量高
- 使用网页抓取方式（豆瓣 API 需要登录）

#### BaiduBaikeScraper（百度百科）

**文件**: `CatClawMusic.Data/BaiduBaikeScraper.cs`

**职责**: 从百度百科获取艺术家信息

**特性**:
- 对中文艺术家的信息最全：本名、昵称、民族、国籍、出生地、生日、星座、身高、经纪公司、代表作品、职业等
- 解析 HTML 中的信息框（infobox）

#### AiArtistScraper（AI 搜索）

**文件**: `CatClawMusic.Data/AiArtistScraper.cs`

**职责**: 使用 LLM（大语言模型）搜索艺术家信息

**特性**:
- 支持获取性别、国籍、简介等元数据

---

## 7. UI 层

### 7.1 入口文件

#### MainApplication（应用入口）

**文件**: `CatClawMusic.UI/MainApplication.cs`

**职责**: 应用入口，DI 容器配置与初始化

**继承**: `Application`

**关键成员**:
- `static IServiceProvider Services`: 全局服务容器
- `OnCreate()`: 注册所有服务（单例/瞬态），初始化主题、插件、歌词服务
- `RegisterRescanReceiver()`: 注册插件触发的音乐库重新扫描广播

**注册的服务**:
- `MusicDatabase` (Singleton)
- `PlayQueue` (Singleton)
- `NowPlayingViewModel` (Singleton)
- `AudioPlayerService` (Singleton)
- `LyricsService` (Singleton)
- `IPluginManager` (Singleton)
- `IThemeService` (Singleton)
- 等等

#### MainActivity（主 Activity）

**文件**: `CatClawMusic.UI/MainActivity.cs`

**职责**: 主界面 Activity，管理底部 Tab 导航和迷你播放栏

**继承**: `AppCompatActivity`

**关键成员**:
- `static MainActivity Instance`: 全局 Activity 引用
- `TabPagerAdapter`: ViewPager2 适配器，管理 5 个 Tab 页
- `OnCreate()`: 设置沉浸式状态栏、TabLayout + ViewPager2、迷你播放栏绑定
- `OnResume/OnPause/OnDestroy()`: 生命周期管理

#### SplashActivity（启动画面）

**文件**: `CatClawMusic.UI/SplashActivity.cs`

**职责**: 启动画面，加载启动图并等待核心初始化完成

**继承**: `AppCompatActivity`

**关键成员**:
- `LoadSplashImageAsync()`: 根据设置加载启动图（默认猫图/自定义图片/自定义 API）
- `WaitForInitAsync()`: 等待数据库和播放状态恢复
- `TryTransition()`: 满足条件后跳转主界面（最小显示时间 + 图片加载 + 初始化完成）
- `DecodeBitmap()`: 采样率自适应解码避免 OOM

**常量**:
- `MinDisplayMs = 1500`: 最小显示时间
- `NetworkTimeoutMs = 4000`: 网络超时时间

#### TabPagerAdapter（Tab 适配器）

**文件**: `CatClawMusic.UI/TabPagerAdapter.cs`

**职责**: ViewPager2 的 Fragment 适配器，管理 5 个主 Tab 页面

**继承**: `FragmentStateAdapter`

**Tab 页面**:
- 0: FullLyrics（全屏歌词）
- 1: NowPlaying（正在播放）
- 2: Playlist（播放列表）
- 3: Search（搜索）
- 4: Library（音乐库）

**方法**:
- `CreateFragment(int position)`: 按位置创建并缓存 Fragment（避免 DI Transient 重复创建）
- `ItemCount = 5`
- `GetItemId/ContainsItem`: 稳定 ID 支持

### 7.2 ViewModels（9 个）

#### NowPlayingViewModel（正在播放）

**文件**: `CatClawMusic.UI/ViewModels/NowPlayingViewModel.cs`

**职责**: 管理当前播放状态，是整个播放逻辑的核心

**继承**: `ObservableObject`

**关键属性**:
- `CurrentSong` (Song?): 当前歌曲
- `IsPlaying` (bool): 是否正在播放
- `Position` (TimeSpan): 当前播放位置
- `Duration` (TimeSpan): 歌曲总时长
- `Progress` (double): 播放进度（0-1）
- `PlayMode` (PlayMode): 播放模式
- `Volume` (int): 音量
- `LyricLine` (LrcLyricLine?): 当前歌词行
- `LyricProgress` (double): 歌词进度

**关键命令**:
- `PlayPauseCommand`: 播放/暂停
- `NextCommand`: 下一首
- `PreviousCommand`: 上一首
- `SeekCommand`: 跳转

**依赖**:
- `PlayQueue`: 播放队列
- `IAudioPlayerService`: 音频播放服务
- `ILyricsService`: 歌词服务
- `MusicDatabase`: 数据库

#### LibraryViewModel（音乐库）

**文件**: `CatClawMusic.UI/ViewModels/LibraryViewModel.cs`

**职责**: 音乐库管理，加载/刷新本地和网络歌曲列表

**继承**: `ObservableObject`

**关键属性**:
- `Songs` (ObservableCollection<Song>): 歌曲列表
- `Albums` (List<Album>): 专辑列表
- `Artists` (List<Artist>): 艺术家列表
- `Playlists` (List<Playlist>): 播放列表
- `IsLoading` (bool): 是否正在加载
- `CurrentView` (string): 当前视图

**关键方法**:
- `LoadLocalAsync(bool forceReload)`: 加载本地音乐
- `Refresh()`: 刷新
- `ScanLocalMusicAsync()`: 扫描本地音乐
- `ScanNetworkMusicAsync()`: 扫描网络音乐

**依赖**:
- `MusicDatabase`: 数据库
- `AndroidMediaScanner`: Android 媒体扫描器
- `SafeContentScanner`: SAF 扫描器
- `INetworkFileService`: 网络文件服务

#### PlaylistViewModel（播放列表）

**文件**: `CatClawMusic.UI/ViewModels/PlaylistViewModel.cs`

**职责**: 播放列表管理

**关键属性**:
- `Playlists` (ObservableCollection<Playlist>): 播放列表

**关键命令**:
- `CreatePlaylistCommand`: 创建播放列表
- `DeletePlaylistCommand`: 删除播放列表

**依赖**: `MusicDatabase`

#### PlaylistDetailViewModel（播放列表详情）

**文件**: `CatClawMusic.UI/ViewModels/PlaylistDetailViewModel.cs`

**职责**: 播放列表详情页

**关键属性**:
- `Playlist` (Playlist): 播放列表
- `Songs` (ObservableCollection<Song>): 歌曲列表

**关键命令**:
- `RemoveSongCommand`: 移除歌曲
- `PlayAllCommand`: 播放全部

#### SearchViewModel（搜索）

**文件**: `CatClawMusic.UI/ViewModels/SearchViewModel.cs`

**职责**: 搜索功能，带防抖的关键词搜索

**关键属性**:
- `Keyword` (string): 搜索关键词
- `SearchResults` (ObservableCollection<Song>): 搜索结果
- `IsSearching` (bool): 是否正在搜索
- `ResultCount` (int): 结果数量

**关键方法**:
- `SearchAsync(string keyword)`: 300ms 防抖搜索

**依赖**: `IMusicLibrary`

#### SettingsViewModel（设置）

**文件**: `CatClawMusic.UI/ViewModels/SettingsViewModel.cs`

**职责**: 设置管理（WebDAV 连接配置 + 音乐文件夹路径）

**关键属性**:
- `Host`, `Port`, `UserName`, `Password`, `BasePath`: WebDAV 配置
- `CacheSizeGB`: 缓存大小
- `MusicFolders`: 音乐文件夹路径
- `OnlyWiFiCache`: 仅 WiFi 缓存

**关键命令**:
- `AddMusicFolderCommand`: 添加音乐文件夹
- `ClearMusicFoldersCommand`: 清除音乐文件夹
- `TestConnectionCommand`: 测试连接
- `SaveConnectionCommand`: 保存连接
- `ClearCacheCommand`: 清除缓存

**依赖**:
- `INetworkFileService`: 网络文件服务
- `MusicDatabase`: 数据库
- `IDialogService`: 对话框服务
- `FolderPicker`: 文件夹选择器

#### WebDavSettingsViewModel（WebDAV 设置）

**文件**: `CatClawMusic.UI/ViewModels/WebDavSettingsViewModel.cs`

**职责**: WebDAV 连接配置的加载和保存

**关键属性**:
- `Name`, `Host`, `Port`, `UserName`, `Password`, `BasePath`, `UseHttps`: 连接配置
- `IsBusy` (bool): 是否繁忙
- `StatusText` (string): 状态文本

**关键方法**:
- `LoadAsync()`: 加载配置
- `SaveAsync()`: 保存配置

#### SmbSettingsViewModel（SMB 设置）

**文件**: `CatClawMusic.UI/ViewModels/SmbSettingsViewModel.cs`

**职责**: SMB 连接配置的加载和保存

**关键属性**:
- `Name`, `Host`, `Port`, `UserName`, `Password`, `DomainName`, `ShareName`, `BasePath`: 连接配置

**关键方法**:
- `LoadAsync()`: 加载配置
- `SaveAsync()`: 保存配置

#### NavidromeSettingsViewModel（Navidrome 设置）

**文件**: `CatClawMusic.UI/ViewModels/NavidromeSettingsViewModel.cs`

**职责**: Navidrome 服务器连接配置

### 7.3 Fragments（35 个）

#### 主要播放相关

| 文件 | 类名 | 职责 |
|------|------|------|
| `NowPlayingFragment.cs` | `NowPlayingFragment` | 播放页 UI：封面、进度条、歌词、控制按钮、音效面板 |
| `LandscapeNowPlayingFragment.cs` | `LandscapeNowPlayingFragment` | 横屏播放页 |
| `FullLyricsFragment.cs` | `FullLyricsFragment` | 全屏歌词页，支持逐字高亮 |
| `DesktopLyricFragment.cs` | `DesktopLyricFragment` | 桌面歌词浮窗设置 |

#### 音乐库相关

| 文件 | 类名 | 职责 |
|------|------|------|
| `LibraryFragment.cs` | `LibraryFragment` | 音乐库主页，显示歌曲/专辑/艺术家/播放列表 |
| `AlbumDetailFragment.cs` | `AlbumDetailFragment` | 专辑详情页 |
| `ArtistDetailFragment.cs` | `ArtistDetailFragment` | 艺术家详情页 |
| `PlaylistFragment.cs` | `PlaylistFragment` | 播放列表页 |
| `PlaylistDetailFragment.cs` | `PlaylistDetailFragment` | 播放列表详情页 |
| `SearchFragment.cs` | `SearchFragment` | 搜索页 |
| `RemoteMusicFragment.cs` | `RemoteMusicFragment` | 远程音乐浏览 |
| `HomeFragment.cs` | `HomeFragment` | 首页推荐 |

#### 设置相关

| 文件 | 类名 | 职责 |
|------|------|------|
| `SettingsFragment.cs` | `SettingsFragment` | 设置主页 |
| `SettingsSubPageFragment.cs` | `SettingsSubPageFragment` | 设置子页面基类 |
| `GeneralSettingsFragment.cs` | `GeneralSettingsFragment` | 通用设置 |
| `AppearanceSettingsFragment.cs` | `AppearanceSettingsFragment` | 外观/主题设置 |
| `LocalMusicSettingsFragment.cs` | `LocalMusicSettingsFragment` | 本地音乐设置 |
| `MusicFolderSettingsFragment.cs` | `MusicFolderSettingsFragment` | 音乐文件夹管理 |
| `WebDavSettingsFragment.cs` | `WebDavSettingsFragment` | WebDAV 设置 |
| `SmbSettingsFragment.cs` | `SmbSettingsFragment` | SMB 设置 |
| `NavidromeSettingsFragment.cs` | `NavidromeSettingsFragment` | Navidrome 设置 |
| `AiSettingsFragment.cs` | `AiSettingsFragment` | AI 设置 |
| `SplashSettingsFragment.cs` | `SplashSettingsFragment` | 启动页设置（默认猫图/自定义 API/自定义图片） |
| `PermissionManagementFragment.cs` | `PermissionManagementFragment` | 权限管理 |
| `BackupRestoreFragment.cs` | `BackupRestoreFragment` | 备份/恢复 |
| `AboutFragment.cs` | `AboutFragment` | 关于页面 |

#### AI/插件相关

| 文件 | 类名 | 职责 |
|------|------|------|
| `ModelManagerFragment.cs` | `ModelManagerFragment` | AI 模型管理 |
| `ModelEditFragment.cs` | `ModelEditFragment` | AI 模型编辑 |
| `PluginManagementFragment.cs` | `PluginManagementFragment` | 插件管理 |
| `ArtistMatchFragment.cs` | `ArtistMatchFragment` | 艺术家匹配 |
| `ArtistMatchDetailFragment.cs` | `ArtistMatchDetailFragment` | 艺术家匹配详情 |

#### 其他

| 文件 | 类名 | 职责 |
|------|------|------|
| `FolderBrowserFragment.cs` | `FolderBrowserFragment` | 文件夹浏览 |
| `SongDetailBottomSheet.cs` | `SongDetailBottomSheet` | 歌曲详情底部弹窗（编辑标签） |

### 7.4 Adapters（13 个）

#### SongAdapter（歌曲适配器）

**文件**: `CatClawMusic.UI/Adapters/SongAdapter.cs`

**职责**: 歌曲列表适配器，支持封面加载、拖拽排序、多选、上下文菜单

**继承**: `RecyclerView.Adapter`

**内部类**:
- `SongViewHolder`: 持有封面、标题、艺术家、菜单等视图引用
  - `Bind(Song, SongAdapter)`: 绑定数据，异步加载封面
  - `LoadCoverWithThrottleAsync()`: 带信号量限流的封面加载（最大 4 并发）
  - `CancelLoad()`: 取消封面加载
  - `GetCoverCachedPath()`: 磁盘缓存路径管理
- `ScrollListener`: 滚动优化监听器（滚动时暂停网络封面加载，停止后补加载）

**封面加载策略**:
- WebDAV/SMB 远程歌曲 → 网络下载（5 秒超时）
- 本地 MediaStore → `MediaStoreCoverHelper`
- content:// URI → `MediaMetadataRetriever`
- 文件路径 → `TagReader.ExtractCoverArt`
- 兜底 → `ICoverProviderPlugin` 插件

**依赖**:
- `BitmapCache`: 位图缓存
- `MediaStoreCoverHelper`: MediaStore 封面加载
- `INetworkMusicService`: 网络音乐服务
- `IPluginManager`: 插件管理器

#### 其他适配器

| 文件 | 类名 | 职责 |
|------|------|------|
| `AlbumAdapter.cs` | `AlbumAdapter` | 专辑列表适配器 |
| `ArtistAdapter.cs` | `ArtistAdapter` | 艺术家列表适配器 |
| `PlaylistAdapter.cs` | `PlaylistAdapter` | 播放列表适配器 |
| `UpcomingSongAdapter.cs` | `UpcomingSongAdapter` | 即将播放歌曲列表 |
| `SearchResultAdapter.cs` | `SearchResultAdapter` | 搜索结果列表适配器 |
| `SearchSuggestionAdapter.cs` | `SearchSuggestionAdapter` | 搜索建议下拉列表适配器 |
| `ChatMessageAdapter.cs` | `ChatMessageAdapter` | AI 聊天消息列表适配器 |
| `ExploreMessageAdapter.cs` | `ExploreMessageAdapter` | 探索/推荐消息适配器 |
| `ExploreSongAdapter.cs` | `ExploreSongAdapter` | 探索页歌曲适配器 |
| `ModelAdapter.cs` | `ModelAdapter` | AI 模型列表适配器 |
| `ConfigEntryAdapter.cs` | `ConfigEntryAdapter` | 配置项列表适配器 |
| `PluginCardAdapter.cs` | `PluginCardAdapter` | 插件卡片列表适配器 |

### 7.5 Services（22 个）

#### 核心播放服务

| 文件 | 类名 | 职责 |
|------|------|------|
| `ForegroundPlayerService.cs` | `ForegroundPlayerService` | 前台播放服务，管理通知、媒体会话、ExoPlayer |
| `AudioPlayerService.cs` (Platforms/Android) | `AudioPlayerService` | 音频播放实现，封装 ExoPlayer 操作 |

#### 音频效果处理链（Effects 子目录，8 个文件）

| 文件 | 类名 | 职责 |
|------|------|------|
| `IAudioEffect.cs` | `IAudioEffect` (接口) | 软件音频效果处理器接口：`Process()`, `SetSampleRate()`, `Reset()`, `Priority`, `Enabled` |
| `AudioEffectChain.cs` | `AudioEffectChain` | 效果处理链管理器，按 Priority 顺序调用各效果器 |
| `CompressorProcessor.cs` | `CompressorProcessor` | 动态压缩器（Priority=10）：Threshold、Ratio、Attack、Release、Knee 参数 |
| `ReverbProcessor.cs` | `ReverbProcessor` | Schroeder/Freeverb 混响（Priority=20）：7 种预设（Studio/Room/Chamber/Hall/Cathedral/Plate/Spring），4 并联梳状 +2 串联全通 |
| `StereoWidenerProcessor.cs` | `StereoWidenerProcessor` | 立体声扩展（Priority=30）：Mid/Side 处理，Width -100%~+100% |
| `TapeSaturationProcessor.cs` | `TapeSaturationProcessor` | 磁带饱和（Priority=40）：tanh() 软削波，Drive/Warmth/Tone 参数 |
| `DeEsserProcessor.cs` | `DeEsserProcessor` | 去齿音处理器（Priority=50）：检测高频能量并动态衰减 |
| `LimiterProcessor.cs` | `LimiterProcessor` | 砖墙限幅器（Priority=60）：瞬时峰值检测 + 快攻慢释，最终安全网 |

#### 其他服务

| 文件 | 类名 | 职责 |
|------|------|------|
| `EqBandProcessor.cs` | `EqBandProcessor` | 均衡器频段处理器 |
| `TeeAudioProcessor.cs` | `TeeAudioProcessor` | 音频分流处理器，将 PCM 数据分流到效果链 |
| `SoundEffectManager.cs` | `SoundEffectManager` | 音效管理器，管理音效预设的加载和保存 |
| `SoundEffectDialog.cs` | `SoundEffectDialog` | 音效设置对话框 |
| `CoverColorExtractor.cs` | `CoverColorExtractor` | 从封面提取主色调，用于 UI 主题色 |
| `DesktopLyricService.cs` | `DesktopLyricService` | 桌面歌词悬浮窗服务 |
| `DialogService.cs` | `DialogService` | 对话框服务实现 |
| `NavigationService.cs` | `NavigationService` | 页面导航服务 |
| `PlaybackStateManager.cs` | `PlaybackStateManager` | 播放状态持久化（SharedPreferences） |
| `MaterialYouPalette.cs` | `MaterialYouPalette` | Material You 动态取色调色板 |
| `MainThreadDispatcher.cs` | `MainThreadDispatcher` | 主线程调度器 |
| `LogService.cs` | `LogService` | 日志服务 |
| `ScanSettings.cs` | `ScanSettings` | 扫描设置管理（本地文件夹路径等） |
| `ScanProgressDialog.cs` | `ScanProgressDialog` | 扫描进度对话框 |
| `AndroidLocalScanner.cs` | `AndroidLocalScanner` | Android 本地扫描服务 |
| `SmbBrowserDialog.cs` | `SmbBrowserDialog` | SMB 目录浏览对话框 |
| `WebDavBrowserDialog.cs` | `WebDavBrowserDialog` | WebDAV 目录浏览对话框 |
| `VerticalSliderView.cs` | `VerticalSliderView` | 竖向滑块控件（用于 EQ） |
| `AgentConfigStorage.cs` | `AgentConfigStorage` | AI Agent 配置存储 |

### 7.6 Helpers（14 个）

| 文件 | 类名 | 职责 |
|------|------|------|
| `UiHelper.cs` | `UiHelper` (静态) | UI 辅助工具：主题色解析、按压缩放动画、ColorStateList 创建 |
| `VisualizerHelper.cs` | `VisualizerHelper` | 频谱可视化：从音频会话捕获 FFT 数据，映射到 64 频带，支持平滑和能量归一化 |
| `WaveformView.cs` | `WaveformView` | 音频波形动画视图：3 根跳动竖条表示播放状态 |
| `AudioVisualizerView.cs` | `AudioVisualizerView` | 音频可视化自定义视图 |
| `SweepGradientView.cs` | `SweepGradientView` | 扫描渐变背景视图，用于播放页动态渐变背景 |
| `FlowLightView.cs` | `FlowLightView` | 流光效果视图 |
| `ColorPickerView.cs` | `ColorPickerView` | 颜色选择器自定义视图 |
| `StrokeTextView.cs` | `StrokeTextView` | 描边文字视图 |
| `SquareFrameLayout.cs` | `SquareFrameLayout` | 正方形布局容器 |
| `GlassDialog.cs` | `GlassDialog` | 毛玻璃效果对话框 |
| `FontHelper.cs` | `FontHelper` | 字体辅助工具 |
| `BindingHelper.cs` | `BindingHelper` | 数据绑定辅助工具 |
| `BatchObservableCollection.cs` | `BatchObservableCollection` | 批量操作的 ObservableCollection（减少通知次数） |
| `DragSortCallback.cs` | `DragSortCallback` | 拖拽排序回调 |
| `ArtistMetadataSaver.cs` | `ArtistMetadataSaver` | 艺术家元数据保存 |
| `WindowInsetsCallback.cs` | `WindowInsetsCallback` | WindowInsets 回调包装 |
| `LyricTextView.cs` | `LyricTextView` | 歌词文本视图，支持逐字着色高亮 |

### 7.7 Platforms/Android（7 个）

| 文件 | 类名 | 职责 |
|------|------|------|
| `AndroidMediaScanner.cs` | `AndroidMediaScanner` | 通过 MediaStore 查询音频文件元数据 |
| `SafeContentScanner.cs` | `SafeContentScanner` | 通过 SAF URI 递归扫描音频文件 |
| `FolderPicker.cs` | `FolderPicker` | SAF 文件夹选择器，管理持久化 URI 权限 |
| `MediaStoreCoverHelper.cs` | `MediaStoreCoverHelper` | 从 MediaStore 加载专辑封面（支持 API 29+ 和旧版） |
| `PermissionService.cs` | `PermissionService` | Android 权限管理（存储、悬浮窗），含 OEM 特定处理 |
| `AudioPlayerService.cs` | `AudioPlayerService` | ExoPlayer 封装的音频播放服务 |
| `ThemeService.cs` | `ThemeService` | 主题服务实现（深色模式、Material You） |

---

## 8. 依赖关系图

### 8.1 项目间引用关系

```
CatClawMusic.UI
├── CatClawMusic.Core
└── CatClawMusic.Data
    └── CatClawMusic.Core

CatClawMusic.Core.Tests
└── CatClawMusic.Core
```

### 8.2 NuGet 包依赖

#### CatClawMusic.Core
- TagLibSharp 2.3.0（音频标签读取）
- sqlite-net-pcl 1.9.172（SQLite 数据库）

#### CatClawMusic.Data
- sqlite-net-pcl 1.9.172（SQLite 数据库）
- SMBLibrary 1.5.2（SMB 协议支持）

#### CatClawMusic.UI
- Xamarin.AndroidX.Media3.ExoPlayer 1.10.1（ExoPlayer 音频引擎）
- Xamarin.AndroidX.Media3.Datasource 1.10.1（ExoPlayer 数据源）
- Xamarin.AndroidX.Media 1.8.0（Android 媒体支持）
- CommunityToolkit.Mvvm 8.4.2（MVVM 工具包）
- Microsoft.Extensions.DependencyInjection 9.0.0（依赖注入）
- Xamarin.AndroidX.Fragment 1.8.9.2（Fragment 支持）
- Xamarin.AndroidX.ConstraintLayout 2.2.1.5（约束布局）
- Xamarin.Google.Android.Material 1.14.0（Material Design）
- Xamarin.AndroidX.Lifecycle.LiveData.Core 2.10.0.2（LiveData）
- Xamarin.AndroidX.Lifecycle.Process 2.10.0.2（Lifecycle Process）
- Xamarin.AndroidX.Lifecycle.Common 2.10.0.2（Lifecycle Common）
- Xamarin.AndroidX.ViewPager2 1.1.0.10（ViewPager2）

### 8.3 第三方库使用说明

#### ExoPlayer (Media3)
- **用途**: 音频播放引擎
- **功能**: 支持多种音频格式、流媒体播放、音频会话管理
- **集成**: 通过 `AudioPlayerService` 封装

#### TagLibSharp
- **用途**: 读取音频文件元数据
- **功能**: 支持 ID3、Vorbis、MP4 等标签格式
- **集成**: 通过 `TagReader` 使用

#### sqlite-net-pcl
- **用途**: SQLite 数据库访问
- **功能**: 异步数据库操作、LINQ 查询、代码优先迁移
- **集成**: 通过 `MusicDatabase` 使用

#### SMBLibrary
- **用途**: SMB/CIFS 协议支持
- **功能**: SMB2 客户端、文件共享访问
- **集成**: 通过 `SmbService` 使用

#### CommunityToolkit.Mvvm
- **用途**: MVVM 架构支持
- **功能**: ObservableObject、RelayCommand、源码生成器
- **集成**: ViewModels 继承 ObservableObject

---

## 9. 构建与运行

### 9.1 环境要求

- **.NET SDK**: 10.0.300 或更高版本
- **Android SDK**: API 31 (Android 12) 或更高版本
- **Android SDK Build Tools**: 34.0.0
- **Java JDK**: 17 或更高版本
- **IDE**: Visual Studio 2022 17.10+ 或 JetBrains Rider 2024.1+

### 9.2 构建配置

#### Debug 配置
- 启用快速部署
- 不启用 AOT 编译
- 不使用代码签名

#### Release 配置
- 启用 AOT 编译（NativeAOT）
- 启用代码裁剪（TrimMode=partial）
- 使用代码签名（catclaw.keystore）
- 输出 APK 格式

### 9.3 构建命令

#### 构建 Debug 版本
```bash
dotnet build CatClawMusic.UI/CatClawMusic.UI.csproj -c Debug
```

#### 构建 Release 版本
```bash
dotnet build CatClawMusic.UI/CatClawMusic.UI.csproj -c Release
```

#### 发布 Release APK
```bash
dotnet publish CatClawMusic.UI/CatClawMusic.UI.csproj -c Release -f net10.0-android
```

APK 输出位置: `publish-apk/CatClawMusic.UI-Signed.apk`

### 9.4 运行方式

#### 在模拟器上运行
```bash
dotnet run --project CatClawMusic.UI/CatClawMusic.UI.csproj -f net10.0-android
```

#### 在真机上运行
1. 启用开发者模式和 USB 调试
2. 连接设备
```bash
dotnet run --project CatClawMusic.UI/CatClawMusic.UI.csproj -f net10.0-android -t:<device-id>
```

### 9.5 部署方式

#### APK 安装
```bash
adb install publish-apk/CatClawMusic.UI-Signed.apk
```

#### 签名配置
- **密钥库文件**: `catclaw.keystore`
- **密钥库密码**: catclaw123
- **密钥别名**: catclaw
- **密钥密码**: catclaw123

### 9.6 特殊配置

#### AndroidManifest.xml
- **最低 SDK 版本**: 31 (Android 12)
- **目标 SDK 版本**: 34 (Android 14)
- **权限**:
  - `READ_EXTERNAL_STORAGE`: 读取外部存储
  - `WRITE_EXTERNAL_STORAGE`: 写入外部存储
  - `MANAGE_EXTERNAL_STORAGE`: 管理外部存储
  - `INTERNET`: 网络访问
  - `FOREGROUND_SERVICE`: 前台服务
  - `SYSTEM_ALERT_WINDOW`: 悬浮窗
  - `WAKE_LOCK`: 保持唤醒

#### TrimmerRoots.xml
- 保留反射使用的类型
- 保留插件接口和实现
- 保留序列化使用的模型

---

## 10. 关键功能实现

### 10.1 音乐播放功能

#### 播放流程
```
用户点击歌曲
    ↓
NowPlayingViewModel.PlaySongCommand
    ↓
PlayQueue.SetSongs() / SelectSong()
    ↓
AudioPlayerService.PlayAsync(filePathOrUrl)
    ↓
ExoPlayer 播放
    ↓
TeeAudioProcessor 分流 PCM 数据
    ↓
AudioEffectChain 处理音频效果
    ↓
ExoPlayer 输出音频
```

#### 播放模式切换
- **顺序播放**: 按原始列表顺序播放
- **随机播放**: 使用 Fisher-Yates 算法洗牌
- **单曲循环**: 重复播放当前歌曲
- **列表循环**: 播放到最后一首后回到第一首

#### 播放状态管理
- `PlaybackStateManager`: 使用 SharedPreferences 持久化播放状态
- 应用重启后恢复上次播放的歌曲和位置
- 支持蓝牙耳机和线控操作

### 10.2 歌词显示功能

#### 歌词获取流程
```
NowPlayingViewModel 监听歌曲变化
    ↓
LyricsService.GetLyricsAsync(song)
    ↓
1. 检查本地 .lrc/.ttml 文件
    ↓
2. 检查嵌入式歌词（音频文件内）
    ↓
3. 检查数据库缓存（Lyric 表）
    ↓
4. 尝试网络歌词提供者（插件）
    ↓
5. 返回 null（无歌词）
```

#### 歌词解析
- **LRC 格式**: `[mm:ss.xx]歌词文本`
- **TTML 格式**: XML 格式，支持逐字时间戳
- **逐字高亮**: 根据当前播放位置计算词索引

#### 歌词显示
- `FullLyricsFragment`: 全屏歌词页
- `LyricTextView`: 支持逐字着色高亮
- 自动滚动到当前歌词行
- 支持手动滚动查看

### 10.3 AI 代理功能

#### AI 对话流程
```
用户发送消息
    ↓
AgentService.SendMessageAsync()
    ↓
构建对话历史（包含系统提示）
    ↓
OpenAiCompatibleLlmClient.ChatAsync()
    ↓
LLM API 调用
    ↓
解析响应（文本 + 工具调用）
    ↓
如果有工具调用：
    ↓
执行工具（IAgentTool.ExecuteAsync）
    ↓
将工具结果添加到对话历史
    ↓
再次调用 LLM（循环直到无工具调用）
    ↓
返回最终响应
```

#### 工具调用机制
- LLM 返回 `ToolCalls` 列表
- 根据 `Name` 查找对应的 `IAgentTool`
- 解析 `Arguments` JSON 参数
- 执行工具并返回结果
- 将结果作为 `tool` 角色消息添加到对话历史

#### 支持的 LLM 提供商
- OpenAI (GPT-4, GPT-3.5)
- Anthropic Claude (Claude 3, Claude 2)
- Google Gemini (Gemini Pro, Gemini Ultra)
- 自定义 OpenAI 兼容 API

#### 内置代理
- **音乐助手**: 搜索、播放、管理音乐库
- **推荐助手**: 根据喜好推荐音乐
- **自定义代理**: 用户自定义系统提示

### 10.4 网络音乐服务

#### 协议分发
```
NetworkMusicService.ScanAsync(profile)
    ↓
根据 profile.Protocol 分发：
    ├── Navidrome → SubsonicService.GetSongsAsync()
    ├── WebDAV → ScanWebDavAsync()
    └── SMB → ScanSmbAsync()
```

#### WebDAV 扫描流程
```
ScanWebDavAsync()
    ↓
PROPFIND 根目录
    ↓
递归扫描子目录（最大深度 20）
    ↓
过滤音频文件（扩展名检查）
    ↓
并发获取元数据（TagReader）
    ↓
MusicScanner 批量入库
    ↓
回调通知进度
```

#### SMB 扫描流程
```
ScanSmbAsync()
    ↓
连接 SMB 服务器
    ↓
列出共享名
    ↓
递归扫描目录
    ↓
过滤音频文件
    ↓
获取元数据
    ↓
批量入库
```

#### Navidrome 扫描流程
```
SubsonicService.GetSongsAsync()
    ↓
获取所有专辑（getAlbumList2 API）
    ↓
遍历专辑获取歌曲（getAlbum API）
    ↓
解析歌曲元数据
    ↓
增量回调进度
    ↓
批量入库
```

#### 流媒体播放
- **WebDAV**: 直接 HTTP 流播放
- **SMB**: 通过 HTTP 代理或本地缓存
- **Navidrome**: Subsonic stream API

### 10.5 插件系统

#### 插件生命周期
```
安装插件（本地文件 / GitHub Release）
    ↓
解压 DLL 到插件目录
    ↓
反射加载程序集
    ↓
查找 IPlugin 实现
    ↓
创建 PluginInfo 包装
    ↓
初始化插件（InitializeAsync）
    ↓
注册到插件管理器
```

#### 插件类型
- **基础插件**: 实现 `IPlugin` 接口
- **封面提供者**: 实现 `ICoverProviderPlugin`
- **歌词提供者**: 实现 `ILyricsProviderPlugin`
- **菜单扩展**: 实现 `IMenuContributorPlugin`
- **协议提供者**: 实现 `IProtocolProviderPlugin`
- **音频增强**: 实现 `IAudioEnhancerPlugin`

#### 插件适配器模式
```
PluginManager
    ↓
反射检测插件实现的接口
    ↓
创建对应的适配器（Adapter）
    ↓
统一包装为 PluginInfo
    ↓
提供统一的 API 访问
```

#### 插件使用示例
```csharp
// 获取所有启用的封面提供者插件
var coverProviders = pluginManager.GetEnabledPlugins<ICoverProviderPlugin>();

// 使用第一个可用的封面提供者
foreach (var provider in coverProviders)
{
    if (provider.IsAvailable)
    {
        var cover = await provider.GetCoverAsync(song);
        if (cover != null) break;
    }
}
```

### 10.6 音频效果处理链

#### 效果链架构
```
ExoPlayer PCM 输出
    ↓
TeeAudioProcessor 分流
    ↓
AudioEffectChain 处理
    ↓
按 Priority 顺序调用：
    1. EqBandProcessor (Priority=0): 7 段均衡器
    2. CompressorProcessor (Priority=10): 动态压缩器
    3. ReverbProcessor (Priority=20): 混响效果
    4. StereoWidenerProcessor (Priority=30): 立体声扩展
    5. TapeSaturationProcessor (Priority=40): 磁带饱和
    6. DeEsserProcessor (Priority=50): 去齿音
    7. LimiterProcessor (Priority=60): 砖墙限幅器
    ↓
ExoPlayer 输出
```

#### 效果器参数

**均衡器 (EQ)**:
- 7 个频段：60Hz, 250Hz, 1kHz, 4kHz, 16kHz
- 增益范围：-12dB ~ +12dB
- Q 值：1.4

**压缩器 (Compressor)**:
- Threshold: -60dB ~ 0dB
- Ratio: 1:1 ~ 20:1
- Attack: 0.1ms ~ 100ms
- Release: 10ms ~ 1000ms
- Knee: 0dB ~ 12dB

**混响 (Reverb)**:
- 7 种预设：Studio, Room, Chamber, Hall, Cathedral, Plate, Spring
- 4 并联梳状滤波器 + 2 串联全通滤波器
- Decay Time: 0.5s ~ 5s
- Damping: 0 ~ 1

**立体声扩展 (Stereo Widener)**:
- Width: -100% ~ +100%
- Mid/Side 处理

**磁带饱和 (Tape Saturation)**:
- Drive: 0 ~ 1
- Warmth: 0 ~ 1
- Tone: 0 ~ 1
- tanh() 软削波

**去齿音 (DeEsser)**:
- Frequency: 2kHz ~ 10kHz
- Threshold: -60dB ~ 0dB
- Ratio: 1:1 ~ 10:1

**限幅器 (Limiter)**:
- Threshold: -20dB ~ 0dB
- Attack: 0.1ms ~ 10ms
- Release: 10ms ~ 100ms
- 砖墙限幅

### 10.7 音乐扫描功能

#### 本地扫描流程
```
LibraryViewModel.ScanLocalMusicAsync()
    ↓
AndroidMediaScanner.ScanFromMediaStore()
    ↓
查询 MediaStore.Audio
    ↓
过滤音频文件
    ↓
读取元数据（TagReader）
    ↓
MusicScanner 批量入库
    ↓
更新 UI
```

#### SAF 扫描流程
```
SafeContentScanner.CollectAudioFiles()
    ↓
遍历持久化 URI 权限
    ↓
递归扫描目录
    ↓
过滤音频文件
    ↓
读取元数据
    ↓
批量入库
```

#### 网络扫描流程
```
LibraryViewModel.ScanNetworkMusicAsync()
    ↓
NetworkMusicService.ScanAsync(profile)
    ↓
根据协议类型分发
    ↓
扫描远程目录
    ↓
获取元数据
    ↓
批量入库
    ↓
清理已移除的歌曲
```

#### 扫描优化
- **动态批次大小**: 初期少量提交，后期大批量
- **内存缓存**: 艺术家和专辑 ID 缓存
- **批次内去重**: HashSet 避免重复
- **并发控制**: 信号量限制并发数
- **进度回调**: 实时报告扫描进度

### 10.8 封面加载策略

#### 加载流程
```
SongAdapter.Bind(song)
    ↓
检查内存缓存 (BitmapCache)
    ├── 命中 → 直接显示
    └── 未命中 → 检查磁盘缓存
         ├── 命中 → 解码显示
         └── 未命中 → 异步加载（SemaphoreSlim 限流 4 并发）
              ├── WebDAV/SMB → 网络下载（5 秒超时）
              ├── 本地 MediaStore → MediaStoreCoverHelper
              ├── content:// URI → MediaMetadataRetriever
              ├── 文件路径 → TagReader.ExtractCoverArt
              └── 兜底 → ICoverProviderPlugin 插件
```

#### 缓存策略
- **内存缓存**: LRU 策略，最大 100 张
- **磁盘缓存**: 按歌曲 ID 哈希命名
- **网络缓存**: HTTP 缓存头控制

#### 滚动优化
- 滚动时暂停网络封面加载
- 停止滚动后补加载
- 使用 SemaphoreSlim 限制并发（最大 4）

### 10.9 主题系统

#### Material You 动态取色
```
CoverColorExtractor.ExtractColors(coverBitmap)
    ↓
提取主色调
    ↓
MaterialYouPalette.GeneratePalette(primaryColor)
    ↓
生成完整调色板
    ↓
应用到 UI 主题
```

#### 深色模式
- 跟随系统设置
- 手动切换
- 自动切换（根据时间）

#### 启动图定制
- 默认猫爪图
- 自定义图片
- 自定义 API（Bing 每日壁纸等）

### 10.10 备份与恢复

#### 备份内容
- 播放列表
- 播放历史
- 收藏
- 艺术家元数据
- AI 模型配置

#### 备份格式
- JSON 格式
- 包含歌曲标题和艺术家信息
- 支持跨设备恢复

#### 恢复流程
```
选择备份文件
    ↓
解析 BackupData
    ↓
根据 Title+Artist 匹配本地歌曲
    ↓
恢复播放列表
    ↓
恢复播放历史
    ↓
恢复收藏
    ↓
恢复艺术家元数据
    ↓
恢复 AI 配置
```

---

## 附录

### A. 数据库表结构

#### Song 表
```sql
CREATE TABLE Song (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Title TEXT NOT NULL,
    ArtistId INTEGER,
    AlbumId INTEGER,
    Duration INTEGER,
    FilePath TEXT NOT NULL,
    FileSize INTEGER,
    Bitrate INTEGER,
    TrackNumber INTEGER,
    Year INTEGER,
    Genre TEXT,
    DateAdded INTEGER,
    DateModified INTEGER,
    CoverArtPath TEXT,
    LyricsPath TEXT,
    Source INTEGER,
    Protocol INTEGER,
    RemoteId TEXT,
    MediaStoreId INTEGER
);
```

#### Artist 表
```sql
CREATE TABLE Artist (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL UNIQUE,
    Cover TEXT,
    Gender TEXT,
    Birthday TEXT,
    Region TEXT,
    Description TEXT
);
```

#### Album 表
```sql
CREATE TABLE Album (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Title TEXT NOT NULL,
    Name TEXT,
    Artist TEXT,
    CoverArtPath TEXT,
    SongCount INTEGER,
    Year INTEGER,
    ArtistId INTEGER,
    Cover TEXT,
    ReleaseYear INTEGER
);
```

#### Playlist 表
```sql
CREATE TABLE Playlist (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    CreatedAt INTEGER,
    UpdatedAt INTEGER,
    SongCount INTEGER,
    IsSystem INTEGER
);
```

#### PlaylistSong 表
```sql
CREATE TABLE PlaylistSong (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PlaylistId INTEGER,
    SongId INTEGER,
    Position INTEGER
);
```

#### SongArtist 表
```sql
CREATE TABLE SongArtist (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SongId INTEGER,
    ArtistId INTEGER
);
```

#### Lyric 表
```sql
CREATE TABLE Lyric (
    SongId INTEGER PRIMARY KEY,
    LrcPath TEXT,
    Content TEXT
);
```

#### PlayHistory 表
```sql
CREATE TABLE PlayHistory (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SongId INTEGER,
    PlayedAt INTEGER,
    PlayCount INTEGER
);
```

#### Favorite 表
```sql
CREATE TABLE Favorite (
    SongId INTEGER PRIMARY KEY,
    AddedAt INTEGER
);
```

#### CachedSong 表
```sql
CREATE TABLE CachedSong (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SongId INTEGER,
    LocalPath TEXT,
    CachedAt INTEGER,
    FileSize INTEGER
);
```

#### ConnectionProfile 表
```sql
CREATE TABLE ConnectionProfile (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT,
    Protocol INTEGER,
    Host TEXT,
    Port INTEGER,
    UserName TEXT,
    Password TEXT,
    BasePath TEXT,
    IsEnabled INTEGER,
    ApiVersion TEXT,
    ClientName TEXT,
    UseHttps INTEGER
);
```

### B. 支持的音频格式

- MP3
- FLAC
- WAV
- AAC
- OGG
- M4A
- WMA
- APE
- ALAC
- OPUS

### C. 支持的歌词格式

- LRC（标准歌词格式）
- TTML（时间文本标记语言，支持逐字）
- 嵌入式歌词（从音频文件中提取）

### D. 支持的网络协议

- WebDAV
- SMB/CIFS (SMB2)
- Navidrome/Subsonic

### E. 项目统计

- **总文件数**: 约 150 个 .cs 文件
- **代码行数**: 约 30,000+ 行
- **Models**: 14 个
- **Interfaces**: 15 个
- **Core Services**: 9 个
- **Data Services**: 15 个
- **ViewModels**: 9 个
- **Fragments**: 35 个
- **Adapters**: 13 个
- **UI Services**: 22 个
- **Helpers**: 14 个
- **Android 平台特定**: 7 个

---

**文档生成时间**: 2026-06-17  
**项目版本**: 1.4.8  
**最后更新**: 2026-06-17
