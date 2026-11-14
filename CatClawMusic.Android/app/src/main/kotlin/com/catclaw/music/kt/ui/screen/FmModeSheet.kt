package com.catclaw.music.kt.ui.screen

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import com.catclaw.music.kt.data.repository.netease.FmMode

/**
 * 私人漫游模式选择抽屉 —— 1:1 对齐网易云官方客户端：
 * 顶部「私人漫游 · 当前模式 ⌃」+ 推荐模式大卡 + 场景模式 4 列图标网格。
 *
 * 数据侧：推荐模式走 /api/v1/radio/get 的 mode 参数；场景模式按插件
 * `FillSceneBatchAsync` 用中文名当关键词搜歌建池（官方 SCENE_RCMD 原生接口无效）。
 * 「拼图模式」是官方客户端的互动玩法，插件/接口都没有对应能力，这里渲染成禁用卡而不做假选项。
 */
private data class FmScene(val label: String, val icon: String, val isNew: Boolean = false)

/** 官方客户端场景模式全表（43 项，前 36 项与插件 FmSceneCodes 一致） */
private val FM_SCENES = listOf(
    FmScene("伤感", "🌙"), FmScene("运动", "🏃"), FmScene("助眠", "😴"), FmScene("放松", "🧘"),
    FmScene("欢快", "😄"), FmScene("抒情", "🌸"), FmScene("治愈", "💊"), FmScene("专注", "📖"),
    FmScene("情歌", "💕"), FmScene("R&B", "🎵", true), FmScene("下雨天", "☔", true), FmScene("打游戏", "🎮", true),
    FmScene("说唱", "🎤"), FmScene("K-Pop", "✨"), FmScene("宝藏原创", "💎"), FmScene("电音", "⚡"),
    FmScene("出行", "🚇"), FmScene("洗澡", "🛁"), FmScene("咖啡馆", "☕"), FmScene("摇滚", "🎸"),
    FmScene("励志", "💪"), FmScene("华语", "🇨🇳"), FmScene("欧美", "🌍"), FmScene("粤语", "🗣️"),
    FmScene("慢摇DJ", "🎧"), FmScene("经典", "🎼"), FmScene("轻音乐", "🍃"), FmScene("国风", "🏮"),
    FmScene("民谣", "🪕"), FmScene("二次元", "🌸"), FmScene("古典", "🎻"), FmScene("爵士", "🎷"),
    FmScene("日语", "🗾"), FmScene("全球", "🌐"), FmScene("法语", "🥐"), FmScene("蓝调", "🎹"),
    FmScene("舞蹈", "💃"), FmScene("拉丁", "🕺"), FmScene("放克", "🪩"), FmScene("乡村乐", "🤠"),
    FmScene("音乐剧", "🎭"), FmScene("影视", "🎬"), FmScene("热歌", "🔥"),
)

private val SheetBg = Color(0xF5202023)
private val CardBg = Color(0x14FFFFFF)
private val CardSelected = Color(0x2EFFFFFF)
private val TitleInk = Color(0xFFF2F2F2)
private val SubInk = Color(0xFF9E9E9E)

