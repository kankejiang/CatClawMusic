package com.catclaw.music.kt.data.repository.scan

import com.catclaw.music.kt.data.di.IoDispatcher
import com.catclaw.music.kt.data.library.LibraryScanner
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.withContext

/** 本地扫描唯一入口：`scanFull()` 是全库文件遍历，必须在 IO 线程 */
@Singleton
class ScanRepository @Inject constructor(
    private val scanner: LibraryScanner,
    @IoDispatcher private val io: CoroutineDispatcher,
) {
    suspend fun scanFull() = withContext(io) { scanner.scanFull() }
}
