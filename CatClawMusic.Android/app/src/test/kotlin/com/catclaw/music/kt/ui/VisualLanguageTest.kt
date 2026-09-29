package com.catclaw.music.kt.ui

import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 视觉语言的静态闸门：全 app 页面美化完之后**不许漂回去**。
 *
 * 这些断言全部来自本轮真实修掉的缺陷，不是凭空立的规矩：
 * 1. 页面里不该再有 stock Material 按钮/输入框——它们自带一套 M3 颜色，跟玻璃卡完全不是一路；
 * 2. 半透明渐变上写 `Color.White` 的字/图标，在浅色主题下会落到浅底直接看不见（设置页 Hero 就是这么翻车的）；
 * 3. 页面颜色必须走 `LocalCatClawPalette`，不能拿 `MaterialTheme.colorScheme`——后者在钉成深色的页面上是环境色，会串味；
 * 4. 聊天覆盖层的让栏规则：只按 `chatMode` 上报会让别的 tab 永久没有底栏。
 */
class VisualLanguageTest {

    private val uiRoot by lazy {
        File("src/main/kotlin/com/catclaw/music/kt/ui").takeIf { it.isDirectory }
            ?: File("app/src/main/kotlin/com/catclaw/music/kt/ui").takeIf { it.isDirectory }
            ?: error("找不到 ui 源码目录（测试工作目录应为 :app 模块根）")
    }

    /** theme/ 是定义玻璃卡与共享零件的地方，不是页面，不参与扫描 */
    private val pages: List<File> by lazy {
        uiRoot.walkTopDown()
            .filter { it.extension == "kt" }
            .filter { !it.absolutePath.replace('\\', '/').contains("/theme/") }
            .toList()
            .also { assertTrue("只扫到 ${it.size} 个页面文件，八成路径错了", it.size >= 10) }
    }

    private fun File.bodyLines(): List<String> =
        readText().lines().filterNot {
            val t = it.trim()
            t.startsWith("//") || t.startsWith("*") || t.startsWith("/*")
        }

    /** 前置 `(?<![\w.])` 是为了放过自制的玻璃零件：`IconButton(` / `PillButton(` 都不该算成 stock Button( */
    private val stock = Regex(
        """(?<![\w.])(Button|OutlinedButton|TextButton|ElevatedButton|FilledTonalButton|""" +
            """OutlinedTextField|ExposedDropdownMenuBox|SegmentedButton)\s*\(""",
    )

    private val whiteForeground = Regex("""(color|tint)\s*=\s*Color\.White""")

    private fun stockHit(line: String): String? = stock.find(line)?.value?.trim()

    @Test
    fun `页面不再使用 stock Material 按钮与输入框`() {
        val hits = pages.flatMap { f ->
            f.bodyLines().mapIndexedNotNull { i, line ->
                val m = stockHit(line)
                if (m == null) null else "${f.name}:${i + 1} $m"
            }
        }
        assertTrue("这些控件自带一套 M3 配色，必须换成玻璃零件:\n$hits", hits.isEmpty())
    }

    @Test
    fun `半透明渐变上不许写死白色前景`() {
        val offenders = pages.filter { whiteForeground.containsMatchIn(it.readText()) }
            .filter { gradientBodies(it.readText()).any(::isTranslucent) }
            .map { it.name }
        assertTrue(
            "渐变是半透明的，浅色主题下会落到浅底上，白字/白图标等于隐形——前景要走 palette: $offenders",
            offenders.isEmpty(),
        )
    }

