package com.catclaw.music.kt.ui.netease

import android.content.res.Configuration
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.data.netease.NeteaseArtist
import com.catclaw.music.kt.data.netease.NeteasePlaylistClient
import com.catclaw.music.kt.data.netease.OnlinePlaylist
import androidx.compose.runtime.saveable.rememberSaveable
import com.catclaw.music.kt.core.library.SongSortFields
import com.catclaw.music.kt.core.library.SongSortKey
import com.catclaw.music.kt.core.library.defaultDescending
import com.catclaw.music.kt.core.library.filterAndSortSongs
import com.catclaw.music.kt.ui.screen.sortLabelRes
import com.catclaw.music.kt.data.netease.OnlineSong
import com.catclaw.music.kt.ui.song.SongCollectionScreen
import com.catclaw.music.kt.ui.screen.PlaylistDetailHeader
import com.catclaw.music.kt.ui.screen.PlaylistSongRowUi
import com.catclaw.music.kt.ui.screen.PlaylistSongUi
import com.catclaw.music.kt.ui.library.NeteaseImportPanel
import com.catclaw.music.kt.ui.library.NeteaseImportViewModel
import com.catclaw.music.kt.ui.player.CoverTile
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.GlassChip
import com.catclaw.music.kt.ui.theme.GlassSearchField
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.glassCard
import com.catclaw.music.kt.ui.theme.isLightPalette
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard
import kotlinx.coroutines.delay

/** 网易云登录覆盖层（从 NeteaseMusicScreen.kt 拆出） */
@Composable
internal fun LoginOverlay(
    accountName: String?,
    state: NeteaseMusicViewModel.Login,
    countdown: Int,
    onSendSms: (String, String) -> Unit,
    onLoginSms: (String, String, String) -> Unit,
    onLoginPassword: (String, String, String) -> Unit,
    onLogout: () -> Unit,
    onDismiss: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val hint = Color(palette.textSecondary)
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE
    val fade = remember { MutableInteractionSource() }
    var tab by remember { mutableStateOf(0) }
    var phone by remember { mutableStateOf("") }
    var captcha by remember { mutableStateOf("") }
    var account by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    val busy = state is NeteaseMusicViewModel.Login.Busy

    // 面板必须是实心的：glassCard 在纯背景页上没有模糊层，透过去正好是推荐卡片的标题，
    // 真机上两行字叠在一起没法读。深色主题压深、浅色主题压白，都是 94% 不透明。
    val panelBg = if (isLightPalette(palette)) {
        Color.White.copy(alpha = 0.94f)
    } else {
        Color(0xFF13161D).copy(alpha = 0.94f)
    }
    val shape = RoundedCornerShape(28.dp)
    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(Color.Black.copy(alpha = 0.55f))
            .clickable(interactionSource = fade, indication = null, onClick = onDismiss),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            modifier = Modifier
                .width(if (landscape) 520.dp else 340.dp)
                .clip(shape)
                .background(panelBg)
                .border(1.dp, Color(palette.textPrimary).copy(alpha = 0.14f), shape)
                .clickable(interactionSource = fade, indication = null) { }
                .padding(horizontal = 20.dp, vertical = 18.dp),
        ) {
            Text(
                text = stringResource(R.string.netease_login_title),
                fontSize = 19.sp,
                fontWeight = FontWeight.Bold,
                color = ink,
            )
            Spacer(Modifier.height(8.dp))

            if (accountName != null) {
                Text(
                    text = accountName,
                    fontSize = 15.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = Color(highlightOnGlassCard(palette)),
                )
                Spacer(Modifier.height(14.dp))
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.End,
                ) {
                    PillLabel(
                        text = stringResource(R.string.netease_logout),
                        onClick = onLogout,
                        filled = false,
                    )
                }
            } else {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    GlassChip(
                        label = stringResource(R.string.netease_login_tab_phone),
                        selected = tab == 0,
                        onClick = { tab = 0 },
                    )
                    GlassChip(
                        label = stringResource(R.string.netease_login_tab_password),
                        selected = tab == 1,
                        onClick = { tab = 1 },
                    )
                }
                Spacer(Modifier.height(12.dp))

                if (tab == 0) {
                    LoginField(
                        value = phone,
                        onChange = { phone = it },
                        placeholder = stringResource(R.string.netease_phone_hint),
                        keyboardType = KeyboardType.Phone,
                    )
                    Spacer(Modifier.height(8.dp))
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        LoginField(
                            value = captcha,
                            onChange = { captcha = it },
                            placeholder = stringResource(R.string.netease_captcha_hint),
                            keyboardType = KeyboardType.Number,
                            modifier = Modifier.weight(1f),
                        )
                        PillLabel(
                            text = if (countdown > 0) {
                                stringResource(R.string.netease_resend_code, countdown)
                            } else {
                                stringResource(R.string.netease_send_code)
                            },
                            onClick = { onSendSms(phone, "86") },
                            filled = false,
                            enabled = phone.isNotBlank() && countdown == 0 && !busy,
                        )
                    }
                    Spacer(Modifier.height(14.dp))
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.End,
                    ) {
                        PillLabel(
                            text = stringResource(R.string.netease_login_action),
                            onClick = { onLoginSms(phone, captcha, "86") },
                            enabled = phone.isNotBlank() && captcha.isNotBlank() && !busy,
                        )
                    }
                } else {
                    LoginField(
                        value = account,
                        onChange = { account = it },
                        placeholder = stringResource(R.string.netease_account_hint),
                    )
                    Spacer(Modifier.height(8.dp))
                    LoginField(
                        value = password,
                        onChange = { password = it },
                        placeholder = stringResource(R.string.netease_password_hint),
                        keyboardType = KeyboardType.Password,
                        password = true,
                    )
                    Spacer(Modifier.height(14.dp))
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.End,
                    ) {
                        PillLabel(
                            text = stringResource(R.string.netease_login_action),
                            onClick = { onLoginPassword(account, password, "86") },
                            enabled = account.isNotBlank() && password.isNotBlank() && !busy,
                        )
                    }
                }

                val failed = state as? NeteaseMusicViewModel.Login.Failed
                if (failed != null) {
                    Spacer(Modifier.height(8.dp))
                    Text(
                        text = failed.message,
                        fontSize = 11.sp,
                        color = Color(highlightOnGlassCard(palette)),
                    )
                }
            }
        }
    }
}

