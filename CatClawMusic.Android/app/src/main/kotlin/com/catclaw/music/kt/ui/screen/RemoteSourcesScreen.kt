package com.catclaw.music.kt.ui.screen

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.material3.TextField
import androidx.compose.material3.TextFieldDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.ui.player.CoverTile
import com.catclaw.music.kt.core.model.ConnectionProfile
import com.catclaw.music.kt.core.model.ProtocolType
import com.catclaw.music.kt.core.model.SongDetailFormat
import com.catclaw.music.kt.data.db.toSong
import com.catclaw.music.kt.data.download.DownloadState
import com.catclaw.music.kt.ui.remote.RemoteMessage
import com.catclaw.music.kt.ui.remote.RemoteViewModel
import androidx.activity.compose.BackHandler
import com.catclaw.music.kt.data.remote.RemoteFile
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.GlassSwitch
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.GlassChip
import com.catclaw.music.kt.ui.theme.inkOn
import com.catclaw.music.kt.ui.theme.EmptyState
import com.catclaw.music.kt.ui.theme.glassCard

/** 远程音源页 —— 内容不变，外观对齐本 App 的玻璃卡语言（Hero 头 + 分区玻璃卡 + 胶囊按钮） */
@Composable
fun RemoteSourcesScreen(onBack: () -> Unit, viewModel: RemoteViewModel = hiltViewModel()) {
    val palette = LocalCatClawPalette.current
    val profiles by viewModel.profiles.collectAsStateWithLifecycle()
    val songs by viewModel.remoteSongs.collectAsStateWithLifecycle()
    val busy by viewModel.busy.collectAsStateWithLifecycle()
    val message by viewModel.message.collectAsStateWithLifecycle()
    val downloadTasks by viewModel.downloadTasks.collectAsStateWithLifecycle()
    val cachedIds by viewModel.cachedSongIds.collectAsStateWithLifecycle()
    val progress by viewModel.progress.collectAsStateWithLifecycle()

    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)

    var name by rememberSaveable { mutableStateOf("") }
    var host by rememberSaveable { mutableStateOf("") }
    var port by rememberSaveable { mutableStateOf("4535") }
    var user by rememberSaveable { mutableStateOf("") }
    var pass by rememberSaveable { mutableStateOf("") }
    var protocol by rememberSaveable { mutableStateOf(ProtocolType.Navidrome.name) }
    var share by rememberSaveable { mutableStateOf("") }
    var domain by rememberSaveable { mutableStateOf("") }
    var basePath by rememberSaveable { mutableStateOf("") }
    var useHttps by rememberSaveable { mutableStateOf(false) }
    var apiVersion by rememberSaveable { mutableStateOf("") }
    // 正在改哪一条（null = 新增）。只活在内存里：ConnectionProfile 不是 Saveable，
    // 进程被杀后表单字段还在但会退回"新增"档——那正是安全的方向（不会把 id 弄丢去覆盖另一条）
    var editing by remember { mutableStateOf<ConnectionProfile?>(null) }

    /**
     * 表单当前这份 → 连接配置。「保存」与「浏览目录」共用同一个构造口：
     * 抄两份的话早晚会一份带 basePath、另一份不带，而 basePath 正是浏览要写进去的那格。
     */
    fun buildDraft(): ConnectionProfile {
        val chosen = ProtocolType.valueOf(protocol)
        return (editing ?: ConnectionProfile(name = "")).copy(
            name = name.ifBlank { host },
            host = host.trim(),
            port = port.trim().toIntOrNull() ?: if (chosen == ProtocolType.SMB) 445 else 4535,
            userName = user.trim(),
            password = pass,
            protocol = chosen,
            shareName = share.trim().ifEmpty { null },
            domainName = domain.trim().ifEmpty { null },
            basePath = basePath.trim(),
            // SMB 没有 http/https 这一说：给它写 useHttps 只是存一格永远没人读的值
            useHttps = useHttps && chosen != ProtocolType.SMB,
            // 留空交给 DAO 的默认值，别在这里重复写死同一个字面量
            apiVersion = apiVersion.trim().ifEmpty { "1.16.1" },
        )
    }

    val browse by viewModel.browse.collectAsStateWithLifecycle()
    // 目录浏览器是整屏层：它画了自己的返回块，系统返回键要退这一层，不是弹掉整页
    BackHandler(enabled = browse != null) { viewModel.closeWebDavBrowser() }
    browse?.let { panel ->
        RemoteBrowserPanel(
            panel = panel,
            onEnter = { viewModel.browseWebDav(buildDraft(), it.path) },
            // 上一级：把路径最后一段切掉。根目录（"/" 或空）时不给这个按钮
            onUp = {
                val parent = panel.path.trimEnd('/').substringBeforeLast('/', "")
                viewModel.browseWebDav(buildDraft(), parent.ifEmpty { "/" })
            },
            onPick = {
                // 选的是**人现在站的这一层**，不是某个子目录：走进去看到里面全是歌，
                // 再说"就要这个"，确认的语义才跟着动作
                basePath = panel.path.trimEnd('/').ifEmpty { "/" }
                viewModel.closeWebDavBrowser()
            },
            onClose = { viewModel.closeWebDavBrowser() },
        )
        return
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 20.dp),
    ) {
        Spacer(Modifier.height(14.dp))
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(text = stringResource(R.string.RemoteMusic), fontSize = 22.sp, fontWeight = FontWeight.Bold, color = primary)
                Text(text = stringResource(R.string.RemoteMusicSubtitle), fontSize = 12.sp, color = secondary)
            }
        }
        Spacer(Modifier.height(14.dp))

        message?.let {
            Text(
                text = when (it) {
                    RemoteMessage.NeedHost -> stringResource(R.string.remote_need_host)
                    is RemoteMessage.TestResult -> stringResource(
                        if (it.ok) R.string.remote_test_ok else R.string.remote_test_fail,
                        it.detail,
                    )
                    is RemoteMessage.SyncResult -> stringResource(
                        R.string.remote_sync_result,
                        it.scanned,
                        it.inserted,
                        it.updated,
                        it.elapsedMs,
                    )
                    is RemoteMessage.Failed -> stringResource(R.string.remote_failed, it.detail)
                },
                fontSize = 12.sp,
                color = Color(palette.textPrimary),
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(14.dp))
                    .background(Color(palette.primary).copy(alpha = 0.14f))
                    .padding(horizontal = 12.dp, vertical = 8.dp),
            )
            Spacer(Modifier.height(10.dp))
        }

        progress?.let {
            // MAUI 的同步进度面板：total 为 0 是"目录还没列完"，不是"一共 0 首"。
            // 此前这里只有一个灰掉的按钮，扫几千个文件的 SMB 源看起来像卡死了
            Text(
                text = if (it.unknownTotal) {
                    stringResource(R.string.remote_sync_listing)
                } else {
                    stringResource(R.string.remote_sync_progress, it.done, it.total)
                },
                fontSize = 12.sp,
                color = Color(palette.textPrimary),
                modifier = Modifier.fillMaxWidth().padding(bottom = 10.dp),
            )
        }

        SectionCard(title = stringResource(R.string.ConfiguredConnections)) {
            if (profiles.isEmpty()) {
                EmptyState(hint = stringResource(R.string.NoConnectionsHint), iconRes = R.drawable.ic_library)
            }
            profiles.forEach { profile ->
                Column(modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp)) {
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(10.dp),
                    ) {
                        Box(
                            modifier = Modifier
                                .size(42.dp)
                                .clip(RoundedCornerShape(12.dp))
                                .background(Color(palette.primary).copy(alpha = 0.18f)),
                            contentAlignment = Alignment.Center,
                        ) {
                            Icon(
                                painter = painterResource(R.drawable.ic_search),
                                contentDescription = null,
                                tint = Color(palette.primary),
                                modifier = Modifier.size(20.dp),
                            )
                        }
                        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                            Text(profile.name, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, color = primary, maxLines = 1, overflow = TextOverflow.Ellipsis)
                            Text(text = "${profile.origin} · ${profile.protocol.name}", fontSize = 12.sp, color = secondary, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        }
                    }
                    Spacer(Modifier.height(8.dp))
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp, Alignment.End), modifier = Modifier.fillMaxWidth()) {
                        PillButton(label = stringResource(R.string.Test), filled = false) { viewModel.test(profile) }
                        PillButton(label = stringResource(R.string.remote_import), filled = true, enabled = !busy) { viewModel.import(profile) }
                        // 此前只能删了重填：改一个密码要把整条连接删掉，而删掉会连带丢 basePath
                        PillButton(label = stringResource(R.string.remote_edit), filled = false) {
                            editing = profile
                            name = profile.name
                            host = profile.host
                            port = profile.port.toString()
                            user = profile.userName
                            pass = profile.password
                            protocol = profile.protocol.name
                            share = profile.shareName.orEmpty()
                            domain = profile.domainName.orEmpty()
                            basePath = profile.basePath
                            useHttps = profile.useHttps
                            apiVersion = profile.apiVersion
                        }
                        PillButton(label = stringResource(R.string.Delete), filled = false) { viewModel.delete(profile.id) }
                    }
                }
            }
        }

        val editingProfile = editing
        SectionCard(
            title = if (editingProfile == null) {
                stringResource(R.string.remote_add_server)
            } else {
                stringResource(R.string.remote_edit_server, editingProfile.name)
            },
        ) {
            GlassField(value = name, onValue = { name = it }, label = stringResource(R.string.Name))
            Spacer(Modifier.height(8.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                GlassField(value = host, onValue = { host = it }, label = stringResource(R.string.HostAddress), modifier = Modifier.weight(1.6f))
                GlassField(value = port, onValue = { port = it }, label = stringResource(R.string.Port), modifier = Modifier.weight(1f))
            }
            Spacer(Modifier.height(8.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                GlassField(value = user, onValue = { user = it }, label = stringResource(R.string.Username), modifier = Modifier.weight(1f))
                GlassField(value = pass, onValue = { pass = it }, label = stringResource(R.string.Password), modifier = Modifier.weight(1f))
            }
            Spacer(Modifier.height(10.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                ProtocolType.entries.forEach { option ->
                    GlassChip(label = option.name, selected = option.name == protocol, onClick = { protocol = option.name })
                }
            }
            // 协议这一格决定 origin 的 scheme，而 origin 是每条请求的前缀——没有这开关时
            // 用户只能存进一条 http 的连接，对端只上 TLS 就连不上（字段一直在，只是没人能填）
            if (protocol != ProtocolType.SMB.name) {
                Spacer(Modifier.height(6.dp))
                GlassSwitch(
                    label = stringResource(R.string.remote_use_https),
                    subtitle = stringResource(R.string.remote_use_https_sub),
                    checked = useHttps,
                    onChange = { useHttps = it },
                )
            }
            if (protocol == ProtocolType.Navidrome.name) {
                Spacer(Modifier.height(8.dp))
                GlassField(
                    value = apiVersion,
                    onValue = { apiVersion = it },
                    label = stringResource(R.string.remote_api_version),
                )
            }
            if (protocol == ProtocolType.SMB.name) {
                Spacer(Modifier.height(8.dp))
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    GlassField(value = share, onValue = { share = it }, label = stringResource(R.string.SmbShareName), modifier = Modifier.weight(1f))
                    GlassField(value = domain, onValue = { domain = it }, label = stringResource(R.string.SmbDomain), modifier = Modifier.weight(1f))
                }
            }
            // 只有 WebDAV 有"目录"这个概念：Subsonic/猫爪服务器给的是曲库不是路径树，
            // 摆在这几档上会是一个填了没用的格子
            if (protocol == ProtocolType.WebDAV.name) {
                Spacer(Modifier.height(8.dp))
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                    GlassField(
                        value = basePath,
                        onValue = { basePath = it },
                        label = stringResource(R.string.remote_base_path),
                        modifier = Modifier.weight(1f),
                    )
                    PillLabel(
                        text = stringResource(R.string.remote_browse),
                        onClick = { viewModel.browseWebDav(buildDraft(), basePath.ifBlank { "/" }) },
                        filled = false,
                    )
                }
            }
            Spacer(Modifier.height(12.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                PillButton(label = stringResource(R.string.Save), filled = true) {
                    // 从正在改的那条出发 copy：id / apiVersion / serverType / useHttps 都不在这个表单里，
                    // 重新 new 一个会把它们悄悄抹回默认值
                    viewModel.save(buildDraft())
                    editing = null
                    name = ""
                    host = ""
                    user = ""
                    pass = ""
                    share = ""
                    domain = ""
                    basePath = ""
                    useHttps = false
                    apiVersion = ""
                    port = "4535"
                }
                if (editingProfile != null) {
                    PillButton(label = stringResource(R.string.remote_cancel_edit), filled = false) {
                        editing = null
                        name = ""
                        host = ""
                        user = ""
                        pass = ""
                        share = ""
                        domain = ""
                        basePath = ""
                        useHttps = false
                        apiVersion = ""
                        port = "4535"
                    }
                }
            }
        }

        SectionCard(title = stringResource(R.string.RemoteMusic) + " · " + stringResource(R.string.playlist_count, songs.size)) {
            songs.forEachIndexed { index, song ->
                val task = downloadTasks.firstOrNull { it.songId == song.id }
                val cached = song.id in cachedIds
                Row(
                    modifier = Modifier.fillMaxWidth().padding(vertical = 5.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    CoverTile(
                        coverArtPath = song.coverArtPath,
                        audioPath = song.filePath,
                        maxPx = 96,
                        modifier = Modifier
                            .size(40.dp)
                            .clip(RoundedCornerShape(12.dp))
                            .background(Color(palette.textPrimary).copy(alpha = 0.10f))
                            .clickable { viewModel.playAt(index) },
                    ) {
                        Text(text = song.title.take(1), fontSize = 16.sp, fontWeight = FontWeight.Bold, color = primary)
                    }
                    Column(
                        modifier = Modifier.weight(1f).clickable { viewModel.playAt(index) },
                        verticalArrangement = Arrangement.spacedBy(2.dp),
                    ) {
                        Text(song.title, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, color = primary, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        Text(
                            text = when {
                                task != null && task.state == DownloadState.Running -> stringResource(
                                    R.string.downloads_bytes,
                                    SongDetailFormat.bytes(task.downloaded),
                                    SongDetailFormat.bytes(task.total),
                                )
                                task != null && task.state == DownloadState.Paused ->
                                    stringResource(R.string.downloads_state_paused)
                                task != null && task.state == DownloadState.Failed -> stringResource(R.string.download_failed)
                                cached -> stringResource(R.string.download_done)
                                else -> listOf(song.artist, song.album).filter { it.isNotBlank() }.joinToString(" · ")
                            },
                            fontSize = 12.sp,
                            color = secondary,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                    if (task != null && task.state == DownloadState.Running) {
                        PillButton(label = stringResource(R.string.download_cancel), filled = false) { viewModel.cancelDownload(song.id) }
                    } else if (!cached) {
                        PillButton(label = stringResource(R.string.download), filled = true) { viewModel.download(song.toSong()) }
                    }
                }
            }
        }
        Spacer(Modifier.height(24.dp))
    }
}

/** 分区玻璃卡：与设置页 Section 同一套面（圆角 26 / textPrimary α.10 / 描边 α.16） */
@Composable
private fun SectionCard(title: String, content: @Composable () -> Unit) {
    val palette = LocalCatClawPalette.current
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(bottom = 14.dp)
            .glassCard(palette = palette, corner = 26)
            .padding(16.dp),
    ) {
        Text(
            text = title,
            fontSize = 12.sp,
            fontWeight = FontWeight.Bold,
            letterSpacing = 2.sp,
            color = Color(palette.textSecondary),
        )
        Spacer(Modifier.height(10.dp))
        content()
    }
}

/** 胶囊按钮：filled = 主色实底（深字），否则描边玻璃底 */
@Composable
private fun PillButton(label: String, filled: Boolean, enabled: Boolean = true, onClick: () -> Unit) {
    val palette = LocalCatClawPalette.current
    val alpha = if (enabled) 1f else 0.4f
    Text(
        text = label,
        fontSize = 13.sp,
        fontWeight = FontWeight.SemiBold,
        color = (if (filled) Color(inkOn(palette.primary)) else Color(palette.textSecondary)).copy(alpha = alpha),
        modifier = Modifier
            .clip(RoundedCornerShape(14.dp))
            .background(if (filled) Color(palette.primary).copy(alpha = alpha) else Color.Transparent)
            .then(
                if (filled) Modifier else Modifier.border(1.dp, Color(palette.textPrimary).copy(alpha = 0.16f * alpha), RoundedCornerShape(14.dp)),
            )
            .clickable(enabled = enabled, onClick = onClick)
            .padding(horizontal = 12.dp, vertical = 8.dp),
    )
}

/**
 * WebDAV 目录浏览器 —— 整屏层：自己的返回块 + 当前路径 + 子目录 + 「用这个目录」。
 *
 * 存在的理由：BasePath 打错一个字符，连接测试照样能过（它会自动试空前缀与 /dav），
 * 导库却是 0 首 —— 一个"看起来连上了"的空结果最难查。
 */
@Composable
private fun RemoteBrowserPanel(
    panel: RemoteViewModel.RemoteBrowser,
    onEnter: (RemoteFile) -> Unit,
    onUp: () -> Unit,
    onPick: () -> Unit,
    onClose: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    val here = panel.path.trimEnd('/')
    val atRoot = here.isEmpty() || here == "/"

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 16.dp),
    ) {
        Spacer(Modifier.height(14.dp))
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            BackTile(onBack = onClose, description = stringResource(R.string.GoBack))
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(text = stringResource(R.string.remote_browse_title), fontSize = 22.sp, fontWeight = FontWeight.Bold, color = primary)
                Text(
                    text = panel.path.ifEmpty { "/" },
                    fontSize = 12.sp,
                    color = secondary,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            if (!atRoot) {
                PillLabel(text = stringResource(R.string.remote_browse_up), onClick = onUp, filled = false)
            }
        }
        Spacer(Modifier.height(12.dp))

        when {
            panel.busy -> Text(stringResource(R.string.remote_browse_loading), fontSize = 13.sp, color = secondary)
            panel.error != null -> Text(
                text = stringResource(R.string.remote_browse_failed, panel.error.orEmpty()),
                fontSize = 13.sp,
                color = secondary,
            )

            panel.dirs.isEmpty() -> EmptyState(
                hint = stringResource(R.string.remote_browse_none),
                iconRes = R.drawable.ic_library,
                modifier = Modifier.padding(top = 24.dp),
            )

            else -> panel.dirs.forEach { dir ->
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(vertical = 5.dp)
                        .clip(RoundedCornerShape(18.dp))
                        .clickable { onEnter(dir) }
                        .padding(horizontal = 12.dp, vertical = 12.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    Box(
                        modifier = Modifier
                            .size(38.dp)
                            .clip(RoundedCornerShape(12.dp))
                            .background(Color(palette.primary).copy(alpha = 0.22f)),
                        contentAlignment = Alignment.Center,
                    ) {
                        Text(
                            text = dir.name.take(1),
                            fontSize = 14.sp,
                            fontWeight = FontWeight.Bold,
                            color = primary,
                        )
                    }
                    Text(
                        text = dir.name,
                        fontSize = 15.sp,
                        color = primary,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.weight(1f),
                    )
                    Icon(
                        painter = painterResource(R.drawable.ic_arrow_forward),
                        contentDescription = null,
                        tint = secondary,
                        modifier = Modifier.size(16.dp),
                    )
                }
            }
        }
        Spacer(Modifier.height(14.dp))
        // 确认按钮排在最后：上面那串列表是"走进去看看"，这一颗才是"就定在这层"
        PillLabel(text = stringResource(R.string.remote_browse_pick), onClick = onPick, filled = true)
        Spacer(Modifier.height(120.dp))
    }
}

/** 玻璃输入框：透明容器、无下划线，标签与光标走调色板 */
@Composable
private fun GlassField(value: String, onValue: (String) -> Unit, label: String, modifier: Modifier = Modifier) {
    val palette = LocalCatClawPalette.current
    TextField(
        value = value,
        onValueChange = onValue,
        label = { Text(label, color = Color(palette.textSecondary), fontSize = 13.sp) },
        singleLine = true,
        colors = TextFieldDefaults.colors(
            focusedContainerColor = Color(palette.textPrimary).copy(alpha = 0.08f),
            unfocusedContainerColor = Color(palette.textPrimary).copy(alpha = 0.06f),
            focusedIndicatorColor = Color.Transparent,
            unfocusedIndicatorColor = Color.Transparent,
            focusedTextColor = Color(palette.textPrimary),
            unfocusedTextColor = Color(palette.textPrimary),
            cursorColor = Color(palette.primary),
        ),
        shape = RoundedCornerShape(16.dp),
        modifier = modifier.fillMaxWidth(),
    )
}
