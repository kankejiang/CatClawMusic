package com.catclaw.music.kt.ui.settings

import androidx.compose.foundation.background
import androidx.compose.foundation.border
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
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.ai.ChatMemoryRules
import com.catclaw.music.kt.core.ai.LlmConfig
import com.catclaw.music.kt.core.ai.LlmProviderInfo
import com.catclaw.music.kt.core.theme.CatClawPalette
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.glassCard

/**
 * AI 设置（2026-11-07 重设计）：**填一个 Key 就能用**。
 *
 * 旧版把「模型配置 / 执行轮数 / 推理力度 / 长期记忆」四块平铺一页，光参数胶囊就有十来颗，
 * 想用 AI 的人得先弄懂这些参数是什么。现在只留三件事：选服务商 → 填 Key → 点「连接并获取模型」。
 * 地址、温度、轮数上限、推理力度、上下文长度全部是**代码里的默认值**，界面上一项都不出现。
 *
 * 两个下拉都是**就地展开**（不弹窗）：菜单是页面流的一部分，不会盖住下面的卡片。
 */
@Composable
fun AiSettingsPanel(viewModel: SettingsViewModel = hiltViewModel()) {
    val palette = LocalCatClawPalette.current
    val configs by viewModel.llmConfigs.collectAsStateWithLifecycle()
    val currentName by viewModel.currentConfigName.collectAsStateWithLifecycle()
    val memories by viewModel.memories.collectAsStateWithLifecycle()

    val ink = Color(palette.textPrimary)
    val sub = Color(palette.textSecondary)
    val accent = Color(palette.primaryLight)

    val current = configs.firstOrNull { it.name == currentName } ?: configs.firstOrNull()
    var providerId by remember(current?.provider) {
        mutableStateOf(current?.provider ?: LlmProviderInfo.ALL.first().id)
    }
    var apiKey by remember(current?.apiKey) { mutableStateOf(current?.apiKey.orEmpty()) }
    var model by remember(current?.model) { mutableStateOf(current?.model.orEmpty()) }

    var providerOpen by remember { mutableStateOf(false) }
    var modelOpen by remember { mutableStateOf(false) }
    var busy by remember { mutableStateOf(false) }
    // 注意：这里**不能**把 current?.apiKey 当 remember 的 key ——
    // 保存配置后 configs 会回流，current 变了 key 就变，列表被重置成 null，
    // 于是「已连接 · 可选 N 个模型」永远显示 0（用户实测到的问题）。
    var fetched by remember { mutableStateOf<List<String>?>(null) }
    var statusOk by remember { mutableStateOf<Boolean?>(null) }

    val provider = LlmProviderInfo.byId(providerId) ?: LlmProviderInfo.ALL.first()
    val models = fetched ?: provider.presetModels.ifEmpty {
        listOfNotNull(provider.defaultModel.ifBlank { null })
    }

    var memoryOpen by remember { mutableStateOf(false) }
    var memoryDraft by remember { mutableStateOf("") }
    val clipboard = LocalClipboardManager.current

    // ① 服务商
    AiCard {
        AiLabel(stringResource(R.string.ai_provider), sub)
        AiSelect(
            badge = provider.name.take(1),
            title = provider.name,
            tail = provider.defaultApiUrl.removePrefix("https://"),
            open = providerOpen,
            ink = ink,
            accent = accent,
            palette = palette,
            onClick = { providerOpen = !providerOpen },
        )
        if (providerOpen) {
            AiMenu {
                LlmProviderInfo.ALL.forEach { p ->
                    AiOption(
                        title = p.name,
                        sub = p.defaultApiUrl.removePrefix("https://"),
                        selected = p.id == providerId,
                        ink = ink,
                        subInk = sub,
                        accent = accent,
                        onClick = {
                            providerId = p.id
                            providerOpen = false
                            // 换服务商 = 换一家账号体系：Key 与模型都要重来
                            apiKey = ""
                            model = p.defaultModel
                            fetched = null
                            statusOk = null
                        },
                    )
                }
            }
        }
    }

    // ② API Key + 连接
    AiCard {
        AiLabel(stringResource(R.string.ai_api_key), sub)
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .height(50.dp)
                .clip(RoundedCornerShape(14.dp))
                .background(ink.copy(alpha = 0.05f))
                .border(1.dp, ink.copy(alpha = 0.12f), RoundedCornerShape(14.dp))
                .padding(horizontal = 14.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            BasicTextField(
                value = apiKey,
                onValueChange = { apiKey = it; statusOk = null },
                singleLine = true,
                textStyle = TextStyle(color = ink, fontSize = 14.sp),
                visualTransformation = PasswordVisualTransformation(),
                modifier = Modifier.weight(1f),
            )
            Spacer(Modifier.size(8.dp))
            Text(
                text = stringResource(R.string.ai_paste),
                fontSize = 11.sp,
                color = sub,
                modifier = Modifier
                    .clip(RoundedCornerShape(8.dp))
                    .border(1.dp, ink.copy(alpha = 0.14f), RoundedCornerShape(8.dp))
                    .clickable {
                        val text = clipboard.getText()?.text?.trim().orEmpty()
                        if (text.isNotEmpty()) {
                            apiKey = text
                            statusOk = null
                        }
                    }
                    .padding(horizontal = 8.dp, vertical = 4.dp),
            )
        }

        Spacer(Modifier.height(14.dp))
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(50.dp)
                .clip(RoundedCornerShape(15.dp))
                .background(if (busy) ink.copy(alpha = 0.08f) else Color(palette.primary).copy(alpha = 0.26f))
                .border(1.dp, Color(palette.primary).copy(alpha = 0.5f), RoundedCornerShape(15.dp))
                .clickable(enabled = !busy && apiKey.isNotBlank()) {
                    busy = true
                    statusOk = null
                    val draft = (current ?: LlmConfig(name = provider.name)).copy(
                        name = current?.name ?: provider.name,
                        provider = providerId,
                        apiUrl = provider.defaultApiUrl,
                        apiKey = apiKey.trim(),
                        model = model.ifBlank { provider.defaultModel },
                        enabled = true,
                    )
                    viewModel.fetchModels(draft) { list ->
                        busy = false
                        if (list.isEmpty()) {
                            statusOk = false
                        } else {
                            statusOk = true
                            fetched = list
                            val picked = model.takeIf { it in list }
                                ?: list.firstOrNull { it == provider.defaultModel }
                                ?: list.first()
                            model = picked
                            viewModel.saveConfig(draft.copy(model = picked))
                            viewModel.useConfig(draft.name)
                        }
                    }
                },
            contentAlignment = Alignment.Center,
        ) {
            Text(
                text = if (busy) "正在连接…" else stringResource(R.string.ai_connect_fetch),
                fontSize = 15.sp,
                fontWeight = FontWeight.SemiBold,
                color = if (busy) sub else ink,
            )
        }

        statusOk?.let { ok ->
            Spacer(Modifier.height(10.dp))
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(
                    modifier = Modifier
                        .size(16.dp)
                        .clip(RoundedCornerShape(999.dp))
                        .border(
                            1.5.dp,
                            Color(if (ok) 0xFF7ED8C3 else 0xFFFF8A80),
                            RoundedCornerShape(999.dp),
                        ),
                )
                Spacer(Modifier.size(8.dp))
                Text(
                    text = if (ok) {
                        stringResource(R.string.ai_connected_models, fetched?.size ?: 0)
                    } else {
                        stringResource(R.string.ai_connect_failed)
                    },
                    fontSize = 12.5.sp,
                    color = Color(if (ok) 0xFF7ED8C3 else 0xFFFF8A80),
                    maxLines = 2,
                )
            }
        }
    }

    // ③ 模型
    if (models.isNotEmpty() || model.isNotBlank()) {
        AiCard {
            AiLabel(stringResource(R.string.ai_model), sub)
            AiSelect(
                badge = "M",
                title = model.ifBlank { models.firstOrNull().orEmpty() },
                tail = "",
                open = modelOpen,
                ink = ink,
                accent = accent,
                palette = palette,
                badgeTint = Color(0xFF7ED8C3),
                onClick = { modelOpen = !modelOpen },
            )
            if (modelOpen) {
                AiMenu {
                    models.forEach { m ->
                        AiOption(
                            title = m,
                            sub = "",
                            selected = m == model,
                            ink = ink,
                            subInk = sub,
                            accent = accent,
                            onClick = { model = m; modelOpen = false },
                        )
                    }
                }
            }
            Spacer(Modifier.height(8.dp))
            Text(
                text = "列表来自「连接并获取模型」的结果；没连过时先用服务商默认值",
                fontSize = 11.sp,
                color = sub.copy(alpha = 0.7f),
            )
        }
    }

    // ④ 长期记忆
    AiCard {
        Row(
            modifier = Modifier.fillMaxWidth().clickable { memoryOpen = !memoryOpen },
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = stringResource(R.string.ai_memory),
                    fontSize = 14.5.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = ink,
                )
                Text(
                    text = if (memories.isEmpty()) {
                        stringResource(R.string.ai_memory_none)
                    } else {
                        "已记 " + memories.size + " 条 · " + memories.first().content
                    },
                    fontSize = 12.sp,
                    color = sub.copy(alpha = 0.75f),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.padding(top = 2.dp),
                )
            }
            PillLabel(
                text = stringResource(R.string.ai_memory_manage),
                onClick = { memoryOpen = !memoryOpen },
                filled = false,
            )
        }
        if (memoryOpen) {
            Spacer(Modifier.height(12.dp))
            memories.forEach { item ->
                Text(
                    text = ChatMemoryRules.categoryLabel(item.category) + " · " + item.content,
                    fontSize = 12.sp,
                    color = sub,
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(vertical = 3.dp)
                        .clickable { viewModel.removeMemory(item.id) },
                )
            }
            Spacer(Modifier.height(8.dp))
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .height(44.dp)
                    .clip(RoundedCornerShape(12.dp))
                    .background(ink.copy(alpha = 0.05f))
                    .border(1.dp, ink.copy(alpha = 0.12f), RoundedCornerShape(12.dp))
                    .padding(horizontal = 12.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                BasicTextField(
                    value = memoryDraft,
                    onValueChange = { memoryDraft = it },
                    singleLine = true,
                    textStyle = TextStyle(color = ink, fontSize = 13.sp),
                    modifier = Modifier.weight(1f),
                )
            }
            Spacer(Modifier.height(8.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                PillLabel(
                    text = stringResource(R.string.ai_memory_add),
                    onClick = {
                        if (memoryDraft.isNotBlank()) {
                            viewModel.addMemory(memoryDraft)
                            memoryDraft = ""
                        }
                    },
                )
                if (memories.isNotEmpty()) {
                    PillLabel(
                        text = stringResource(R.string.ai_memory_clear),
                        onClick = { viewModel.clearMemory() },
                        filled = false,
                    )
                }
            }
        }
    }
}

