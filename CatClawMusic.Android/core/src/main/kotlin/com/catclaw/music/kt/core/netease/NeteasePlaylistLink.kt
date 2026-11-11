package com.catclaw.music.kt.core.netease

/**
 * 从用户粘贴的内容里取出网易云歌单 ID。
 *
 * 用户实际会粘的东西至少有四种，都得认：
 * - 纯 ID：`3778678`
 * - 网页链接：`https://music.163.com/playlist?id=3778678&userid=1`
 * - 分享短链：`https://y.music.163.com/m/playlist?id=3778678&userid=1`
 * - 分享文案：`分享歌单《热歌榜》: https://music.163.com/playlist?id=3778678 (来自@网易云音乐)`
 *
 * 只认 `playlist` 这一种路径：同一串分享文案里常同时出现 `userid=`、`song?id=`、
 * `album?id=`，放宽成「任意 id=数字」会把用户 id 当成歌单 id，导入出来是个空歌单。
 */
object NeteasePlaylistLink {

    /**
     * 歌单 ID 至少 4 位。真实 ID 早就过了 6 位，但 4 位这个下界只用来挡
     * `id=1` 这类明显是别的对象的数字，不做更强校验（防止平台改号段后误伤）。
     */
    private val PLAYLIST_URL = Regex(
        pattern = """playlist(?:\?|/)(?:[^#\s]*?[?&])?id=(\d{4,})""",
        option = RegexOption.IGNORE_CASE,
    )

    /** `.../playlist/3778678` 这种路径式链接（国际版、部分分享格式） */
    private val PLAYLIST_PATH = Regex("""playlist/(\d{4,})(?:[/?#]|$)""", RegexOption.IGNORE_CASE)

    private val PLAIN_ID = Regex("""^\d{4,}$""")

    /** @return 歌单 ID 字符串；找不到返回 null，调用方据此提示"链接里没有歌单" */
    fun parse(raw: String): String? {
        val text = raw.trim()
        if (text.isEmpty()) return null
        PLAIN_ID.matchEntire(text)?.let { return it.value }
        PLAYLIST_URL.find(text)?.let { return it.groupValues[1] }
        PLAYLIST_PATH.find(text)?.let { return it.groupValues[1] }
        return null
    }
}
