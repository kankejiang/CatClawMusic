package com.catclaw.music.kt.ui.song

import androidx.annotation.DrawableRes
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.slideInVertically
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.ui.player.CoverTile
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard
import com.catclaw.music.kt.ui.theme.inkOn
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.glassCard
import kotlinx.coroutines.launch

/**
 * 菜单要对哪首歌动手：id 用来查收藏与写库，两行文字用来说给用户听，
 * [song] 是给「下一首播放」直接插队列用的（队列要的是 Song，不是 id）。
 */
data class SongMenuTarget(
    val id: Int,
    val title: String,
    val subtitle: String,
    val song: Song,
    /** 是不是「正在播放的这首」：决定要不要摆「下一首播放」（已在播的歌再点它没有意义） */
    val isCurrent: Boolean = false,
)

/** 这首歌「属于谁」——菜单要跳去的那一页；由各页映射成自己的下钻类型 */
enum class SongBelongsKind { Artist, Album }

data class SongBelongs(val kind: SongBelongsKind, val id: Int, val name: String)

/**
 * 菜单里的一行：图标 + 主文案 +（可选）右侧状态 +（可选）下钻箭头。
 *
 * [tileColor] 是图标块的**基色**（不带透明度）：行内按 [TILE_ALPHA] 铺一层淡底、图标本身画白色。
 * 这与设置枢纽的 `HubRow` 同款 —— 那里也是「自己的主题色淡底 + 白图标」，
 * 图标画成彩色在小色块上会掉对比度（浅色主题里尤其明显）。
 *
 * `value` 是这次重新设计的关键：开关类项必须把**当前状态**写在行上。
 * 它们原先是播放页角上的图标按钮，靠「点亮」表达状态；进了菜单没有点亮这回事。
 */
data class SongMenuItem(
    val label: String,
    @DrawableRes val icon: Int? = null,
    val value: String? = null,
    /** 会跳到另一页的项（歌手/专辑/歌曲信息）才画箭头：让人知道点下去会离开当前页 */
    val navigates: Boolean = false,
    val tileColor: Long? = null,
    val onClick: () -> Unit,
)

/** 一组行，带小标题。播放页塞「播放」这一节，列表页不传（那时它只有歌相关的项） */
data class SongMenuSection(val title: String, val items: List<SongMenuItem>)

/**
 * 歌曲菜单 —— MAUI `SongContextMenu` 与播放页「更多」的安卓对应物。
 *
 * **2026-10-18 第二次重做**（真机反馈「太丑」）。第一版是 Material `AlertDialog` + 一列平铺的
 * `PillLabel`：能读，但不像这个应用的东西 —— 方框居中、没有封面、没有分组层次、
 * 也没有别处的「彩色图标块 + 玻璃面」那套语言。
 *
 * 现在的形状是**底部弹层**：遮罩 + 玻璃面板（`glassCard`）+ 抓手 + 封面头 + ✦ 分节 + 彩色图标行。
 * 为什么不是 `ModalBottomSheet`：它自带一套 M3 配色与圆角，和这里的玻璃语言打架；
 * 用 `Dialog(usePlatformDefaultWidth = false)` 自己画，既能浮在页面之上，又只用自己的零件。
 *
 * 三个可选项传 null 就不画：摆一个点了不动的项，比少摆一项更糟。
 */
