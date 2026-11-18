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
        val ds = sess.connectShare(target.share) as DiskShare
        return ds.also {
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
        // 只要 FileDirectoryInformation：默认的 FileIdBothDirectoryInformation 每条多带
        // 16 字节文件 ID 和两个 8 字节时间戳，1226 条的目录上这些字段全是我们不读的。
        // SMB 会把结果按响应包分页传，每条少一点就少几个来回。
        val entries = diskShare().list(
            dir,
            com.hierynomus.msfscc.fileinformation.FileDirectoryInformation::class.java,
        )
        return entries.mapNotNull { info ->
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
        // 显式登出再断连接：只 close  TCP 的话服务端会话要等自己超时才释放，
        // 连续打开几次浏览器就会把 NAS 的并发会话配额占满（真机表现为 timeout）
        runCatching { session?.logoff() }
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
 * ## 共享枚举走 jcifs-ng，其余仍走 smbj
 *
 * smbj 没有共享枚举 API（整个包里没有 `dcerpc` 类，`smbj-rpc` 在 Maven Central
 * 上也不存在），而[候选名探测](旧实现)只能命中写死的名字 —— 真机 FNOS 上 12 个共享
 * 里大半都探不到。所以列共享交给 jcifs-ng（它自带 `NetShareEnum`），
 * 列目录与读取继续用 smbj。详见 [SmbShareLister]。
 */
object SmbBrowser {

    /**
     * 列出服务器上的共享名。
     *
     * 走真正的 `NetShareEnum`（见 [SmbShareLister]），不是候选名探测 ——
     * 探测只能命中写死的名字，真机 FNOS 上 12 个共享里大半都探不到。
     *
     * @return 共享名；空列表表示服务器确实没有可列出的共享
     */
    fun listShares(target: SmbTarget): List<String> = SmbShareLister.list(target)

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
     * 一个可复用的浏览会话。
     *
     * 浏览目录是**连续多次**操作（进一层、退一层、再进另一层），而每次新建
     * [SmbjReader] 都要支付 connect + authenticate + treeConnect（真机约 177ms）。
     * 会话级复用把这笔开销摊到整个浏览过程里，只付一次。
     *
     * 用完必须 [close]：内部持有 TCP 连接与登录会话，泄漏会占满 NAS 的并发配额。
     */
    class BrowseSession(private val target: SmbTarget) {
        private var reader: SmbjReader? = null
        private val lock = Any()

        fun list(dir: String): List<RemoteFile> {
            val r = synchronized(lock) {
                reader ?: SmbjReader(target).also { reader = it }
            }
            return r.list(normalizeDir(dir))
        }

        fun close() {
            synchronized(lock) {
                runCatching { reader?.close() }
                reader = null
            }
        }
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
