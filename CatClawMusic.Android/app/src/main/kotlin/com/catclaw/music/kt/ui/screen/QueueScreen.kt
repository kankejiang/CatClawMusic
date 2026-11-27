package com.catclaw.music.kt.ui.screen

import android.content.res.Configuration
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.slideInVertically
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGesturesAfterLongPress
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.zIndex
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.ui.playback.PlayingViewModel
import com.catclaw.music.kt.ui.player.Icon
import com.catclaw.music.kt.ui.player.PlayerIcon
import com.catclaw.music.kt.ui.player.PlayerIcons
import com.catclaw.music.kt.ui.player.rememberCover
import com.catclaw.music.kt.ui.theme.EmptyState
import com.catclaw.music.kt.ui.theme.HeroScrimDeepArgb
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.glassCard
import com.catclaw.music.kt.ui.theme.highlightOnSelectedCard
import com.catclaw.music.kt.ui.theme.liquidGlass

/** 清空队列的垃圾桶图标（`PlayerIcons` 里没有删除项，这里就地注册一枚） */
private val TrashIcon = PlayerIcon(R.drawable.ic_delete)

private const val TabQueue = 0
private const val TabHistory = 1

/** 行高 60dp + 行距 4dp：拖动时以此为一步换算「挪过一行」 */
private val RowPitch = 64.dp

/**
 * 播放列表 —— 底部弹窗形态（版式参考网易云队列面板），表面走本 App 的半透明液态玻璃
 * （[liquidGlass]，与迷你播放器 / 底部导航栏同一套材质）。
 *
 * 三个 tab / 手势行为都是真实现，不是装饰：
 * 1. 「当前播放」：点行 = 播它然后关弹窗；× = 移出队列；右侧 ≡ 长按拖动 = 调整顺序
 *    （[PlayingViewModel.moveAt] → `PlayerController.moveInQueue`，只重排 timeline 不打断当前曲）；
 * 2. 「历史播放」：读真实 `PlayHistory`（`LibraryDao.observeRecentSongs`），点行 = 用它作为新队列起播；
 * 3. 垃圾桶先弹确认框再清空；关闭弹窗靠点遮罩 / 返回键，不再单独放 × 按钮。
 *
 * 弹窗必须由外壳在 `backdropSource` 录制层**之外**渲染：`liquidGlass` 需要采样身后的播放页，
 * 落在录制层内部会采样到自己（RenderThread 自采样崩溃）。`CatClawRoot` 里它正是那层的兄弟节点。
 */
