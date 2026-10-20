package com.catclaw.music.kt.ui.chat

import androidx.compose.foundation.Image
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
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.ime
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextField
import androidx.compose.material3.TextFieldDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.snapshotFlow
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.painterResource
import kotlinx.coroutines.flow.distinctUntilChanged
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import com.catclaw.music.kt.core.ai.ChatPaging
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.ui.player.CoverTile
import com.catclaw.music.kt.core.ai.Bubble
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.catClawBackground
import com.catclaw.music.kt.ui.theme.inkOn

/**
 * Yuki 聊天页，对应 MAUI `SearchPage.xaml` 的 ChatOverlay 那一层。
 *
 * ⚠ 消息列表是**倒序**的（index 0 = 最新），配 `reverseLayout = true` 后最新消息自然贴在底部，
 * 新消息到达不会把用户正在读的旧消息顶走；加载更多历史因此是尾部追加（视觉上向上长）。
 */
@Composable
fun ChatScreen(
    onBack: () -> Unit,
    /** 外壳那条迷你播放条压在这一层底下时要让出的高度（聊天模式下它仍然显示） */
    bottomReserve: Dp = 0.dp,
    viewModel: ChatViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    val messages by viewModel.messages.collectAsStateWithLifecycle()
    val thinking by viewModel.thinking.collectAsStateWithLifecycle()
    val title by viewModel.title.collectAsStateWithLifecycle()
    var text by rememberSaveable { mutableStateOf("") }
    val listState = rememberLazyListState()
    // agent 抓网页时顶部会出现的小窗（可手动关）；宿主登记在 AgentBrowserPreview 里做
    val browserPreview = remember { AgentBrowserPreviewController() }

    LaunchedEffect(Unit) { viewModel.open() }

    // 滚到列表末端（= 最旧的一侧）时翻下一页。
    // 判据挂在**可见项**上而不是 `messages.size` 上：挂消息数的话只有来新消息才顺带判一次，
    // 用户往上滚到顶什么都等不到（会话层的 `loadMore` 自带 loadingMore / hasMoreHistory 守卫，
    // 这里可以放心地每变化一次就调）
    LaunchedEffect(listState) {
        snapshotFlow { listState.layoutInfo.visibleItemsInfo.lastOrNull()?.index to listState.layoutInfo.totalItemsCount }
            .distinctUntilChanged()
            .collect { (lastVisible, total) ->
                // 首帧 layoutInfo 还是空的：lastVisible 为 null，与规则里的 -1 同一含义
                if (lastVisible != null && ChatPaging.shouldLoadOlder(lastVisible, total)) viewModel.loadMore()
            }
    }

    // 键盘弹起时外壳那条迷你条自己在键盘背后，这里再让出高度就变成输入行上方一块死白。
    // 读一次 IME 高度：把内容顶上去是 `imePadding` 的活，这里只负责别叠着让位
    val density = LocalDensity.current
    val imeOpen = WindowInsets.ime.getBottom(density) > 0
    Column(
        modifier = Modifier
            .fillMaxSize()
            // 原来铺的是底色纯色：既吞掉了主题渐变与两层光晕，也让用户自定义背景图在这页失效
            .catClawBackground()
            .padding(bottom = if (imeOpen) 0.dp else bottomReserve)
            .imePadding(),
    ) {
        ChatHeader(title = title, onBack = onBack)

        AgentBrowserPreview(controller = browserPreview)

        LazyColumn(
            state = listState,
            reverseLayout = true,
            contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 14.dp, vertical = 8.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
            modifier = Modifier
                .fillMaxWidth()
                .weight(1f),
        ) {
            items(messages.size) { i ->
                MessageRow(
                    bubble = messages[i],
                    primary = Color(palette.primary),
                    onPlay = { song, cards -> viewModel.play(song, cards) },
                )
            }
            if (thinking) {
                item(key = "typing") { TypingIndicator(Color(palette.textSecondary)) }
            }
        }

        InputRow(
            text = text,
            thinking = thinking,
            onTextChange = { text = it },
            onSend = {
                viewModel.send(text)
                text = ""
            },
            onStop = viewModel::stop,
        )
        Spacer(Modifier.height(8.dp))
    }
}

