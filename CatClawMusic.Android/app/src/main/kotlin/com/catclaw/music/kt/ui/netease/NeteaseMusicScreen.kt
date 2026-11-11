package com.catclaw.music.kt.ui.netease

import android.content.res.Configuration
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.data.netease.OnlinePlaylist
import com.catclaw.music.kt.data.netease.OnlineSong
import com.catclaw.music.kt.ui.library.NeteaseImportPanel
import com.catclaw.music.kt.ui.library.NeteaseImportViewModel
import com.catclaw.music.kt.ui.player.CoverTile
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.GlassChip
import com.catclaw.music.kt.ui.theme.GlassSearchField
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.glassCard
import com.catclaw.music.kt.ui.theme.isLightPalette
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard
import kotlinx.coroutines.delay

/**
 * 网易云在线音乐页 —— 对应插件 `NeteaseOnlineMusicPage`。
 *
 * 结构：首页（快捷入口 + 推荐歌单 + 我的歌单 + 榜单）→ 歌单/每日推荐/FM 详情 → 搜索 → 登录。
 * 数据与请求都在 [NeteaseMusicViewModel] 里，页面只按状态画。
 *
 * 竖屏优先：顶部固定两行（标题与账号 / 整行搜索框），卡片用方形封面；
 * 横屏保留单行顶栏与 112dp 封面，避免首屏被大卡片吃掉。
 */
@Composable
fun NeteaseMusicScreen(
    onBack: () -> Unit,
    viewModel: NeteaseMusicViewModel = hiltViewModel(),
    importViewModel: NeteaseImportViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    val home by viewModel.home.collectAsStateWithLifecycle()
    val search by viewModel.search.collectAsStateWithLifecycle()
    val detail by viewModel.detail.collectAsStateWithLifecycle()
    val account by viewModel.account.collectAsStateWithLifecycle()
    val myPlaylists by viewModel.myPlaylists.collectAsStateWithLifecycle()
    val loginState by viewModel.login.collectAsStateWithLifecycle()
    val smsCountdown by viewModel.smsCountdown.collectAsStateWithLifecycle()
    val likedIds by viewModel.likedIds.collectAsStateWithLifecycle()
    val toast by viewModel.toast.collectAsStateWithLifecycle()
    var query by remember { mutableStateOf("") }
    var showLogin by remember { mutableStateOf(false) }
    var showImport by remember { mutableStateOf(false) }

    LaunchedEffect(Unit) { viewModel.loadHome() }

    // 详情页在页内切换而不是导航路由：返回键应当先退回列表，再退整页
    BackHandler(enabled = detail != null) { viewModel.closeDetail() }
    BackHandler(enabled = showLogin) { showLogin = false }
    BackHandler(enabled = showImport) { showImport = false }

    Box(modifier = Modifier.fillMaxSize()) {
        val currentDetail = detail
        if (currentDetail != null) {
            NeteasePlaylistDetail(
                detail = currentDetail,
                likedIds = likedIds,
                onBack = viewModel::closeDetail,
                onPlayAll = { viewModel.playAll(currentDetail.songs) },
                onPlayAt = { viewModel.playFrom(currentDetail.songs, it) },
                onLike = { song, like -> viewModel.like(song, like) },
            )
        } else {
            Column(modifier = Modifier.fillMaxSize()) {
                TopBar(
                    query = query,
                    onQueryChange = {
                        query = it
                        viewModel.search(it)
                    },
                    accountName = account?.nickname,
                    onOpenLogin = { showLogin = true },
                    onBack = onBack,
                )

                if (search.query.isNotBlank()) {
                    SearchResults(
                        search = search,
                        likedIds = likedIds,
                        onPlay = { index -> viewModel.playFrom(search.songs, index) },
                        onOpenPlaylist = viewModel::openPlaylist,
                        onLike = viewModel::like,
                    )
                } else {
                    HomeList(
                        home = home,
                        myPlaylists = myPlaylists,
                        loggedIn = account != null,
                        onOpenPlaylist = viewModel::openPlaylist,
                        onDaily = viewModel::openDaily,
                        onPrivateFm = viewModel::openPrivateFm,
                        onLiked = viewModel::openLiked,
                        onImport = { showImport = true },
                        onReload = viewModel::loadHome,
                    )
                }
            }
        }

        if (showImport) {
            val importState by importViewModel.state.collectAsStateWithLifecycle()
            NeteaseImportPanel(
                state = importState,
                onStart = importViewModel::start,
                onReset = importViewModel::reset,
                onDismiss = { showImport = false },
            )
        }

        if (showLogin) {
            LoginOverlay(
                accountName = account?.nickname,
                state = loginState,
                countdown = smsCountdown,
                onSendSms = { phone, country -> viewModel.sendSmsCode(phone, country) },
                onLoginSms = { phone, captcha, country ->
                    viewModel.loginWithSms(phone, captcha, country)
                },
                onLoginPassword = { accountName2, password, country ->
                    viewModel.loginWithPassword(accountName2, password, country)
                },
                onLogout = {
                    viewModel.logout()
                    showLogin = false
                },
                onDismiss = {
                    viewModel.resetLoginState()
                    showLogin = false
                },
            )
        }

        toast?.let { message ->
            LaunchedEffect(message) {
                delay(2_500)
                viewModel.consumeToast()
            }
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(bottom = 92.dp)
                    .align(Alignment.BottomCenter),
                contentAlignment = Alignment.Center,
            ) {
                Text(
                    text = message,
                    fontSize = 12.sp,
                    color = Color(palette.textPrimary),
                    modifier = Modifier
                        .glassCard(palette = palette, corner = 999)
                        .padding(horizontal = 16.dp, vertical = 8.dp),
                )
            }
        }
    }
}

