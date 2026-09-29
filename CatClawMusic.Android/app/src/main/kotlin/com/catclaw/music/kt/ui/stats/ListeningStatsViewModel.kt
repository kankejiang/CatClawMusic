package com.catclaw.music.kt.ui.stats

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.core.diagnostic.AppLog
import com.catclaw.music.kt.core.playback.PlayerController
import com.catclaw.music.kt.core.stats.ListeningStatsRules
import com.catclaw.music.kt.data.db.ArtistPlayRow
import com.catclaw.music.kt.data.db.DailyStatRow
import com.catclaw.music.kt.data.db.LibraryDao
import com.catclaw.music.kt.data.db.PlayCountRow
import com.catclaw.music.kt.data.db.SongListItem
import com.catclaw.music.kt.data.db.toSong
import com.catclaw.music.kt.data.service.PlaybackSessionStarter
import dagger.hilt.android.lifecycle.HiltViewModel
import java.time.LocalDate
import java.time.format.DateTimeFormatter
import javax.inject.Inject
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

/**
 * 听歌统计页的数据装配。
 *
 * 算术不在这里 —— 派生数字全在 [ListeningStatsRules]（那部分有 JVM 测试钉着），
 * 这层只负责：把范围翻成 DAO 的起止日期、并发送话、把三张排行榜和汇总拼成页面要的一整份。
 *
 * 为什么每次切范围都重发四条查询而不是缓存：范围一变，趋势柱、时段分布、连续天数、
 * 环比**全部**跟着变，缓存四份中间结果反而更容易留下"哪一格忘了跟着变"的旧数据。
 */
@HiltViewModel
class ListeningStatsViewModel @Inject constructor(
    private val dao: LibraryDao,
    private val controller: PlayerController,
    private val session: PlaybackSessionStarter,
) : ViewModel() {

    /** 页面一次渲染要的全部东西。榜单和汇总放在一起，UI 就不会把两者画成不同时刻的数据 */
    data class View(
        val range: ListeningStatsRules.Range = ListeningStatsRules.Range.MONTH,
        val byDuration: Boolean = false,
        val loading: Boolean = false,
        val stats: ListeningStatsRules.Result = ListeningStatsRules.summarize(
            emptyList(), emptyList(), 0, ListeningStatsRules.Range.MONTH, LocalDate.now(),
        ),
        val topSongs: List<PlayCountRow> = emptyList(),
        val topArtists: List<ArtistPlayRow> = emptyList(),
        val recent: List<SongListItem> = emptyList(),
    ) {
        val hasData: Boolean get() = stats.totalPlays > 0 || topSongs.isNotEmpty()
    }

    private val _view = MutableStateFlow(View())
    val view: StateFlow<View> = _view.asStateFlow()

    init {
        reload()
    }

    fun setRange(range: ListeningStatsRules.Range) {
        if (_view.value.range == range) return
        _view.value = _view.value.copy(range = range)
        reload()
    }

    /** 趋势图在"次数"和"时长"之间切换。柱子的两种 ratio 都算好了，这里只换口径 */
    fun toggleMetric() {
        _view.value = _view.value.copy(byDuration = !_view.value.byDuration)
    }

    fun reload() {
        _view.value = _view.value.copy(loading = true)
        viewModelScope.launch {
            val range = _view.value.range
            val today = LocalDate.now()
            val (start, end) = rangeBounds(range, today)
            // 上一周期只用于环比，"全部"没有上一周期
            val prevBounds = prevRangeBounds(range, today)
            runCatching {
                val daily = dao.dailyStatsRange(start, end).toRules()
                val hourly = dao.hourlyHoursRange(start, end)
                    .map { ListeningStatsRules.HourBucket(it.hour, it.plays, it.ms) }
                val heard = dao.songsHeardRange(start, end)
                val stats = ListeningStatsRules.summarize(
                    days = daily,
                    hours = hourly,
                    songsHeard = heard,
                    range = range,
                    today = today,
                    prevDays = if (prevBounds == null) {
                        emptyList()
                    } else {
                        dao.dailyStatsRange(prevBounds.first, prevBounds.second).toRules()
                    },
                )
                _view.value = View(
                    range = range,
                    byDuration = _view.value.byDuration,
                    stats = stats,
                    topSongs = dao.topPlayed(TOP_SONGS),
                    topArtists = dao.topArtists(TOP_ARTISTS),
                    recent = dao.recentSongs(RECENT_SONGS),
                )
            }.onFailure {
                // 失败要出声：这页整页都是数字，静默退回空态就等于告诉用户"你从没听过歌"
                AppLog.w(TAG, "load stats failed range=${_view.value.range}", it)
            }
            _view.value = _view.value.copy(loading = false)
        }
    }

    /** 榜单与"最近在听"都能直接点播，和发现页同一条链（先拉起会话再换队列） */
    fun play(list: List<SongListItem>, index: Int) {
        val songs = list.map { it.toSong() }
        if (songs.isEmpty() || index !in songs.indices) return
        session.ensure()
        controller.playSongs(songs, startIndex = index)
    }

    private fun List<DailyStatRow>.toRules() =
        map {
            ListeningStatsRules.Day(
                date = LocalDate.parse(it.date),
                plays = it.plays,
                ms = it.ms,
                nightPlays = it.nightPlays,
            )
        }

    private companion object {
        const val TAG = "CatClawStats"
        const val TOP_SONGS = 10
        const val TOP_ARTISTS = 5
        const val RECENT_SONGS = 8

        val DATE: DateTimeFormatter = DateTimeFormatter.ofPattern("yyyy-MM-dd")

        /** 范围内含今天：近 7 天是"今天 + 前 6 天"，不是"昨天往前数 7 天" */
        fun rangeBounds(range: ListeningStatsRules.Range, today: LocalDate): Pair<String, String> {
            if (range == ListeningStatsRules.Range.ALL) return "" to ""
            return today.minusDays((range.days - 1).toLong()).format(DATE) to today.format(DATE)
        }

        /** 上一周期：紧挨着当前范围往前再推同样长度的一天，两端都不重叠 */
        fun prevRangeBounds(range: ListeningStatsRules.Range, today: LocalDate): Pair<String, String>? {
            if (range == ListeningStatsRules.Range.ALL) return null
            val n = range.days.toLong()
            val prevEnd = today.minusDays(n)
            val prevStart = today.minusDays(n * 2 - 1)
            return prevStart.format(DATE) to prevEnd.format(DATE)
        }
    }
}