    /**
     * 品牌**实心**面（primary / primaryLight / primaryDark / accent）全都在中低对比区：
     * 真机像素量到白字压 primaryLight(#FFD9E7) 只有 **1.29:1**、压 primary(#FF8FB8) 只有 **2.13:1**。
     * 上面那条白字闸门只认「半透明渐变」，这类不透明品牌面正是它的盲区（本轮就是被它漏了 4 处）。
     * 规则：面上有字/图标时，surface 行之后 12 行代码内（注释不计）必须出现配这块面的
     * `inkOn(palette.X)`（或渐变用的 `inkOnGradient(`）；纯色块没有前景就不管（进度条那种）。
     */
    private fun solidFillInkMisses(text: String): List<Int> {
        val lines = text.lines()
        val surface = Regex("""background\(Color\(palette\.(primary|primaryLight|primaryDark|accent)\)\)""")
        return lines.mapIndexedNotNull { i, raw ->
            val m = surface.find(raw) ?: return@mapIndexedNotNull null
            val clean = { ls: List<String> ->
                ls.filterNot { val t = it.trim(); t.startsWith("//") || t.startsWith("*") }.joinToString(" ")
            }
            // 前景可能在面之前（modifier 挂在同一个 Text 的 modifier 参数上，如「新建歌单」那颗按钮），也可能在之后（Box 套 Text）
            val window = clean(lines.drop(maxOf(0, i - 8)).take(8)) + " " + clean(lines.drop(i + 1).take(12))
            if (!Regex("""(color|tint)\s*=\s*Color\(""").containsMatchIn(window)) return@mapIndexedNotNull null
            if ("inkOn(palette.${m.groupValues[1]})" in window || "inkOnGradient(" in window) null else i + 1
        }
    }

    @Test
    fun `品牌实心面上的前景走 inkOn`() {
        val offenders = pages.flatMap { f -> solidFillInkMisses(f.readText()).map { "${f.name}:$it" } }
        assertTrue("粉面压白字只有 1.29–2.13:1，字色请改 inkOn(palette.同一块面): $offenders", offenders.isEmpty())
    }

    /**
     * 强调色（薄荷绿 #7ED8C3）当文字色用，在浅色主题的卡面上真机量到 **1.29:1**
     * （音乐库「资料库 2」那个计数徽标）。要拿它标数字得先过 badgeTintOn 这类判据。
     */
    @Test
    fun `强调色当文字色必须先过可读性判据`() {
        val hits = pages.flatMap { f ->
            f.bodyLines().mapIndexedNotNull { i, line ->
                val t = line.trim()
                if (t.startsWith("//") || t.startsWith("*")) return@mapIndexedNotNull null
                if (Regex("""(color|tint)\s*=\s*Color\(palette\.accent\)""").containsMatchIn(t)) "${f.name}:${i + 1}" else null
            }
        }
        assertTrue("accent 直接当字色在浅色主题只有 1.29:1，请走 badgeTintOn(palette, palette.accent): $hits", hits.isEmpty())
    }

    @Test
    fun `探针 品牌面少字色必须被抓到而 inkOn 与纯色块不误报`() {
        val wrong = """
            .background(Color(palette.primaryLight))
            .border(1.dp, Color(palette.textPrimary).copy(alpha = 0.16f), shape),
            contentAlignment = Alignment.Center,
        ) {
            Text("春", color = Color(palette.textPrimary))
        }
        """
        assertTrue("primaryLight 面上用 textPrimary 没被抓到", solidFillInkMisses(wrong).isNotEmpty())
        val wrongPrimary = wrong.replace("primaryLight", "primary")
        assertTrue("primary 面上用 textPrimary 没被抓到（闸门要覆盖四种面，不止 primaryLight）", solidFillInkMisses(wrongPrimary).isNotEmpty())
        val right = wrong.replace("Text(\"春\", color = Color(palette.textPrimary))", "Text(\"春\", color = Color(inkOn(palette.primaryLight)))")
        assertTrue("inkOn 写法被误报", solidFillInkMisses(right).isEmpty())
        val noForeground = """
            Box(
                modifier = Modifier
                    .fillMaxWidth(fraction)
                    .background(Color(palette.primary)),
            )
        """
        assertTrue("没有前景的纯色块（进度条那种）被误报", solidFillInkMisses(noForeground).isEmpty())
    }