@Composable
fun FmModeSheet(
    label: String,
    currentMode: FmMode,
    currentScene: String?,
    onPickMode: (FmMode) -> Unit,
    onPickScene: (String) -> Unit,
    onDismiss: () -> Unit,
) {
    Dialog(
        onDismissRequest = onDismiss,
        properties = DialogProperties(usePlatformDefaultWidth = false),
    ) {
        Box(
            modifier = Modifier
                .fillMaxSize()
                .background(SheetBg),
        ) {
            Column(
                modifier = Modifier
                    .fillMaxSize()
                    .verticalScroll(rememberScrollState())
                    .padding(horizontal = 20.dp, vertical = 18.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                // 顶栏：左关闭 / 中标题（官方同款）
                Box(modifier = Modifier.fillMaxWidth().height(44.dp)) {
                    Text(
                        text = "×",
                        fontSize = 26.sp,
                        color = TitleInk,
                        modifier = Modifier
                            .align(Alignment.CenterStart)
                            .clickable(onClick = onDismiss)
                            .padding(horizontal = 8.dp),
                    )
                    Row(modifier = Modifier.align(Alignment.Center)) {
                        Text("私人漫游 · $label", fontSize = 15.sp, color = TitleInk)
                        Text(" ⌃", fontSize = 15.sp, color = TitleInk)
                    }
                }
                Spacer(Modifier.height(6.dp))

                val recommendModeSelected: (FmMode) -> Boolean = {
                    currentScene == null && currentMode == it
                }
                ModeCard(
                    title = "默认模式",
                    subtitle = "沿着目前喜好继续聆听",
                    selected = recommendModeSelected(FmMode.DEFAULT),
                    onClick = { onPickMode(FmMode.DEFAULT) },
                )
                ModeCard(
                    title = "熟悉模式",
                    subtitle = "喜欢过的歌曲与相似推荐",
                    selected = recommendModeSelected(FmMode.FAMILIAR),
                    onClick = { onPickMode(FmMode.FAMILIAR) },
                )
                ModeCard(
                    title = "探索模式",
                    subtitle = "偏好曲风与潜力好歌",
                    selected = recommendModeSelected(FmMode.EXPLORE),
                    onClick = { onPickMode(FmMode.EXPLORE) },
                )
                ModeCard(
                    title = "拼图模式",
                    subtitle = "边听歌边拼歌曲封面 · 本端暂未开放",
                    selected = false,
                    enabled = false,
                    onClick = {},
                )

                Spacer(Modifier.height(10.dp))
                Text("场景模式", fontSize = 13.sp, color = SubInk)
                Spacer(Modifier.height(2.dp))

                FM_SCENES.chunked(4).forEach { row ->
                    Row(modifier = Modifier.fillMaxWidth()) {
                        row.forEach { scene ->
                            SceneTile(
                                scene = scene,
                                selected = currentScene == scene.label,
                                onClick = { onPickScene(scene.label) },
                                modifier = Modifier.weight(1f),
                            )
                        }
                        repeat(4 - row.size) { Spacer(Modifier.weight(1f)) }
                    }
                }
                Spacer(Modifier.height(24.dp))
            }
        }
    }
}

@Composable
private fun ModeCard(
    title: String,
    subtitle: String,
    selected: Boolean,
    enabled: Boolean = true,
    onClick: () -> Unit,
) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(16.dp))
            .background(if (selected) CardSelected else CardBg)
            .clickable(enabled = enabled, onClick = onClick)
            .padding(vertical = 14.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        Text(
            text = title,
            fontSize = 16.sp,
            fontWeight = if (selected) FontWeight.Bold else FontWeight.Normal,
            color = if (enabled) TitleInk else SubInk,
            textAlign = TextAlign.Center,
        )
        Text(text = subtitle, fontSize = 12.sp, color = SubInk, textAlign = TextAlign.Center)
    }
}

@Composable
private fun SceneTile(
    scene: FmScene,
    selected: Boolean,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Box(
        modifier = modifier
            .padding(3.dp)
            .clip(RoundedCornerShape(14.dp))
            .background(if (selected) CardSelected else CardBg)
            .clickable(onClick = onClick)
            .padding(vertical = 12.dp),
        contentAlignment = Alignment.Center,
    ) {
        if (scene.isNew) {
            Text(
                text = "NEW",
                fontSize = 8.sp,
                color = Color(0xFFFF8A80),
                modifier = Modifier
                    .align(Alignment.TopEnd)
                    .padding(end = 6.dp),
            )
        }
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Text(text = scene.icon, fontSize = 22.sp)
            Spacer(Modifier.height(4.dp))
            Text(text = scene.label, fontSize = 12.sp, color = TitleInk, textAlign = TextAlign.Center)
        }
    }
}
