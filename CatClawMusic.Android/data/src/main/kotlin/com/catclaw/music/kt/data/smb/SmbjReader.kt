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
        const val TIMEOUT_SECONDS = 30L

        /** `FileAttributes.Directory` 的位值；SMBJ 这里给的是原始属性字，按位与 */
        const val DIRECTORY_ATTR = 0x10L
    }
}
