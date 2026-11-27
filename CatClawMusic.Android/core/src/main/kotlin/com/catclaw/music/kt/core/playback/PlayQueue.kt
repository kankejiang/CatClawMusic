package com.catclaw.music.kt.core.playback

import com.catclaw.music.kt.core.model.Song
import kotlin.random.Random

enum class PlayMode { Sequential, Shuffle, SingleRepeat, ListRepeat }

/**
 * 播放队列，行为对齐 C# Core/Services/PlayQueue.cs：
 * 双列表（原始 / 洗牌）+ 索引历史栈（上一曲走历史而非 index-1）+ 随机模式复用同一份洗牌序循环，
 * 直到用户再次洗牌；空闲时开启随机从随机起点开始而不是列表第一首。
 */
class PlayQueue {

    private val originalList = mutableListOf<Song>()
    private val shuffledList = mutableListOf<Song>()
    private val history = ArrayDeque<Int>()
    private val songIdToIndex = HashMap<Int, Int>()
    private var currentIndex = -1

    /**
     * 是否处于私人漫游（FM）电台模式：激活时仅允许「单曲循环 ↔ 无限（Sequential）」切换，
     * 禁用随机/列表循环；播放页模式按钮据此显示「无限」。
     * 对齐 MAUI `Core/PlayQueue.IsFmMode`。
     */
    var isFmMode: Boolean = false

    /** 恢复持久化队列期间置位：此时 `playMode` 的 setter 不洗牌，原样保留快照里的顺序 */
    private var restoring = false

    var playMode: PlayMode = PlayMode.ListRepeat
        set(value) {
            val old = field
            if (old == value) return
            // 切模式不能顺手换歌：先把当前曲记下来（此刻 activeList() 还是旧模式），
            // 改完模式再按 id 在新列表里重新定位
            val playing = currentSong
            field = value
            // 历史栈里存的是旧模式列表的下标，切完模式全部错位 —— 清掉
            //（previous() 在空栈时的既有行为就是回到本曲）
            history.clear()
            if (value == PlayMode.Shuffle) {
                // 每次切到随机都重新洗一次（用户预期：重新进随机就该换一种顺序）；
                // 恢复快照时除外，那份顺序本身就是要原样恢复的洗牌序
                if (!restoring) {
                    shuffledList.clear()
                    shuffledList.addAll(shuffle(originalList))
                }
                if (playing == null) {
                    // 空闲态随机起点（原语义：随机播放不能永远从第一首开始）
                    if (!restoring && shuffledList.isNotEmpty()) currentIndex = Random.nextInt(shuffledList.size)
                } else {
                    // 当前曲挪到洗牌列表队首：从它继续播，下一曲才进入随机序
                    when (val idx = shuffledList.indexOfFirst { it.id == playing.id }) {
                        0 -> currentIndex = 0
                        in 1..Int.MAX_VALUE -> {
                            shuffledList.add(0, shuffledList.removeAt(idx))
                            currentIndex = 0
                        }
                        else -> {
                            shuffledList.add(0, playing)
                            currentIndex = 0
                        }
                    }
                }
            } else if (playing != null) {
                // 洗牌 → 其他：按 id 映射回原始列表（不能拿洗牌下标直接读原始列表）
                songIdToIndex[playing.id]?.let { currentIndex = it }
            }
        }

    val currentSong: Song?
        get() {
            if (currentIndex < 0) return null
            val list = activeList()
            return list.getOrNull(currentIndex)
        }

    fun setSongs(songs: List<Song>) {
        originalList.clear()
        originalList.addAll(songs)
        shuffledList.clear()
        currentIndex = -1
        history.clear()
        rebuildIndex()
        if (playMode == PlayMode.Shuffle) enableShuffle()
    }

