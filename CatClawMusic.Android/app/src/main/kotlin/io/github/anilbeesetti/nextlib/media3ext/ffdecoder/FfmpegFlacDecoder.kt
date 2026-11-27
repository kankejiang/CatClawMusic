package io.github.anilbeesetti.nextlib.media3ext.ffdecoder

import androidx.media3.common.Format
import androidx.media3.decoder.DecoderInputBuffer
import androidx.media3.decoder.SimpleDecoderOutputBuffer

/**
 * 离线转码用的小门面。
 *
 * `FfmpegAudioDecoder` 在 nextlib 里是 package-private（只给 ExoPlayer 渲染器用），
 * 想直接拿 FFmpeg 解码 FLAC 就得把调用方放进同一个包。这里只暴露转码真正需要的几个方法。
 */
class FfmpegFlacDecoder(format: Format) {

    private val decoder = FfmpegAudioDecoder(
        format,
        4, // 输入缓冲数
        4, // 输出缓冲数
        256 * 1024, // 初始输入缓冲
        false, // 输出 16bit PCM（true 是 float）
    )

    val encoding: Int get() = decoder.encoding
    val channelCount: Int get() = decoder.channelCount
    val sampleRate: Int get() = decoder.sampleRate

    fun dequeueInput(): DecoderInputBuffer? = decoder.dequeueInputBuffer()

    fun queueInput(buffer: DecoderInputBuffer) = decoder.queueInputBuffer(buffer)

    fun dequeueOutput(): SimpleDecoderOutputBuffer? = decoder.dequeueOutputBuffer()

    fun releaseOutput(buffer: SimpleDecoderOutputBuffer) = buffer.release()

    fun release() = decoder.release()
}
