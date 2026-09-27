package com.catclaw.music.kt.data.ai

import android.content.Context
import android.database.sqlite.SQLiteDatabase
import com.catclaw.music.kt.core.ai.WordPair
import com.catclaw.music.kt.core.ai.WordSource
import java.io.File
import javax.inject.Inject
import javax.inject.Singleton

/**
 * `wordlib.db` 的只读 SQLite 实现，SQL 与 MAUI `YukiWordLibrary` 逐条对齐。
 *
 * ⚠ **必须先把资源复制到可写目录**：APK 内的 asset 是压缩存储的，
 * `SQLiteDatabase.openDatabase` 打不开压缩流（C# 那边同一处理）。
 * 复制一次常驻，之后每次查询都是按需读表 —— 词库有几千条，全量进内存不划算。
 */
@Singleton
class SqliteWordSource @Inject constructor(
    private val appContext: Context,
) : WordSource {

    private val db: SQLiteDatabase? by lazy { open() }

    private fun open(): SQLiteDatabase? = runCatching {
        val target = File(appContext.filesDir, "wordlib.db")
        if (!target.exists()) {
            appContext.assets.open(ASSET).use { input -> target.outputStream().use(input::copyTo) }
        }
        SQLiteDatabase.openDatabase(target.absolutePath, null, SQLiteDatabase.OPEN_READONLY)
    }.getOrNull()

    override fun matches(message: String, limit: Int): List<WordPair> {
        val database = db ?: return emptyList()
        // 触发词是用户输入的子串 = 命中；按触发词长度降序，最具体的排在前
        val sql = "SELECT Trigger, Reply FROM WordEntry WHERE ? LIKE '%' || Trigger || '%' " +
            "ORDER BY LENGTH(Trigger) DESC LIMIT ?"
        return database.rawQuery(sql, arrayOf(message, limit.toString())).use { cursor ->
            buildList {
                while (cursor.moveToNext()) {
                    add(WordPair(cursor.getString(0), cursor.getString(1)))
                }
            }
        }
    }

    override fun random(limit: Int): List<WordPair> {
        val database = db ?: return emptyList()
        return database
            .rawQuery("SELECT Trigger, Reply FROM WordEntry ORDER BY RANDOM() LIMIT ?", arrayOf(limit.toString()))
            .use { cursor ->
                buildList {
                    while (cursor.moveToNext()) {
                        add(WordPair(cursor.getString(0), cursor.getString(1)))
                    }
                }
            }
    }

    private companion object {
        const val ASSET = "wordlib.db"
    }
}