/** 登录表单的输入框：自绘胶囊，密码走掩码（项目视觉闸门不允许 stock TextField） */
@Composable
internal fun LoginField(
    value: String,
    onChange: (String) -> Unit,
    placeholder: String,
    modifier: Modifier = Modifier,
    keyboardType: KeyboardType = KeyboardType.Text,
    password: Boolean = false,
) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val hint = Color(palette.textSecondary)
    BasicTextField(
        value = value,
        onValueChange = onChange,
        singleLine = true,
        textStyle = TextStyle(color = ink, fontSize = 13.sp),
        cursorBrush = SolidColor(Color(palette.primary)),
        visualTransformation = if (password) PasswordVisualTransformation() else VisualTransformation.None,
        keyboardOptions = KeyboardOptions(keyboardType = keyboardType),
        decorationBox = { inner ->
            Box(
                modifier = Modifier
                    .clip(RoundedCornerShape(16.dp))
                    .background(ink.copy(alpha = 0.08f))
                    .padding(horizontal = 12.dp, vertical = 11.dp),
            ) {
                if (value.isEmpty()) Text(placeholder, color = hint, fontSize = 13.sp)
                inner()
            }
        },
        modifier = modifier.fillMaxWidth(),
    )
}

@Composable
internal fun SectionTitle(text: String, color: Color) {
    Text(
        text = text,
        fontSize = 15.sp,
        fontWeight = FontWeight.Bold,
        color = color,
        modifier = Modifier.padding(top = 8.dp, bottom = 2.dp),
    )
}

@Composable
internal fun HintText(text: String, color: Color, modifier: Modifier = Modifier) {
    Box(modifier = modifier.fillMaxWidth().padding(vertical = 18.dp), contentAlignment = Alignment.Center) {
        Text(text = text, fontSize = 12.5.sp, color = color)
    }
}
