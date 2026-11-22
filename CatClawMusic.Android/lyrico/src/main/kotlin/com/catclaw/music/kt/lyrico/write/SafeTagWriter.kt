package com.catclaw.music.kt.lyrico.write

import android.content.Context
import android.net.Uri
import android.os.ParcelFileDescriptor
import com.catclaw.music.kt.core.diagnostic.AppLog
import java.io.File
import java.nio.file.Files
import java.nio.file.StandardCopyOption

/**
 * 在「可能正被播放器读取」的音频文件上安全地改标签。
 *
 * ## 为什么不能直接改原文件
 *
 * 之前的做法是 `ParcelFileDescriptor.open(file, MODE_READ_WRITE)` 然后原地改。
 * 如果这首歌**正在播放**，ExoPlayer 手里有同一个文件的句柄在读——
 * 一边写一边读同一 inode，会造成：
 * - 播放器读到写了一半的标签区，解码失败
 * - 文件 IO 互相阻塞，播放卡死（真机反馈：写封面后必须切歌才恢复）
 *
 * ## 做法：写临时副本 + 原子替换
 *
 * 1. 原文件复制到同目录 `.tmp-` 前缀的临时文件
 * 2. 在副本上做标签写入（此时没有任何读者）
 * 3. `Files.move(ATOMIC_MOVE)` 原子替换原文件
 *
 * ExoPlayer 持有的旧句柄仍指向被替换掉的旧 inode，可以继续正常读完当前这首歌；
 * 下次打开时自然读到新文件。整个过程不需要暂停播放，也不会让用户听到中断。
 *
 * 任何一步失败都清理临时文件，**原文件保持原样**（比原地写失败更安全）。
 */
object SafeTagWriter {

    private const val TAG = "LyricoWrite"

    /**
     * @param block 在临时副本的写句柄上执行标签写入，返回是否成功
     * @return true = 已成功替换原文件
     */
    suspend fun writeSafely(
        context: Context,
        path: String,
        block: suspend (ParcelFileDescriptor) -> Boolean,
    ): Boolean {
        // SAF 的 content:// 没法用原子替换（拿不到真实路径），只能原地写。
        // 这类 URI 通常是用户通过系统选择器授权的目录，不属于应用私有曲库，
        // 播放器一般也不直接持有 —— 保持原行为并记日志。
        if (path.startsWith("content://")) {
            return runCatching {
                val pfd = context.contentResolver.openFileDescriptor(Uri.parse(path), "rw") ?: return false
                try {
                    block(pfd)
                } finally {
                    runCatching { pfd.close() }
                }
            }.getOrElse {
                AppLog.w(TAG, "SAF 文件写入失败：${it.message}", it)
                false
            }
        }

        val original = File(path)
        if (!original.isFile) return false
        val dir = original.parentFile ?: return false
        // 同目录才能保证 ATOMIC_MOVE 生效（跨文件系统会退化成 copy，不原子）
        val temp = File.createTempFile(".tmp-${original.nameWithoutExtension}-", ".tmp", dir)

        return try {
            original.copyTo(temp, overwrite = true)
            val pfd = ParcelFileDescriptor.open(temp, ParcelFileDescriptor.MODE_READ_WRITE)
            val ok = try {
                block(pfd)
            } finally {
                runCatching { pfd.close() }
            }
            if (!ok) {
                AppLog.w(TAG, "标签写入未成功，保留原文件：$path")
                temp.delete()
                return false
            }
            // 保留原文件的权限/属主属性。
            // 临时文件是应用身份创建的，直接替换会让音乐文件变成 `u0_aXXX` 所有，
            // 系统媒体库、其他播放器、USB 拷贝都可能因此读不到（真机文件是 media_rw 所有）。
            runCatching {
                val perms = Files.getPosixFilePermissions(original.toPath())
                Files.setPosixFilePermissions(temp.toPath(), perms)
            }.onFailure {
                AppLog.w(TAG, "复制文件权限失败（继续替换）：${it.message}")
            }

            runCatching {
                Files.move(
                    temp.toPath(),
                    original.toPath(),
                    StandardCopyOption.REPLACE_EXISTING,
                    StandardCopyOption.ATOMIC_MOVE,
                )
            }.getOrElse {
                // 个别文件系统不支持 ATOMIC_MOVE，退回普通替换
                AppLog.w(TAG, "ATOMIC_MOVE 不可用，退回普通替换：${it.message}")
                Files.move(
                    temp.toPath(),
                    original.toPath(),
                    StandardCopyOption.REPLACE_EXISTING,
                )
            }
            true
        } catch (t: Throwable) {
            AppLog.w(TAG, "安全写入失败：${t.message}", t)
            temp.delete()
            false
        }
    }

    /** 只读打开（回读校验用）。也走副本语义：读原文件即可，不会与写入冲突。 */
    suspend fun <T> readSafely(context: Context, path: String, block: suspend (ParcelFileDescriptor) -> T?): T? =
        runCatching {
            if (path.startsWith("content://")) {
                val pfd = context.contentResolver.openFileDescriptor(Uri.parse(path), "r") ?: return null
                try {
                    block(pfd)
                } finally {
                    runCatching { pfd.close() }
                }
            } else {
                val file = File(path)
                if (!file.isFile) return null
                val pfd = ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY)
                try {
                    block(pfd)
                } finally {
                    runCatching { pfd.close() }
                }
            }
        }.getOrElse {
            AppLog.w(TAG, "读取失败：${it.message}", it)
            null
        }
}
