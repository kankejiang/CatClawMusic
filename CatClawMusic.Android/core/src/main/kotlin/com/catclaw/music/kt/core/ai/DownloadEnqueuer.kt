package com.catclaw.music.kt.core.ai

/**
 * 「把一个直链放进下载队列」这条能力。
 *
 * 为什么单列一个接口而不是直接复用 `MusicDownloader.enqueue(song)`：
 * 那条路径是**按库内 song.id 键控**的（`jobs[song.id]` + 完成后 `upsert(song.id, …)` 写缓存表），
 * 而音乐源直链在下载那一刻**还没有库内 id** —— 硬塞 `id = 0` 会让所有源下载共用同一个任务槽，
 * 还会往缓存表写进假行。所以这里按 **URL + 文件名** 键控，落库交给完成后的扫描/导入去做。
 *
 * 返回给用户/模型的一句话（null 表示没接上下载器）。
 */
fun interface DownloadEnqueuer {
    fun enqueue(url: String, filename: String): String?
}