@Composable
fun SongActionMenu(
    target: SongMenuTarget,
    isFavorite: Boolean,
    playlists: List<Pair<Int, String>>,
    onToggleFavorite: () -> Unit,
    onOpenDetail: () -> Unit,
    onPlayNext: () -> Unit,
    /** 为 null 表示这首歌给不出一个对方真能读到的地址 —— 那就**不画这一项** */
    onShare: (() -> Unit)?,
    onOpenArtist: (() -> Unit)?,
    onOpenAlbum: (() -> Unit)?,
    onAddToPlaylist: (Int) -> Unit,
    /** 只在歌单详情页才有意义：为 null 就不画（音乐库页没有那层关联可删） */
    onRemoveFromPlaylist: (() -> Unit)?,
    /** 播放页专属的那一节（定时停止/音效/桌面歌词/横竖屏）；列表页传空 */
    sections: List<SongMenuSection> = emptyList(),
    onDismiss: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    // 「添加到歌单」开一层：歌单一多，内联一整列会把菜单撑成整屏
    var pickingPlaylist by remember { mutableStateOf(false) }

    if (pickingPlaylist) {
        AddToPlaylistDialog(
            title = stringResource(R.string.song_menu_add_to_playlist),
            playlists = playlists,
            onPick = {
                onAddToPlaylist(it)
                pickingPlaylist = false
                onDismiss()
            },
            onDismiss = { pickingPlaylist = false },
        )
        return
    }

    Dialog(
        onDismissRequest = onDismiss,
        properties = DialogProperties(usePlatformDefaultWidth = false),
    ) {
        // 进场：面板从下方滑入 + 淡入。出场不做 —— 关掉就是关掉，多一帧动画反而慢
        var entered by remember { mutableStateOf(false) }
        LaunchedEffect(Unit) { entered = true }

        Box(
            modifier = Modifier
                .fillMaxSize()
                .background(Color.Black.copy(alpha = 0.42f))
                // 点遮罩关掉。这里要一个「没有涟漪的空动作」来吃掉点击，
                // 否则点在面板上的手势会穿到遮罩、把菜单关掉
                .clickable(
                    interactionSource = remember { MutableInteractionSource() },
                    indication = null,
                    onClick = onDismiss,
                ),
        ) {
            AnimatedVisibility(
                visible = entered,
                enter = slideInVertically(tween(180)) { it / 4 } + fadeIn(tween(140)),
                modifier = Modifier.align(Alignment.BottomCenter),
            ) {
                // 面板高度必须由**窗口剩余高度**倒推，不能只靠滚动区自己 `heightIn`：
                // 只夹滚动区时（真机 2026-10-18）整个面板会高过屏幕，底部那几行够不到。
                // 这里用 BoxWithConstraints 拿到可用高度 → 面板整体限高 → 滚动区用 weight 吃剩下的。
                BoxWithConstraints(modifier = Modifier.fillMaxWidth()) {
                    val panelCap = maxHeight - 20.dp
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(horizontal = 10.dp, vertical = 12.dp)
                            .heightIn(max = panelCap)
                            .glassCard(palette = palette, corner = 28)
                        // 同上：吃掉面板上的点击，别穿透到遮罩
                        .clickable(
                            interactionSource = remember { MutableInteractionSource() },
                            indication = null,
                            onClick = {},
                        )
                            .padding(top = 8.dp, bottom = 8.dp),
                        // 抓手要居中：Column 默认是 Start，不写这一句那条小线会贴在**左边**
                        //（2026-10-20 用户圈出来的就是这里）。子行都是 fillMaxWidth，不受影响。
                        horizontalAlignment = Alignment.CenterHorizontally,
                    ) {
                        PanelGrabber()
                        SongHeader(target = target, onClose = onDismiss)
                        // 快捷区：播着歌最常做的三件事。旧版把它们混在 11 行平铺里，没有主次
                        QuickActions(
                            isFavorite = isFavorite,
                            onToggleFavorite = { onToggleFavorite(); onDismiss() },
                            onSongInfo = { onOpenDetail(); onDismiss() },
                            onShare = onShare?.let { share -> { share(); onDismiss() } },
                        )
                        Column(
                            modifier = Modifier
                                .fillMaxWidth()
                                // fill=false：内容不足时不吃满，免得面板被撑出一大块空白；
                                // 超出时剩下的高度归它、由 verticalScroll 接管
                                .weight(1f, fill = false)
                                .verticalScroll(rememberScrollState()),
                        ) {
                        MenuSection(stringResource(R.string.song_menu_section_song)) {
                            // 「下一首播放」只在**不是当前播放的这首**时出现：歌已经在播了，
                            // 再让「下一首播它」没有意义（2026-10-20 用户指出的设计错误）。
                            // 队列/歌单/搜索里点开别的歌时 isCurrent=false，这一项照常出现。
                            if (!target.isCurrent) {
                                MenuRow(
                                    SongMenuItem(
                                        label = stringResource(R.string.song_menu_play_next),
                                        icon = R.drawable.ic_play,
                                        onClick = { onPlayNext(); onDismiss() },
                                    ),
                                )
                            }
                            MenuRow(
                                SongMenuItem(
                                    label = stringResource(R.string.song_menu_add_to_playlist),
                                    icon = R.drawable.ic_playlist,
                                    // 一个歌单都没有时不写值：那一层弹窗里已经写了要去哪儿建
                                    value = playlists.takeIf { it.isNotEmpty() }?.size?.toString(),
                                    tileColor = palette.primary,
                                    onClick = { pickingPlaylist = true },
                                ),
                            )
                        }

                        sections.forEach { section ->
                            MenuSection(section.title) {
                                section.items.forEach { MenuRow(it) }
                            }
                        }

                        MenuSection(stringResource(R.string.song_menu_section_about)) {
                            onOpenArtist?.let { open ->
                                MenuRow(
                                    SongMenuItem(
                                        label = stringResource(R.string.song_menu_open_artist),
                                        icon = R.drawable.ic_artist,
                                        navigates = true,
                                        tileColor = palette.primary,
                                        onClick = { open(); onDismiss() },
                                    ),
                                )
                            }
                            onOpenAlbum?.let { open ->
                                MenuRow(
                                    SongMenuItem(
                                        label = stringResource(R.string.song_menu_open_album),
                                        icon = R.drawable.ic_album,
                                        navigates = true,
                                        tileColor = palette.primary,
                                        onClick = { open(); onDismiss() },
                                    ),
                                )
                            }
                            // 移除排在最后：它是这一堆里唯一会少东西的一项
                            onRemoveFromPlaylist?.let { remove ->
                                MenuRow(
                                    SongMenuItem(
                                        label = stringResource(R.string.song_menu_remove_from_playlist),
                                        icon = R.drawable.ic_close,
                                        tileColor = palette.primaryDark,
                                        onClick = { remove(); onDismiss() },
                                    ),
                                )
                            }
                        }
                        Spacer(Modifier.height(4.dp))
                    }
                }
            }
        }
    }
}
}

