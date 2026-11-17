package com.catclaw.music.kt.data.smb

import jcifs.CIFSContext
import jcifs.config.PropertyConfiguration
import jcifs.context.BaseContext
import jcifs.smb.NtlmPasswordAuthenticator
import jcifs.smb.SmbFile
import java.util.Properties

/**
 * 列出 SMB 服务器上的共享名（`NetShareEnum`）。
 *
 * ## 为什么不用 smbj
 *
 * smbj 没有共享枚举 API —— 它整个包里没有一个 `dcerpc` 类，`Connection`/`Session`
 * 只到 `connectShare(名字)` 这一层。而共享枚举要在**进入任何共享之前**发一次
 * DCE/RPC `NetShareEnum`（`net view \\host` 走的就是它）。补这条路要手写 NDR
 * 编解码，`smbj-rpc` 这个 artifact 在 Maven Central 上又不存在。
 *
 * jcifs-ng 自带 `jcifs/internal/smb1/net/NetShareEnum`，所以只借它做这一件事；
 * 列目录与读取仍走 smbj（见 [SmbjReader]），两条链路互不影响。
 *
 * ## 为什么之前的"候选名探测"被换掉
 *
 * 探测只能命中写死在表里的名字。真机上的 FNOS 有 12 个共享
 * （`Music`/`Photos`/`openlist`/`PVE`/`solara`/`TimeMachine`/`xiaoya` + 6 个中文名），
 * 候选表永远追不上，用户新建或改名的共享更是完全漏掉。
 */
object SmbShareLister {

    /**
     * @return 共享名列表；连不上或认证失败时抛异常，由调用方转成界面提示
     */
    fun list(target: SmbTarget, timeoutMs: Int = 15_000): List<String> {
        val props = Properties().apply {
            // 只用来列共享，不做文件传输；超时设短一点，免得界面等太久
            setProperty("jcifs.smb.client.responseTimeout", timeoutMs.toString())
            setProperty("jcifs.smb.client.soTimeout", timeoutMs.toString())
            setProperty("jcifs.smb.client.connTimeout", timeoutMs.toString())
            // SMB2/3 优先：现代 NAS 基本都关了 SMB1
            setProperty("jcifs.smb.client.minVersion", "SMB202")
            setProperty("jcifs.smb.client.maxVersion", "SMB311")
            // 自签名/无签名的内网 NAS 很常见，签名要求交给服务端协商
            setProperty("jcifs.smb.client.signingPreferred", "false")
        }
        val base: CIFSContext = BaseContext(PropertyConfiguration(props))
        val auth = NtlmPasswordAuthenticator(
            target.domain.ifBlank { null },
            target.user.ifBlank { null },
            target.password.ifBlank { null },
        )
        val ctx = base.withCredentials(auth)

        // 根 URL：只有主机。listFiles() 在这一层返回的就是共享列表。
        val root = SmbFile("smb://${target.host}/", ctx)
        return root.listFiles()
            .map { it.name.trimEnd('/') }
            // 管理共享（C$/IPC$/ADMIN$ 等）列给用户只会误导
            .filter { it.isNotBlank() && !it.endsWith("$") }
            .distinctBy { it.lowercase() }
            .sortedBy { it.lowercase() }
    }
}