// ── 顶栏 ───────────────────────────────────────────────────────

@Composable
private fun TopBar(
    query: String,
    onQueryChange: (String) -> Unit,
    accountName: String?,
    onOpenLogin: () -> Unit,
    onBack: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE

    val accountSlot: @Composable () -> Unit = {
        if (accountName == null) {
            Text(
                text = stringResource(R.string.netease_login),
                fontSize = 11.sp,
                fontWeight = FontWeight.Medium,
                color = Color(highlightOnGlassCard(palette)),
                maxLines = 1,
                modifier = Modifier
                    .clip(RoundedCornerShape(999.dp))
                    .background(Color(palette.textPrimary).copy(alpha = 0.10f))
                    .clickable(onClick = onOpenLogin)
                    .padding(horizontal = 12.dp, vertical = 5.dp),
            )
        } else {
            Text(
                text = accountName,
                fontSize = 11.sp,
                color = secondary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier
                    .clip(RoundedCornerShape(999.dp))
                    .clickable(onClick = onOpenLogin)
                    .padding(horizontal = 8.dp, vertical = 4.dp),
            )
        }
    }

    if (landscape) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 18.dp, end = 18.dp, top = 8.dp, bottom = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
            Text(
                text = stringResource(R.string.netease_music_title),
                fontSize = 22.sp,
                fontWeight = FontWeight.Bold,
                color = primary,
            )
            GlassSearchField(
                query = query,
                onChange = onQueryChange,
                placeholder = stringResource(R.string.netease_search_hint),
                modifier = Modifier.weight(1f),
                compact = true,
            )
            accountSlot()
        }
    } else {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 18.dp, end = 18.dp, top = 6.dp, bottom = 4.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
                Text(
                    text = stringResource(R.string.netease_music_title),
                    fontSize = 22.sp,
                    fontWeight = FontWeight.Bold,
                    color = primary,
                    modifier = Modifier.weight(1f),
                )
                accountSlot()
            }
            GlassSearchField(
                query = query,
                onChange = onQueryChange,
                placeholder = stringResource(R.string.netease_search_hint),
                modifier = Modifier.fillMaxWidth(),
                compact = true,
            )
        }
    }
}

