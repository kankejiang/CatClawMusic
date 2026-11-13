package com.catclaw.music.kt.architecture

import java.io.File
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * MVVM 架构闸门（源码扫描式，与现有 HiltViewModelWiringTest 同一套做法）。
 *
 * 这些规则**不能**靠代码评审保证：它们约束的是 import、字段名、注解与文件位置，
 * 编译器和运行时都不会报错，只有扫描源码才能兜住。白名单只允许缩小。
 */
class ArchitectureGatesTest {

    private val appRoot: File = File("src/main/kotlin").takeIf { it.isDirectory }
        ?: File("app/src/main/kotlin").takeIf { it.isDirectory }
        ?: error("找不到 :app 源码目录（测试工作目录应为 app 模块根）")

    private val dataRoot: File? = listOf(
        File("../data/src/main/kotlin"),
        File("data/src/main/kotlin"),
    ).firstOrNull { it.isDirectory }

    private val coreRoot: File? = listOf(
        File("../core/src/main/kotlin"),
        File("core/src/main/kotlin"),
    ).firstOrNull { it.isDirectory }

    private fun kt(root: File): List<File> =
        root.walkTopDown().filter { it.extension == "kt" }.toList()

    /** 允许暂时违反的状态重复（B2 清空；只许减不许增） */
    private val duplicateStateAllowlist = setOf("SearchViewModel.kt")

    @Test
    fun `Repository 必须单例且不能被 UI 依赖`() {
        val root = dataRoot ?: error("找不到 :data 源码目录")
        val repos = kt(root).filter { it.path.replace('\\', '/').contains("/data/repository/") }
        assertTrue("Repository 一个都没扫到，路径八成错了", repos.isNotEmpty())
        val offenders = repos.filter { f ->
            val text = f.readText()
            val isRepoClass = Regex("""class (\w*Repository)\b""").containsMatchIn(text)
            val hasSingleton = "@Singleton" in text
            val importsUi = "import com.catclaw.music.kt.ui." in text
            isRepoClass && (!hasSingleton || importsUi)
        }.map { it.name }
        assertTrue("Repository 必须 @Singleton 且禁止 import ui.*: $offenders", offenders.isEmpty())
    }

    @Test
    fun `跨页状态只允许出现在 Repository 层`() {
        val forbidden = listOf("_likedIds", "_account", "_neteaseAccount", "_loggedIn", "_favorites")
        val offenders = kt(appRoot)
            .filter { it.name !in duplicateStateAllowlist }
            .flatMap { f ->
                val text = f.readText()
                forbidden.filter { text.contains("val $it") || text.contains("var $it") }
                    .map { "${f.name}:$it" }
            }
        assertTrue("这些状态必须收敛到 :data/repository（UI/VM 不得再持有副本）: $offenders", offenders.isEmpty())
    }

    @Test
    fun `UI 与 ViewModel 不得直接依赖 DAO 和网络客户端`() {
        // 注意：data.db 包里的 *ListItem/SongMapper 是只读 DTO，允许 UI 使用；
        // 禁止的是 DAO / Entity / Database 本体
        val banned = listOf(
            "import com.catclaw.music.kt.data.db.LibraryDao",
            "import com.catclaw.music.kt.data.db.PlaylistDao",
            "import com.catclaw.music.kt.data.db.ConnectionDao",
            "import com.catclaw.music.kt.data.db.ChatDao",
            "import com.catclaw.music.kt.data.db.BackupDao",
            "import com.catclaw.music.kt.data.db.CatClawDatabase",
            "import com.catclaw.music.kt.data.db.entity.",
            "import com.catclaw.music.kt.data.netease.NeteaseApiClient",
            "import com.catclaw.music.kt.data.netease.NeteaseLoginClient",
            "import com.catclaw.music.kt.data.netease.NeteasePlayUrlResolver",
            "import com.catclaw.music.kt.data.library.LibraryScanner",
            "import com.catclaw.music.kt.core.audio.AudioCache",
            "import com.catclaw.music.kt.data.download.MusicDownloader",
        )
        // B1 白名单=当前存量；B2 收口后应清空
        val allowlist = setOf(
            "LibraryViewModel.kt", "PlayingViewModel.kt", "RemoteViewModel.kt", "ChatViewModel.kt",
            "PlaylistViewModel.kt", "SearchViewModel.kt", "ListeningStatsViewModel.kt",
            "LocalMusicSettingsViewModel.kt", "DownloadsViewModel.kt", "SettingsViewModel.kt",
            "NeteaseImportViewModel.kt", "NeteaseMusicViewModel.kt",
            // 歌单页及其拆分文件直接用 PlaylistEntity 做拖拽排序（B3 收口）
            "PlaylistScreen.kt", "PlaylistParts.kt", "PlaylistLandscape.kt", "PlaylistShelf.kt",
        )
        val offenders = kt(File(appRoot, "com/catclaw/music/kt/ui"))
            .filter { it.name !in allowlist }
            .flatMap { f ->
                val text = f.readText()
                banned.filter { text.contains(it) }.map { "${f.name}:$it" }
            }
        assertTrue("UI/VM 直连数据层（应改走 Repository）: $offenders", offenders.isEmpty())
    }

    @Test
    fun `测试总量不得下降`() {
        fun count(root: File?): Int {
            if (root == null) return -1
            return kt(root).sumOf { f ->
                Regex("""@Test\b""").findAll(f.readText()).count()
            }
        }
        // 注意：这里只扫 main，测试源码在 src/test；下面用相对路径单独数
        fun countTests(module: String): Int {
            val dir = listOf(File("../$module/src/test/kotlin"), File("$module/src/test/kotlin"))
                .firstOrNull { it.isDirectory } ?: return -1
            return kt(dir).sumOf { f -> Regex("""@Test\b""").findAll(f.readText()).count() }
        }
        val core = countTests("core")
        val data = countTests("data")
        val app = countTests("app")
        assertTrue("core 测试数 $core < 506，疑似被删", core < 0 || core >= 506)
        assertTrue("data 测试数 $data < 283，疑似被删", data < 0 || data >= 283)
        assertTrue("app 测试数 $app < 85，疑似被删", app < 0 || app >= 85)
    }

    @Test
    fun `闸门要真能抓住违规写法`() {
        val bannedText = "import com.catclaw.music.kt.data.db.LibraryDao"
        assertFalse("违规 import 没被识别", !bannedText.contains("data.db."))
        assertTrue("字段名规则没生效", "private val _likedIds".contains("_likedIds"))
    }
}
