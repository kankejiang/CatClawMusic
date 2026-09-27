package com.catclaw.music.kt.ui.settings

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Slider
import androidx.compose.material3.Switch
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
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.ai.LlmConfig
import com.catclaw.music.kt.core.ai.LlmProviderInfo
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette

/** 设置页里的 AI 分节：模型配置列表 + 编辑表单 + Agent 运行参数 */
@Composable
fun AiSettingsPanel(viewModel: SettingsViewModel = hiltViewModel()) {
    val palette = LocalCatClawPalette.current
    val configs by viewModel.llmConfigs.collectAsStateWithLifecycle()
    val currentName by viewModel.currentConfigName.collectAsStateWithLifecycle()
    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)

    // 编辑的是副本：点「保存」才写回存储，中途改错不至于污染当前生效配置
    var draft by remember { mutableStateOf<LlmConfig?>(null) }
    var notice by remember { mutableStateOf<String?>(null) }
    // onClick / 回调 lambda 不是 composable 上下文，字符串要先取出来
    val newConfigName = stringResource(R.string.ai_new_config_name)
    val testFailed = stringResource(R.string.ai_test_failed)

    AiSection(stringResource(R.string.ai_configs)) {
        if (configs.isEmpty()) {
            Text(stringResource(R.string.ai_no_config), style = MaterialTheme.typography.bodySmall, color = secondary)
        }
        configs.forEach { config ->
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .clickable { viewModel.useConfig(config.name) }
                    .padding(vertical = 10.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = config.name,
                    style = MaterialTheme.typography.bodyMedium,
                    color = if (config.name == currentName) Color(palette.primary) else primary,
                    modifier = Modifier.weight(1f),
                )
                Text(
                    text = listOfNotNull(
                        config.provider.takeIf { it.isNotBlank() },
                        config.model.takeIf { it.isNotBlank() },
                        if (config.enabled) stringResource(R.string.ai_on) else stringResource(R.string.ai_off),
                    ).joinToString(" · "),
                    style = MaterialTheme.typography.labelSmall,
                    color = secondary,
                )
                Text(
                    text = stringResource(R.string.ai_edit),
                    style = MaterialTheme.typography.labelMedium,
                    color = Color(palette.primary),
                    modifier = Modifier
                        .padding(start = 12.dp)
                        .clickable { draft = config },
                )
            }
        }
        OutlinedButton(
            onClick = { draft = LlmConfig(name = newConfigName) },
            modifier = Modifier.padding(top = 6.dp),
        ) {
            Text(stringResource(R.string.ai_new))
        }
    }

    draft?.let { editing ->
        AiSection(stringResource(R.string.ai_editing)) {
            AiField(stringResource(R.string.ai_name), editing.name) { draft = editing.copy(name = it) }
            Text(stringResource(R.string.ai_provider), style = MaterialTheme.typography.labelMedium, color = secondary)
            Spacer(Modifier.height(4.dp))
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
                        style = MaterialTheme.typography.labelMedium,
                        color = secondary,
                    )
                    Spacer(Modifier.height(4.dp))
                    FlowChips(presets.map { it to it }, selected = editing.model) { draft = editing.copy(model = it) }
                }

            Text(
                stringResource(R.string.ai_temperature, editing.temperature),
                style = MaterialTheme.typography.labelMedium,
                color = secondary,
            )
            Slider(
                value = editing.temperature.toFloat(),
                onValueChange = { draft = editing.copy(temperature = it.toDouble()) },
                valueRange = 0f..2f,
                steps = 19,
            )
            AiSwitch(stringResource(R.string.ai_enabled), editing.enabled) { draft = editing.copy(enabled = it) }
            AiSwitch(stringResource(R.string.ai_fallback), editing.fallbackEnabled) {
                draft = editing.copy(fallbackEnabled = it)
            }

            Row(horizontalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.padding(top = 8.dp)) {
                Button(onClick = { viewModel.saveConfig(editing) }) { Text(stringResource(R.string.ai_save)) }
                OutlinedButton(
                    onClick = {
                        viewModel.deleteConfig(editing.name)
                        draft = null
                    },
                ) {
                    Text(stringResource(R.string.ai_delete), color = primary)
                }
                OutlinedButton(onClick = { draft = null }) {
                    Text(stringResource(R.string.ai_cancel), color = secondary)
                }
            }
            notice?.let {
                Text(it, style = MaterialTheme.typography.labelSmall, color = secondary, modifier = Modifier.padding(top = 6.dp))
            }
            OutlinedButton(
                onClick = { viewModel.testConnection { ok -> notice = if (ok) "OK" else testFailed } },
                modifier = Modifier.padding(top = 6.dp),
            ) {
                Text(stringResource(R.string.ai_test), color = primary)
            }
        }
    }

    AiSection(stringResource(R.string.ai_run)) {
        Text(stringResource(R.string.ai_rounds), style = MaterialTheme.typography.labelMedium, color = secondary)
        Spacer(Modifier.height(4.dp))
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
            style = MaterialTheme.typography.labelMedium,
            color = secondary,
            modifier = Modifier.padding(top = 12.dp),
        )
        Spacer(Modifier.height(4.dp))
        FlowChips(
            REASONING_EFFORTS.map { it to it },
            selected = REASONING_EFFORTS.firstOrNull { it == viewModel.reasoningEffort },
        ) { viewModel.reasoningEffort = it }
    }
}