// ── 首页 ───────────────────────────────────────────────────────

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun HomeList(
    home: NeteaseMusicViewModel.Home,
    myPlaylists: List<OnlinePlaylist>,
    loggedIn: Boolean,
    onOpenPlaylist: (OnlinePlaylist) -> Unit,
    onDaily: () -> Unit,
    onPrivateFm: () -> Unit,
    onLiked: () -> Unit,
    onImport: () -> Unit,
    onReload: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE

    LazyColumn(
        modifier = Modifier.fillMaxSize().padding(horizontal = 18.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item {
            FlowRow(
                modifier = Modifier.fillMaxWidth().padding(top = 4.dp),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                // 导入歌单不需要登录（本地匹配 + 在线外链），所以不受 loggedIn 限制
                GlassChip(label = stringResource(R.string.netease_import_chip), selected = false, onClick = onImport)
                if (loggedIn) {
                    GlassChip(label = stringResource(R.string.netease_daily), selected = false, onClick = onDaily)
                    GlassChip(label = stringResource(R.string.netease_private_fm), selected = false, onClick = onPrivateFm)
                    GlassChip(label = stringResource(R.string.netease_liked), selected = false, onClick = onLiked)
                }
            }
        }

        item { SectionTitle(stringResource(R.string.netease_recommend), primary) }
        items(count = home.recommend.chunked(2).size, key = { "rec-$it" }) { rowIndex ->
            val row = home.recommend.chunked(2)[rowIndex]
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                row.forEach { playlist ->
                    PlaylistCard(
                        playlist = playlist,
                        landscape = landscape,
                        modifier = Modifier.weight(1f),
                        onClick = { onOpenPlaylist(playlist) },
                    )
                }
                if (row.size == 1) Spacer(Modifier.weight(1f))
            }
        }

        if (myPlaylists.isNotEmpty()) {
            item { SectionTitle(stringResource(R.string.netease_my_playlists), primary) }
            items(count = myPlaylists.chunked(2).size, key = { "mine-$it" }) { rowIndex ->
                val row = myPlaylists.chunked(2)[rowIndex]
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    row.forEach { playlist ->
                        PlaylistCard(
                            playlist = playlist,
                            landscape = landscape,
                            modifier = Modifier.weight(1f),
                            onClick = { onOpenPlaylist(playlist) },
                        )
                    }
                    if (row.size == 1) Spacer(Modifier.weight(1f))
                }
            }
        }

        item { SectionTitle(stringResource(R.string.netease_toplists), primary) }
        items(count = home.toplists.size, key = { "top-$it" }) { index ->
            val playlist = home.toplists[index]
            PlaylistRow(playlist = playlist, onClick = { onOpenPlaylist(playlist) })
        }

        if (home.loading) {
            item { HintText(stringResource(R.string.netease_loading), secondary) }
        } else if (home.error) {
            item {
                HintText(
                    text = stringResource(R.string.netease_failed),
                    color = secondary,
                    modifier = Modifier.clickable(onClick = onReload),
                )
            }
        }
        item { Spacer(Modifier.height(96.dp)) }
    }
}

// ── 搜索 ───────────────────────────────────────────────────────

@Composable
private fun SearchResults(
    search: NeteaseMusicViewModel.Search,
    likedIds: Set<String>,
    onPlay: (Int) -> Unit,
    onOpenPlaylist: (OnlinePlaylist) -> Unit,
    onLike: (OnlineSong, Boolean) -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE
    LazyColumn(
        modifier = Modifier.fillMaxSize().padding(horizontal = 18.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        if (search.loading) {
            item { HintText(stringResource(R.string.netease_loading), secondary) }
        }
        if (search.songs.isNotEmpty()) {
            item { SectionTitle(stringResource(R.string.netease_songs_section), primary) }
            itemsIndexed(search.songs, key = { index, song -> "s-$index-${song.id}" }) { index, song ->
                SongRow(
                    song = song,
                    index = index,
                    liked = song.id in likedIds,
                    onClick = { onPlay(index) },
                    onLike = { onLike(song, song.id !in likedIds) },
                )
            }
        }
        if (search.playlists.isNotEmpty()) {
            item { SectionTitle(stringResource(R.string.netease_playlists_section), primary) }
            items(count = search.playlists.chunked(2).size, key = { "sp-$it" }) { rowIndex ->
                val row = search.playlists.chunked(2)[rowIndex]
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    row.forEach { playlist ->
                        PlaylistCard(
                            playlist = playlist,
                            landscape = landscape,
                            modifier = Modifier.weight(1f),
                            onClick = { onOpenPlaylist(playlist) },
                        )
                    }
                    if (row.size == 1) Spacer(Modifier.weight(1f))
                }
            }
        }
        if (!search.loading && search.songs.isEmpty() && search.playlists.isEmpty()) {
            item { HintText(stringResource(R.string.netease_empty), secondary) }
        }
        item { Spacer(Modifier.height(96.dp)) }
    }
}

// ── 详情 ───────────────────────────────────────────────────────

