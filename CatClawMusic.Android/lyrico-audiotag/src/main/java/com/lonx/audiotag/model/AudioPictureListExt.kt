// lyrico-audiotag/src/main/java/com/lonx/audiotag/model/AudioPictureListExt.kt
package com.lonx.audiotag.model

/**
 * 标签中可承载艺术家图片的图片类型。
 *
 * 同一首歌可以有多张艺术家图片：它们用 [AudioPicture.description] 区分归属的艺术家，
 * 因此这里不再做「只取第一张」的假设。
 */
val artistPictureTypes: List<AudioPictureType> = listOf(
    AudioPictureType.Artist,
    AudioPictureType.LeadArtist,
    AudioPictureType.Band
)

fun List<AudioPicture>.frontCoverOrFallback(): AudioPicture? {
    return firstOrNull { it.type == AudioPictureType.FrontCover }
        ?: firstOrNull { it.type == AudioPictureType.Other }
        ?: firstOrNull()
}

fun List<AudioPicture>.pictureOfType(type: AudioPictureType): AudioPicture? {
    return firstOrNull { it.type == type }
}

fun List<AudioPicture>.replacePicture(
    picture: AudioPicture,
    type: AudioPictureType = AudioPictureType.FrontCover
): List<AudioPicture> {
    val normalizedPicture = picture.copy(pictureType = type.tagLibName)
    return listOf(normalizedPicture) + filterNot { it.type == type }
}

fun List<AudioPicture>.removePictureType(
    type: AudioPictureType
): List<AudioPicture> {
    return filterNot { it.type == type }
}

/** 追加一张图片。 */
fun List<AudioPicture>.addPicture(picture: AudioPicture): List<AudioPicture> {
    return this + picture
}