    /**
     * 值标签/状态文字想用主色，必须先过 `highlightOnGlassCard` / `highlightOnSelectedCard` / `badgeTintOn`
     * 这类判据：浅色主题的 primary 就是 #F56FA0，压在玻璃卡上真机量到 **2.28:1**
     * （通用设置「跟随系统」「500 MB」两行值标签就是这么写漏的）。
     * 唯一例外是装饰件：分组标题前那个 ✦——旁边就是同义文字，WCAG 不计装饰。
     */
    @Test
    fun `主色当字色必须先过可读性判据`() {
        val hits = pages.flatMap { f ->
            f.bodyLines().mapIndexedNotNull { i, line ->
                val t = line.trim()
                if ("✦" in t) return@mapIndexedNotNull null
                if (Regex("""color\s*=\s*Color\(palette\.(primary|primaryDark|primaryLight|accent)\)""").containsMatchIn(t)) {
                    "${f.name}:${i + 1}"
                } else null
            }
        }
        assertTrue("主色字色请改 highlightOnGlassCard(palette) / badgeTintOn(palette, …): $hits", hits.isEmpty())
    }

    @Test
    fun `页面颜色走 LocalCatClawPalette 而不是 colorScheme`() {
        val hits = pages.flatMap { f ->
            f.bodyLines().mapIndexedNotNull { i, line ->
                val idx = line.indexOf("MaterialTheme.colorScheme")
                if (idx < 0) null else "${f.name}:${i + 1}"
            }
        }
        assertTrue("环境色在钉深色的页面上会拿错方案: $hits", hits.isEmpty())
    }

    /** 紧跟 fillMaxSize 的 `background(backgroundStops…)` = 整页铺纯色；闸门和探针共用这份实现 */
    private fun flatPageHits(text: String): List<Int> {
        val lines = text.lines()
        return lines.mapIndexedNotNull { i, raw ->
            val t = raw.trim()
            if (t.startsWith("//") || t.startsWith("*")) return@mapIndexedNotNull null
            if ("background(" !in t || "backgroundStops" !in t) return@mapIndexedNotNull null
            if ("fillMaxSize()" !in lines.take(i).takeLast(3).joinToString(" ")) return@mapIndexedNotNull null
            i + 1
        }
    }

    /**
     * 整页不许用 `backgroundStops` 纯色铺底。主题底是「三层渐变 + 两层径向光晕 + 可替换的自定义背景图」，
     * 纯色铺满会把这三样全吞掉——本轮 ChatScreen 就是这么让自定义背景图失效的（已修）。
     * 对话框/底部面板这类局部表面用实色底是对的，所以只认紧跟 fillMaxSize 的写法。
     */
    /**
     * 每个页面文件都必须用到共享件之一。这是「所有页面同一套语言」唯一能被机器守住的形态：
     * 本轮把六类重复实现（卡面/胶囊/chip/返回方块/空状态/前景墨色）收进 `GlassParts` 之后，
     * 新页面只要不引用任何共享件就会被这条报下来。
     */
    @Test
    fun `每个页面都走共享件`() {
        val pageFiles = listOf(
            "CatClawRoot.kt", "chat/ChatScreen.kt", "player/MiniPlayer.kt",
            "screen/FullLyricsScreen.kt", "screen/LibraryScreen.kt", "screen/PlayingScreen.kt",
            "screen/PlaylistScreen.kt", "screen/QueueScreen.kt", "screen/RemoteSourcesScreen.kt",
            "screen/SearchScreen.kt", "screen/SettingsScreen.kt", "settings/AiSettingsScreen.kt", "settings/BackupRestoreScreen.kt", "settings/PermissionManagementScreen.kt", "settings/AboutScreen.kt", "settings/DiagnosticLogScreen.kt", "song/SongDetailScreen.kt", "equalizer/SoundEffectsScreen.kt", "player/SleepTimerDialog.kt", "stats/ListeningStatsScreen.kt",
        )
        val primitives = listOf(
            "glassCard(", "PillLabel(", "GlassChip(", "EmptyState(", "BackTile(",
            "inkOn(", "highlightOn", "catClawBackground(", "BarGlass(", "GlassTokens.",
        )
        val missing = pageFiles.filter { rel ->
            val f = File(uiRoot, rel)
            check(f.isFile) { "页面文件不见了: $rel（改名了请同步这条清单）" }
            primitives.none { it in f.readText() }
        }
        assertTrue("这些页面没引用任何共享件，等于自成一套视觉: $missing", missing.isEmpty())
    }