    /** 跨进程重启恢复：持久化列表本身就是期望顺序，随机模式下即洗牌顺序，不再重洗 */
    fun restorePersisted(songs: List<Song>, currentIndex: Int, mode: PlayMode) {
        originalList.clear()
        originalList.addAll(songs)
        shuffledList.clear()
        if (mode == PlayMode.Shuffle) shuffledList.addAll(songs)
        // 先清空当前曲再切模式：切模式的 setter 会把「当前曲挪到洗牌队首」，
        // 恢复场景要原样保留持久化顺序，不能被这条规则重排
        this.currentIndex = -1
        history.clear()
        rebuildIndex()
        restoring = true
        try {
            playMode = mode
        } finally {
            restoring = false
        }
        this.currentIndex = if (currentIndex in songs.indices) currentIndex else -1
    }

    fun enableShuffle() {
        if (originalList.isEmpty()) {
            shuffledList.clear()
            currentIndex = -1
            history.clear()
            return
        }

        // 洗牌前先记住当前正在播放的歌：currentIndex 在重复洗牌时已是洗牌列表内的位置，
        // 直接按它去索引原始列表会错位取到别的歌。
        val playing = currentSong
        shuffledList.clear()
        shuffledList.addAll(shuffle(originalList))
        history.clear()

        currentIndex = if (playing != null) {
            shuffledList.indexOfFirst { it.id == playing.id }.coerceAtLeast(0)
        } else {
            // 空闲态：随机起点，避免"随机播放"永远从第一首开始
            Random.nextInt(shuffledList.size)
        }
    }

    fun next(): Song? {
        if (originalList.isEmpty()) return null
        if (currentIndex >= 0) history.addLast(currentIndex)
        currentIndex = nextIndex() ?: return null
        return currentSong.also { if (it == null) currentIndex = -1 }
    }

    fun previous(): Song? {
        val idx = history.removeLastOrNull() ?: return currentSong
        val list = activeList()
        if (idx !in list.indices) return currentSong
        currentIndex = idx
        return currentSong
    }

    /** 预览下一首（不推进位置），用于预缓冲；随机模式只预览当前洗牌序里的下一首，不触发重洗 */
    fun peekNextSong(): Song? = upcomingSongs(1).firstOrNull()

    /** 接下来 N 首（不改变队列状态） */
    fun upcomingSongs(count: Int = 3): List<Song> {
        val result = mutableListOf<Song>()
        if (originalList.isEmpty() || currentIndex < 0) return result
        val list = activeList()
        if (list.isEmpty() || currentIndex >= list.size) return result

        var peek = currentIndex
        repeat(count) {
            if (playMode == PlayMode.SingleRepeat) {
                list.getOrNull(peek)?.let { result += it }
                return@repeat
            }
            peek++
            if (peek >= list.size) {
                if (playMode == PlayMode.ListRepeat || playMode == PlayMode.Shuffle) {
                    peek = 0
                } else {
                    return result
                }
            }
            list.getOrNull(peek)?.let { result += it }
        }
        return result
    }

    /** 用户手动点歌 */
    fun selectSong(songId: Int) {
        if (currentIndex >= 0) history.addLast(currentIndex)
        currentIndex = if (playMode == PlayMode.Shuffle && shuffledList.isNotEmpty()) {
            shuffledList.indexOfFirst { it.id == songId }
        } else {
            songIdToIndex[songId] ?: -1
        }
    }

    /**
     * 末尾追加（私人漫游续播用）：不改变当前索引与播放位置。
     * 随机模式下同时追加到洗牌列表，否则 ExoPlayer 的 shuffle 序会与队列不一致。
     */
    fun appendAll(songs: List<Song>) {
        if (songs.isEmpty()) return
        originalList.addAll(songs)
        if (playMode == PlayMode.Shuffle) shuffledList.addAll(songs)
        rebuildIndex()
    }

    /** 原位替换某首歌（直链升级用）：id 不变，只换数据 */
    fun replaceSong(id: Int, new: Song) {
        val i = originalList.indexOfFirst { it.id == id }
        if (i >= 0) originalList[i] = new
        val j = shuffledList.indexOfFirst { it.id == id }
        if (j >= 0) shuffledList[j] = new
        rebuildIndex()
    }

