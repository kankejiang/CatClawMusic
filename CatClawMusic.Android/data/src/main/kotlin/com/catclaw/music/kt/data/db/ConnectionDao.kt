package com.catclaw.music.kt.data.db

import androidx.room.Dao
import androidx.room.Query
import androidx.room.Upsert
import com.catclaw.music.kt.core.model.ConnectionProfile
import com.catclaw.music.kt.core.model.ProtocolType
import com.catclaw.music.kt.data.db.entity.ConnectionProfileEntity
import kotlinx.coroutines.flow.Flow

/**
 * 连接配置的读写。C# 侧 Songs 表没有 ProfileId 列，远程曲回落到具体服务器时
 * 按 host + port 匹配（见 [byOrigin]），这里保持同一套解析规则，不偷偷加列。
 */
@Dao
interface ConnectionDao {

    @Query("SELECT * FROM ConnectionProfiles ORDER BY Id ASC")
    fun observeAll(): Flow<List<ConnectionProfileEntity>>

    @Query("SELECT * FROM ConnectionProfiles ORDER BY Id ASC")
    suspend fun all(): List<ConnectionProfileEntity>

    @Query("SELECT * FROM ConnectionProfiles WHERE Id = :id")
    suspend fun byId(id: Int): ConnectionProfileEntity?

    @Query("SELECT * FROM ConnectionProfiles WHERE Host = :host AND Port = :port LIMIT 1")
    suspend fun byOrigin(host: String, port: Int): ConnectionProfileEntity?

    @Query("SELECT * FROM ConnectionProfiles WHERE Name = :name LIMIT 1")
    suspend fun byName(name: String): ConnectionProfileEntity?

    @Upsert
    suspend fun upsert(profile: ConnectionProfileEntity): Long

    @Query("DELETE FROM ConnectionProfiles WHERE Id = :id")
    suspend fun delete(id: Int)
}

/** 行 → 领域模型；Protocol 越界退回 WebDAV（与 C# 未知值兜底一致） */
fun ConnectionProfileEntity.toModel(): ConnectionProfile = ConnectionProfile(
    id = id,
    name = name,
    protocol = ProtocolType.entries.getOrElse(protocol) { ProtocolType.WebDAV },
    host = host,
    port = port,
    userName = userName,
    password = password,
    basePath = basePath,
    isEnabled = isEnabled,
    apiVersion = apiVersion ?: "1.16.1",
    clientName = clientName ?: "CatClawMusic",
    useHttps = useHttps,
    domainName = domainName,
    shareName = shareName,
    serverType = serverType,
)
