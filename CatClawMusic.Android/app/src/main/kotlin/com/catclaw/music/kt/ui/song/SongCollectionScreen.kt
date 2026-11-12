package com.catclaw.music.kt.ui.song

import android.content.res.Configuration
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.screen.PlaylistDetailHeader
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.catclaw.music.kt.R
import com.catclaw.music.kt.ui.player.CoverTile
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.GlassChip
import com.catclaw.music.kt.ui.theme.GlassSearchField
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.inkOn

/** 二级页头部的一颗动作（播放全部 / 随机播放 / 加入…） */
data class CollectionAction(
    val label: String,
    val onClick: () -> Unit,
    /** 主按钮走实心品牌面，其余是玻璃描边胶囊（横竖屏同一套 TextAction） */
    val primary: Boolean = false,
    /**
     * 预留：横屏顶栏目前走 10.5sp 文字胶囊（208dp 装不下图标 + 文字），
     * 图标资源先留着，等以后信息列加宽再启用。
     */
    val iconRes: Int? = null,
)

/** 筛选或排序档。横屏会把所有组合并进同一条横滚轨道 */
data class CollectionChip(
    val label: String,
    val selected: Boolean = false,
    val onClick: () -> Unit,
)

data class CollectionSearch(
    val query: String,
    val hint: String,
    val onChange: (String) -> Unit,
)

/** 可选头图：歌单 / 专辑二级页有，艺人页与统计列表页没有 */
data class CollectionCover(
    val coverArtPath: String?,
    val audioPath: String?,
    val fallback: String,
)

/**
 * 通用的「歌单二级页」骨架：返回 + 标题/计数（+ 头图）+ 播放动作 + 筛选/排序 + 搜索 + 列表。
 *
 * 此前这套骨架在四个地方各写了一遍：音乐库统计列表页、专辑/艺人详情、歌单详情、
 * 收藏/最近播放入口页；横屏 411dp 的画布被「返回行 + 动作行 + chips 两行 + 搜索框 +
 * 列表」堆满，歌曲列表只剩一条缝（2026-11-09 真机截图）。这里把布局收成一份：
 *
 * - 竖屏：头部 → 动作行 → 每组 chips 一行 → 搜索框 → 列表，与原来逐页堆的顺序一致；
 * - 横屏：头部一行（把动作收进标题行右侧）→ 搜索与所有 chips 合并成一行 → 列表。
 *
 * 数据、菜单、播放路径全部由调用方注入，组件只负责版式 —— 所以音乐库用 LibraryViewModel、
 * 歌单页用 PlaylistViewModel 也能共用同一份骨架。
 */
@Composable
fun SongCollectionScreen(
    title: String,
    countText: String,
    onBack: () -> Unit,
    modifier: Modifier = Modifier,
    subtitle: String? = null,
    cover: CollectionCover? = null,
    actions: List<CollectionAction> = emptyList(),
    chipGroups: List<List<CollectionChip>> = emptyList(),
    search: CollectionSearch? = null,
    trailing: @Composable () -> Unit = {},
    // 必须是最后一个参数：调用方用 SongCollectionScreen(...) { 列表 } 的尾随 lambda
    content: @Composable () -> Unit,
) {
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE
    if (landscape) {
        CollectionLandscape(
            title, countText, onBack, content, modifier,
            subtitle, cover, actions, chipGroups, search, trailing,
        )
    } else {
        CollectionPortrait(
            title, countText, onBack, content, modifier,
            subtitle, cover, actions, chipGroups, search, trailing,
        )
    }
}

/** 横屏信息列宽度：822dp 可用宽度里切出 240，列表留 582dp（≈ 每行歌名 380dp 可视） */
private val LandscapeInfoWidth = 240.dp

