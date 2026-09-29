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
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextField
import androidx.compose.material3.TextFieldDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
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
import com.catclaw.music.kt.ui.theme.GlassChip
import com.catclaw.music.kt.ui.theme.GlassSwitch
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.inkOn
import com.catclaw.music.kt.ui.theme.highlightOnSelectedCard
import com.catclaw.music.kt.ui.theme.glassCard

/** 设置页里的 AI 分节：模型配置列表 + 编辑表单 + Agent 运行参数 */
@Composable
fun AiSettingsPanel(viewModel: SettingsViewModel = hiltViewModel()) {
    val palette = LocalCatClawPalette.current
    val configs by viewModel.llmConfigs.collectAsStateWithLifecycle()
    val currentName by viewModel.currentConfigName.collectAsStateWithLifecycle()
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)
    val deleteLabel = stringResource(R.string.Delete)
    var memoryDraft by remember { mutableStateOf("") }

    // 编辑的是副本：点「保存」才写回存储，中途改错不至于污染当前生效配置
    var draft by remember { mutableStateOf<LlmConfig?>(null) }
    var notice by remember { mutableStateOf<String?>(null) }
    /**
     * 「获取模型列表」的结果，连同**它来自哪个 apiUrl** 一起存。
     * 只存列表的话，用户拉完再改 URL，下面那排 chip 还是上一家的模型，点了就填错。
     */
    var fetched by remember { mutableStateOf<Pair<String, List<String>>?>(null) }
    var fetching by remember { mutableStateOf(false) }
    // onClick / 回调 lambda 不是 composable 上下文，字符串要先取出来
    val newConfigName = stringResource(R.string.ai_new_config_name)
    val testFailed = stringResource(R.string.ai_test_failed)
    val modelsEmpty = stringResource(R.string.ai_models_empty)

    AiSection(stringResource(R.string.ai_configs)) {
        if (configs.isEmpty()) {
            Text(stringResource(R.string.ai_no_config), fontSize = 13.sp, color = secondary)
        }
        configs.forEach { config ->
            val isCurrent = config.name == currentName
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(vertical = 5.dp)
                    .clip(RoundedCornerShape(18.dp))
                    .background(
                        if (isCurrent) Color(palette.primary).copy(alpha = 0.16f)
                        else Color(palette.textPrimary).copy(alpha = 0.08f),
                    )
                    .then(
                        if (isCurrent) Modifier.border(1.dp, Color(palette.primary).copy(alpha = 0.45f), RoundedCornerShape(18.dp))
                        else Modifier,
                    )
                    .clickable { viewModel.useConfig(config.name) }
                    .padding(horizontal = 10.dp, vertical = 10.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                Box(
                    modifier = Modifier
                        .size(40.dp)
                        .clip(RoundedCornerShape(12.dp))
                        .background(Color(palette.textPrimary).copy(alpha = 0.10f)),
                    contentAlignment = Alignment.Center,
                ) {
                    Text(text = config.name.take(1), fontSize = 16.sp, fontWeight = FontWeight.Bold, color = primary)
                }
                Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    Text(
                        text = config.name,
                        fontSize = 14.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = if (isCurrent) Color(highlightOnSelectedCard(palette)) else primary,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    Text(
                        text = listOfNotNull(
                            config.provider.takeIf { it.isNotBlank() },
                            config.model.takeIf { it.isNotBlank() },
                            if (config.enabled) stringResource(R.string.ai_on) else stringResource(R.string.ai_off),
                        ).joinToString(" · "),
                        fontSize = 12.sp,
                        color = secondary,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
                AiPill(label = stringResource(R.string.ai_edit), filled = false) { draft = config }
            }
        }
        Spacer(Modifier.height(8.dp))
        AiPill(label = stringResource(R.string.ai_new), filled = true) { draft = LlmConfig(name = newConfigName) }
    }

    draft?.let { editing ->
        AiSection(stringResource(R.string.ai_editing)) {
            AiField(stringResource(R.string.ai_name), editing.name) { draft = editing.copy(name = it) }
            Text(stringResource(R.string.ai_provider), fontSize = 12.sp, fontWeight = FontWeight.Bold, letterSpacing = 1.sp, color = secondary)
            Spacer(Modifier.height(6.dp))
            FlowChips(
                LlmProviderInfo.ALL.map { it.name to it },
                selected = LlmProviderInfo.ALL.firstOrNull { it.id == editing.provider }?.name,
            ) { provider ->
                draft = editing.copy(
                    provider = provider.id,
                    apiUrl = provider.defaultApiUrl.ifBlank { editing.apiUrl },
                    model = provider.defaultModel.ifBlank { editing.model },
                )
            }
            Spacer(Modifier.height(8.dp))
            AiField("API URL", editing.apiUrl) { draft = editing.copy(apiUrl = it) }
            AiField(stringResource(R.string.ai_api_key), editing.apiKey, password = true) {
                draft = editing.copy(apiKey = it)
            }
            AiField(stringResource(R.string.ai_model), editing.model) { draft = editing.copy(model = it) }

            LlmProviderInfo.ALL.firstOrNull { it.id == editing.provider }?.presetModels
                ?.takeIf { it.isNotEmpty() }
                ?.let { presets ->
                    Text(
                        stringResource(R.string.ai_preset_models),
                        fontSize = 12.sp,
                        fontWeight = FontWeight.Bold,
                        letterSpacing = 1.sp,
                        color = secondary,
                    )
                    Spacer(Modifier.height(6.dp))
                    FlowChips(presets.map { it to it }, selected = editing.model) { draft = editing.copy(model = it) }
                    Spacer(Modifier.height(8.dp))
                }

            // 账号实际返回的那一份：点一下就填进上面「模型」那格。
            // 只在 URL 没被改过的时候画——改过 URL 之后这批 id 已经属于另一家了
            fetched?.takeIf { it.first == editing.apiUrl && it.second.isNotEmpty() }?.let { (_, models) ->
                Text(
                    stringResource(R.string.ai_models_from_account, models.size),
                    fontSize = 12.sp,
                    fontWeight = FontWeight.Bold,
                    letterSpacing = 1.sp,
                    color = secondary,
                )
                Spacer(Modifier.height(6.dp))
                FlowChips(models.map { it to it }, selected = editing.model) { draft = editing.copy(model = it) }
                Spacer(Modifier.height(8.dp))
            }

            Text(stringResource(R.string.ai_temperature, editing.temperature), fontSize = 12.sp, fontWeight = FontWeight.Bold, letterSpacing = 1.sp, color = secondary)
            Slider(
                value = editing.temperature.toFloat(),
                onValueChange = { draft = editing.copy(temperature = it.toDouble()) },
                valueRange = 0f..2f,
                steps = 19,
                colors = SliderDefaults.colors(
                    thumbColor = Color(palette.primary),
                    activeTrackColor = Color(palette.primary),
                    inactiveTrackColor = Color(palette.textPrimary).copy(alpha = 0.18f),
                ),
            )
            GlassSwitch(
                label = stringResource(R.string.ai_enabled),
                checked = editing.enabled,
                onChange = { draft = editing.copy(enabled = it) },
            )
            GlassSwitch(
                label = stringResource(R.string.ai_fallback),
                checked = editing.fallbackEnabled,
                onChange = { draft = editing.copy(fallbackEnabled = it) },
            )

            Row(horizontalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.padding(top = 10.dp)) {
                AiPill(label = stringResource(R.string.ai_save), filled = true) { viewModel.saveConfig(editing) }
                AiPill(label = stringResource(R.string.ai_delete), filled = false) {
                    viewModel.deleteConfig(editing.name)
                    draft = null
                }
                AiPill(label = stringResource(R.string.ai_cancel), filled = false) { draft = null }
            }
            notice?.let {
                Text(it, fontSize = 12.sp, color = secondary, modifier = Modifier.padding(top = 8.dp))
            }
            Spacer(Modifier.height(8.dp))
            AiPill(label = stringResource(R.string.ai_test), filled = false) {
                viewModel.testConnection { ok -> notice = if (ok) "OK" else testFailed }
            }
            Spacer(Modifier.height(6.dp))
            // 拉列表是网络调用：拉取中把按钮置灰，不然连点会并发发好几份请求
            AiPill(
                label = stringResource(if (fetching) R.string.ai_models_fetching else R.string.ai_fetch_models),
                filled = false,
                enabled = !fetching,
            ) {
                fetching = true
                notice = null
                viewModel.fetchModels(editing) { models ->
                    fetching = false
                    fetched = editing.apiUrl to models
                    if (models.isEmpty()) notice = modelsEmpty
                }
            }
        }
    }

    AiSection(stringResource(R.string.ai_run)) {
        Text(stringResource(R.string.ai_rounds), fontSize = 12.sp, fontWeight = FontWeight.Bold, letterSpacing = 1.sp, color = secondary)
        Spacer(Modifier.height(6.dp))
        FlowChips(
            listOf(
                stringResource(R.string.ai_rounds_unlimited) to 0,
                "8" to 8,
                "12" to 12,
                "20" to 20,
            ),
            selected = when (viewModel.maxToolRounds) {
                0 -> stringResource(R.string.ai_rounds_unlimited)
                8 -> "8"
                12 -> "12"
                20 -> "20"
                else -> null
            },
        ) { viewModel.maxToolRounds = it }

        Text(
            stringResource(R.string.ai_effort),
            fontSize = 12.sp,
            fontWeight = FontWeight.Bold,
            letterSpacing = 1.sp,
            color = secondary,
            modifier = Modifier.padding(top = 12.dp),
        )
        Spacer(Modifier.height(6.dp))
        FlowChips(
            REASONING_EFFORTS.map { it to it },
            selected = REASONING_EFFORTS.firstOrNull { it == viewModel.reasoningEffort },
        ) { viewModel.reasoningEffort = it }
    }

    // 长期记忆：模型从对话里提炼出来的东西必须看得见、删得掉，也能手动补一条。
    // MAUI 的 AiSettingsPage 就是「添加 / 清空记忆」两件事，本端此前整块没有 ——
    // 而记忆文件已经会被写进 prompt，看不见就等于"它在背后记了你的东西而你没处查看"。
    AiSection(stringResource(R.string.ai_memory)) {
        val memories by viewModel.memories.collectAsStateWithLifecycle()
        if (memories.isEmpty()) {
            Text(stringResource(R.string.ai_memory_empty), fontSize = 12.sp, color = secondary)
        }
        memories.forEach { item ->
            Row(
                modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(item.content, fontSize = 14.sp, color = primary, maxLines = 2)
                    Text(
                        text = stringResource(R.string.ai_memory_meta, ChatMemoryRules.categoryLabel(item.category), item.importance),
                        fontSize = 11.sp,
                        color = secondary,
                    )
                }
                Text(
                    text = "✕",
                    fontSize = 15.sp,
                    color = secondary,
                    // padding 在 clickable 之前：先撑出 44dp 量级的触达面，再挂点击
                    modifier = Modifier
                        .padding(10.dp)
                        .clickable(onClickLabel = deleteLabel) { viewModel.removeMemory(item.id) },
                )
            }
        }
        Spacer(Modifier.height(8.dp))
        AiField(stringResource(R.string.ai_memory_hint), memoryDraft) { memoryDraft = it }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(top = 6.dp)) {
            AiPill(stringResource(R.string.ai_memory_add), filled = true) {
                if (memoryDraft.isNotBlank()) {
                    viewModel.addMemory(memoryDraft)
                    memoryDraft = ""
                }
            }
            if (memories.isNotEmpty()) {
                AiPill(stringResource(R.string.ai_memory_clear), filled = false) { viewModel.clearMemory() }
            }
        }
    }
}

