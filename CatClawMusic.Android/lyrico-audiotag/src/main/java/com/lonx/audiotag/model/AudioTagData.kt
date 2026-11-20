package com.lonx.audiotag.model

import androidx.annotation.Keep
import java.util.ArrayList

@Keep
data class AudioTagData(
    val title: String? = null,
    val artist: String? = null,
    val album: String? = null,
    val albumArtist: String? = null,
    val genre: String? = null,
    val date: String? = null,
    val language: String? = null,
    val trackNumber: String? = null,
    val discNumber: Int? = null,

    val composer: String? = null,
    val lyricist: String? = null,
    val comment: String? = null,
    val lyrics: String? = null,
    val copyright: String? = null,
    val rating: Int? = null,
    val replayGainTrackGain: String? = null,
    val replayGainTrackPeak: String? = null,
    val replayGainAlbumGain: String? = null,
    val replayGainAlbumPeak: String? = null,
    val replayGainReferenceLoudness: String? = null,
    val fileName: String = "",
    val durationMilliseconds: Int = 0,
    val bitrate: Int = 0,
    val sampleRate: Int = 0,
    val channels: Int = 0,

    val rawProperties: Map<String, Array<String>>? = null,
    val customFields: List<CustomTagField> = emptyList(),

    val pictures: List<AudioPicture> = ArrayList(),
    val picUrl: String? = null,
    val supportsTypedPictures: Boolean = false,
)

@Keep
data class CustomTagField(
    val key: String = "",
    val value: String = ""
)

@Keep
data class AudioPicture(
    val data: ByteArray,
    val mimeType: String = "image/jpeg",
    val description: String = "",
    val pictureType: String = "Front Cover"
) {
    /**
     * 比较图片数据与归属信息。
     *
     * 早期实现只比较 [data]，于是「同图不同描述」的两张图片互相相等：改描述不会让列表或
     * [AudioTagData] 变得「不相等」，按列表做 `remember`/去重就会漏掉艺术家归属的修改。
     * 图片类型同理——艺术家图片靠 [description] 区分归属、靠 [pictureType] 区分用途。
     */
    override fun equals(other: Any?): Boolean {
        if (this === other) return true
        if (other !is AudioPicture) return false
        if (mimeType != other.mimeType) return false
        if (description != other.description) return false
        if (pictureType != other.pictureType) return false
        return data.contentEquals(other.data)
    }

    override fun hashCode(): Int {
        var result = data.contentHashCode()
        result = 31 * result + mimeType.hashCode()
        result = 31 * result + description.hashCode()
        result = 31 * result + pictureType.hashCode()
        return result
    }
}
