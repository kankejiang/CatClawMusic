package com.catclaw.music.kt.ui.settings

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.core.content.FileProvider
import androidx.lifecycle.ViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.diagnostic.DiagnosticLogRules
import com.catclaw.music.kt.core.diagnostic.LogEntry
import com.catclaw.music.kt.core.diagnostic.LogLevel
import com.catclaw.music.kt.data.diagnostic.DiagnosticLogStore
import com.catclaw.music.kt.ui.screen.SwitchRow
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.EmptyState
import com.catclaw.music.kt.ui.theme.GlassChip
import com.catclaw.music.kt.ui.theme.GlassSearchField
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.glassCard
import dagger.hilt.android.lifecycle.HiltViewModel
import java.io.File
import javax.inject.Inject
import kotlinx.coroutines.launch

/**
 * 诊断日志页。
 *
 * 每次进页/点刷新都现读文件：日志是别的协程在写的，缓存进 VM 会看到一份旧文件，
 * 而"我复现了却没记上"正是这类页面最容易被误判成坏了的地方。
 */
@HiltViewModel
class DiagnosticLogViewModel @Inject constructor(
    private val store: DiagnosticLogStore,
) : ViewModel() {



    // 属性名与开关方法名要错开：`var loggingEnabled` 自己就会生成 setLoggingEnabled(Z)，
    // 再写一个同名方法就是 JVM 签名冲突（这坑踩了两次才干净）
    var loggingEnabled by mutableStateOf(store.enabled)
        private set

    fun toggleLogging(on: Boolean) {
        store.enabled = on
        loggingEnabled = on
    }

    suspend fun entries(): List<LogEntry> = store.entries()

    suspend fun clearLog() = store.clear()

    suspend fun exportPackage(): File = store.exportPackage()

    fun path(): String = store.path()
}

