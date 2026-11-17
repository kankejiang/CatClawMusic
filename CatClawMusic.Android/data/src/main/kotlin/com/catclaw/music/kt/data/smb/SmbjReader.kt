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
 * 填表阶段的 SMB 浏览：列共享、列目录。用完即关连接，不进 `SmbStreamProxy` 的缓存。
 *
 * ## 共享名不是必填项
 *
 * 用户不该被迫先知道共享名。SMB 客户端（含 MAUI 那套）的正常体验是：
 * 填主机 → 点浏览 → **列出共享** → 点一个（自动填进共享名）→ 在里面逐级选目录。
 *
 * ## 怎么列出共享（没有 RPC）
 *
 * 标准做法是连 `IPC$` 再发一次 DCE/RPC `NetShareEnum`。本项目只依赖 `smbj`，
 * 它整个包里没有一个 `dcerpc` 类（`smbj-rpc` 这个 artifact 在 Maven Central 上也不存在），
 * 所以走不了那条路。
 *
 * 改用**探测**：在同一条已认证连接上，对一批候选名逐个 `connectShare`，
 * 能 tree-connect 成功的就是真实共享。代价是每次浏览要试若干次，好处是不加依赖、
 * 且失败只发生在单个候选名上（不像 RPC 一旦结构不对就整体失败）。
 *
 * 候选名 = 常见共享名 + 从用户已填路径/主机名里能推出来的词。探测不全时
 * 仍可手填 —— 手填和点选是并存的，不是二选一。
 */
object SmbBrowser {

    /**
     * 常见共享名。大小写不敏感去重；Windows 侧是 `Users/Public` 那套，
     * NAS 侧最常见的是 `music/video/photo/share/data/public/home`。
     */
    private val COMMON_SHARES = listOf(
        "music", "Music", "audio", "Audio", "media", "Media",
        "video", "Video", "photo", "Photo", "photos", "Photos",
        "share", "Share", "shared", "Shared", "public", "Public",
        "data", "Data", "home", "Home", "users", "Users",
        "download", "downloads", "Downloads", "backup", "Backup",
        "homes", "homeshare", "disk", "disk1", "volume1", "volume2",
    )

    /**
     * 列出可用的共享名。
     *
     * 一次连接、一次认证，然后对候选名逐个 tree-connect；成功的收进结果并立即断开，
     * 免得把服务器的 tree 连接配额占满（部分 NAS 对并发 tree 数有限制）。
     *
     * @param extraCandidates 调用方补的候选（比如从主机名、已填路径里猜出来的词）
     * @return 可用的共享名；一个都探不到时返回空列表（调用方据此提示手填）
     */
    fun listShares(target: SmbTarget, extraCandidates: List<String> = emptyList()): List<String> {
        val candidates = (extraCandidates + COMMON_SHARES)
            .map { it.trim() }
            .filter { it.isNotEmpty() && !it.endsWith("$") }
            .distinctBy { it.lowercase() }
        if (candidates.isEmpty()) return emptyList()

        val config = smbConfig()
        val conn = runCatching { SMBClient(config).connect(target.host, target.port) }.getOrNull()
            ?: return emptyList()
        return try {
            val sess = runCatching {
                conn.authenticate(
                    AuthenticationContext(target.user, target.password.toCharArray(), target.domain),
                )
            }.getOrNull() ?: return emptyList()

            val found = ArrayList<String>()
            for (name in candidates) {
                val ok = runCatching {
                    val share = sess.connectShare(name)
                    runCatching { share.close() }
                    true
                }.getOrDefault(false)
                if (ok) found += name
            }
            found.sortedBy { it.lowercase() }
        } finally {
            runCatching { conn.close() }
        }
    }

    /**
     * 从任意线索推断共享名。
     *
     * 用户粘进来的可能是完整 SMB URL（`smb://host/music/专辑`）、Windows 路径
     * （`\\host\music\专辑`）、或就是 `music/专辑` —— 这三种的第一段都可能是共享名。
     * 推不出来返回空串，调用方继续走探测。
     */
    fun guessShare(rawPath: String): String {
        if (rawPath.isBlank()) return ""
        var rest = rawPath.trim()

        // 先剥掉"带主机的那一层"。三种写法的主机部分不同，剥法也不同：
        //   smb://[user:pass@]host[:port]/music/...   → 去掉 scheme + authority
        //   \\host\music\...                          → 去掉 \\host
        //   music/...                                 → 本来就没有主机
        val hasAuthority = when {
            rest.startsWith("smb://", ignoreCase = true) -> {
                rest = rest.substring("smb://".length)
                true
            }
            rest.startsWith("\\\\") -> {
                rest = rest.substring(2)
                true
            }
            else -> false
        }
        if (hasAuthority) {
            val slash = rest.indexOfFirst { it == '/' || it == '\\' }
            if (slash < 0) return ""   // 只有主机名，没有共享段
            rest = rest.substring(slash + 1)
        }

        return rest.split('/', '\\').firstOrNull { it.isNotEmpty() } ?: ""
    }

    /**
     * 列共享内目录。
     *
     * @param share 共享名；**空**时先探测一遍（[listShares]），唯一命中就自动用它，
     *              多个命中或全无命中都由调用方决定下一步（选择或手填）。
     * @return `(实际使用的共享名, 目录列表)`；共享名仍为空说明没探到
     */
    fun listDirs(
        target: SmbTarget,
        dir: String,
        share: String = target.share,
    ): Pair<String, List<RemoteFile>> {
        val resolved = share.ifBlank {
            val guessed = guessShare(dir)
            val found = listShares(target, listOfNotNull(guessed.ifEmpty { null }))
            found.singleOrNull() ?: return "" to emptyList()
        }
        val reader = SmbjReader(target.copy(share = resolved))
        val dirs = try {
            reader.list(normalizeDir(dir))
        } finally {
            runCatching { reader.close() }
        }
        return resolved to dirs
    }

    private fun normalizeDir(path: String): String {
        val cleaned = path.replace('/', '\\').trim('\\')
        return if (cleaned.isEmpty()) "\\" else "\\$cleaned"
    }

    private fun smbConfig(): SmbConfig = SmbConfig.builder()
        .withTimeout(TIMEOUT_SECONDS, TimeUnit.SECONDS)
        .withSoTimeout(TIMEOUT_SECONDS, TimeUnit.SECONDS)
        .withMultiProtocolNegotiate(true)
        .build()

    private const val TIMEOUT_SECONDS = 30L
}