private val REASONING_EFFORTS = listOf("auto", "disabled", "low", "high", "max")

/** 玻璃分区卡：与设置页 Section 同一套面（圆角 26 / textPrimary α.10 / 描边 α.16 / 12sp 字距标题） */
@Composable
private fun AiSection(title: String, content: @Composable () -> Unit) {
    val palette = LocalCatClawPalette.current
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(bottom = 14.dp)
            .glassCard(palette = palette, corner = 26)
            .padding(16.dp),
    ) {
        Text(
            text = title,
            fontSize = 12.sp,
            fontWeight = FontWeight.Bold,
            letterSpacing = 2.sp,
            color = Color(palette.textSecondary),
            modifier = Modifier.padding(bottom = 10.dp),
        )
        content()
    }
}

/** 玻璃输入框：透明容器、无下划线，标签与光标走调色板 */
@Composable
private fun AiField(label: String, value: String, password: Boolean = false, onChange: (String) -> Unit) {
    val palette = LocalCatClawPalette.current
    Column(modifier = Modifier.padding(bottom = 8.dp)) {
        Text(label, fontSize = 12.sp, fontWeight = FontWeight.Bold, letterSpacing = 1.sp, color = Color(palette.textSecondary))
        Spacer(Modifier.height(4.dp))
        TextField(
            value = value,
            onValueChange = onChange,
            singleLine = true,
            visualTransformation = if (password) PasswordVisualTransformation() else androidx.compose.ui.text.input.VisualTransformation.None,
            colors = TextFieldDefaults.colors(
                focusedContainerColor = Color(palette.textPrimary).copy(alpha = 0.08f),
                unfocusedContainerColor = Color(palette.textPrimary).copy(alpha = 0.06f),
                focusedIndicatorColor = Color.Transparent,
                unfocusedIndicatorColor = Color.Transparent,
                focusedTextColor = Color(palette.textPrimary),
                unfocusedTextColor = Color(palette.textPrimary),
                cursorColor = Color(palette.primary),
            ),
            shape = RoundedCornerShape(16.dp),
            modifier = Modifier.fillMaxWidth(),
        )
    }
}