@Composable
fun QueueScreen(
    viewModel: PlayingViewModel,
    onBack: () -> Unit,
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val songs by viewModel.queueSongs.collectAsStateWithLifecycle()
    val historySongs by viewModel.historySongs.collectAsStateWithLifecycle()
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE
    val fade = remember { MutableInteractionSource() }

    var entered by remember { mutableStateOf(false) }
    LaunchedEffect(Unit) { entered = true }

    var tab by remember { mutableStateOf(TabQueue) }
    var draggingId by remember { mutableStateOf<Int?>(null) }
    var dragOffset by remember { mutableFloatStateOf(0f) }
    // 清空是破坏性操作，先弹确认；遮罩点击 / 返回键只关闭弹窗，不该顺手清队列
    var confirmClear by remember { mutableStateOf(false) }
    // 手势协程生命周期比一次组合长：直接捕获 songs 会停在长按那一刻，读 rememberUpdatedState 才拿得到最新顺序
    val liveSongs by rememberUpdatedState(songs)
    val rowPitchPx = with(LocalDensity.current) { RowPitch.toPx() }

    // 队列里允许出现同一首歌两次，纯 id 做 key 会重复；按「id + 第几次出现」保证唯一，
    // 同时没有重复曲时 key 不随排序变化，拖动中的行不会被 LazyColumn 当成新节点重建。
    val queueKeys = remember(songs) { occurrenceKeys(songs, "q") }
    val historyKeys = remember(historySongs) { occurrenceKeys(historySongs, "h") }

    Box(
        modifier = Modifier
            .fillMaxSize()
            // 遮罩：压暗身后的播放页，点击空白处关闭
            .background(Color.Black.copy(alpha = 0.34f))
            .clickable(interactionSource = fade, indication = null, onClick = onBack),
    ) {
        AnimatedVisibility(
            visible = entered,
            enter = slideInVertically(tween(240)) { it } + fadeIn(tween(160)),
            modifier = Modifier.align(Alignment.BottomCenter),
        ) {
            BoxWithConstraints(modifier = Modifier.fillMaxSize()) {
                // 竖屏留出约 1/5 屏给身后的播放页；横屏收得更多，面板顶部不压到状态栏（时钟）
                val panelCap = if (landscape) maxHeight * 0.88f else maxHeight * 0.82f
                Column(
                    modifier = Modifier
                        .align(Alignment.BottomCenter)
                        .fillMaxWidth()
                        .navigationBarsPadding()
                        .padding(start = 12.dp, end = 12.dp, bottom = 10.dp)
                        .heightIn(max = panelCap)
                        // 液态玻璃：宿主是同一窗口里的 CatClawRoot.backdropSource，能真正采样背后的播放页
                        .liquidGlass(palette = palette, corner = 30)
                        // 吃掉面板上的点击，别让它们穿透到遮罩
                        .clickable(interactionSource = fade, indication = null) {}
                        .padding(top = 6.dp, bottom = 8.dp),
                    horizontalAlignment = Alignment.CenterHorizontally,
                ) {
                    // 顶部拖拽条
                    Box(
                        modifier = Modifier
                            .padding(top = 2.dp, bottom = 4.dp)
                            .size(width = 34.dp, height = 4.dp)
                            .clip(RoundedCornerShape(999.dp))
                            .background(primary.copy(alpha = 0.22f)),
                    )
                    // 头部：当前播放 / 历史播放 + 右侧清空、关闭
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(start = 20.dp, end = 10.dp, top = 4.dp, bottom = 6.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        QueueTabLabel(
                            text = stringResource(R.string.queue_current),
                            count = songs.size,
                            selected = tab == TabQueue,
                            primary = primary,
                            secondary = secondary,
                            onClick = {
                                tab = TabQueue
                                draggingId = null
                                dragOffset = 0f
                            },
                        )
                        Spacer(Modifier.width(18.dp))
                        QueueTabLabel(
                            text = stringResource(R.string.queue_history),
                            count = null,
                            selected = tab == TabHistory,
                            primary = primary,
                            secondary = secondary,
                            onClick = {
                                tab = TabHistory
                                draggingId = null
                                dragOffset = 0f
                            },
                        )
                        Spacer(Modifier.weight(1f))
                        if (tab == TabQueue) {
                            SheetIconAction(
                                icon = TrashIcon,
                                description = stringResource(R.string.queue_clear),
                                tint = secondary,
                                onClick = { confirmClear = true },
                            )
                        }
                    }

                    val isHistory = tab == TabHistory
                    val list = if (isHistory) historySongs else songs
                    LazyColumn(
                        // 空队列不占满整块玻璃：不给 weight，列表按内容收缩，弹窗跟着变矮
                        modifier = if (list.isEmpty()) Modifier.fillMaxWidth() else Modifier.fillMaxWidth().weight(1f),
                        contentPadding = PaddingValues(horizontal = 12.dp, vertical = 2.dp),
                        verticalArrangement = Arrangement.spacedBy(4.dp),
                    ) {
                        if (list.isEmpty()) {
                            item(key = if (isHistory) "history_empty" else "queue_empty") {
                                EmptyState(
                                    hint = stringResource(
                                        if (isHistory) R.string.queue_history_empty else R.string.queue_empty,
                                    ),
                                    iconRes = if (isHistory) R.drawable.ic_history else R.drawable.ic_playlist,
                                )
                            }
                        }
                        if (isHistory) {
                            itemsIndexed(
                                items = historySongs,
                                key = { index, _ -> historyKeys.getOrElse(index) { "h-$index" } },
                            ) { index, song ->
                                QueueSongRow(
                                    song = song,
                                    index = index,
                                    isCurrent = song.id == state.currentSong?.id,
                                    onClick = {
                                        viewModel.playSongs(historySongs, index)
                                        onBack()
                                    },
                                )
                            }
                        } else {
                            itemsIndexed(
                                items = songs,
                                key = { index, _ -> queueKeys.getOrElse(index) { "q-$index" } },
                            ) { index, song ->
                                val isDragging = draggingId == song.id
                                QueueSongRow(
                                    song = song,
                                    index = index,
                                    isCurrent = song.id == state.currentSong?.id,
                                    modifier = Modifier
                                        .zIndex(if (isDragging) 1f else 0f)
                                        .graphicsLayer {
                                            translationY = if (isDragging) dragOffset else 0f
                                            // 轻微放大，抬起的一行能压住邻居
                                            val s = if (isDragging) 1.02f else 1f
                                            scaleX = s
                                            scaleY = s
                                        },
                                    onClick = {
                                        if (draggingId == null) {
                                            viewModel.playAt(index)
                                            onBack()
                                        }
                                    },
                                ) {
                                    Box(
                                        modifier = Modifier
                                            .size(40.dp)
                                            .clip(RoundedCornerShape(14.dp))
                                            .clickable { viewModel.removeAt(song.id) },
                                        contentAlignment = Alignment.Center,
                                    ) {
                                        Icon(
                                            imageVector = PlayerIcons.Close,
                                            contentDescription = stringResource(R.string.queue_remove),
                                            tint = secondary,
                                            modifier = Modifier.size(16.dp),
                                        )
                                    }
                                    DragHandle(
                                        tint = secondary,
                                        onDragStart = {
                                            draggingId = song.id
                                            dragOffset = 0f
                                        },
                                        onDrag = { dy ->
                                            if (draggingId == song.id) {
                                                dragOffset += dy
                                                val indexNow = liveSongs.indexOfFirst { it.id == song.id }
                                                if (indexNow >= 0) {
                                                    val threshold = rowPitchPx / 2f
                                                    if (dragOffset > threshold && indexNow < liveSongs.lastIndex) {
                                                        viewModel.moveAt(indexNow, indexNow + 1)
                                                        // 挪完一步后行的布局位置也前进了一行，减掉它才不会在手指下跳两行
                                                        dragOffset -= rowPitchPx
                                                    } else if (dragOffset < -threshold && indexNow > 0) {
                                                        viewModel.moveAt(indexNow, indexNow - 1)
                                                        dragOffset += rowPitchPx
                                                    }
                                                }
                                            }
                                        },
                                        onDragEnd = {
                                            draggingId = null
                                            dragOffset = 0f
                                        },
                                    )
                                }
                            }
                        }
                    }

                    // 底部播放模式：真实能力（点一下切到下一档），只属于「当前播放」
                    if (tab == TabQueue && songs.isNotEmpty()) {
                        val modeLabel = stringResource(modeLabelRes(state.mode, state.isFmMode))
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = 12.dp, vertical = 4.dp)
                                .height(52.dp)
                                .glassCard(palette = palette, corner = 18)
                                .clickable { viewModel.cycleMode() },
                            horizontalArrangement = Arrangement.Center,
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Icon(
                                imageVector = modeIcon(state.mode, state.isFmMode),
                                contentDescription = stringResource(R.string.mode_change) + "：" + modeLabel,
                                tint = primary,
                                modifier = Modifier.size(18.dp),
                            )
                            Spacer(Modifier.width(8.dp))
                            Text(text = modeLabel, fontSize = 13.sp, color = primary)
                        }
                    }
                }
            }
        }
    }

    if (confirmClear) {
        AlertDialog(
            onDismissRequest = { confirmClear = false },
            containerColor = Color(palette.backgroundStops.last().argb),
            title = { Text(text = stringResource(R.string.queue_clear_confirm_title), color = primary) },
            text = {
                Text(
                    text = stringResource(R.string.queue_clear_confirm_body),
                    fontSize = 13.sp,
                    color = secondary,
                    lineHeight = 18.sp,
                )
            },
            confirmButton = {
                QueueDialogAction(
                    text = stringResource(R.string.queue_clear_confirm_action),
                    color = Color(0xFFFF6B6B),
                    onClick = {
                        confirmClear = false
                        viewModel.clearQueue()
                    },
                )
            },
            dismissButton = {
                QueueDialogAction(
                    text = stringResource(R.string.queue_cancel),
                    color = secondary,
                    onClick = { confirmClear = false },
                )
            },
        )
    }
}