@Composable
private fun AiCard(content: @Composable () -> Unit) {
    val palette = LocalCatClawPalette.current
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(bottom = 16.dp)
            .glassCard(palette = palette, corner = 22)
            .padding(16.dp),
    ) { content() }
}

@Composable
private fun AiLabel(text: String, color: Color) {
    Text(
        text = text,
        fontSize = 12.sp,
        letterSpacing = 0.6.sp,
        color = color,
        modifier = Modifier.padding(bottom = 10.dp),
    )
}

@Composable
private fun AiSelect(
    badge: String,
    title: String,
    tail: String,
    open: Boolean,
    ink: Color,
    accent: Color,
    palette: CatClawPalette,
    badgeTint: Color? = null,
    onClick: () -> Unit,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .height(50.dp)
            .clip(RoundedCornerShape(14.dp))
            .background(if (open) Color(palette.primary).copy(alpha = 0.10f) else ink.copy(alpha = 0.05f))
            .border(
                1.dp,
                if (open) Color(palette.primary).copy(alpha = 0.55f) else ink.copy(alpha = 0.12f),
                RoundedCornerShape(14.dp),
            )
            .clickable(onClick = onClick)
            .padding(horizontal = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Box(
            modifier = Modifier
                .size(26.dp)
                .clip(RoundedCornerShape(8.dp))
                .background((badgeTint ?: Color(palette.primary)).copy(alpha = 0.22f))
                .border(1.dp, (badgeTint ?: Color(palette.primary)).copy(alpha = 0.34f), RoundedCornerShape(8.dp)),
            contentAlignment = Alignment.Center,
        ) {
            Text(
                text = badge,
                fontSize = 12.sp,
                fontWeight = FontWeight.Bold,
                color = badgeTint?.copy(alpha = 0.9f) ?: accent,
            )
        }
        Text(
            text = title,
            fontSize = 14.5.sp,
            fontWeight = FontWeight.SemiBold,
            color = ink,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        Spacer(Modifier.weight(1f))
        if (tail.isNotEmpty()) {
            Text(text = tail, fontSize = 11.5.sp, color = ink.copy(alpha = 0.4f), maxLines = 1)
        }
        Text(text = if (open) "\u2303" else "\u2304", fontSize = 12.sp, color = ink.copy(alpha = 0.55f))
    }
}

@Composable
private fun AiMenu(content: @Composable () -> Unit) {
    val palette = LocalCatClawPalette.current
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(top = 8.dp)
            .clip(RoundedCornerShape(14.dp))
            .background(Color(palette.backgroundStops.last().argb).copy(alpha = 0.55f))
            .border(1.dp, Color(palette.textPrimary).copy(alpha = 0.10f), RoundedCornerShape(14.dp)),
    ) { content() }
}