@Composable
fun DiagnosticLogScreen(onBack: () -> Unit, viewModel: DiagnosticLogViewModel = hiltViewModel()) {
    val palette = LocalCatClawPalette.current
    val scope = rememberCoroutineScope()
    val context = LocalContext.current
    var tick by remember { mutableStateOf(0) }
    var level by remember { mutableStateOf<LogLevel?>(null) }
    var tag by remember { mutableStateOf<String?>(null) }
    // 关键词筛选。规则层一直支持它（`DiagnosticLogRules.filter` 的 query 参数），只是界面没接 ——
    // 于是"复现了却在几百行里肉眼翻"成了这页唯一的用法
    var query by remember { mutableStateOf("") }
    // 刚复制了几条：给一句会自己消失的回执，否则点完没有任何反馈，用户会以为按钮坏了
    var copied by remember { mutableStateOf(0) }
    LaunchedEffect(copied) {
        if (copied > 0) {
            kotlinx.coroutines.delay(1800)
            copied = 0
        }
    }
    // 只有 tick 变化才重读文件：level/tag/query 都是在内存里筛，跟日志文件无关
    val all = produceState<List<LogEntry>>(initialValue = emptyList(), tick) {
        value = viewModel.entries()
    }
    val entries = DiagnosticLogRules.filter(all.value, level = level, tag = tag, query = query)
    val tags = remember(all.value) { DiagnosticLogRules.tagsOf(all.value).take(6) }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(horizontal = 16.dp),
    ) {
        Spacer(Modifier.height(10.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
            Spacer(Modifier.width(12.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = stringResource(R.string.diagnostic_title),
                    fontSize = 24.sp,
                    fontWeight = FontWeight.Bold,
                    color = Color(palette.textPrimary),
                )
                Text(
                    text = stringResource(R.string.diagnostic_subtitle),
                    fontSize = 12.sp,
                    color = Color(palette.textSecondary),
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            PillLabel(text = stringResource(R.string.diagnostic_refresh), onClick = { tick += 1 }, filled = false)

        }
        Spacer(Modifier.height(10.dp))

        Column(
            modifier = Modifier
                .fillMaxWidth()
                .glassCard(palette = palette, corner = 24)
                .padding(horizontal = 14.dp, vertical = 6.dp),
        ) {
            SwitchRow(
                title = stringResource(R.string.diagnostic_enable_title),
                subtitle = stringResource(R.string.diagnostic_enable_desc),
                checked = viewModel.loggingEnabled,
                onChecked = { on ->
                    viewModel.toggleLogging(on)
                    scope.launch { tick += 1 }
                },
            )
        }
        Spacer(Modifier.height(10.dp))

        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
            GlassChip(label = stringResource(R.string.diagnostic_all), selected = level == null, onClick = { level = null })
            LogLevel.entries.forEach { item ->
                GlassChip(
                    label = item.code,
                    selected = level == item,
                    onClick = { level = if (level == item) null else item },
                )
            }
        }
        Spacer(Modifier.height(6.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
            tags.forEach { name ->
                GlassChip(
                    label = name,
                    selected = tag == name,
                    onClick = { tag = if (tag == name) null else name },
                )
            }
        }
        Spacer(Modifier.height(8.dp))
        GlassSearchField(
            query = query,
            onChange = { query = it },
            placeholder = stringResource(R.string.diagnostic_search_hint),
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(Modifier.height(8.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            PillLabel(
                text = stringResource(R.string.diagnostic_clear),
                onClick = {
                    scope.launch {
                        viewModel.clearLog()
                        tick += 1
                    }
                },
                filled = false,
            )
            PillLabel(
                text = stringResource(R.string.diagnostic_export),
                onClick = {
                    scope.launch {
                        val file = viewModel.exportPackage()
                        val uri = FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", file)
                        val send = Intent(Intent.ACTION_SEND).apply {
                            type = "application/zip"
                            putExtra(Intent.EXTRA_STREAM, uri)
                            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                        }
                        context.startActivity(
                            Intent.createChooser(send, context.getString(R.string.diagnostic_export_title)),
                        )
                    }
                },
                filled = false,
            )
            // 复制的是**屏幕上这几条**（跟着级别/标签/关键词筛过的），不是整个文件 ——
            // 排障时要把哪一段发给别人，看到的和复制到的一致才不用再解释一遍
            PillLabel(
                text = stringResource(R.string.diagnostic_copy),
                onClick = {
                    val text = entries.joinToString("\n") {
                        DiagnosticLogRules.format(it.timestamp, it.level, it.tag, it.message)
                    }
                    val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                    clipboard.setPrimaryClip(ClipData.newPlainText("catclaw-log", text))
                    copied = entries.size
                },
                filled = false,
                enabled = entries.isNotEmpty(),
            )
        }
        if (copied > 0) {
            Text(
                text = stringResource(R.string.diagnostic_copied, copied),
                fontSize = 11.sp,
                color = Color(palette.textSecondary),
                modifier = Modifier.padding(top = 6.dp),
            )
        }
        Spacer(Modifier.height(8.dp))

        if (entries.isEmpty()) {
            // weight(1f) 而不是 fillMaxSize：在 Column 里 fillMaxSize 会把底下那行文件路径顶出屏幕
            Box(modifier = Modifier.weight(1f).fillMaxWidth().padding(horizontal = 20.dp), contentAlignment = Alignment.Center) {
                EmptyState(
                    // 用日志自己的文案，不复用歌曲搜索的 `search_none`——那句写的是"没找到相关的歌"，
                    // 摆在日志页上等于答非所问
                    hint = when {
                        query.isNotBlank() -> stringResource(R.string.diagnostic_search_none, query.trim())
                        !viewModel.loggingEnabled -> stringResource(R.string.diagnostic_disabled_hint)
                        else -> stringResource(R.string.diagnostic_empty)
                    },
                    iconRes = R.drawable.ic_info,
                )
            }
        } else {
            LazyColumn(
                modifier = Modifier.weight(1f),
                contentPadding = PaddingValues(bottom = 24.dp),
                verticalArrangement = Arrangement.spacedBy(6.dp),
            ) {
                items(entries) { entry ->
                    LogRow(entry)
                }
            }
        }
        Text(
            text = stringResource(R.string.diagnostic_path, viewModel.path()),
            fontSize = 10.sp,
            color = Color(palette.textSecondary),
            maxLines = 2,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.padding(bottom = 8.dp),
        )
    }
}

@Composable
private fun LogRow(entry: LogEntry) {
    val palette = LocalCatClawPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .glassCard(palette = palette, corner = 16)
            .padding(horizontal = 10.dp, vertical = 8.dp),
        verticalAlignment = Alignment.Top,
    ) {
        // 级别用字母方块而不是颜色：深浅两套主题下要同时保证可读，靠颜色区分很容易掉到判据以下
        Box(
            modifier = Modifier
                .size(20.dp)
                .clip(RoundedCornerShape(6.dp))
                .background(Color(palette.textPrimary).copy(alpha = if (entry.level == LogLevel.Error) 0.28f else 0.12f)),
            contentAlignment = Alignment.Center,
        ) {
            Text(
                text = entry.level.code,
                fontSize = 11.sp,
                fontWeight = FontWeight.Bold,
                color = Color(palette.textPrimary),
            )
        }
        Spacer(Modifier.width(8.dp))
        Text(
            text = entry.timestamp,
            style = MaterialTheme.typography.bodySmall,
            fontFamily = FontFamily.Monospace,
            color = Color(palette.textSecondary),
        )
        Spacer(Modifier.width(8.dp))
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = entry.tag,
                fontSize = 11.sp,
                fontWeight = FontWeight.SemiBold,
                color = Color(palette.textPrimary),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = entry.message,
                style = MaterialTheme.typography.bodySmall,
                color = Color(palette.textSecondary),
                maxLines = 3,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}
