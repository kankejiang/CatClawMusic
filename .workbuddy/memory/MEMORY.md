# CatClawMusic 项目记忆

## 构建
- **构建命令**: `dotnet publish CatClawMusic.UI.csproj -c Release -f net10.0-android -p:AndroidSdkDirectory="C:/Users/lvjin/AppData/Local/Android/Sdk" -p:JavaSdkDirectory="C:/Program Files/Android/openjdk/jdk-21.0.8"`
- **产出路径**: `publish-apk/CatClawMusic.UI-Signed.apk`
- **规则**: 每次有较大代码改动后，自动构建 APK
- **版本**: v1.5.1

## 艺术家元数据
- **模型**: `ArtistSearchResult` 类（在 `IArtistMetadataScraper.cs` 中）包含以下字段：
  - 基本信息：Name, NameAliases, Gender, Area, Birthday, Description, PhotoUrl
  - 扩展信息（v1.4.9新增）：RealName（本名）, Nickname（昵称）, Ethnicity（民族）, BirthPlace（出生地）, Education（毕业院校）, Zodiac（星座）, Height（身高）, Agency（经纪公司）, RepresentativeWorks（代表作品）, Occupation（职业）
- **保存**: `ArtistMetadataSaver.cs` 将元数据保存为 JSON（`artist_info.json`）
- **显示**: `ArtistDetailFragment.cs` 显示本名和扩展信息

## 数据源优先级
- **网易云** → **百度百科** → **豆瓣** → **QQ音乐**
- `BaiduBaikeScraper.cs`：解析百度百科 infobox，提取丰富的艺术家元数据
- `DoubanScraper.cs`：提供中文简介（网页抓取方式）
- `MultiSourcePhotoScraper.cs`：只实现 QQ音乐（已移除 iTunes/Wikipedia）
- 已删除：MusicBrainz、TheAudioDB、iTunes、Wikipedia（对中文艺术家效果差）

## 探索设置
- 来源选择 Chip 定义在 `fragment_artist_match.xml` 和 `fragment_artist_match_detail.xml`
- C# 映射在 `ArtistMatchFragment.SourceChipToName` 和 `ArtistMatchDetailFragment.SourceChipToName`
- 当前来源选项：网易云 / 百科 / 豆瓣 / QQ音乐
- 多源来源的搜索/下载通过 `MultiSourcePhotoScraper` 按 Source 前缀过滤

## 歌词支持
- **LRC 格式**：标准歌词格式（原有支持）
- **TTML 格式**：v1.5.1 新增，W3C 标准（常用于 Apple Music）
  - 支持文件扩展名：`.lrc`、`.ttml`、`.xml`（自动检测是否为 TTML）
  - 支持逐字时间戳（`<span begin="..." end="...">`）
  - 支持时间戳格式：`HH:MM:SS.mmm`、`MM:SS.mmm`、秒数、`PT...S`
- **文件匹配规则**（精确 → 模糊）：
  1. `歌曲名.lrc` / `歌曲名.ttml` / `歌曲名.xml`
  2. `歌曲名*.lrc` / `歌曲名*.ttml` / `歌曲名*.xml`
- **相关文件**：`LyricsService.cs`（解析器）、`MusicUtility.cs`（文件查找）、`ILyricsService.cs`（接口）