    /**
     * 每个整屏覆盖层都必须接住系统返回键。
     *
     * 这条是**真机上撞出来的**：统计页开着按返回，整个应用直接退出（此前全工程没有一处
     * `BackHandler`，覆盖层只认自己画的那个返回方块）。检测方式是拿 `if (showXxx)` 这种
     * 覆盖层开关当清单，逐个要求配套的 `BackHandler(enabled = showXxx …)`。
     */
    @Test
    fun `整屏覆盖层都接住系统返回键`() {
        // 页内那几层承载的是 id / 对象而不是布尔（`songDetailId?.let` 形状），下面的正则抓不到，
        // 就按名字点名：它们各自画了 BackTile，就必须也接住系统返回键，否则按返回直接退出应用
        // （本轮就是在歌单详情按返回掉回模拟器桌面，才顺手把这三页一起点名）
        val inPageLayers = mapOf(
            "screen/PlayingScreen.kt" to listOf("songDetailId", "belongsDetail"),
            "screen/PlaylistScreen.kt" to listOf("openId", "entry", "belongsDetail", "songDetailId"),
            "screen/LibraryScreen.kt" to listOf("opened", "detail", "songDetailId"),
        )
        val unhandled = inPageLayers.flatMap { (rel, flags) ->
            val text = File(uiRoot, rel).readText()
            flags.filterNot { Regex("""BackHandler\(enabled = $it != null""").containsMatchIn(text) }
                .map { "$rel:$it" }
        }
        assertTrue("页内整屏层画了返回方块却不接系统返回键: $unhandled", unhandled.isEmpty())

        // 早退结构里**先命中的那块才是看得见的那一层**：歌曲信息必须排在歌手/专辑层之前判。
        // 歌单页此前正排反了——在歌手页点「歌曲信息」把 id 存好了却仍然画歌手页，纹丝不动
        val playlist = File(uiRoot, "screen/PlaylistScreen.kt").readText()
        val songAt = playlist.indexOf("songDetailId?.let")
        val belongsAt = playlist.indexOf("belongsDetail?.let")
        assertTrue(
            "PlaylistScreen 里 songDetailId?.let 必须排在 belongsDetail?.let 之前（song=$songAt belongs=$belongsAt）",
            songAt >= 0 && belongsAt >= 0 && songAt < belongsAt,
        )

        val files = listOf("CatClawRoot.kt", "screen/SearchScreen.kt")
        val offenders = files.flatMap { rel ->
            val lines = File(uiRoot, rel).readText().lines()
            buildList {
                lines.forEachIndexed { i, line ->
                    val m = Regex("""^\s*if \((\w+)\)\s*\{?""").find(line) ?: return@forEachIndexed
                    val flag = m.groupValues[1]
                    // 只认"自己画了出口"的那一层：块体里出现 `flag = false` 说明它是个可以关掉的覆盖层，
                    // 而 `if (showMiniPlayer)` 这类只是显隐开关，不该被算进来
                    val body = lines.drop(i).take(10).joinToString(" ")
                    if ("$flag = false" !in body) return@forEachIndexed
                    if (!Regex("""BackHandler\(enabled = $flag\b""").containsMatchIn(lines.joinToString("\n"))) {
                        add("$rel:$flag (行 ${i + 1})")
                    }
                }
            }
        }
        assertTrue("覆盖层画了自己的返回方块却不接系统返回键，按返回会直接退出应用: $offenders", offenders.isEmpty())
    }

    @Test
    fun `页面不铺背景色纯色底`() {
        val offenders = pages.flatMap { f -> flatPageHits(f.readText()).map { "${f.name}:$it" } }
        assertTrue("整页请走 Modifier.catClawBackground()（渐变+光晕+自定义图）: $offenders", offenders.isEmpty())
    }

