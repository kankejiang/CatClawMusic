package com.catclaw.music.kt.data.repository.netease

import com.catclaw.music.kt.data.netease.NeteaseApiClient
import com.catclaw.music.kt.data.netease.OnlineSong
import javax.inject.Inject
import javax.inject.Singleton

/** 在线曲库查询唯一入口（搜索 / 专辑 / 歌手），VM 不再直连 ApiClient */
@Singleton
class NeteaseCatalogRepository @Inject constructor(
    private val api: NeteaseApiClient,
) {
    suspend fun searchSongs(keyword: String, page: Int = 1, pageSize: Int = 30): List<OnlineSong> {
        val result = runCatching { api.searchSongs(keyword, page, pageSize) }
        com.catclaw.music.kt.core.diagnostic.AppLog.d(
            "NeteaseSearch",
            "q=$keyword size=${result.getOrNull()?.size ?: -1} err=${result.exceptionOrNull()?.message}",
        )
        return result.getOrNull().orEmpty()
    }
}