/**
 * 快捷区：播着歌最常做的三件事 —— 收藏 / 歌曲信息 / 分享。
 *
 * 旧版把它们混在 11 行平铺的列表里，和「横竖屏切换」长得一模一样，没有主次；
 * 现在是三颗大胶囊：收藏态用主题色描边 + 淡粉底 + 实心心，一眼可见。
 * 「下一首播放」**不在**这里：正在播放的歌再点它没有意义（2026-10-20 用户指出的设计错误）。
 */
@Composable
private fun QuickActions(
    isFavorite: Boolean,
    onToggleFavorite: () -> Unit,
    onSongInfo: () -> Unit,
    onShare: (() -> Unit)?,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(start = 16.dp, end = 16.dp, bottom = 12.dp),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        QuickPill(
            label = stringResource(if (isFavorite) R.string.favorite_remove else R.string.favorite_add),
            icon = if (isFavorite) R.drawable.ic_favorite else R.drawable.ic_favorite_border,
            on = isFavorite,
            onClick = onToggleFavorite,
            modifier = Modifier.weight(1f),
        )
        QuickPill(
            label = stringResource(R.string.song_menu_detail),
            icon = R.drawable.ic_info,
            onClick = onSongInfo,
            modifier = Modifier.weight(1f),
        )
        if (onShare != null) {
            QuickPill(
                label = stringResource(R.string.song_menu_share),
                icon = R.drawable.ic_share,
                onClick = onShare,
                modifier = Modifier.weight(1f),
            )
        }
    }
}