    @Test
    fun `探针 纯色铺满必须被抓到而局部实色底不该误报`() {
        val flat = """
            Column(
                modifier = Modifier
                    .fillMaxSize()
                    .background(Color(palette.backgroundStops.first().argb))
                    .imePadding(),
            ) { }
        """
        assertTrue("纯色铺满写法没被抓到", flatPageHits(flat).isNotEmpty())
        val dialog = """
            AlertDialog(
                containerColor = Color(palette.backgroundStops.last().argb),
            )
        """
        assertTrue("对话框的局部实色底被误判", flatPageHits(dialog).isEmpty())
        val sheet = """
            Column(
                modifier = Modifier
                    .align(Alignment.BottomCenter)
                    .fillMaxWidth()
                    .background(Color(palette.backgroundStops.last().argb))
            ) { }
        """
        assertTrue("局部面板用 backgroundStops 实色底被误判", flatPageHits(sheet).isEmpty())
    }

    @Test
    fun `覆盖层的让栏规则同时看两个覆盖层与 active`() {
        val search = File(uiRoot, "screen/SearchScreen.kt").readText()
        val root = File(uiRoot, "CatClawRoot.kt").readText()
        // Pager 不销毁别的页面，横滑走开时标志仍是 true；少了 active 底栏就永久消失。
        // 统计页是后加的第二层覆盖，让栏上报必须把它一起算进去 —— 漏了就是统计页底下一截点不到
        assertTrue(
            "SearchScreen 的上报必须是 (chatMode || statsOpen) && active",
            Regex("""onChatOpen\(\s*\(\s*chatMode\s*\|\|\s*statsOpen\s*\)\s*&&\s*active\s*\)""").containsMatchIn(search),
        )
        assertTrue(
            "CatClawRoot 的悬浮两栏必须在聊天打开时让位",
            Regex("""!\s*chatOpen""").containsMatchIn(root),
        )
    }

    /**
     * 正控制：检测器对合成片段必须报警。
     * 页面上的断言现在全是「零命中」，没有这一条就无法区分「真干净」和「正则写废了」。
     */
    /** 手写「α.10 面 + α.16 边」卡面配方的行号——闸门和探针共用这一份实现 */
    private fun cardRecipeHits(text: String): List<Int> {
        val lines = text.lines()
        return lines.mapIndexedNotNull { i, raw ->
            val t = raw.trim()
            if (t.startsWith("//") || t.startsWith("*") || "background(" !in t || "alpha = 0.10f" !in t) return@mapIndexedNotNull null
            if ("alpha = 0.16f" !in lines.drop(i).take(3).joinToString(" ")) return@mapIndexedNotNull null
            i + 1
        }
    }

    /**
     * 卡面配方（α.10 面 + α.16 边）必须在 `Modifier.glassCard` 里定义一次。
     * 各页过去各抄一遍，抄出过 22/24/26 三种圆角和 α.08/.10 两种底色——那就是「页面之间不一样」的根因。
     * 注意：嵌在卡里的小行用的是 α.08 无描边（另一档，故不在禁用范围）。
     */
    /**
     * chip 只许有一份定义（`GlassChip`）。五个页面各自实现过时，圆角是 26/50/999/14、
     * 未选中底色是 α.08/α.18/实心卡底、字级是 11/13/labelMedium——同一类控件五种长相。
     * 薄封装（只是换个参数名转调 GlassChip）放过，另写一份视觉才报。
     */
    @Test
    fun `chip 不在页面里各自实现`() {
        val offenders = pages.flatMap { f ->
            val lines = f.readText().lines()
            lines.mapIndexedNotNull { i, line ->
                val t = line.trim()
                if (t.startsWith("//") || t.startsWith("*")) return@mapIndexedNotNull null
                val m = Regex("""fun (\w*Chip\w*)\(""").find(t) ?: return@mapIndexedNotNull null
                // 窗口必须从同一份 lines 里取：早先用「过滤后的下标」去切「原始行」，
                // 差了注释行数，把自己写的薄封装误判成了另实现
                val body = lines.drop(i).take(8).joinToString(" ")
                if ("GlassChip(" in body && "clip(" !in body && "background(" !in body) return@mapIndexedNotNull null
                "${f.name}:${i + 1} ${m.groupValues[1]}"
            }
        }
        assertTrue("请统一走 GlassChip（圆角/底色/字级只该有一处定义）: $offenders", offenders.isEmpty())
    }

