package com.catclaw.music.kt.ui.stats

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.stats.ListeningStatsRules
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.EmptyState
import com.catclaw.music.kt.ui.theme.GlassChip
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.glassCard
import java.time.LocalDate
import java.time.format.DateTimeFormatter
import java.util.Locale
import kotlin.math.abs
import kotlin.math.roundToInt

/**
 * 听歌统计页 —— 对应 MAUI 的 `ListeningStatsView` + `ListeningStatsViewModel`。
 *
 * 版面顺序照 MAUI：四格概览 → 时长卡 → 趋势图 → 时段分布 → 连续听歌 → 环比 → 三张榜单。
 * 数字一律来自 [ListeningStatsRules]（那一层有 JVM 测试钉着口径），这层只管摆出来和措辞。
 *
 * 两处与 MAUI 有意不同：
 * - 柱状图标签走 locale 感知的 [DateTimeFormatter]，而不是硬编码的「3月」——
 *   这一页要跟着应用内语言切换，月份名写死就成了 50 个语言里的一个中文洞；
 * - 「常听歌曲」榜不可点播。它来自按播放量聚合的投影，没有可播的文件路径，
 *   把一行画成能点而点了没反应，比少一个入口糟得多。「最近在听」是完整歌曲行，能点。
 */
@Composable
fun ListeningStatsScreen(
    onBack: () -> Unit,
    viewModel: ListeningStatsViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    val view by viewModel.view.collectAsStateWithLifecycle()
    // 每次进入这层覆盖都重查一遍。VM 跟着整页活著，只在第一次组合加载会把"刚才听的那首"漏掉，
    // 而统计页恰好是最容易被再次进来的页面（关掉 → 听一首 → 回来看数字）
    LaunchedEffect(Unit) { viewModel.reload() }
    val stats = view.stats
    val ink = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    val primary = Color(palette.primary)

    Column(modifier = Modifier.fillMaxSize().padding(horizontal = 16.dp)) {
        Spacer(Modifier.height(12.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
            Text(
                text = stringResource(R.string.StatsTitle),
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold,
                color = ink,
                modifier = Modifier.padding(start = 10.dp),
            )
        }
        Spacer(Modifier.height(10.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            ListeningStatsRules.Range.entries.forEach { r ->
                GlassChip(
                    label = stringResource(rangeLabelRes(r)),
                    selected = r == view.range,
                    onClick = { viewModel.setRange(r) },
                    modifier = Modifier.weight(1f),
                )
            }
        }

        if (!view.hasData && !view.loading) {
            EmptyState(hint = stringResource(R.string.StatsEmpty), iconRes = R.drawable.ic_library)
            return@Column
        }

        Column(
            modifier = Modifier
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(top = 12.dp, bottom = 24.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            StatGrid(
                listOf(
                    stats.totalPlays.toString() to stringResource(R.string.disc_stat_total_plays),
                    stats.songsHeard.toString() to stringResource(R.string.StatsSongsHeard),
                    stats.listeningDays.toString() to stringResource(R.string.StatsListeningDays),
                    stats.companionDays.toString() to stringResource(R.string.StatsCompanionDays),
                ),
            )

            Card(title = stringResource(R.string.StatsDurationTitle)) {
                ValueRow(stringResource(R.string.StatsDurationTotal), durationText(stats.totalMs))
                ValueRow(stringResource(R.string.StatsDurationAvg), durationText(stats.avgDayMs))
                ValueRow(
                    stringResource(R.string.StatsDurationNight),
                    stringResource(R.string.StatsPercentValue, stats.nightPercent),
                )
            }

            Card(title = stringResource(R.string.StatsTrendTitle)) {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    GlassChip(
                        label = stringResource(R.string.StatsMetricPlays),
                        selected = !view.byDuration,
                        onClick = { if (view.byDuration) viewModel.toggleMetric() },
                        modifier = Modifier.weight(1f),
                    )
                    GlassChip(
                        label = stringResource(R.string.StatsMetricDuration),
                        selected = view.byDuration,
                        onClick = { if (!view.byDuration) viewModel.toggleMetric() },
                        modifier = Modifier.weight(1f),
                    )
                }
                Spacer(Modifier.height(10.dp))
                Bars(bars = stats.bars, byDuration = view.byDuration, accent = primary)
                Text(
                    text = if (view.byDuration) {
                        stringResource(
                            R.string.StatsTrendTotal,
                            stringResource(rangeLabelRes(view.range)),
                            durationText(stats.totalMs),
                        )
                    } else {
                        stringResource(
                            R.string.StatsTrendActiveDays,
                            stringResource(rangeLabelRes(view.range)),
                            stats.activeDaysInRange,
                        )
                    },
                    fontSize = 12.sp,
                    color = secondary,
                    modifier = Modifier.padding(top = 8.dp),
                )
            }

            Card(title = stringResource(R.string.StatsSlotsTitle)) {
                val slotLabels = listOf(
                    stringResource(R.string.StatsSlotDawn),
                    stringResource(R.string.StatsSlotMorning),
                    stringResource(R.string.StatsSlotNoon),
                    stringResource(R.string.StatsSlotDusk),
                    stringResource(R.string.StatsSlotNight),
                    stringResource(R.string.StatsSlotLateNight),
                )
                stats.slots.forEachIndexed { i, slot ->
                    ValueRow(
                        slotLabels.getOrElse(i) { "" },
                        stringResource(R.string.StatsPercentValue, slot.percent),
                    )
                }
                // 深夜 = 后两格（21–24 与 0–5）之和。判据与 PlayStatRules 的深夜时段同一处定义
                val late = stats.slots.drop(4).sumOf { it.percent }
                Text(
                    text = when {
                        late >= 30 -> stringResource(R.string.StatsNightHeavy, late)
                        late >= 15 -> stringResource(R.string.StatsNightSome, late)
                        else -> stringResource(R.string.StatsNightLight, late)
                    },
                    fontSize = 12.sp,
                    color = secondary,
                    modifier = Modifier.padding(top = 6.dp),
                )
            }

            StatGrid(
                listOf(
                    stats.streakCurrent.toString() to stringResource(R.string.StatsStreakCurrent),
                    stats.streakBest.toString() to stringResource(R.string.StatsStreakBest),
                ),
            )

            stats.compare?.let { c ->
                Card(title = stringResource(R.string.StatsCompareTitle)) {
                    Text(
                        text = stringResource(R.string.StatsCompareCap, view.range.days),
                        fontSize = 12.sp,
                        color = Color(palette.textSecondary),
                    )
                    Spacer(Modifier.height(6.dp))
                    ValueRow(
                        stringResource(R.string.disc_stat_total_plays),
                        deltaText(c.playsDelta, c.playsDelta >= 0),
                    )
                    ValueRow(stringResource(R.string.StatsDurationTotal), deltaText(c.msDelta, c.msDelta >= 0))
                    ValueRow(stringResource(R.string.StatsListeningDays), deltaText(c.daysDelta, c.daysDelta >= 0))
                }
            }

            if (view.topSongs.isNotEmpty()) {
                Card(title = stringResource(R.string.StatsTopSongsTitle)) {
                    RankList(
                        rows = view.topSongs.map { s ->
                            RankRowData(s.title, s.artist, s.playCount.toString(), s.playCount)
                        },
                        accent = primary,
                    )
                }
            }

            if (view.topArtists.isNotEmpty()) {
                Card(title = stringResource(R.string.StatsTopArtistsTitle)) {
                    RankList(
                        rows = view.topArtists.map { a ->
                            RankRowData(
                                main = a.name.ifBlank { stringResource(R.string.StatsUnknownArtist) },
                                sub = "",
                                end = a.plays.toString(),
                                weight = a.plays,
                            )
                        },
                        accent = primary,
                    )
                }
            }

            Card(title = stringResource(R.string.StatsRecentTitle)) {
                view.recent.forEachIndexed { i, song ->
                    PlayableRow(
                        main = song.title,
                        sub = song.artist,
                        onClick = { viewModel.play(view.recent, i) },
                    )
                }
            }
        }
    }
}

private data class RankRowData(val main: String, val sub: String, val end: String, val weight: Int)

/**
 * 排行列表。相对宽度的条用的是**榜内最大值**，
 * 所以它读作"这首歌在这个榜里排第几档"，不是绝对播放量 —— 榜换了分母就换，这是有意的。
 */
@Composable
private fun RankList(rows: List<RankRowData>, accent: Color) {
    val palette = LocalCatClawPalette.current
    val max = rows.maxOf { it.weight }.coerceAtLeast(1)
    rows.forEachIndexed { i, row ->
        Column(modifier = Modifier.fillMaxWidth().padding(vertical = 5.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = "${i + 1}",
                    fontSize = 12.sp,
                    color = Color(palette.textSecondary),
                    modifier = Modifier.width(20.dp),
                )
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        row.main,
                        fontSize = 14.sp,
                        color = Color(palette.textPrimary),
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    if (row.sub.isNotBlank()) {
                        Text(
                            row.sub,
                            fontSize = 11.sp,
                            color = Color(palette.textSecondary),
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                }
                Text(row.end, fontSize = 13.sp, fontWeight = FontWeight.Medium, color = Color(palette.textPrimary))
            }
            Spacer(Modifier.height(4.dp))
            Bar(value = row.weight.toFloat() / max, accent = accent)
        }
    }
}

@Composable
private fun Bar(value: Float, accent: Color) {
    val palette = LocalCatClawPalette.current
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .height(3.dp)
            .background(Color(palette.textPrimary).copy(alpha = 0.08f)),
    ) {
        Box(
            modifier = Modifier
                .fillMaxWidth(value.coerceIn(0f, 1f))
                .height(3.dp)
                .background(accent.copy(alpha = 0.75f)),
        )
    }
}

/**
 * 趋势柱。
 *
 * 柱高用 :core 预算好的两种 ratio 之一，**不在这里**按"当前最大柱"归一：
 * 那样每根柱的相对高度会随可视窗口重算，切指标时看着像数据变了，其实只是换了尺子。
 * 零播放的柱子也画（0.18 透明度的极矮一截），否则横轴会随数据稀疏而错位。
 */
@Composable
private fun Bars(bars: List<ListeningStatsRules.Bar>, byDuration: Boolean, accent: Color) {
    val palette = LocalCatClawPalette.current
    // 30 根柱时逐个标日会挤成一团糊，改成每 5 天一个刻度：与 MAUI 同一条密度规则
    val labelEvery = if (bars.size > 14) 5 else 1
    Row(
        modifier = Modifier.fillMaxWidth().height(96.dp),
        verticalAlignment = Alignment.Bottom,
        horizontalArrangement = Arrangement.spacedBy(2.dp),
    ) {
        bars.forEach { bar ->
            val ratio = (if (byDuration) bar.msRatio else bar.playsRatio).coerceIn(0f, 1f)
            Box(
                modifier = Modifier
                    .weight(1f)
                    .height((96.dp * ratio).coerceAtLeast(3.dp))
                    .clip(RoundedCornerShape(topStart = 3.dp, topEnd = 3.dp))
                    .background(if (ratio > 0f) accent else Color(palette.textPrimary).copy(alpha = 0.12f)),
            )
        }
    }
    Row(modifier = Modifier.fillMaxWidth().padding(top = 4.dp), horizontalArrangement = Arrangement.spacedBy(2.dp)) {
        bars.forEachIndexed { i, bar ->
            Box(modifier = Modifier.weight(1f), contentAlignment = Alignment.Center) {
                if (i % labelEvery == 0) {
                    Text(
                        text = barLabel(bar.key, bars.size),
                        fontSize = 9.sp,
                        color = Color(palette.textSecondary),
                        maxLines = 1,
                    )
                }
            }
        }
    }
}

@Composable
private fun StatGrid(cells: List<Pair<String, String>>) {
    val palette = LocalCatClawPalette.current
    Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        cells.chunked(2).forEach { line ->
            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                line.forEach { (value, label) ->
                    Column(
                        modifier = Modifier
                            .weight(1f)
                            .glassCard(palette = palette, corner = 20)
                            .padding(14.dp),
                    ) {
                        Text(value, fontSize = 22.sp, fontWeight = FontWeight.Bold, color = Color(palette.textPrimary))
                        Text(
                            label,
                            fontSize = 11.sp,
                            color = Color(palette.textSecondary),
                            modifier = Modifier.padding(top = 2.dp),
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun Card(title: String, content: @Composable () -> Unit) {
    val palette = LocalCatClawPalette.current
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .glassCard(palette = palette)
            .padding(14.dp),
    ) {
        Text(title, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, color = Color(palette.textPrimary))
        Spacer(Modifier.height(8.dp))
        content()
    }
}

@Composable
private fun ValueRow(label: String, value: String) {
    val palette = LocalCatClawPalette.current
    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = 3.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(label, fontSize = 13.sp, color = Color(palette.textSecondary), modifier = Modifier.weight(1f))
        Text(value, fontSize = 14.sp, fontWeight = FontWeight.Medium, color = Color(palette.textPrimary))
    }
}

/** 可点播的一行：尺寸在前、clickable 在后，整行才是 44dp 的触达面 */
@Composable
private fun PlayableRow(main: String, sub: String, onClick: () -> Unit) {
    val palette = LocalCatClawPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .heightIn(min = 44.dp)
            .clip(RoundedCornerShape(14.dp))
            .clickable(onClick = onClick)
            .padding(vertical = 6.dp, horizontal = 2.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                main,
                fontSize = 14.sp,
                color = Color(palette.textPrimary),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            if (sub.isNotBlank()) {
                Text(
                    sub,
                    fontSize = 11.sp,
                    color = Color(palette.textSecondary),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
    }
}

/** 毫秒 → 「X 天 Y 小时」/「X 小时 Y 分」/「X 分」。单位词走资源，所以这个函数是 @Composable */
@Composable
private fun durationText(ms: Long): String {
    val minutes = (ms / 60_000.0).roundToInt()
    val days = minutes / 1440
    val hours = (minutes % 1440) / 60
    val mins = minutes % 60
    return when {
        minutes <= 0 -> stringResource(R.string.StatsDurationZero)
        days > 0 && hours > 0 -> stringResource(R.string.StatsDurationDaysHours, days, hours)
        days > 0 -> stringResource(R.string.StatsDurationDays, days)
        hours > 0 && mins > 0 -> stringResource(R.string.StatsDurationHoursMinutes, hours, mins)
        hours > 0 -> stringResource(R.string.StatsDurationHours, hours)
        else -> stringResource(R.string.StatsDurationMinutes, mins)
    }
}

/** 环比的一行文字。0 显示"持平"而不是「▲ 0%」—— 那是四舍五入的结果，不是涨跌 */
@Composable
private fun deltaText(delta: Int, up: Boolean): String = when {
    delta == 0 -> stringResource(R.string.StatsDeltaFlat)
    up -> stringResource(R.string.StatsDeltaUp, abs(delta))
    else -> stringResource(R.string.StatsDeltaDown, abs(delta))
}

private fun barLabel(key: LocalDate, barCount: Int): String {
    // 「全部」的桶是月，其余是日；月名交给 locale，日/月数字不需要翻译
    val pattern = if (barCount > 14 && key.dayOfMonth == 1) "MMM" else if (barCount > 14) "d" else "M/d"
    return DateTimeFormatter.ofPattern(pattern, Locale.getDefault()).format(key)
}

private fun rangeLabelRes(range: ListeningStatsRules.Range): Int = when (range) {
    ListeningStatsRules.Range.WEEK -> R.string.StatsRangeWeek
    ListeningStatsRules.Range.MONTH -> R.string.StatsRangeMonth
    ListeningStatsRules.Range.ALL -> R.string.StatsRangeAll
}