/** 一颗快捷胶囊：图标在上、文字在下；[on] 为真时整颗转成主题色（收藏态） */
@Composable
private fun QuickPill(
    label: String,
    @DrawableRes icon: Int,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    on: Boolean = false,
) {
    val palette = LocalCatClawPalette.current
    // 收藏态：**实心主题色**底 + inkOn 字色。项目视觉判据里「粉面压白字」只有 1.29–2.13:1，
    // 所以强调块上的前景一律走 inkOn（与播放页那颗收藏按钮同一套）
    val onInk = Color(inkOn(palette.primary))
    Column(
        modifier = modifier
            .height(58.dp)
            .clip(RoundedCornerShape(15.dp))
            .background(
                if (on) Color(palette.primary)
                else Color(palette.textPrimary).copy(alpha = 0.07f),
            )
            .clickable(onClick = onClick),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
        Icon(
            painter = painterResource(icon),
            contentDescription = null,
            tint = if (on) onInk else Color(palette.textPrimary).copy(alpha = 0.82f),
            modifier = Modifier.size(19.dp),
        )
        Spacer(Modifier.height(5.dp))
        Text(
            text = label,
            fontSize = 11.5.sp,
            fontWeight = FontWeight.Medium,
            color = if (on) onInk else Color(palette.textPrimary),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
    }
}

/** 头部那枚「正在播放」徽标：小粉点 + 文案 */
@Composable
private fun CurrentPlayingBadge() {
    val palette = LocalCatClawPalette.current
    // 主色当字色必须过判据（浅色主题 primary 压玻璃卡只有 2.28:1）：
    // 文案与圆点都走 highlightOnGlassCard，底色用它的低透明度
    val highlight = Color(highlightOnGlassCard(palette))
    Row(
        modifier = Modifier
            .clip(CircleShape)
            .background(highlight.copy(alpha = 0.14f))
            .padding(horizontal = 7.dp, vertical = 2.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        Box(Modifier.size(4.dp).clip(CircleShape).background(highlight))
        Text(
            text = stringResource(R.string.PageTitleNowPlaying),
            fontSize = 10.sp,
            fontWeight = FontWeight.SemiBold,
            color = highlight,
        )
    }
}

/** 面板顶上的小抓手：告诉用户这是个可以往下甩掉的弹层 */
@Composable
private fun PanelGrabber() {
    val palette = LocalCatClawPalette.current
    Box(
        modifier = Modifier
            .padding(top = 4.dp, bottom = 10.dp)
            .width(38.dp)
            .height(4.dp)
            .clip(CircleShape)
            .background(Color(palette.textSecondary).copy(alpha = 0.35f)),
    )
}

/** 面板头：封面 + 歌名/歌手 + 关闭。封面沿用列表那套（无图时用首字占位） */
@Composable
private fun SongHeader(target: SongMenuTarget, onClose: () -> Unit) {
    val palette = LocalCatClawPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(start = 16.dp, end = 10.dp, bottom = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        CoverTile(
            coverArtPath = target.song.coverArtPath,
            audioPath = target.song.filePath,
            maxPx = 128,
            modifier = Modifier
                .size(56.dp)
                .clip(RoundedCornerShape(14.dp)),
        ) {
            Box(
                modifier = Modifier
                    .size(56.dp)
                    .clip(RoundedCornerShape(14.dp))
                    .background(Color(palette.primary).copy(alpha = TILE_ALPHA)),
                contentAlignment = Alignment.Center,
            ) {
                Text(
                    text = target.title.take(1),
                    fontSize = 20.sp,
                    fontWeight = FontWeight.Bold,
                    color = Color(palette.textPrimary),
                )
            }
        }
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Text(
                    text = target.title,
                    fontSize = 16.5.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = Color(palette.textPrimary),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.weight(1f, fill = false),
                )
                // 正在播放的这首加一枚徽标：不读文字也知道「这首就是现在放的」
                if (target.isCurrent) CurrentPlayingBadge()
            }
            Text(
                text = target.subtitle,
                fontSize = 12.sp,
                color = Color(palette.textSecondary),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        Box(
            modifier = Modifier
                .size(34.dp)
                .clip(CircleShape)
                .clickable(onClick = onClose),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                painter = painterResource(R.drawable.ic_close),
                contentDescription = stringResource(R.string.song_menu_close),
                tint = Color(palette.textSecondary),
                modifier = Modifier.size(16.dp),
            )
        }
    }
}

/** 分组小标题：与设置页那个 ✦ 小标同款（11sp Bold、字距 2） */
@Composable
private fun MenuSection(title: String, content: @Composable () -> Unit) {
    val palette = LocalCatClawPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(start = 16.dp, top = 8.dp, bottom = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Text(text = "✦", fontSize = 11.sp, color = Color(palette.primary))
        Text(
            text = title,
            fontSize = 11.sp,
            fontWeight = FontWeight.Bold,
            letterSpacing = 2.sp,
            color = Color(palette.textSecondary),
        )
    }
    content()
}

/** 菜单的一行：彩色图标块 + 文案 + 状态 +（可选的）下钻箭头 */
@Composable
private fun MenuRow(item: SongMenuItem) {
    val palette = LocalCatClawPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = item.onClick)
            .padding(horizontal = 16.dp, vertical = 7.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(13.dp),
    ) {
        if (item.icon != null) {
            Box(
                modifier = Modifier
                    .size(30.dp)
                    // 图标块改**中性**：旧版每行一块高饱和色，11 种颜色互相抢注意力。
                    // 现在全菜单只留一处强调色（粉），其余交给灰白层次（2026-10-20 设计定稿）
                    .clip(RoundedCornerShape(9.dp))
                    .background(Color(palette.textPrimary).copy(alpha = 0.07f)),
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    painter = painterResource(item.icon),
                    contentDescription = null,
                    tint = Color(palette.textPrimary).copy(alpha = 0.82f),
                    modifier = Modifier.size(17.dp),
                )
            }
        }
        Text(
            text = item.label,
            fontSize = 15.sp,
            color = Color(palette.textPrimary),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        item.value?.let {
            Text(text = it, fontSize = 12.sp, color = Color(palette.textSecondary), maxLines = 1)
        }
        if (item.navigates) {
            Icon(
                painter = painterResource(R.drawable.ic_arrow_forward),
                contentDescription = null,
                tint = Color(palette.textPrimary).copy(alpha = 0.4f),
                modifier = Modifier.size(15.dp),
            )
        }
    }
}