    /**
     * `.size()` 之后紧跟 `.padding()` 再到 `.clickable` 是**顺序错**：padding 会把 clickable
     * 那个节点缩掉，图标和可点区一起变小（播放页那两个图标原本因此只有 17dp，比设计的小一半）。
     * 正确写法是可点节点在外：`.size(外框).clickable{}.padding{}`。
     */
    private fun shrinkOrderHits(text: String): List<Int> {
        val lines = text.lines().map { it.trim() }
        return lines.indices
            .filter { i ->
                lines[i].startsWith(".size(") &&
                    lines.getOrNull(i + 1)?.startsWith(".padding(") == true &&
                    lines.getOrNull(i + 2)?.startsWith(".clickable") == true
            }
            .map { it + 1 }
    }

    @Test
    fun `可点节点不在 padding 之内`() {
        val offenders = pages.flatMap { f -> shrinkOrderHits(f.readText()).map { "${f.name}:$it" } }
        assertTrue("size→padding→clickable 会把图标与触达区一起缩小，请改成 size→clickable→padding: $offenders", offenders.isEmpty())
    }

    @Test
    fun `探针 顺序错必须被识别而正确写法不误报`() {
        val wrong = "modifier = Modifier\n    .size(33.dp)\n    .padding(8.dp)\n    .clickable(onClick = f),"
        assertTrue("顺序错没被抓到", shrinkOrderHits(wrong).isNotEmpty())
        val right = "modifier = Modifier\n    .size(49.dp)\n    .clickable(onClick = f)\n    .padding(8.dp),"
        assertTrue("正确写法被误报", shrinkOrderHits(right).isEmpty())
    }

    /**
     * 共享可点零件必须 honor TouchMin。`PillLabel` / `BackTile` 是全 app 用得最多的两个控件，
     * 触达尺寸只该有一处定义；页面里自己写的小方块不在本条管辖内（已另列待办）。
     */
    @Test
    fun `共享零件的触达尺寸只有一个定义处`() {
        val parts = File(uiRoot, "theme/GlassParts.kt").readText()
        assertTrue("TouchMin 不见了或不再是 44dp", Regex("""val TouchMin = 44\.dp""").containsMatchIn(parts))
        assertTrue("BackTile 没走 TouchMin", Regex("""fun BackTile[\s\S]{0,260}\.size\(TouchMin\)""").containsMatchIn(parts))
        assertTrue(
            "PillLabel 的上下内边距没走 TouchPadV（触达≈44dp 靠它）",
            Regex("""fun PillLabel[\s\S]{0,1200}vertical = TouchPadV""").containsMatchIn(parts),
        )
        assertTrue("TouchPadV 不见了", Regex("""private val TouchPadV = 13\.dp""").containsMatchIn(parts))
    }

    @Test
    fun `卡面配方只在 glassCard 里出现一次`() {
        val offenders = pages.flatMap { f -> cardRecipeHits(f.readText()).map { "${f.name}:$it" } }
        assertTrue("手写 α.10+α.16 卡面请改走 Modifier.glassCard(...): $offenders", offenders.isEmpty())
    }