@Composable
private fun NeteasePlaylistDetail(
    detail: NeteaseMusicViewModel.Detail,
    likedIds: Set<String>,
    onBack: () -> Unit,
    onPlayAll: () -> Unit,
    onPlayAt: (Int) -> Unit,
    onLike: (OnlineSong, Boolean) -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    Column(Modifier.fillMaxSize()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 18.dp, end = 18.dp, top = 8.dp, bottom = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
            CoverTile(
                coverArtPath = detail.playlist?.coverUrl,
                audioPath = null,
                maxPx = 160,
                modifier = Modifier
                    .size(52.dp)
                    .clip(RoundedCornerShape(14.dp))
                    .background(Color(palette.primary).copy(alpha = 0.18f)),
            ) {
                Text(
                    text = detail.playlist?.name?.take(1).orEmpty(),
                    color = primary,
                    fontWeight = FontWeight.Bold,
                    fontSize = 18.sp,
                )
            }
            Column(Modifier.weight(1f)) {
                Text(
                    text = detail.playlist?.name.orEmpty(),
                    fontSize = 19.sp,
                    fontWeight = FontWeight.Bold,
                    color = primary,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    text = stringResource(R.string.netease_songs_unit, detail.songs.size),
                    fontSize = 11.sp,
                    color = secondary,
                )
            }
            PillLabel(
                text = stringResource(R.string.netease_play_all),
                onClick = onPlayAll,
                enabled = detail.songs.isNotEmpty(),
            )
        }

        if (detail.loading) {
            HintText(stringResource(R.string.netease_loading), secondary)
        }
        LazyColumn(
            modifier = Modifier.fillMaxSize().padding(horizontal = 18.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            itemsIndexed(detail.songs, key = { index, song -> "d-$index-${song.id}" }) { index, song ->
                SongRow(
                    song = song,
                    index = index,
                    liked = song.id in likedIds,
                    onClick = { onPlayAt(index) },
                    onLike = { onLike(song, song.id !in likedIds) },
                )
            }
            item { Spacer(Modifier.height(96.dp)) }
        }
    }
}

// ── 组件 ───────────────────────────────────────────────────────