/** 确认弹窗里的文字按钮（Material 默认按钮色与本 App 配色不一致） */
@Composable
private fun QueueDialogAction(text: String, color: Color, onClick: () -> Unit) {
    Box(
        modifier = Modifier
            .clip(RoundedCornerShape(10.dp))
            .clickable(onClick = onClick)
            .padding(horizontal = 14.dp, vertical = 9.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(text = text, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, color = color)
    }
}
@Composable
private fun QueueTabLabel(
    text: String,
    count: Int?,
    selected: Boolean,
    primary: Color,
    secondary: Color,
    onClick: () -> Unit,
) {
    Row(
        modifier = Modifier
            .clip(RoundedCornerShape(12.dp))
            .clickable(onClick = onClick)
            .padding(horizontal = 2.dp, vertical = 4.dp),
        verticalAlignment = Alignment.Bottom,
    ) {
        Text(
            text = text,
            fontSize = if (selected) 22.sp else 17.sp,
            fontWeight = if (selected) FontWeight.Bold else FontWeight.Medium,
            color = if (selected) primary else secondary.copy(alpha = 0.72f),
        )
        if (count != null && count > 0) {
            Spacer(Modifier.width(3.dp))
            Text(
                text = count.toString(),
                fontSize = 11.sp,
                fontWeight = FontWeight.SemiBold,
                color = if (selected) primary else secondary,
                modifier = Modifier.padding(bottom = 3.dp),
            )
        }
    }
}

/** 一行歌曲：封面 / 播放指示 + 歌名 + 歌手 + 末尾动作（移除、拖动手柄由调用方给） */
@Composable
private fun QueueSongRow(
    song: Song,
    index: Int,
    isCurrent: Boolean,
    modifier: Modifier = Modifier,
    onClick: () -> Unit,
    trailing: @Composable RowScope.() -> Unit = {},
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    Row(
        modifier = modifier
            .fillMaxWidth()
            .height(60.dp)
            .clip(RoundedCornerShape(16.dp))
            .background(
                if (isCurrent) Color(palette.primary).copy(alpha = 0.16f)
                else primary.copy(alpha = 0.06f),
            )
            .then(
                if (isCurrent) Modifier.border(1.dp, Color(palette.primary).copy(alpha = 0.45f), RoundedCornerShape(16.dp))
                else Modifier,
            )
            .clickable(onClick = onClick)
            .padding(horizontal = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        val cover = rememberCover(song.coverArtPath, song.filePath, maxPx = 128, songId = song.id)
        Box(
            modifier = Modifier
                .size(38.dp)
                .clip(RoundedCornerShape(11.dp))
                .background(primary.copy(alpha = if (isCurrent) 0.18f else 0.10f)),
            contentAlignment = Alignment.Center,
        ) {
            if (cover != null) {
                Image(
                    bitmap = cover.asImageBitmap(),
                    contentDescription = null,
                    contentScale = ContentScale.Crop,
                    modifier = Modifier.matchParentSize(),
                )
            }
            if (isCurrent) {
                // 「正在播放」这块线索不能因为换了封面就丢掉：压在照片上必须自带垫层，
                // 否则遇到浅色封面就看不出来了
                Box(
                    modifier = Modifier
                        .size(22.dp)
                        .clip(CircleShape)
                        .background(Color(HeroScrimDeepArgb)),
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(
                        imageVector = PlayerIcons.Play,
                        contentDescription = null,
                        tint = Color.White,
                        modifier = Modifier.size(12.dp),
                    )
                }
            } else if (cover == null) {
                Text(text = (index + 1).toString(), fontSize = 13.sp, fontWeight = FontWeight.SemiBold, color = secondary)
            }
        }
        Spacer(Modifier.width(12.dp))
        Column(
            modifier = Modifier.weight(1f),
            verticalArrangement = Arrangement.spacedBy(2.dp),
        ) {
            Text(
                text = song.title,
                fontSize = 14.sp,
                fontWeight = if (isCurrent) FontWeight.Bold else FontWeight.SemiBold,
                color = if (isCurrent) Color(highlightOnSelectedCard(palette)) else primary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = song.artist,
                fontSize = 12.sp,
                color = secondary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        trailing()
    }
}

/** 最右侧的拖动把手（三条横线）；长按后才进入拖动，避免和点行抢手势 */
@Composable
private fun DragHandle(
    tint: Color,
    onDragStart: () -> Unit,
    onDrag: (Float) -> Unit,
    onDragEnd: () -> Unit,
) {
    val label = stringResource(R.string.queue_drag)
    Box(
        modifier = Modifier
            .width(30.dp)
            .height(40.dp)
            .semantics { contentDescription = label }
            .pointerInput(Unit) {
                detectDragGesturesAfterLongPress(
                    onDragStart = { onDragStart() },
                    onDragEnd = { onDragEnd() },
                    onDragCancel = { onDragEnd() },
                    onDrag = { change, amount ->
                        // 消费掉事件，别让列表跟着滚动
                        change.consume()
                        onDrag(amount.y)
                    },
                )
            },
        contentAlignment = Alignment.Center,
    ) {
        Column(verticalArrangement = Arrangement.spacedBy(3.dp)) {
            repeat(3) {
                Box(
                    modifier = Modifier
                        .size(width = 12.dp, height = 1.5.dp)
                        .clip(RoundedCornerShape(1.dp))
                        .background(tint.copy(alpha = 0.8f)),
                )
            }
        }
    }
}

/** 头部的一颗图标按钮：40dp 点击区，图标 18dp */
@Composable
private fun SheetIconAction(
    icon: PlayerIcon,
    description: String,
    tint: Color,
    onClick: () -> Unit,
) {
    Box(
        modifier = Modifier
            .size(40.dp)
            .clip(RoundedCornerShape(14.dp))
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Icon(
            imageVector = icon,
            contentDescription = description,
            tint = tint,
            modifier = Modifier.size(18.dp),
        )
    }
}

/** LazyColumn 的 key：同 id 多次出现时以「第几次」区分，避免 Duplicate key 崩溃 */
private fun occurrenceKeys(songs: List<Song>, prefix: String): List<String> {
    val seen = HashMap<Int, Int>()
    return songs.map { song ->
        val n = (seen[song.id] ?: 0) + 1
        seen[song.id] = n
        "$prefix-${song.id}-$n"
    }
}