@Composable
private fun ChatHeader(title: String, onBack: () -> Unit) {
    val palette = LocalCatClawPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .statusBarsPadding()
            .padding(horizontal = 12.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        BackTile(onBack = onBack, description = stringResource(R.string.chat_back))
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(
                text = title.ifBlank { stringResource(R.string.chat_title) },
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold,
                color = Color(palette.textPrimary),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = stringResource(R.string.chat_yuki_desc),
                fontSize = 12.sp,
                color = Color(palette.textSecondary),
            )
        }
        YukiAvatar(size = 34.dp)
    }
}

@Composable
private fun MessageRow(bubble: Bubble, primary: Color, onPlay: (Song, List<Song>) -> Unit) {
    val palette = LocalCatClawPalette.current
    val isUser = bubble.role == "user"
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = if (isUser) Arrangement.End else Arrangement.Start,
        verticalAlignment = Alignment.Top,
    ) {
        if (!isUser) {
            YukiAvatar(size = 30.dp)
            Spacer(Modifier.size(8.dp))
        }
        Column(horizontalAlignment = if (isUser) Alignment.End else Alignment.Start) {
            MessageBubble(
                bubble = bubble,
                isUser = isUser,
                primary = primary,
                onPlay = onPlay,
            )
        }
    }
}

@Composable
private fun MessageBubble(
    bubble: Bubble,
    isUser: Boolean,
    primary: Color,
    onPlay: (Song, List<Song>) -> Unit,
) {
    val palette = LocalCatClawPalette.current
    Column(
        modifier = Modifier
            .widthIn(max = 300.dp)
            .clip(
                RoundedCornerShape(
                    topStart = if (isUser) 18.dp else 6.dp,
                    topEnd = if (isUser) 6.dp else 18.dp,
                    bottomStart = 18.dp,
                    bottomEnd = 18.dp,
                ),
            )
            .background(
                if (isUser) Color(palette.primary)
                else Color(palette.textPrimary).copy(alpha = 0.10f),
            )
            .then(
                if (isUser) Modifier
                else Modifier.border(1.dp, Color(palette.textPrimary).copy(alpha = 0.16f), RoundedCornerShape(18.dp)),
            )
            .padding(horizontal = 12.dp, vertical = 9.dp),
    ) {
        if (bubble.steps.isNotEmpty()) {
            bubble.steps.forEach { step ->
                Text(
                    text = step,
                    fontSize = 11.sp,
                    color = if (isUser) Color(inkOn(palette.primary)) else Color(palette.textSecondary),
                )
            }
        }
        if (bubble.reasoning.isNotEmpty()) {
            Text(
                text = bubble.reasoning,
                fontSize = 12.sp,
                color = if (isUser) Color(inkOn(palette.primary)) else Color(palette.textSecondary),
                maxLines = 6,
                overflow = TextOverflow.Ellipsis,
            )
        }
        if (bubble.content.isNotEmpty() || (bubble.thinking && bubble.steps.isEmpty())) {
            Text(
                text = bubble.content,
                fontSize = 14.sp,
                color = if (isUser) Color(inkOn(palette.primary)) else Color(palette.textPrimary),
            )
        }
        if (bubble.songs.isNotEmpty()) {
            Spacer(Modifier.height(6.dp))
            bubble.songs.forEach { song ->
                SongCardRow(song = song, onPlay = { onPlay(song, bubble.songs) })
            }
        }
    }
}

