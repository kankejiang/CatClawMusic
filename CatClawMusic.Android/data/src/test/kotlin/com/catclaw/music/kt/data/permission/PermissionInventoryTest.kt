package com.catclaw.music.kt.data.permission

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** 权限页「快照 → 行」这一步的闸门：行序、状态映射、什么时候给"去授权" */
class PermissionInventoryTest {

    @Test
    fun `四行顺序与 MAUI 一致，一项不多一项不少`() {
        val rows = PermissionInventory.rows(PermissionSnapshot(audio = true, manageStorage = false, overlay = true, notification = false))
        assertEquals(
            listOf(PermissionKind.Audio, PermissionKind.ManageStorage, PermissionKind.Overlay, PermissionKind.Notification),
            rows.map { it.kind },
        )
        assertEquals(listOf(true, false, true, false), rows.map { it.granted })
    }

    @Test
    fun `只有未授予的行才给「去授权」`() {
        val rows = PermissionInventory.rows(PermissionSnapshot(audio = true, manageStorage = false, overlay = true, notification = false))
        assertEquals(listOf(false, true, false, true), rows.map { PermissionInventory.actionIsRequest(it) })
    }

    @Test
    fun `快照与行的对应取自 kind 而不是位置 —— 漏一项会立刻红`() {
        val all = PermissionSnapshot(true, true, true, true)
        PermissionInventory.ORDER.forEach {
            assertTrue(it.name, PermissionInventory.grantedOf(all, it))
        }
        // 逐项单独关掉，确认关的是它自己而不是别人
        PermissionInventory.ORDER.forEach { kind ->
            val off = PermissionSnapshot(
                audio = kind != PermissionKind.Audio,
                manageStorage = kind != PermissionKind.ManageStorage,
                overlay = kind != PermissionKind.Overlay,
                notification = kind != PermissionKind.Notification,
            )
            assertFalse(kind.name, PermissionInventory.grantedOf(off, kind))
            assertEquals(
                1,
                PermissionInventory.rows(off).count { !it.granted },
            )
        }
    }
}
