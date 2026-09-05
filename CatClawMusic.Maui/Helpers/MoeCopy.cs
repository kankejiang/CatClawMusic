namespace CatClawMusic.Maui.Helpers;

/// <summary>
/// 萌系文案常量（猫娘人格化）。空状态/提示类文案集中于此，避免散落各页面。
/// 语气约定：温柔克制带一点点喵口癖；设置/下载等工具页不使用过强的卖萌。
/// 注意：NoLyrics 必须包含 <see cref="CatClawMusic.Core.Services.LyricsService.NoLyricsPlaceholderText"/>
///（"还没有歌词"）子串——LRC 解析用它识别占位行。
/// </summary>
public static class MoeCopy
{
    public const string NoLyrics = "这里静悄悄的喵，还没有歌词";

    public const string EmptyPlaylistSongs = "歌单还空空的喵…去挑几首塞满它吧~";
    public const string EmptyAlbums = "还没有专辑喵…先扫描入库几首吧~";
    public const string EmptyArtistSongs = "这里还空空的喵…去音乐库挑几首吧~";
    public const string EmptyAlbumDetailSongs = "这张专辑还空空的喵…";
    public const string EmptyDownloads = "下载架还空着喵…去挑几首下载吧~";
    public const string EmptyModelConfig = "还没有模型配置喵…去添加一个吧~";
    public const string EmptyPlaylists = "歌单架还空着喵…新建一个开始整理吧~";
    public const string NoPlayRecords = "这段时间还没听歌喵…放几首就有了~";
    public const string EmptyMemory = "（Yuki 还没有长期记忆喵）";

    public const string AboutTagline = "一只会放音乐的猫爪喵——本地、私域、在线，都给你安排好啦";
}
