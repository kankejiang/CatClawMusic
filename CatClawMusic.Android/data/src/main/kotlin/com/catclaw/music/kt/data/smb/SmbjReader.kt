package com.catclaw.music.kt.data.smb

import com.catclaw.music.kt.data.remote.RemoteFile
import com.hierynomus.msdtyp.AccessMask
import com.hierynomus.msfscc.FileAttributes
import com.hierynomus.mssmb2.SMB2CreateDisposition
import com.hierynomus.mssmb2.SMB2ShareAccess
import com.hierynomus.smbj.SMBClient
import com.hierynomus.smbj.SmbConfig
import com.hierynomus.smbj.auth.AuthenticationContext
import com.hierynomus.smbj.connection.Connection
import com.hierynomus.smbj.session.Session
import com.hierynomus.smbj.share.DiskShare
import com.hierynomus.smbj.share.File
import java.util.EnumSet
import java.util.concurrent.TimeUnit

/**
 * SMBJ 版的读取面，替 C# 的 `SmbService`（SMBLibrary 是它的 .NET 对位库）。
 * 连接策略照 C#：一条连接一个 session 一个 share，按 `host|port|share|user` 复用，
 * 只暴露代理真正会用到的三件事 —— 列目录、问大小、按区间读。
 */
class SmbjReader(private val target: SmbTarget) : SmbReader {

    private var connection: Connection? = null
    private var session: Session? = null
    private var share: DiskShare? = null

    @Synchronized
    private fun diskShare(): DiskShare {
        share?.let { if (!it.isConnected) Unit else return it }
        val config = SmbConfig.builder()
            .withTimeout(TIMEOUT_SECONDS, TimeUnit.SECONDS)
            .withSoTimeout(TIMEOUT_SECONDS, TimeUnit.SECONDS)
            .withMultiProtocolNegotiate(true)
            .build()
        val conn = SMBClient(config).connect(target.host, target.port)
        val sess = conn.authenticate(
            AuthenticationContext(target.user, target.password.toCharArray(), target.domain),
        )
        return (sess.connectShare(target.share) as DiskShare).also {
            connection = conn
            session = sess
            share = it
        }
    }

    override fun fileSize(remotePath: String): Long =
        entryOf(remotePath)?.endOfFile ?: -1L

    override fun read(remotePath: String, offset: Long, buffer: ByteArray, count: Int): Int {
        val file = open(remotePath)
        return try {
            file.read(buffer, offset, 0, count)
        } finally {
            runCatching { file.close() }
        }
    }

    override fun list(remotePath: String): List<RemoteFile> {
        val dir = normalizeDir(remotePath)
        return diskShare().list(dir).mapNotNull { info ->
            val name = info.fileName
            if (name == "." || name == "..") return@mapNotNull null
            RemoteFile(
                name = name,
                path = if (dir == "\\") "\\$name" else "$dir\\$name",
                isDirectory = info.fileAttributes and DIRECTORY_ATTR != 0L,
                size = info.endOfFile,
                lastModifiedSeconds = info.lastWriteTime.toEpochMillis() / 1000,
            )
        }
    }

    override fun close() {
        runCatching { share?.close() }
        runCatching { session?.close() }
        runCatching { connection?.close() }
        share = null
        session = null
        connection = null
    }


    private fun open(remotePath: String): File = diskShare().openFile(
        remotePath,
        EnumSet.of(AccessMask.GENERIC_READ),
        null,
        SMB2ShareAccess.ALL,
        SMB2CreateDisposition.FILE_OPEN,
        null,
    )

    private fun entryOf(remotePath: String) = runCatching {
        val trimmed = remotePath.trimStart('\\')
        val name = trimmed.substringAfterLast('\\')
        val parent = trimmed.substringBeforeLast('\\', "")
        diskShare().list(if (parent.isEmpty()) "\\" else "\\$parent")
            .firstOrNull { it.fileName.equals(name, ignoreCase = true) }
    }.getOrNull()

    private fun normalizeDir(path: String): String {
        val trimmed = path.trimEnd('\\')
        return if (trimmed.isEmpty()) "\\" else if (trimmed.startsWith("\\")) trimmed else "\\$trimmed"
    }

    private companion object {
        /**
         * 列出共享内的目录，供「选择 SMB 目录」用。
         *
         * 与实例方法 [list] 的区别：这是给**填表阶段**用的（还没保存连接），
         * 用完就把连接关掉，不留在缓存里。返回的是共享内相对路径。
         *
         * ## 为什么没有「列出共享」这一步
         *
         * 列服务器上的共享名要走 `IPC$` + 一次 DCE/RPC `NetShareEnum`。
         * 本项目只依赖 `smbj`（它的 `Connection`/`Session` 没有共享枚举 API），
         * 要走 RPC 得再加 `smbj-rpc` 依赖、手写 NDR 结构 —— 为选一个共享名引入这些
         * 不划算，且那条链路一旦协议细节不对就是难查的静默失败。
         *
         * 所以共享名**手填**，浏览器从共享根目录开始 —— 用户要选的"文件夹"
         * 本来就在共享内部，那一层照常可以点选。
         *
         * [share] 为空时由调用方先要求填写，这里直接返回空。
         */
        const val TIMEOUT_SECONDS = 30L

        /** `FileAttributes.Directory` 的位值；SMBJ 这里给的是原始属性字，按位与 */
        const val DIRECTORY_ATTR = 0x10L
    }
}

/**
 * 「选择 SMB 目录」用的列目录入口。
 *
 * 与 [SmbjReader.list] 的区别：这是给**填表阶段**用的（连接还没保存），
 * 用完就把连接关掉，不留在 `SmbStreamProxy` 的缓存里。
 *
 * ## 为什么没有「列出共享」这一步
 *
 * 列服务器上的共享名要走 `IPC$` + 一次 DCE/RPC `NetShareEnum`。
 * 本项目只依赖 `smbj`（它的 `Connection` / `Session` 都没有共享枚举 API），
 * 要补得再加 `smbj-rpc` 依赖并手写 NDR 结构 —— 为选一个共享名引入这些不划算，
 * 而且那条链路一旦协议细节不对，就是很难查的静默失败。
 *
 * 所以共享名**手填**，浏览器从共享根目录开始 —— 用户要选的"文件夹"本来就在共享内部，
 * 那一层照常可以逐级点选。这也正是 Android 此前完全缺失的能力。
 */
object SmbBrowser {

    /**
     * @param target 只用到 host / port / 账号 / 域名 / share，`remotePath` 不参与
     * @param dir 共享内相对路径（`\` 分隔、以 `\` 开头；空串等同根目录）
     */
    fun listDirs(target: SmbTarget, dir: String): List<RemoteFile> {
        if (target.share.isBlank()) return emptyList()
        val reader = SmbjReader(target)
        return try {
            reader.list(dir)
        } finally {
            runCatching { reader.close() }
        }
    }
}