@Composable
private fun <T> FlowChips(options: List<Pair<String, T>>, selected: String?, onSelect: (T) -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        options.chunked(3).forEach { row ->
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                row.forEach { (label, value) ->
                    AiChip(label = label, selected = label == selected, onClick = { onSelect(value) })
                }
            }
        }
    }
}

/** 胶囊 chip：选中 = 主色实底深字，未选中 = 玻璃底次级字 */
@Composable
private fun AiChip(label: String, selected: Boolean, onClick: () -> Unit) {
    GlassChip(label = label, selected = selected, onClick = onClick)
}

/** 胶囊按钮：filled = 主色实底深字，否则描边玻璃底 */
@Composable
private fun AiPill(label: String, filled: Boolean, enabled: Boolean = true, onClick: () -> Unit) {
    val palette = LocalCatClawPalette.current
    Text(
        text = label,
        fontSize = 13.sp,
        fontWeight = FontWeight.SemiBold,
        color = when {
            !enabled -> Color(palette.textSecondary).copy(alpha = 0.5f)
            filled -> Color(inkOn(palette.primary))
            else -> Color(palette.textSecondary)
        },
        modifier = Modifier
            .clip(RoundedCornerShape(14.dp))
            .background(if (filled) Color(palette.primary) else Color.Transparent)
            .then(
                if (filled) Modifier else Modifier.border(1.dp, Color(palette.textPrimary).copy(alpha = 0.16f), RoundedCornerShape(14.dp)),
            )
            .clickable(enabled = enabled, onClick = onClick)
            .padding(horizontal = 14.dp, vertical = 9.dp),
    )
}

/** 独立成页：AI 配置项比桌面歌词多得多，塞进设置主页那一列会滚不到底 */
@Composable
fun AiSettingsScreen(onBack: () -> Unit, viewModel: SettingsViewModel = hiltViewModel()) {
    val palette = LocalCatClawPalette.current
    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 20.dp),
    ) {
        Spacer(Modifier.height(14.dp))
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
            Text(
                text = stringResource(R.string.ai_settings),
                fontSize = 22.sp,
                fontWeight = FontWeight.Bold,
                color = Color(palette.textPrimary),
            )
        }
        Spacer(Modifier.height(12.dp))
        AiSettingsPanel(viewModel)
        Spacer(Modifier.height(24.dp))
    }
}