@Composable
private fun SongCardRow(song: Song, onPlay: () -> Unit) {
    val palette = LocalCatClawPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 4.dp)
            .clip(RoundedCornerShape(14.dp))
            .background(Color(palette.textPrimary).copy(alpha = 0.12f))
            .clickable(onClick = onPlay)
            .padding(horizontal = 8.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        // 有封面时这块不再需要 `inkOn` 现算的面前景 —— 图上不写字；取不到图才回落到首字母
        CoverTile(
            coverArtPath = song.coverArtPath,
            audioPath = song.filePath,
            maxPx = 96,
            modifier = Modifier
                .size(34.dp)
                .clip(RoundedCornerShape(10.dp))
                .background(Color(palette.primary)),
        ) {
            Text(text = song.title.take(1), fontSize = 14.sp, fontWeight = FontWeight.Bold, color = Color(inkOn(palette.primary)))
        }
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(1.dp)) {
            Text(
                text = song.title,
                fontSize = 13.sp,
                fontWeight = FontWeight.SemiBold,
                color = Color(palette.textPrimary),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = song.artist,
                fontSize = 11.sp,
                color = Color(palette.textSecondary),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        Icon(
            painter = painterResource(R.drawable.ic_play),
            contentDescription = null,
            tint = Color(palette.primary),
            modifier = Modifier.size(16.dp),
        )
    }
}

@Composable
private fun YukiAvatar(size: androidx.compose.ui.unit.Dp) {
    Image(
        painter = painterResource(R.drawable.avatar_yuki),
        contentDescription = null,
        modifier = Modifier.size(size).clip(CircleShape),
    )
}

@Composable
private fun TypingIndicator(hint: Color) {
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(start = 40.dp)) {
        CircularProgressIndicator(strokeWidth = 2.dp, color = hint, modifier = Modifier.size(14.dp))
        Spacer(Modifier.size(8.dp))
        Text(stringResource(R.string.chat_thinking), style = MaterialTheme.typography.labelSmall, color = hint)
    }
}

@Composable
private fun InputRow(
    text: String,
    thinking: Boolean,
    onTextChange: (String) -> Unit,
    onSend: () -> Unit,
    onStop: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 12.dp),
        verticalAlignment = Alignment.Bottom,
    ) {
        TextField(
            value = text,
            onValueChange = onTextChange,
            placeholder = { Text(stringResource(R.string.chat_placeholder), color = Color(palette.textSecondary), fontSize = 14.sp) },
            singleLine = true,
            colors = TextFieldDefaults.colors(
                focusedContainerColor = Color(palette.textPrimary).copy(alpha = 0.10f),
                unfocusedContainerColor = Color(palette.textPrimary).copy(alpha = 0.08f),
                focusedIndicatorColor = Color.Transparent,
                unfocusedIndicatorColor = Color.Transparent,
                focusedTextColor = Color(palette.textPrimary),
                unfocusedTextColor = Color(palette.textPrimary),
                cursorColor = Color(palette.primary),
            ),
            shape = RoundedCornerShape(22.dp),
            modifier = Modifier
                .weight(1f)
                .heightIn(max = 120.dp),
        )
        Spacer(Modifier.width(8.dp))
        if (thinking) {
            Text(
                text = stringResource(R.string.chat_stop),
                fontSize = 13.sp,
                fontWeight = FontWeight.SemiBold,
                color = Color(palette.textSecondary),
                modifier = Modifier
                    .clip(RoundedCornerShape(22.dp))
                    .border(1.dp, Color(palette.textPrimary).copy(alpha = 0.16f), RoundedCornerShape(22.dp))
                    .clickable(onClick = onStop)
                    .padding(horizontal = 16.dp, vertical = 12.dp),
            )
        } else {
            val canSend = text.isNotBlank()
            Text(
                text = stringResource(R.string.chat_send),
                fontSize = 13.sp,
                fontWeight = FontWeight.SemiBold,
                color = Color(inkOn(palette.primary)).copy(alpha = if (canSend) 1f else 0.4f),
                modifier = Modifier
                    .clip(RoundedCornerShape(22.dp))
                    .background(Color(palette.primary).copy(alpha = if (canSend) 1f else 0.4f))
                    .clickable(enabled = canSend, onClick = onSend)
                    .padding(horizontal = 16.dp, vertical = 12.dp),
            )
        }
    }
}
