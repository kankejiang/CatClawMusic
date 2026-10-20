package com.catclaw.music.kt.data.service

/**
 * 通知栏那两个自定义按钮要用的图标资源 id。
 *
 * 为什么由外面注入：`:data` 模块里一个 drawable 都没有（只有 `values/`），而图标天然是 app 的资产。
 * 在这里复制三份同样的 vector 也能编译，但同名资源跨模块会**互相覆盖**（app 优先），
 * 于是「通知栏那颗心」和「界面上那颗心」哪天改了一个、另一个没变，没人说得清哪个生效。
 * 交给 `:app` 的 DI 注入之后，两边永远是同一份资产。
 *
 * 2026-10-20 用户反馈「桌面歌词和收藏按钮只有一种形态」：桌面歌词原本**只注入了一张 on 图标**
 * （off 根本没有资源），按钮怎么切都是同一个样子。现在开/关各一张。
 */
class NotificationIcons(
    /** 桌面歌词**开**（实心面板） */
    val desktopLyricOn: Int,
    /** 桌面歌词**关**（空心面板） */
    val desktopLyricOff: Int,
    val favoriteOn: Int,
    val favoriteOff: Int,
)