    /** 下一首播放：插到当前之后 */
    fun addNext(song: Song) {
        if (currentIndex >= 0 && currentIndex + 1 < originalList.size) {
            originalList.add(currentIndex + 1, song)
        } else {
            originalList.add(song)
        }
        if (shuffledList.isNotEmpty()) {
            shuffledList.add((currentIndex + 1).coerceIn(0, shuffledList.size), song)
        }
        rebuildIndex()
    }

    fun addToEnd(song: Song) {
        originalList.add(song)
        if (shuffledList.isNotEmpty()) shuffledList.add(song)
        rebuildIndex()
    }

    /** 移除歌曲；移除的是当前曲时索引自动指向同位置的下一首 */
    fun removeSong(songId: Int): Boolean {
        val idx = originalList.indexOfFirst { it.id == songId }
        if (idx < 0) return false
        val wasCurrent = idx == currentIndex
        originalList.removeAt(idx)
        shuffledList.indexOfFirst { it.id == songId }.takeIf { it >= 0 }?.let { shuffledList.removeAt(it) }

        when {
            idx < currentIndex -> currentIndex--
            wasCurrent -> currentIndex = if (currentIndex >= originalList.size) {
                if (originalList.isNotEmpty()) 0 else -1
            } else currentIndex
        }
        rebuildIndex()
        return true
    }

    /**
     * 拖动排序：把**当前生效列表**里的 [from] 挪到 [to]。
     *
     * 随机模式下生效列表是洗牌序，原始列表不动 —— 它只在重洗时用作素材，顺序没有播放含义。
     * 移动后按 id 重新定位 currentIndex（不能沿用下标：挪动当前曲时旧下标已经指向别人了），
     * 历史栈存的也是下标、重排后全部错位，直接清空（上一曲最多退回「回到本曲」的既有行为）。
     */
    fun moveSong(from: Int, to: Int): Boolean {
        val list = activeList()
        if (from !in list.indices || to !in list.indices || from == to) return false
        val playingId = currentSong?.id
        list.add(to, list.removeAt(from))
        if (playingId != null) {
            list.indexOfFirst { it.id == playingId }.takeIf { it >= 0 }?.let { currentIndex = it }
        }
        history.clear()
        rebuildIndex()
        return true
    }

    fun songs(): List<Song> = activeList().toList()

    /** 队列里是否已有这首歌（按 id 去重，搜索页单击起播时用） */
    fun containsSong(songId: Int): Boolean = activeList().any { it.id == songId }

    fun snapshot(): Snapshot = Snapshot(
        // 随机模式下持久化的是洗牌序，恢复时直接复用
        songs = activeList().toList(),
        currentIndex = currentIndex,
        mode = playMode,
    )

    data class Snapshot(val songs: List<Song>, val currentIndex: Int, val mode: PlayMode)

    private fun activeList(): MutableList<Song> =
        if (playMode == PlayMode.Shuffle && shuffledList.isNotEmpty()) shuffledList else originalList

    private fun nextIndex(): Int? {
        val list = activeList()
        if (playMode == PlayMode.SingleRepeat) {
            return if (currentIndex in list.indices) currentIndex else if (list.isNotEmpty()) 0 else null
        }
        var next = currentIndex + 1
        if (next >= list.size) {
            // 随机 / 列表循环：回到头部继续用同一份顺序
            next = if (playMode == PlayMode.ListRepeat || playMode == PlayMode.Shuffle) 0 else return null
        }
        return next
    }

    private fun rebuildIndex() {
        songIdToIndex.clear()
        originalList.forEachIndexed { i, song -> songIdToIndex[song.id] = i }
    }

    companion object {
        /** Fisher-Yates，与 C# ShuffleService.Shuffle 等价 */
        fun shuffle(list: List<Song>, random: Random = Random): List<Song> {
            val result = list.toMutableList()
            for (i in result.lastIndex downTo 1) {
                val j = random.nextInt(i + 1)
                val tmp = result[i]
                result[i] = result[j]
                result[j] = tmp
            }
            return result
        }
    }
}
