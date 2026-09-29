package com.catclaw.music.kt.data.download

import android.content.Context
import com.catclaw.music.kt.core.ai.DownloadEnqueuer
import com.catclaw.music.kt.core.download.RangeDownloader
import dagger.hilt.android.qualifiers.ApplicationContext
import java.io.File
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch

/**
 * 音乐源直链的下载入口：**按文件名键控**，与库内 id 无关。
 *
 * 目录沿用 `MusicDownloader` 的 `getExternalFilesDir(null)/downloads`，
 * 这样设置页的"已用空间/上限"和"清除缓存"对两类下载是同一本账 ——
 * 分成两个目录会出现"清了缓存但源下载还占着"的错觉。
 */
@Singleton
class SourceDownloadManager @Inject constructor(
    @ApplicationContext private val context: Context,
) : DownloadEnqueuer {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val jobs = JobTable<String>()

    private val directory: File
        get() = File(context.getExternalFilesDir(null), "downloads").apply { if (!exists()) mkdirs() }

    private val downloader = RangeDownloader(directory)

    override fun enqueue(url: String, filename: String): String? {
        if (filename.isBlank() || url.isBlank()) return null
        // 去重必须拿**清洗后**的名字当键：原来这里查的是 `jobs[filename]`（未清洗），
        // 而存的是 `sanitize(filename)` —— 名字里带 `/`、`\`、开头点号时两边对不上，
        // 去重形同没有，同一个 `.part` 会被两个任务同时写。
        val target = sanitize(filename)
        if (jobs.isActive(target)) return "该文件已在下载中：$target"
        val job = scope.launch {
            runCatching { downloader.download(target, url) { _, _ -> } }
            // 认货再摘：这一格可能已经被后一次排队换成新任务了
            coroutineContext[Job]?.let { jobs.release(target, it) }
        }
        jobs.put(target, job)
        return "已开始下载：$target（保存到 ${directory.absolutePath}）"
    }

    /** 源返回的歌名里会有 `/`、`\\`、`..` —— 不清洗就是目录穿越，写到哪里都有可能 */
    private fun sanitize(raw: String): String =
        raw.replace('\\', '_').replace('/', '_').removePrefix(".").ifBlank { "download.bin" }

    /** 供设置页/测试查看当前在下的文件名 */
    fun active(): List<String> = jobs.activeKeys()
}
