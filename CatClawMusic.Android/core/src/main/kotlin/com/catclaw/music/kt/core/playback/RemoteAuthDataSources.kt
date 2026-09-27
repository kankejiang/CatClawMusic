package com.catclaw.music.kt.core.playback

import android.content.Context
import android.net.Uri
import androidx.media3.datasource.DataSpec
import androidx.media3.datasource.DataSource
import androidx.media3.datasource.DefaultDataSource
import androidx.media3.datasource.DefaultHttpDataSource
import androidx.media3.datasource.ResolvingDataSource
import com.catclaw.music.kt.core.net.UrlCredentials

/**
 * 远程音源的取流工厂。
 *
 * WebDAV 的播放地址是 `http://user:pass@host/dav/…` 形态（与 C# `BuildStreamUrl` 保持一致），
 * 但 `HttpURLConnection` 不会把 URL 里的 userinfo 变成 `Authorization` 头 ——
 * 真机上的表现是每一次 GET 都吃 401（曲库导入正常、就是不出声）。
 * 这里在解析 DataSpec 时把凭据摘出来，改发标准 Basic 头，URL 本身退回无凭据形态。
 */
object RemoteAuthDataSources {

    fun factory(context: Context): DataSource.Factory {
        val http = DefaultHttpDataSource.Factory()
            .setAllowCrossProtocolRedirects(true)
            .setConnectTimeoutMs(10_000)
            .setReadTimeoutMs(30_000)
        // 本地 file:// 与 content:// 也得能开：DefaultDataSource 按 scheme 分派，
        // 直接把解析器挂在 Http 工厂上会让本地曲全部播不出来
        val switching = DefaultDataSource.Factory(context, http)
        return ResolvingDataSource.Factory(switching) { dataSpec -> withBasicAuth(dataSpec) }
    }

    private fun withBasicAuth(dataSpec: DataSpec): DataSpec {
        val (cleanUrl, header) = UrlCredentials.split(dataSpec.uri.toString())
        if (header == null) return dataSpec
        val builder = dataSpec.buildUpon()
        builder.setUri(Uri.parse(cleanUrl))
        builder.setHttpRequestHeaders(mapOf("Authorization" to header))
        return builder.build()
    }

}
