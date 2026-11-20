package com.lonx.audiotag

import com.lonx.audiotag.model.AudioPictureType
import com.lonx.audiotag.model.AudioProperties
import com.lonx.audiotag.model.AudioPropertiesReadStyle
import com.lonx.audiotag.model.Metadata
import com.lonx.audiotag.model.Picture
import com.lonx.audiotag.model.PropertyMap
import com.lonx.audiotag.model.artistPictureTypes

/**
 * An object that provides access to the native TagLib library.
 */
public object TagLib {

    init {
        System.loadLibrary("taglib")
    }

    @JvmStatic
    private external fun getAudioProperties(
        fd: Int,
        readStyle: Int,
    ): AudioProperties?

    /**
     * Get audio properties from file descriptor.
     *
     * @param fd File descriptor
     * @param readStyle Read style for audio properties to balance speed and accuracy
     */
    @JvmStatic
    public fun getAudioProperties(
        fd: Int,
        readStyle: AudioPropertiesReadStyle = AudioPropertiesReadStyle.Average,
    ): AudioProperties? = getAudioProperties(fd, readStyle.ordinal)

    /**
     * Get metadata from file descriptor.
     *
     * @param fd File descriptor
     * @param readPictures Whether to read pictures
     */
    @JvmStatic
    public external fun getMetadata(
        fd: Int,
        readPictures: Boolean = true,
    ): Metadata?

    /**
     * Get metadata property values from file descriptor.
     *
     * @param fd File descriptor
     * @param propertyName Property name
     */
    @JvmStatic
    public external fun getMetadataPropertyValues(
        fd: Int,
        propertyName: String,
    ): Array<String>?

    /**
     * Get pictures from file descriptor. There may be multiple pictures with different types.
     */
    @JvmStatic
    public external fun getPictures(fd: Int): Array<Picture>

    /**
     * Get picture with the requested type from file descriptor.
     *
     * @param description Artist name this artwork belongs to. Artist artwork may contain several
     *   pictures distinguished by their description, so when it is given:
     *   1. a picture whose description matches (ignoring case and surrounding space) is used;
     *   2. otherwise a picture **without** a description is used - those are files written before
     *      descriptions were used, so they cannot contradict the request;
     *   3. otherwise [fallbackToAny] decides: true returns a picture that carries *another* artist's
     *      description. That is a last resort the caller is expected to try only after its own
     *      alternatives (e.g. the external poster folders), because hiding the picture entirely would
     *      leave the user with no way to see - or re-assign - what is actually in the tag.
     */
    @JvmStatic
    public fun getPicture(
        fd: Int,
        pictureType: AudioPictureType,
        fallbackPictureTypes: List<AudioPictureType> = emptyList(),
        fallbackToAny: Boolean = false,
        description: String? = null,
    ): Picture? {
        val pictures = getPictures(fd)
        val types = listOf(pictureType) + fallbackPictureTypes
        val requested = description?.trim()?.takeIf { it.isNotEmpty() }

        if (requested != null) {
            return types.firstNotNullOfOrNull { type ->
                pictures.find { picture ->
                    picture.pictureType == type.tagLibName &&
                        picture.description.trim().equals(requested, ignoreCase = true)
                }
            }
                ?: types.firstNotNullOfOrNull { type ->
                    pictures.find { picture ->
                        picture.pictureType == type.tagLibName && picture.description.isBlank()
                    }
                }
                ?: if (fallbackToAny) pictures.firstOrNull() else null
        }

        return pictures.find { picture -> picture.pictureType == pictureType.tagLibName }
            ?: fallbackPictureTypes.firstNotNullOfOrNull { fallbackType ->
                pictures.find { picture -> picture.pictureType == fallbackType.tagLibName }
            }
            ?: if (fallbackToAny) pictures.firstOrNull() else null
    }

    /**
     * Get front cover from file descriptor.
     */
    @JvmStatic
    public fun getFrontCover(fd: Int): Picture? {
        return getPicture(
            fd = fd,
            pictureType = AudioPictureType.FrontCover,
            fallbackToAny = true
        )
    }

    /**
     * Save metadata by file descriptor.
     *
     * @param fd File descriptor
     * @param propertyMap Property map to save
     *
     * @return Whether the operation was successful
     */
    @JvmStatic
    public external fun savePropertyMap(
        fd: Int,
        propertyMap: PropertyMap,
    ): Boolean

    /**
     * Save pictures by file descriptor.
     *
     * @param fd File descriptor
     * @param pictures Pictures to save
     *
     * @return Whether the operation was successful
     */
    @JvmStatic
    public external fun savePictures(
        fd: Int,
        pictures: Array<Picture>,
    ): Boolean
}