@Composable
private fun SongRow(
    song: OnlineSong,
    index: Int,
    liked: Boolean,
    onClick: () -> Unit,
    onLike: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .glassCard(palette = palette, corner = 16)
            // 点一下播放；长按切换红心（在线曲没有"移出歌单"的概念，长按就一件事）
            .combinedClickable(onClick = onClick, onLongClick = onLike)
            .padding(horizontal = 10.dp, vertical = 7.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        CoverTile(
            coverArtPath = song.coverUrl,
            audioPath = null,
            maxPx = 128,
            modifier = Modifier
                .size(40.dp)
                .clip(RoundedCornerShape(12.dp))
                .background(Color(palette.primary).copy(alpha = 0.16f)),
        ) {
            Text(
                text = (index + 1).toString(),
                fontSize = 13.sp,
                fontWeight = FontWeight.Bold,
                color = primary,
            )
        }
        Column(Modifier.weight(1f)) {
            Text(
                text = song.title,
                fontSize = 14.sp,
                fontWeight = FontWeight.Medium,
                color = primary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = listOf(song.artist, song.album).filter { it.isNotBlank() }.joinToString(" · "),
                fontSize = 11.sp,
                color = secondary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        if (liked) {
            Text(text = "❤", fontSize = 12.sp, color = Color(highlightOnGlassCard(palette)))
        }
        if (song.vip) {
            Text(
                text = "VIP",
                fontSize = 9.5.sp,
                color = Color(highlightOnGlassCard(palette)),
                modifier = Modifier
                    .clip(RoundedCornerShape(999.dp))
                    .background(Color(palette.textPrimary).copy(alpha = 0.10f))
                    .padding(horizontal = 6.dp, vertical = 2.dp),
            )
        }
    }
}

@Composable
private fun PlaylistCard(
    playlist: OnlinePlaylist,
    landscape: Boolean,
    modifier: Modifier = Modifier,
    onClick: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    // 横屏两列时卡片本身不高（首屏要塞两行），封面固定 112dp；竖屏每列约 200dp 宽，
    // 同样的 112dp 会把封面裁成横条，改成 1:1 与歌单卡的传统观感一致
    Column(
        modifier = modifier
            .glassCard(palette = palette, corner = 18)
            .clickable(onClick = onClick)
            .padding(8.dp),
    ) {
        CoverTile(
            coverArtPath = playlist.coverUrl,
            audioPath = null,
            maxPx = 320,
            modifier = Modifier
                .fillMaxWidth()
                .then(if (landscape) Modifier.height(112.dp) else Modifier.aspectRatio(1f))
                .clip(RoundedCornerShape(13.dp))
                .background(Color(palette.primary).copy(alpha = 0.18f)),
        ) {
            Text(
                text = playlist.name.take(1),
                fontSize = 26.sp,
                fontWeight = FontWeight.Bold,
                color = primary,
            )
        }
        Spacer(Modifier.height(6.dp))
        Text(
            text = playlist.name,
            fontSize = 12.5.sp,
            color = primary,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        if (playlist.songCount > 0) {
            Text(
                text = stringResource(R.string.netease_songs_unit, playlist.songCount),
                fontSize = 10.sp,
                color = secondary,
            )
        }
    }
}

@Composable
private fun PlaylistRow(playlist: OnlinePlaylist, onClick: () -> Unit) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .glassCard(palette = palette, corner = 16)
            .clickable(onClick = onClick)
            .padding(horizontal = 10.dp, vertical = 7.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        CoverTile(
            coverArtPath = playlist.coverUrl,
            audioPath = null,
            maxPx = 128,
            modifier = Modifier
                .size(44.dp)
                .clip(RoundedCornerShape(12.dp))
                .background(Color(palette.primary).copy(alpha = 0.16f)),
        ) {
            Text(playlist.name.take(1), color = primary, fontWeight = FontWeight.Bold)
        }
        Column(Modifier.weight(1f)) {
            Text(playlist.name, fontSize = 14.sp, color = primary, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(
                text = stringResource(R.string.netease_songs_unit, playlist.songCount),
                fontSize = 11.sp,
                color = secondary,
            )
        }
    }
}

// ── 登录面板 ───────────────────────────────────────────────────

@Composable
private fun LoginOverlay(
    accountName: String?,
    state: NeteaseMusicViewModel.Login,
    countdown: Int,
    onSendSms: (String, String) -> Unit,
    onLoginSms: (String, String, String) -> Unit,
    onLoginPassword: (String, String, String) -> Unit,
    onLogout: () -> Unit,
    onDismiss: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val hint = Color(palette.textSecondary)
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE
    val fade = remember { MutableInteractionSource() }
    var tab by remember { mutableStateOf(0) }
    var phone by remember { mutableStateOf("") }
    var captcha by remember { mutableStateOf("") }
    var account by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    val busy = state is NeteaseMusicViewModel.Login.Busy

    // 面板必须是实心的：glassCard 在纯背景页上没有模糊层，透过去正好是推荐卡片的标题，
    // 真机上两行字叠在一起没法读。深色主题压深、浅色主题压白，都是 94% 不透明。
    val panelBg = if (isLightPalette(palette)) {
        Color.White.copy(alpha = 0.94f)
    } else {
        Color(0xFF13161D).copy(alpha = 0.94f)
    }
    val shape = RoundedCornerShape(28.dp)
    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(Color.Black.copy(alpha = 0.55f))
            .clickable(interactionSource = fade, indication = null, onClick = onDismiss),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            modifier = Modifier
                .width(if (landscape) 520.dp else 340.dp)
                .clip(shape)
                .background(panelBg)
                .border(1.dp, Color(palette.textPrimary).copy(alpha = 0.14f), shape)
                .clickable(interactionSource = fade, indication = null) { }
                .padding(horizontal = 20.dp, vertical = 18.dp),
        ) {
            Text(
                text = stringResource(R.string.netease_login_title),
                fontSize = 19.sp,
                fontWeight = FontWeight.Bold,
                color = ink,
            )
            Spacer(Modifier.height(8.dp))

            if (accountName != null) {
                Text(
                    text = accountName,
                    fontSize = 15.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = Color(highlightOnGlassCard(palette)),
                )
                Spacer(Modifier.height(14.dp))
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.End,
                ) {
                    PillLabel(
                        text = stringResource(R.string.netease_logout),
                        onClick = onLogout,
                        filled = false,
                    )
                }
            } else {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    GlassChip(
                        label = stringResource(R.string.netease_login_tab_phone),
                        selected = tab == 0,
                        onClick = { tab = 0 },
                    )
                    GlassChip(
                        label = stringResource(R.string.netease_login_tab_password),
                        selected = tab == 1,
                        onClick = { tab = 1 },
                    )
                }
                Spacer(Modifier.height(12.dp))

                if (tab == 0) {
                    LoginField(
                        value = phone,
                        onChange = { phone = it },
                        placeholder = stringResource(R.string.netease_phone_hint),
                        keyboardType = KeyboardType.Phone,
                    )
                    Spacer(Modifier.height(8.dp))
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        LoginField(
                            value = captcha,
                            onChange = { captcha = it },
                            placeholder = stringResource(R.string.netease_captcha_hint),
                            keyboardType = KeyboardType.Number,
                            modifier = Modifier.weight(1f),
                        )
                        PillLabel(
                            text = if (countdown > 0) {
                                stringResource(R.string.netease_resend_code, countdown)
                            } else {
                                stringResource(R.string.netease_send_code)
                            },
                            onClick = { onSendSms(phone, "86") },
                            filled = false,
                            enabled = phone.isNotBlank() && countdown == 0 && !busy,
                        )
                    }
                    Spacer(Modifier.height(14.dp))
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.End,
                    ) {
                        PillLabel(
                            text = stringResource(R.string.netease_login_action),
                            onClick = { onLoginSms(phone, captcha, "86") },
                            enabled = phone.isNotBlank() && captcha.isNotBlank() && !busy,
                        )
                    }
                } else {
                    LoginField(
                        value = account,
                        onChange = { account = it },
                        placeholder = stringResource(R.string.netease_account_hint),
                    )
                    Spacer(Modifier.height(8.dp))
                    LoginField(
                        value = password,
                        onChange = { password = it },
                        placeholder = stringResource(R.string.netease_password_hint),
                        keyboardType = KeyboardType.Password,
                        password = true,
                    )
                    Spacer(Modifier.height(14.dp))
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.End,
                    ) {
                        PillLabel(
                            text = stringResource(R.string.netease_login_action),
                            onClick = { onLoginPassword(account, password, "86") },
                            enabled = account.isNotBlank() && password.isNotBlank() && !busy,
                        )
                    }
                }

                val failed = state as? NeteaseMusicViewModel.Login.Failed
                if (failed != null) {
                    Spacer(Modifier.height(8.dp))
                    Text(
                        text = failed.message,
                        fontSize = 11.sp,
                        color = Color(highlightOnGlassCard(palette)),
                    )
                }
            }
        }
    }
}