/**
 * 横屏：左信息列 + 右列表（参照 PC 端「导航 / 信息列 / 列表」三栏）。
 *
 * 原来把返回、标题、动作、chips、搜索全部压在顶部两行，列表只能铺满剩下的高度，
 * 首屏只露 4 首（2026-11-09 真机截图）。现在这些控制项全部下沉到 240dp 的信息列，
 * 列表从组件顶部一直铺到迷你条上方，首屏行数翻倍；信息列本身也有了封面这个视觉重心，
 * 左侧不再是一片空玻璃。
 */
@Composable
private fun CollectionLandscape(
    title: String,
    countText: String,
    onBack: () -> Unit,
    content: @Composable () -> Unit,
    modifier: Modifier,
    subtitle: String?,
    cover: CollectionCover?,
    actions: List<CollectionAction>,
    chipGroups: List<List<CollectionChip>>,
    search: CollectionSearch?,
    trailing: @Composable () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    Row(modifier.fillMaxSize()) {
        Column(
            modifier = Modifier
                .width(LandscapeInfoWidth)
                .fillMaxHeight()
                .padding(start = 18.dp, end = 14.dp, top = 4.dp),
            verticalArrangement = Arrangement.spacedBy(5.dp),
        ) {
            // 返回在左，动作靠右（用户嫌贴太近）：返回 + weight + 文字胶囊。
            // 208dp 的账：44 + 8 + 「播放全部」60 + 5 + 「随机」39 = 156；三动作约 197，都放得下。
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(5.dp),
            ) {
                BackTile(onBack = onBack, description = stringResource(R.string.queue_back))
                Spacer(Modifier.weight(1f))
                actions.forEach { action ->
                    TextAction(action, primary, secondary)
                }
                trailing()
            }
            // 标题行：专辑/歌单详情把 52dp 封面放在标题左侧（顶栏塞不下 3 个动作 + 封面）
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                cover?.let { CollectionCoverTile(it, 52.dp) }
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = title,
                        fontSize = 20.sp,
                        fontWeight = FontWeight.Bold,
                        color = primary,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    Text(
                        text = listOfNotNull(countText, subtitle?.takeIf { it.isNotBlank() })
                            .joinToString(" · "),
                        fontSize = 10.5.sp,
                        color = secondary,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
            }
            search?.let {
                GlassSearchField(
                    query = it.query,
                    onChange = it.onChange,
                    placeholder = it.hint,
                    modifier = Modifier.fillMaxWidth(),
                    compact = true,
                )
            }
            // 每组 chips 一行、组内横向滚动：240dp 装不下「标题/艺术家/专辑/时长/入库时间」六档
            chipGroups.filter { it.isNotEmpty() }.forEach { group ->
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .horizontalScroll(rememberScrollState()),
                    horizontalArrangement = Arrangement.spacedBy(6.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    group.forEach { chip ->
                        GlassChip(
                            label = chip.label,
                            selected = chip.selected,
                            onClick = chip.onClick,
                            compact = true,
                        )
                    }
                }
            }
        }
        Box(
            modifier = Modifier
                .weight(1f)
                .fillMaxHeight()
                .padding(top = 4.dp),
        ) { content() }
    }
}