private val REASONING_EFFORTS = listOf("auto", "disabled", "low", "high", "max")

@Composable
private fun AiSection(title: String, content: @Composable () -> Unit) {
    val palette = LocalCatClawPalette.current
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(bottom = 14.dp)
            .clip(RoundedCornerShape(14.dp))
            .background(Color(palette.backgroundStops.last().argb))
            .padding(14.dp),
    ) {
        Text(
            text = title,
            style = MaterialTheme.typography.titleSmall,
            color = Color(palette.textPrimary),
            modifier = Modifier.padding(bottom = 8.dp),
        )
        content()
    }
}

@Composable
private fun AiField(label: String, value: String, password: Boolean = false, onChange: (String) -> Unit) {
    val palette = LocalCatClawPalette.current
    Column(modifier = Modifier.padding(bottom = 8.dp)) {
        Text(label, style = MaterialTheme.typography.labelMedium, color = Color(palette.textSecondary))
        OutlinedTextField(
            value = value,
            onValueChange = onChange,
            singleLine = true,
            visualTransformation = if (password) PasswordVisualTransformation() else androidx.compose.ui.text.input.VisualTransformation.None,
            modifier = Modifier.fillMaxWidth(),
        )
    }
}

@Composable
private fun AiSwitch(label: String, checked: Boolean, onChange: (Boolean) -> Unit) {
    val palette = LocalCatClawPalette.current
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(vertical = 4.dp)) {
        Text(label, style = MaterialTheme.typography.bodyMedium, color = Color(palette.textPrimary), modifier = Modifier.weight(1f))
        Switch(checked = checked, onCheckedChange = onChange)
    }
}

@Composable
private fun <T> FlowChips(options: List<Pair<String, T>>, selected: String?, onSelect: (T) -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        options.chunked(3).forEach { row ->
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                row.forEach { (label, value) ->
                    AiChip(label = label, selected = label == selected, onClick = { onSelect(value) })
                }
            }
        }
    }
}

@Composable
private fun AiChip(label: String, selected: Boolean, onClick: () -> Unit) {
    val palette = LocalCatClawPalette.current
    Text(
        text = label,
        style = MaterialTheme.typography.labelMedium,
        color = if (selected) Color(palette.primary) else Color(palette.textSecondary),
        modifier = Modifier
            .clip(RoundedCornerShape(14.dp))
            .background(if (selected) Color(palette.primaryLight) else Color(palette.backgroundStops.last().argb))
            .clickable(onClick = onClick)
            .padding(horizontal = 10.dp, vertical = 5.dp),
    )
}

/** 独立成页：AI 配置项比桌面歌词多得多，塞进设置主页那一列会滚不到底 */
@Composable
fun AiSettingsScreen(onBack: () -> Unit, viewModel: SettingsViewModel = hiltViewModel()) {
    val palette = LocalCatClawPalette.current
    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(Color(palette.backgroundStops.first().argb))
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 20.dp),
    ) {
        Spacer(Modifier.height(14.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(
                text = stringResource(R.string.GoBack),
                style = MaterialTheme.typography.bodyMedium,
                color = Color(palette.textSecondary),
                modifier = Modifier
                    .clickable(onClick = onBack)
                    .padding(vertical = 8.dp, horizontal = 2.dp),
            )
            Spacer(Modifier.width(14.dp))
            Text(
                text = stringResource(R.string.ai_settings),
                style = MaterialTheme.typography.titleLarge,
                color = Color(palette.textPrimary),
            )
        }
        Spacer(Modifier.height(10.dp))
        AiSettingsPanel(viewModel)
        Spacer(Modifier.height(24.dp))
    }
}
