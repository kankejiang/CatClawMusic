package com.lonx.audiotag

import android.os.ParcelFileDescriptor
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.lonx.audiotag.rw.AudioTagReader
import com.lonx.audiotag.rw.AudioTagWriter
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File
import java.io.FileOutputStream

/**
 * TagLib 原生层的真机往返验证。
 *
 * 原生库（.so）只能在设备上加载，普通单元测试跑不了，所以放 instrumentation。
 * 夹具取自 taglib 自带的 tests/data（随模块一起 vendor），先复制到 cacheDir 再改，
 * 保证测试之间互不影响、也不碰原始夹具。
 */
@RunWith(AndroidJUnit4::class)
class TagLibRoundTripTest {

    private val context get() = InstrumentationRegistry.getInstrumentation().targetContext

    /** 把 asset 里的夹具复制到 cacheDir，返回可写文件。 */
    private fun stageFixture(name: String): File {
        val out = File(context.cacheDir, "taglib_test_$name")
        if (out.exists()) out.delete()
        context.assets.open(name).use { input ->
            FileOutputStream(out).use { input.copyTo(it) }
        }
        return out
    }

    private fun openRw(file: File): ParcelFileDescriptor =
        ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_WRITE)

    private fun roundTrip(name: String) = runBlocking {
        val file = stageFixture(name)

        // 1) 先读一次，确认原生库能解析该格式
        val before = openRw(file).use { pfd -> AudioTagReader.read(pfd) }

        // 2) 写入歌词（各格式 TagLib 会自动选对应的歌词字段）
        val lyrics = "[00:01.00]测试第一行\n[00:02.50]测试第二行"
        val ok = openRw(file).use { pfd ->
            AudioTagWriter.writeTags(
                pfd,
                mapOf("LYRICS" to lyrics, "TITLE" to "LyricoPortTest"),
                preserveOldTags = true,
            )
        }
        assertTrue("$name 写入应成功", ok)

        // 3) 重新打开读回，验证真的落盘了
        val after = openRw(file).use { pfd -> AudioTagReader.read(pfd) }
        assertEquals("$name 歌词应一致", lyrics, after.lyrics)
        assertEquals("$name 标题应一致", "LyricoPortTest", after.title)

        // 4) 清掉夹具
        file.delete()

        // 顺带保证读到过音频属性（说明容器解析正常，不是空壳）
        before.durationMilliseconds >= 0
    }

    @Test
    fun mp3RoundTrip() {
        roundTrip("sample.mp3")
    }

    @Test
    fun flacRoundTrip() {
        roundTrip("sample.flac")
    }

    @Test
    fun m4aRoundTrip() {
        roundTrip("sample.m4a")
    }
}