/** 登录表单的输入框：自绘胶囊，密码走掩码（项目视觉闸门不允许 stock TextField） */
@Composable
private fun LoginField(
    value: String,
    onChange: (String) -> Unit,
    placeholder: String,
    modifier: Modifier = Modifier,
    keyboardType: KeyboardType = KeyboardType.Text,
    password: Boolean = false,
) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val hint = Color(palette.textSecondary)
    BasicTextField(
        value = value,
        onValueChange = onChange,
        singleLine = true,
        textStyle = TextStyle(color = ink, fontSize = 13.sp),
        cursorBrush = SolidColor(Color(palette.primary)),
        visualTransformation = if (password) PasswordVisualTransformation() else VisualTransformation.None,
        keyboardOptions = KeyboardOptions(keyboardType = keyboardType),
        decorationBox = { inner ->
            Box(
                modifier = Modifier
                    .clip(RoundedCornerShape(16.dp))
                    .background(ink.copy(alpha = 0.08f))
                    .padding(horizontal = 12.dp, vertical = 11.dp),
            ) {
                if (value.isEmpty()) Text(placeholder, color = hint, fontSize = 13.sp)
                inner()
            }
        },
        modifier = modifier.fillMaxWidth(),
    )
}

@Composable
private fun SectionTitle(text: String, color: Color) {
    Text(
        text = text,
        fontSize = 15.sp,
        fontWeight = FontWeight.Bold,
        color = color,
        modifier = Modifier.padding(top = 8.dp, bottom = 2.dp),
    )
}

@Composable
private fun HintText(text: String, color: Color, modifier: Modifier = Modifier) {
    Box(modifier = modifier.fillMaxWidth().padding(vertical = 18.dp), contentAlignment = Alignment.Center) {
        Text(text = text, fontSize = 12.5.sp, color = color)
    }
}