/** 图标块的淡底强度（与设置枢纽 `HubRow` 各处传进来的 12.5% 同量级） */
private const val TILE_ALPHA = 0.16f

/**
 * 「手里已经有一首歌 → 弹出该给的菜单」这一段，列表页和播放页都要用，所以连带的两处判断一起收在这儿：
 * - 分享：这一刻判一次能不能给出对方真能读到的地址，判不出就不画这一项；
 * - 歌手/专辑：行里只有名字没有 id，点下去才现读一次详情行（为这两项给每条列表 SQL 加两列不划算）。
 *
 * 解析跑在 [SongMenuActions.scope]（ViewModel 的 scope）而不是这里 `rememberCoroutineScope()`：
 * 菜单项点完立刻 dismiss，菜单自己的 scope 当场被取消，真机上表现成「点了没反应」。
 */
@Composable
fun SongMenuFor(
    target: SongMenuTarget,
    menu: SongMenuActions,
    sections: List<SongMenuSection> = emptyList(),
    onDismiss: () -> Unit,
) {
    val context = LocalContext.current
    val shareUri = shareUriFor(context, target.song.filePath)
    val belongs = menu.onOpenBelongs

    fun openBelongs(kind: SongBelongsKind, name: String) {
        if (belongs == null) return
        menu.scope.launch {
            val detail = menu.loadDetail(target.id) ?: return@launch
            val id = if (kind == SongBelongsKind.Artist) detail.artistId else detail.albumId
            if (id > 0) belongs(SongBelongs(kind, id, name))
        }
    }

    SongActionMenu(
        target = target,
        isFavorite = target.id in menu.favoriteIds,
        playlists = menu.playlists,
        onToggleFavorite = { menu.onToggleFavorite(target.id) },
        onAddToPlaylist = { playlistId -> menu.onAddToPlaylist(playlistId, target.id) },
        onPlayNext = { menu.onPlayNext(target.song) },
        onShare = if (shareUri == null) {
            null
        } else {
            { openShareSheet(context, shareUri, target.title, target.song.artist) }
        },
        onOpenArtist = if (belongs != null && target.song.artist.isNotBlank()) {
            { openBelongs(SongBelongsKind.Artist, target.song.artist) }
        } else {
            null
        },
        onOpenAlbum = if (belongs != null && target.song.album.isNotBlank()) {
            { openBelongs(SongBelongsKind.Album, target.song.album) }
        } else {
            null
        },
        onOpenDetail = { menu.onOpenDetail(target.id) },
        onRemoveFromPlaylist = menu.onRemoveFromPlaylist?.let { remove -> { remove(target.id) } },
        sections = sections,
        onDismiss = onDismiss,
    )
}

/**
 * 歌单选择弹窗（二级）。与音乐库页共用 —— 原来它私有在 `LibraryScreen` 里，
 * 两处都要用之后提到这里（两份迟早分叉，比如一边加了「新建歌单」）。
 */
@Composable
fun AddToPlaylistDialog(
    title: String,
    playlists: List<Pair<Int, String>>,
    onPick: (Int) -> Unit,
    onDismiss: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = Color(palette.backgroundStops.last().argb),
        title = { Text(text = title, color = Color(palette.textPrimary)) },
        text = {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .heightIn(max = 360.dp)
                    .verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(6.dp),
            ) {
                if (playlists.isEmpty()) {
                    Text(
                        text = stringResource(R.string.song_menu_no_playlist),
                        style = MaterialTheme.typography.bodySmall,
                        color = Color(palette.textSecondary),
                    )
                } else {
                    playlists.forEach { (id, name) ->
                        MenuRow(
                            SongMenuItem(
                                label = name,
                                icon = R.drawable.ic_playlist,
                                tileColor = palette.primary,
                                onClick = { onPick(id) },
                            ),
                        )
                    }
                }
            }
        },
        confirmButton = {
            PillLabel(text = stringResource(R.string.song_menu_close), onClick = onDismiss, filled = false)
        },
    )
}

/** 菜单第二行：歌手 · 专辑，缺一段就少一段，不留「· 」这种断口 */
internal fun songMenuSubtitle(artist: String, album: String): String =
    listOf(artist, album).filter { it.isNotBlank() }.joinToString(" · ")
