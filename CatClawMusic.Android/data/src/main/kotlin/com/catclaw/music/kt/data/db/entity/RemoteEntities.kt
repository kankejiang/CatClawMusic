package com.catclaw.music.kt.data.db.entity

import androidx.room.ColumnInfo
import androidx.room.Entity
import androidx.room.Index
import androidx.room.PrimaryKey

/** 远程音乐服务连接配置（Subsonic / WebDAV / SMB / 猫爪服务器） */
@Entity(tableName = "ConnectionProfiles")
data class ConnectionProfileEntity(
    @PrimaryKey(autoGenerate = true) @ColumnInfo(name = "Id") val id: Int = 0,
    @ColumnInfo(name = "Name") val name: String,
    @ColumnInfo(name = "Protocol") val protocol: Int = 0,
    @ColumnInfo(name = "Host") val host: String = "",
    @ColumnInfo(name = "Port") val port: Int = 0,
    @ColumnInfo(name = "UserName") val userName: String = "",
    @ColumnInfo(name = "Password") val password: String = "",
    @ColumnInfo(name = "BasePath") val basePath: String = "",
    @ColumnInfo(name = "IsEnabled") val isEnabled: Boolean = true,
    @ColumnInfo(name = "ApiVersion") val apiVersion: String? = null,
    @ColumnInfo(name = "ClientName") val clientName: String? = null,
    @ColumnInfo(name = "UseHttps") val useHttps: Boolean = false,
    @ColumnInfo(name = "DomainName") val domainName: String? = null,
    @ColumnInfo(name = "ShareName") val shareName: String? = null,
    @ColumnInfo(name = "ServerType") val serverType: Int = 0,
)

/** 网络曲目的本地缓存映射（与 core/audio/AudioCache 的文件缓存配对的元数据） */
@Entity(tableName = "CachedSongs")
data class CachedSongEntity(
    @PrimaryKey(autoGenerate = true) @ColumnInfo(name = "Id") val id: Int = 0,
    @ColumnInfo(name = "SongId") val songId: Int,
    @ColumnInfo(name = "LocalPath") val localPath: String,
    @ColumnInfo(name = "CachedAt") val cachedAt: Long = 0,
    @ColumnInfo(name = "FileSize") val fileSize: Long = 0,
)

@Entity(tableName = "ChatMessageRecord", indices = [Index("Timestamp")])
data class ChatMessageEntity(
    @PrimaryKey(autoGenerate = true) @ColumnInfo(name = "Id") val id: Int = 0,
    @ColumnInfo(name = "Role") val role: String,
    @ColumnInfo(name = "Content") val content: String,
    @ColumnInfo(name = "Timestamp") val timestamp: Long = 0,
)

/** schema 迁移位标记，C# 侧靠它跑一次性迁移 */
@Entity(tableName = "MigrationFlag")
data class MigrationFlagEntity(
    @PrimaryKey @ColumnInfo(name = "Name") val name: String,
    @ColumnInfo(name = "Value") val value: String? = null,
)
