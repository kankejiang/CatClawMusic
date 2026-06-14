# CatClawMusic 项目记忆

## 构建
- **构建命令**: `dotnet publish CatClawMusic.UI.csproj -c Release -f net10.0-android -p:AndroidSdkDirectory="C:/Users/lvjin/AppData/Local/Android/Sdk" -p:JavaSdkDirectory="C:/Program Files/Android/openjdk/jdk-21.0.8"`
- **产出路径**: `publish-apk/CatClawMusic.UI-Signed.apk`
- **规则**: 每次有较大代码改动后，自动构建 APK
- **版本**: v1.4.6

## 艺术家照片
- 多源链：网易云 → AI搜索 → 多源聚合（QQ音乐 → iTunes → Wikipedia）
- `MultiSourcePhotoScraper.cs` 实现了 `IArtistMetadataScraper`
- 探索设置中已添加 QQ音乐 / iTunes / Wikipedia 作为手动匹配来源选项

## 探索设置
- 来源选择 Chip 定义在 `fragment_artist_match.xml` 和 `fragment_artist_match_detail.xml`
- C# 映射在 `ArtistMatchFragment.SourceChipToName` 和 `ArtistMatchDetailFragment.SourceChipToName`
- 多源来源的搜索/下载通过 `MultiSourcePhotoScraper` 按 Source 前缀过滤
