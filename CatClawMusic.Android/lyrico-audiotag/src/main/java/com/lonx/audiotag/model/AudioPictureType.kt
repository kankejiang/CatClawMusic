package com.lonx.audiotag.model

enum class AudioPictureType(
    val tagLibName: String
) {
    Other("Other"),
    FileIcon("32x32 pixels 'file icon' (PNG only)"),
    OtherFileIcon("Other file icon"),
    FrontCover("Front Cover"),
    BackCover("Back Cover"),
    LeafletPage("Leaflet Page"),
    Media("Media"),
    LeadArtist("Lead Artist"),
    Artist("Artist"),
    Conductor("Conductor"),
    Band("Band"),
    Composer("Composer"),
    Lyricist("Lyricist"),
    RecordingLocation("Recording Location"),
    DuringRecording("During Recording"),
    DuringPerformance("During Performance"),
    MovieCapture("Movie Capture"),
    BrightColoredFish("A bright coloured fish"),
    Illustration("Illustration"),
    BandLogo("Band Logo"),
    PublisherLogo("Publisher Logo"),
    Unknown("Unknown");

    companion object {
        fun fromTagLibName(value: String?): AudioPictureType {
            val normalized = value.orEmpty().trim()
            if (normalized.isBlank()) return Other

            return entries.firstOrNull {
                it.tagLibName.equals(normalized, ignoreCase = true)
            } ?: when {
                normalized.equals("Cover (front)", ignoreCase = true) -> FrontCover
                normalized.equals("Cover (back)", ignoreCase = true) -> BackCover
                normalized.equals("Band/Orchestra", ignoreCase = true) -> Band
                normalized.equals("Lead performer", ignoreCase = true) -> LeadArtist
                else -> Unknown
            }
        }
    }
}

val AudioPicture.type: AudioPictureType
    get() = AudioPictureType.fromTagLibName(pictureType)