    @Test
    fun `探针 检测器必须能对合成片段报警`() {
        // 卡面检测器必须真抓到手写配方，否则上一条的「零命中」没有意义
        val handWritten = """
            Modifier
                .clip(shape)
                .background(Color(palette.textPrimary).copy(alpha = 0.10f))
                .border(1.dp, Color(palette.textPrimary).copy(alpha = 0.16f), shape)
        """
        assertTrue("手写卡面没被抓到", cardRecipeHits(handWritten).isNotEmpty())
        // α.08 无描边的嵌套行不该误报
        val nested = """
            Modifier
                .background(primary.copy(alpha = 0.08f))
                .clickable { viewModel.useConfig(name) }
        """
        assertTrue("嵌套行的 α.08 被误判成手写卡面", cardRecipeHits(nested).isEmpty())

        assertEquals("TextButton(", stockHit("            TextButton(onClick = {}) {"))
        assertEquals("OutlinedTextField(", stockHit("OutlinedTextField(value = q, onValueChange = f)"))
        assertEquals("Button(", stockHit("Button(onClick = viewModel::scan) { Text(\"x\") }"))
        // 自制玻璃零件与 IconButton 不能误报
        assertTrue("IconButton 被误判成 stock Button", stockHit("IconButton(onClick = {}) {}") == null)
        assertTrue("PillButton 被误判成 stock Button", stockHit("PillButton(label = t, onClick = f)") == null)
        assertTrue("PillLabel 被误判", stockHit("PillLabel(text = t, onClick = f)") == null)

        assertTrue("hex 半透明没识别", isTranslucent("linearGradient(listOf(Color(0x7A55D6FF), Color(0x4D7ED8C3)))"))
        assertTrue("copy(alpha=) 半透明没识别", isTranslucent("linearGradient(listOf(Color(palette.primary).copy(alpha = 0.40f)))"))
        assertTrue("不透明渐变误报", !isTranslucent("linearGradient(listOf(Color(0xFF667EEA), Color(0xFF764BA2)))"))
        // 括号必须平衡：渐变体外挂的 α.16 描边不能被算进渐变
        val borderAfterOpaqueGradient =
            "linearGradient(listOf(Color(0xFFFF8FB8), Color(0xFF7ED8C3))),\n" +
                ".border(1.dp, Color(palette.textPrimary).copy(alpha = 0.16f), shape)"
        assertTrue("描边 alpha 混进了渐变体会误报", gradientBodies(borderAfterOpaqueGradient).none(::isTranslucent))
        assertTrue(
            "半透明渐变带描边时漏报",
            gradientBodies(
                "linearGradient(listOf(Color(0x7A55D6FF), Color(0x4D7ED8C3))),\n" +
                    ".border(1.dp, primary.copy(alpha = 0.16f), shape)",
            ).any(::isTranslucent),
        )

        assertTrue("白字没识别", whiteForeground.containsMatchIn("    tint = Color.White,"))
        assertTrue("palette 前景误报", !whiteForeground.containsMatchIn("color = Color(palette.textPrimary),"))
    }

    /** 取出每个 `linearGradient(...)` 的完整括号体——只看渐变本身，不殃及邻近的描边 alpha */
    private fun gradientBodies(text: String): List<String> {
        val marker = "linearGradient("
        val out = mutableListOf<String>()
        var idx = text.indexOf(marker)
        while (idx >= 0) {
            val open = idx + marker.length - 1
            var depth = 0
            var j = open
            while (j < text.length) {
                when (text[j]) {
                    '(' -> depth++
                    ')' -> {
                        depth--
                        if (depth == 0) break
                    }
                }
                j++
            }
            out += text.substring(open, minOf(j + 1, text.length))
            idx = text.indexOf(marker, idx + 1)
        }
        return out
    }

    private fun isTranslucent(body: String): Boolean {
        val hexAlpha = Regex("""Color\(0x([0-9A-Fa-f]{2})[0-9A-Fa-f]{6}""")
            .findAll(body)
            .any { it.groupValues[1].uppercase() != "FF" }
        val copiedAlpha = Regex("""alpha\s*=\s*0\.(\d{1,2})f""")
            .findAll(body)
            .any { it.groupValues[1].trimEnd('0').toIntOrNull().orDefault() < 99 }
        return hexAlpha || copiedAlpha
    }

    private fun Int?.orDefault(): Int = this ?: 0
}