@Composable
private fun CollectionPortrait(
    title: String,
    countText: String,
    onBack: () -> Unit,
    content: @Composable () -> Unit,
    modifier: Modifier,
    subtitle: String?,
    cover: CollectionCover?,
    actions: List<CollectionAction>,
    chipGroups: List<List<CollectionChip>>,
    search: CollectionSearch?,
    trailing: @Composable () -> Unit,
) {
    val primary = Color(LocalCatClawPalette.current.textPrimary)
    val secondary = Color(LocalCatClawPalette.current.textSecondary)
    Column(modifier.fillMaxSize()) {
        // 网易云式大封面头（与在线歌单/音乐库歌单同一套 PlaylistDetailHeader）：
        // 封面 104 + 标题/副标题/计数 + 圆形播放键 + 其余动作胶囊
        PlaylistDetailHeader(
            title = title,
            cover = cover?.coverArtPath,
            creator = subtitle,
            playCount = null,
            countText = countText,
            songCount = -1,
            isLandscape = false,
            onPlayAll = actions.firstOrNull { it.primary }?.onClick,
            actions = {
                actions.filter { !it.primary }.forEach { action ->
                    PillLabel(text = action.label, onClick = action.onClick, filled = false)
                }
                trailing()
            },
        )
        search?.let {
            GlassSearchField(
                query = it.query,
                onChange = it.onChange,
                placeholder = it.hint,
                modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp),
                compact = true,
            )
        }
        val groups = chipGroups.filter { it.isNotEmpty() }
        if (groups.isNotEmpty()) {
            Spacer(Modifier.height(6.dp))
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 16.dp)
                    .horizontalScroll(rememberScrollState()),
                horizontalArrangement = Arrangement.spacedBy(6.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                groups.forEachIndexed { index, group ->
                    // 组间竖线：来源与排序合成一条轨道后仍能一眼分开
                    if (index > 0) {
                        Box(
                            modifier = Modifier
                                .width(1.dp)
                                .height(14.dp)
                                .background(secondary.copy(alpha = 0.22f)),
                        )
                    }
                    group.forEach { chip ->
                        GlassChip(
                            label = chip.label,
                            selected = chip.selected,
                            onClick = chip.onClick,
                            compact = true,
                        )
                    }
                }
            }
        }
        Spacer(Modifier.height(2.dp))
        Box(modifier = Modifier.fillMaxWidth().weight(1f)) { content() }
    }
}

/** 头图：横屏 60dp、竖屏 56dp；取不到封面时退回名称首字 + 品牌色渐变，不留空框 */
@Composable
private fun CollectionCoverTile(cover: CollectionCover, size: Dp) {
    val palette = LocalCatClawPalette.current
    val shape = RoundedCornerShape(size * 0.26f)
    CoverTile(
        coverArtPath = cover.coverArtPath,
        audioPath = cover.audioPath,
        maxPx = if (size >= 56.dp) 256 else 128,
        modifier = Modifier
            .size(size)
            .clip(shape)
            // 22% 的平涂在浅色莫奈背景上几乎看不见（真机采样与背景差 <10）；
            // 用 primaryDark 压深一档，封面块才有「图」的重量
            .background(
                Brush.linearGradient(
                    listOf(
                        Color(palette.primaryDark).copy(alpha = 0.62f),
                        Color(palette.primary).copy(alpha = 0.30f),
                    ),
                ),
            )
            .border(1.dp, Color(palette.textPrimary).copy(alpha = 0.12f), shape),
    ) {
        Text(
            text = cover.fallback.take(1),
            fontSize = (size.value * 0.34f).sp,
            fontWeight = FontWeight.Bold,
            color = Color(palette.textPrimary),
        )
    }
}

/** 动作胶囊：横屏 30dp / 10.5sp（省高度），竖屏 32dp / 12sp；primary 走实心品牌面 + inkOn */
@Composable
private fun TextAction(
    action: CollectionAction,
    primary: Color,
    secondary: Color,
    compact: Boolean = true,
) {
    val palette = LocalCatClawPalette.current
    val shape = RoundedCornerShape(if (compact) 10.dp else 12.dp)
    Box(
        modifier = Modifier
            .height(if (compact) 30.dp else 32.dp)
            .clip(shape)
            .background(if (action.primary) Color(palette.primary) else Color.Transparent)
            .then(
                if (action.primary) Modifier
                else Modifier.border(1.dp, primary.copy(alpha = 0.16f), shape),
            )
            .clickable(onClick = action.onClick)
            .padding(horizontal = if (compact) 9.dp else 12.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = action.label,
            fontSize = if (compact) 10.5.sp else 12.sp,
            fontWeight = if (action.primary) FontWeight.SemiBold else FontWeight.Medium,
            color = if (action.primary) Color(inkOn(palette.primary)) else secondary,
            maxLines = 1,
        )
    }
}
