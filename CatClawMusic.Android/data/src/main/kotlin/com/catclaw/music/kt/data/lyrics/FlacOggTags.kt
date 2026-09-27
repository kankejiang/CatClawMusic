package com.catclaw.music.kt.data.lyrics

import java.io.RandomAccessFile

/** FLAC：跳元数据块找 type=4（VORBIS_COMMENT），只读那一块 */
internal object FlacTag {

    private const val VORBIS_COMMENT = 4

    fun lyrics(raf: RandomAccessFile, length: Long): String? {
        var cursor = 4L
        while (cursor + 4 <= length) {
            val header = TagBytes.readAt(raf, cursor, 4)
            if (header.size < 4) return null
            val last = header[0].toInt() and 0x80 != 0
            val type = header[0].toInt() and 0x7F
            val size = ((header[1].toInt() and 0xFF) shl 16) or
                ((header[2].toInt() and 0xFF) shl 8) or (header[3].toInt() and 0xFF)
            cursor += 4
            if (type == VORBIS_COMMENT) {
                return VorbisTag.lyrics(TagBytes.readAt(raf, cursor, size))
            }
            if (size <= 0 || last) return null
            cursor += size
        }
        return null
    }
}

/** OGG：在头部窗口里找 comment 包的 "vorbis" 魔数（前一字节是包类型 3） */
internal object OggTag {

    private val MARKER = byteArrayOf(3, 'v'.code.toByte(), 'o'.code.toByte(), 'r'.code.toByte(), 'b'.code.toByte(), 'i'.code.toByte(), 's'.code.toByte())

    fun lyrics(head: ByteArray): String? {
        val index = indexOfMarker(head)
        if (index < 0) return null
        return VorbisTag.lyrics(head.copyOfRange(index + MARKER.size, head.size))
    }

    private fun indexOfMarker(head: ByteArray): Int {
        for (i in 0..head.size - MARKER.size) {
            var hit = true
            for (j in MARKER.indices) if (head[i + j] != MARKER[j]) { hit = false; break }
            if (hit) return i
        }
        return -1
    }
}
