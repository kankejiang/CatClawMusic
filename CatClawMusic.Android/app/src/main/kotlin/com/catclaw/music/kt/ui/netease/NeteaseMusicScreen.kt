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
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.lazy.LazyRow
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
import androidx.compose.ui.draw.alpha
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
import com.catclaw.music.kt.data.netease.NeteaseArtist
import com.catclaw.music.kt.data.netease.NeteasePlaylistClient
import com.catclaw.music.kt.data.netease.OnlinePlaylist
import androidx.compose.runtime.saveable.rememberSaveable
import com.catclaw.music.kt.core.library.SongSortFields
import com.catclaw.music.kt.core.library.SongSortKey
import com.catclaw.music.kt.core.library.defaultDescending
import com.catclaw.music.kt.core.library.filterAndSortSongs
import com.catclaw.music.kt.ui.screen.sortLabelRes
import com.catclaw.music.kt.data.netease.OnlineSong
import com.catclaw.music.kt.ui.song.SongCollectionScreen
import com.catclaw.music.kt.ui.screen.PlaylistDetailHeader
import com.catclaw.music.kt.ui.screen.PlaylistSongRowUi
import com.catclaw.music.kt.ui.screen.PlaylistSongUi
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
    /** 从歌单 tab 的「网易云歌单」货架进来时，直接打开这个歌单 */
    initialPlaylist: OnlinePlaylist? = null,
    onInitialPlaylistConsumed: () -> Unit = {},
    /** true：详情页返回时直接退出整个在线音乐路由（从歌单 tab 深链进来时用） */
    exitOnDetailClose: Boolean = false,
    viewModel: NeteaseMusicViewModel = hiltViewModel(),
    importViewModel: NeteaseImportViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    val home by viewModel.home.collectAsStateWithLifecycle()
    val homeTab by viewModel.homeTab.collectAsStateWithLifecycle()
    val square by viewModel.square.collectAsStateWithLifecycle()
    val topArtists by viewModel.topArtists.collectAsStateWithLifecycle()
    val search by viewModel.search.collectAsStateWithLifecycle()
    val detail by viewModel.detail.collectAsStateWithLifecycle()
    val account by viewModel.account.collectAsStateWithLifecycle()
    val myPlaylists by viewModel.myPlaylists.collectAsStateWithLifecycle()
    val loginState by viewModel.login.collectAsStateWithLifecycle()
    val smsCountdown by viewModel.smsCountdown.collectAsStateWithLifecycle()
    val likedIds by viewModel.likedIds.collectAsStateWithLifecycle()
    val toast by viewModel.toast.collectAsStateWithLifecycle()
    val quality by viewModel.quality.collectAsStateWithLifecycle()
    var query by remember { mutableStateOf("") }
    var showLogin by remember { mutableStateOf(false) }
    var showImport by remember { mutableStateOf(false) }

    LaunchedEffect(Unit) { viewModel.loadHome() }
    LaunchedEffect(initialPlaylist?.id) {
        initialPlaylist?.let {
            viewModel.openPlaylist(it)
            onInitialPlaylistConsumed()
        }
    }

    // 详情页在页内切换而不是导航路由：返回键应当先退回列表，再退整页
    BackHandler(enabled = detail != null) {
        if (exitOnDetailClose) onBack() else viewModel.closeDetail()
    }
    BackHandler(enabled = showLogin) { showLogin = false }
    BackHandler(enabled = showImport) { showImport = false }

    // 详情页的过滤 / 排序状态由 VM 提供（UI 不再自己维护）
    val detailSongs by viewModel.detailDisplaySongs.collectAsStateWithLifecycle()
    val detailQuery by viewModel.detailQuery.collectAsStateWithLifecycle()
    val detailSort by viewModel.detailSort.collectAsStateWithLifecycle()
    val detailDescending by viewModel.detailDescending.collectAsStateWithLifecycle()
    Box(modifier = Modifier.fillMaxSize()) {
        val currentDetail = detail
        if (currentDetail != null) {
            NeteasePlaylistDetail(
                detail = currentDetail,
                likedIds = likedIds,
                onBack = { if (exitOnDetailClose) onBack() else viewModel.closeDetail() },
                onPlayAll = { viewModel.playAll(currentDetail.songs) },
                onPlayAllShuffled = { viewModel.playAll(currentDetail.songs.shuffled()) },
                songs = detailSongs,
                query = detailQuery,
                sort = detailSort,
                descending = detailDescending,
                onQuery = viewModel::setDetailQuery,
                onSort = viewModel::toggleDetailSort,
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
                    quality = quality,
                    onCycleQuality = viewModel::cycleQuality,
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
                        onLoadMore = viewModel::loadMoreSearch,
                    )
                } else {
                    Column(modifier = Modifier.fillMaxSize()) {
                        HomeTabsBar(selected = homeTab, onSelect = viewModel::selectHomeTab)
                        when (homeTab) {
                            // ① 歌单广场：官方分类 + 2 列网格 + 分页
                            1 -> SquareList(
                                square = square,
                                onSelectCategory = { viewModel.loadSquare(it) },
                                onOpenPlaylist = viewModel::openPlaylist,
                                onLoadMore = viewModel::loadMoreSquare,
                            )
                            // ② 排行榜：全量榜单卡片折行
                            2 -> RankGrid(
                                toplists = home.toplists,
                                onOpenPlaylist = viewModel::openPlaylist,
                            )
                            // ③ 歌手：热门歌手网格
                            3 -> ArtistGrid(artists = topArtists)
                            // ④ 精选：原有推荐流
                            else -> HomeList(
                                home = home,
                                myPlaylists = myPlaylists,
                                loggedIn = account != null,
                                onOpenPlaylist = viewModel::openPlaylist,
                                onDaily = viewModel::openDaily,
                                onPrivateFm = viewModel::playPrivateFm,
                                onLiked = viewModel::openLiked,
                                onImport = { showImport = true },
                                onReload = viewModel::loadHome,
                            )
                        }
                    }
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
