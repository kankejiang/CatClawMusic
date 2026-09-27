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

    var playMode: PlayMode = PlayMode.ListRepeat
        set(value) {
            val old = field
            field = value
            if (value == PlayMode.Shuffle && shuffledList.isEmpty()) {
                enableShuffle()
            } else if (old == PlayMode.Shuffle && value != PlayMode.Shuffle && currentIndex >= 0) {
                // 必须从**洗牌列表**里取当前曲再映射回原始索引：field 已是新模式，
                // 此刻走 currentSong 会按旧索引读到原始列表里的另一首歌
                val playing = shuffledList.getOrNull(currentIndex)
                if (playing != null) songIdToIndex[playing.id]?.let { currentIndex = it }
            }
        }

    /** 私人漫游（FM）模式：激活时 UI 只允许「单曲循环 ↔ 无限」，其余模式禁用 */
    var isFmMode: Boolean = false

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
        this.currentIndex = if (currentIndex in songs.indices) currentIndex else -1
        history.clear()
        playMode = mode
        rebuildIndex()
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

    fun songs(): List<Song> = activeList().toList()

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