@Composable
private fun AiOption(
    title: String,
    sub: String,
    selected: Boolean,
    ink: Color,
    subInk: Color,
    accent: Color,
    onClick: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(if (selected) Color(palette.primary).copy(alpha = 0.13f) else Color.Transparent)
            .clickable(onClick = onClick)
            .padding(horizontal = 14.dp, vertical = 13.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Text(text = title, fontSize = 14.sp, color = if (selected) ink else subInk)
        if (sub.isNotEmpty()) {
            Text(text = sub, fontSize = 11.sp, color = subInk.copy(alpha = 0.6f))
        }
        Spacer(Modifier.weight(1f))
        if (selected) Text(text = "\u2713", fontSize = 13.sp, color = accent)
    }
}

/** 整屏的 AI 设置子页：页头（返回 + 标题）+ [AiSettingsPanel]。 */
@Composable
fun AiSettingsScreen(
    onBack: () -> Unit,
    viewModel: SettingsViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 20.dp),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(top = 12.dp, bottom = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
            Text(
                text = stringResource(R.string.ai_settings),
                fontSize = 22.sp,
                fontWeight = FontWeight.Bold,
                color = Color(palette.textPrimary),
            )
        }
        Text(
            text = "填一个 API Key 就能用。服务商、模型、地址都有默认值，不用管。",
            fontSize = 12.5.sp,
            color = Color(palette.textSecondary).copy(alpha = 0.8f),
            modifier = Modifier.padding(bottom = 14.dp),
        )
        AiSettingsPanel(viewModel)
        Spacer(Modifier.height(24.dp))
    }
}
