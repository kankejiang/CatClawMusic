package com.catclaw.music.kt.data.netease

/**
 * 网易云在线音乐的数据模型。
 *
 * 对应插件 `NeteaseModels.cs` + Core 的 `OnlineSong` / `OnlinePlaylist`。
 * C# 版把「是否 VIP / 是否下架 / MV id」塞在 `Internal: Dictionary<string, object>` 里
 * （宿主 Core 不认识这些字段，只能靠反射读）；Kotlin 端没有那层跨插件约束，
 * 直接升成强类型字段 —— 调用方少一次 `as? Boolean` 的猜测。
 */
data class OnlineSong(
    /** 平台内唯一 id（网易云是数字，其它平台可能是字符串，统一按字符串存） */
    val id: String,
    val platform: String = PLATFORM,
    val platformName: String = PLATFORM_NAME,
    val title: String,
    val artist: String,
    val album: String = "",
    val durationMs: Long = 0,
    val coverUrl: String? = null,
    /** fee=1/4：VIP 或数字专辑，未登录时多半只能试听 */
    val vip: Boolean = false,
    /** st=-1：下架/无版权 */
    val blocked: Boolean = false,
    val mvId: Long = 0,
    /** 搜索时已拿到的直链（如 iTunes 预览）；网易云始终为空，播放前现取 */
    val directPlayUrl: String? = null,
) {
    val durationSeconds: Int get() = (durationMs / 1000).toInt()

    companion object {
        const val PLATFORM = "netease"
        const val PLATFORM_NAME = "网易云音乐"
    }
}

data class OnlinePlaylist(
    val id: String,
    val platform: String = OnlineSong.PLATFORM,
    val name: String,
    val coverUrl: String? = null,
    val description: String? = null,
    val songCount: Int = 0,
    /** 累计播放次数（卡片角标；接口没给就不显示） */
    val playCount: Long? = null,
)

/** 排行榜 tab 的一块：榜单本体 + 更新频率 + 前三首预览 */
data class ToplistBlock(
    val playlist: OnlinePlaylist,
    val updateFrequency: String = "",
    val topSongs: List<OnlineSong> = emptyList(),
)

/** 歌单广场官方分类分组（语种 / 风格 / 场景 / 情感 / 主题） */
data class CatalogueGroup(val group: String, val names: List<String>)

data class NeteaseArtist(
    val id: String,
    val name: String,
    val picUrl: String? = null,
    val songCount: Int = 0,
    val albumCount: Int = 0,
)

data class NeteaseAlbum(
    val id: String,
    val name: String,
    val picUrl: String? = null,
    val songCount: Int = 0,
    val artistName: String? = null,
    val publishYear: String? = null,
)

data class NeteaseMv(
    val id: String,
    val name: String,
    val coverUrl: String? = null,
    val playCount: Long = 0,
    val durationMs: Int = 0,
)

/** 歌手简介：一句话 + 分节长文（演艺经历 / 代表作品 / 重要里程碑…） */
data class ArtistIntro(
    val briefDesc: String = "",
    val sections: List<IntroSection> = emptyList(),
)

data class IntroSection(val title: String, val text: String)

data class SongComment(
    val id: Long,
    val user: String,
    val content: String,
    val time: Long,
    val likedCount: Int,
    val avatarUrl: String? = null,
)

/** 搜索联想词：type = song / album / artist */
data class SearchSuggestion(val word: String, val type: String = "song")

data class SimilarPlaylistInfo(
    val id: String,
    val name: String,
    val coverUrl: String? = null,
    val songCount: Int = 0,
    val playCount: Int = 0,
    val creator: String = "",
)

/** 歌单动态：详情页头部的创建者、播放量、收藏/评论/分享数 */
data class PlaylistDynamicInfo(
    val creatorName: String? = null,
    val creatorAvatar: String? = null,
    val playCount: Long = 0,
    val subscribedCount: Long = 0,
    val commentCount: Long = 0,
    val shareCount: Long = 0,
    val trackCount: Int = 0,
)

/** 歌词三流：原版 / 翻译 / 罗马音 */
data class NeteaseLyrics(
    val lrc: String,
    val translation: String? = null,
    val roma: String? = null,
)
