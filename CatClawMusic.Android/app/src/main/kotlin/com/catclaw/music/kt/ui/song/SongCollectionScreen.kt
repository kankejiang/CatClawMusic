package com.catclaw.music.kt.ui.song

import android.content.res.Configuration
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
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
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
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
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.inkOn

/** 二级页头部的一颗动作（播放全部 / 随机播放 / 加入…） */
data class CollectionAction(
    val label: String,
    val onClick: () -> Unit,
    /** 横屏主按钮走实心品牌面，其余是玻璃描边胶囊；竖屏一律走 PillLabel */
    val primary: Boolean = false,
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
    Column(modifier.fillMaxSize()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 18.dp, end = 18.dp, top = 6.dp, bottom = 4.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(9.dp),
        ) {
            BackTile(onBack = onBack, description = stringResource(R.string.queue_back))
            cover?.let { CollectionCoverTile(it, 38.dp) }
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = title,
                    fontSize = 18.sp,
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
            trailing()
            actions.forEach { action ->
                CompactAction(action, primary, secondary)
            }
        }

        if (search != null || chipGroups.any { it.isNotEmpty() }) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 18.dp, vertical = 3.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                search?.let {
                    GlassSearchField(
                        query = it.query,
                        onChange = it.onChange,
                        placeholder = it.hint,
                        modifier = Modifier.weight(1f),
                    )
                }
                val chips = chipGroups.flatten()
                if (chips.isNotEmpty()) {
                    Row(
                        // 与搜索框对半分：不给 weight 的话 chips 会按内容宽量走 500+dp，
                        // 搜索框只剩 ~200dp，placeholder 被折成两行（真机截图实测）
                        modifier = Modifier.weight(1f).horizontalScroll(rememberScrollState()),
                        horizontalArrangement = Arrangement.spacedBy(6.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        chips.forEach { chip ->
                            GlassChip(label = chip.label, selected = chip.selected, onClick = chip.onClick)
                        }
                    }
                }
            }
        }

        Box(modifier = Modifier.fillMaxWidth().weight(1f)) { content() }
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
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 16.dp, end = 16.dp, top = 14.dp, bottom = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            BackTile(onBack = onBack, description = stringResource(R.string.queue_back))
            cover?.let { CollectionCoverTile(it, 56.dp) }
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = title,
                    fontSize = 22.sp,
                    fontWeight = FontWeight.Bold,
                    color = primary,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                subtitle?.takeIf { it.isNotBlank() }?.let {
                    Text(
                        text = it,
                        fontSize = 13.sp,
                        color = secondary,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
                Text(text = countText, fontSize = 12.sp, color = secondary)
            }
            trailing()
        }
        if (actions.isNotEmpty()) {
            Row(
                modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp),
                horizontalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                actions.forEach { action ->
                    PillLabel(text = action.label, onClick = action.onClick, filled = false)
                }
            }
            Spacer(Modifier.height(8.dp))
        }
        chipGroups.filter { it.isNotEmpty() }.forEach { group ->
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 16.dp, vertical = 4.dp)
                    .horizontalScroll(rememberScrollState()),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                group.forEach { chip ->
                    GlassChip(label = chip.label, selected = chip.selected, onClick = chip.onClick)
                }
            }
        }
        search?.let {
            GlassSearchField(
                query = it.query,
                onChange = it.onChange,
                placeholder = it.hint,
                modifier = Modifier.padding(horizontal = 16.dp, vertical = 6.dp),
            )
        }
        Box(modifier = Modifier.fillMaxWidth().weight(1f)) { content() }
    }
}

/** 头图：横屏 38dp、竖屏 56dp；取不到封面时退回名称首字，不留空框 */
@Composable
private fun CollectionCoverTile(cover: CollectionCover, size: Dp) {
    val palette = LocalCatClawPalette.current
    CoverTile(
        coverArtPath = cover.coverArtPath,
        audioPath = cover.audioPath,
        maxPx = 128,
        modifier = Modifier
            .size(size)
            .clip(RoundedCornerShape(if (size == 38.dp) 12.dp else 16.dp))
            .background(Color(palette.primary).copy(alpha = 0.22f)),
    ) {
        Text(
            text = cover.fallback.take(1),
            fontSize = if (size == 38.dp) 17.sp else 22.sp,
            fontWeight = FontWeight.Bold,
            color = Color(palette.textPrimary),
        )
    }
}

/** 横屏头部那颗紧凑胶囊：12sp + 9dp 内边距 ≈ 33dp 高，44dp 的头部行放得下三颗 */
@Composable
private fun CompactAction(action: CollectionAction, primary: Color, secondary: Color) {
    val shape = RoundedCornerShape(999.dp)
    if (action.primary) {
        val palette = LocalCatClawPalette.current
        Text(
            text = action.label,
            fontSize = 12.sp,
            fontWeight = FontWeight.SemiBold,
            color = Color(inkOn(palette.primary)),
            maxLines = 1,
            modifier = Modifier
                .clip(shape)
                .background(Color(palette.primary))
                .clickable(onClick = action.onClick)
                .padding(horizontal = 14.dp, vertical = 9.dp),
        )
    } else {
        Text(
            text = action.label,
            fontSize = 12.sp,
            color = secondary,
            maxLines = 1,
            modifier = Modifier
                .clip(shape)
                .border(1.dp, primary.copy(alpha = 0.16f), shape)
                .clickable(onClick = action.onClick)
                .padding(horizontal = 13.dp, vertical = 9.dp),
        )
    }
}
