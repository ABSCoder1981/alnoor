package com.alnoor.app.ui.classroom

import android.Manifest
import android.content.pm.PackageManager
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.alnoor.app.R
import com.alnoor.app.data.auth.ApiException
import com.alnoor.app.livekit.ClassroomManager
import com.alnoor.app.livekit.ClassroomNotice
import com.alnoor.app.livekit.ClassroomService
import com.alnoor.app.livekit.ClassroomState
import com.alnoor.app.livekit.ClassroomStatus
import com.alnoor.app.livekit.EndReason
import com.alnoor.app.livekit.RoomParticipant
import com.alnoor.app.ui.common.ConfirmDialog
import com.alnoor.app.ui.common.ErrorMessage
import io.livekit.android.compose.ui.VideoTrackView
import io.livekit.android.room.participant.ConnectionQuality
import kotlinx.coroutines.launch

/** Live classroom (PRD 11.2 screens 9–12). The [ClassroomManager] owns the call; this screen only shows it. */
@Composable
fun ClassroomScreen(manager: ClassroomManager, sessionId: String, classTitle: String, onExit: () -> Unit) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val state by manager.state.collectAsStateWithLifecycle()
    var micDenied by remember { mutableStateOf(false) }
    var requested by rememberSaveable { mutableStateOf(false) }

    fun joinNow() {
        scope.launch {
            manager.join(sessionId, classTitle)
            if (manager.isActive) ClassroomService.start(context) // From the visible screen (PRD 20.4).
        }
    }

    val notifications = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { joinNow() }
    val mic = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (!granted) {
            micDenied = true
        } else if (Build.VERSION.SDK_INT >= 33 && !granted(context, Manifest.permission.POST_NOTIFICATIONS)) {
            notifications.launch(Manifest.permission.POST_NOTIFICATIONS) // The in-class notification; the class works without it.
        } else {
            joinNow()
        }
    }

    LaunchedEffect(sessionId) {
        if (requested) return@LaunchedEffect
        requested = true
        if (manager.isActive && state.sessionId == sessionId) return@LaunchedEffect
        manager.reset()
        if (granted(context, Manifest.permission.RECORD_AUDIO)) {
            if (Build.VERSION.SDK_INT >= 33 && !granted(context, Manifest.permission.POST_NOTIFICATIONS)) {
                notifications.launch(Manifest.permission.POST_NOTIFICATIONS)
            } else {
                joinNow()
            }
        } else {
            mic.launch(Manifest.permission.RECORD_AUDIO)
        }
    }

    when {
        micDenied -> Centered {
            Text(stringResource(R.string.classroom_mic_explain))
            Text(stringResource(R.string.classroom_mic_permission), color = MaterialTheme.colorScheme.error)
            Button(onClick = onExit, modifier = Modifier.heightIn(min = 48.dp)) { Text(stringResource(R.string.classroom_back)) }
        }
        state.status == ClassroomStatus.Ended -> EndedScreen(state, onRejoin = {
            manager.reset()
            requested = false
            joinNow()
        }, onExit = { manager.reset(); onExit() })
        state.status == ClassroomStatus.Idle || state.status == ClassroomStatus.Joining -> Centered {
            CircularProgressIndicator()
            Text(stringResource(R.string.classroom_joining))
        }
        else -> LiveRoom(state, manager)
    }
}

private fun granted(context: android.content.Context, permission: String) =
    ContextCompat.checkSelfPermission(context, permission) == PackageManager.PERMISSION_GRANTED

@Composable
private fun Centered(content: @Composable () -> Unit) {
    Column(
        Modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp, Alignment.CenterVertically),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) { content() }
}

@Composable
private fun EndedScreen(state: ClassroomState, onRejoin: () -> Unit, onExit: () -> Unit) = Centered {
    Text(stringResource(R.string.classroom_ended_title), style = MaterialTheme.typography.headlineSmall)
    when (state.endReason) {
        EndReason.ClassEnded -> Text(stringResource(R.string.classroom_ended_body))
        EndReason.Removed -> Text(stringResource(R.string.classroom_removed_body))
        EndReason.Left -> Text(stringResource(R.string.classroom_left_body))
        EndReason.ConnectionLost -> Text(stringResource(R.string.classroom_lost_body))
        EndReason.Failed, null -> state.error?.let { ErrorMessage(it) }
    }
    val joined = state.joinedAtMillis
    val ended = state.endedAtMillis
    if (joined != null && ended != null && state.endReason != EndReason.Failed) {
        Text(stringResource(R.string.classroom_duration, ((ended - joined) / 60_000).toInt()))
    }
    // Rejoin is offered while the class may still be live (left or connection lost), never after removal or end.
    if (state.endReason == EndReason.Left || state.endReason == EndReason.ConnectionLost ||
        (state.endReason == EndReason.Failed && state.error?.code == ApiException.NETWORK_ERROR)
    ) {
        Button(onClick = onRejoin, modifier = Modifier.heightIn(min = 56.dp)) { Text(stringResource(R.string.classroom_rejoin)) }
    }
    OutlinedButton(onClick = onExit, modifier = Modifier.heightIn(min = 48.dp)) { Text(stringResource(R.string.classroom_back)) }
}

private sealed interface Confirm {
    data object End : Confirm
    data object MuteAll : Confirm
    data class Remove(val p: RoomParticipant) : Confirm
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun LiveRoom(state: ClassroomState, manager: ClassroomManager) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var showParticipants by remember { mutableStateOf(false) }
    var confirm by remember { mutableStateOf<Confirm?>(null) }
    var actionError by remember { mutableStateOf<ApiException?>(null) }
    val camera = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { if (it) manager.toggleCamera() }

    fun run(block: suspend () -> Unit) {
        actionError = null
        scope.launch {
            try {
                block()
            } catch (e: ApiException) {
                actionError = e
            }
        }
    }

    val teacher = state.participants.firstOrNull { it.isTeacher }
    val students = state.participants.filter { !it.isTeacher }

    Column(Modifier.fillMaxSize().padding(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            val reconnecting = state.status == ClassroomStatus.Reconnecting
            Surface(shape = RoundedCornerShape(50), color = if (reconnecting) MaterialTheme.colorScheme.secondary else MaterialTheme.colorScheme.primary) {
                Text(
                    stringResource(if (reconnecting) R.string.classroom_reconnecting else R.string.classroom_connected),
                    color = Color.White,
                    modifier = Modifier.padding(horizontal = 12.dp, vertical = 4.dp).semantics { liveRegion = LiveRegionMode.Polite },
                )
            }
            Text(state.classTitle, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
        }

        state.notice?.let { notice ->
            Surface(color = MaterialTheme.colorScheme.secondaryContainer, shape = RoundedCornerShape(8.dp), modifier = Modifier.fillMaxWidth()) {
                Row(Modifier.padding(12.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        stringResource(if (notice == ClassroomNotice.MutedEveryone) R.string.classroom_muted_everyone else R.string.classroom_muted_by_teacher),
                        modifier = Modifier.weight(1f).semantics { liveRegion = LiveRegionMode.Assertive },
                    )
                    TextButton(onClick = manager::clearNotice) { Text(stringResource(R.string.done)) }
                }
            }
        }
        actionError?.let { ErrorMessage(it) }

        // Teacher tile: top ~40% (PRD 11.2 layout). Video when the camera is on, otherwise initials.
        Box(Modifier.fillMaxWidth().weight(0.4f)) {
            if (teacher != null) Tile(teacher, manager, large = true) else Text(stringResource(R.string.classroom_teacher_not_here), modifier = Modifier.align(Alignment.Center))
        }

        LazyVerticalGrid(columns = GridCells.Fixed(3), modifier = Modifier.weight(0.6f), verticalArrangement = Arrangement.spacedBy(6.dp), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            items(students, key = { it.userId }) { Tile(it, manager, large = false) }
        }

        Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            val big = Modifier.weight(1f).heightIn(min = 64.dp)
            FilledTonalButton(onClick = manager::toggleMic, modifier = big, colors = if (state.micOn) ButtonDefaults.filledTonalButtonColors() else ButtonDefaults.filledTonalButtonColors(containerColor = MaterialTheme.colorScheme.errorContainer)) {
                Text(stringResource(if (state.micOn) R.string.classroom_mic_on else R.string.classroom_mic_off))
            }
            FilledTonalButton(
                onClick = { if (state.cameraOn || granted(context, Manifest.permission.CAMERA)) manager.toggleCamera() else camera.launch(Manifest.permission.CAMERA) },
                enabled = !state.audioOnly,
                modifier = big,
            ) { Text(stringResource(if (state.cameraOn) R.string.classroom_camera_on else R.string.classroom_camera_off)) }
            OutlinedButton(onClick = manager::toggleAudioOnly, modifier = big) {
                Text(stringResource(R.string.classroom_audio_only) + if (state.audioOnly) " ✓" else "")
            }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            val big = Modifier.weight(1f).heightIn(min = 56.dp)
            OutlinedButton(onClick = { showParticipants = true }, modifier = big) { Text(stringResource(R.string.classroom_participants, state.participants.size)) }
            OutlinedButton(onClick = manager::leave, modifier = big) { Text(stringResource(R.string.classroom_leave)) }
            if (state.isTeacher) {
                Button(onClick = { confirm = Confirm.End }, modifier = big, colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error)) {
                    Text(stringResource(R.string.classroom_end))
                }
            }
        }
    }

    if (showParticipants) {
        ModalBottomSheet(onDismissRequest = { showParticipants = false }) {
            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(stringResource(R.string.classroom_participants, state.participants.size), style = MaterialTheme.typography.titleMedium, modifier = Modifier.weight(1f))
                    if (state.isTeacher) {
                        OutlinedButton(onClick = { confirm = Confirm.MuteAll }, modifier = Modifier.heightIn(min = 48.dp)) { Text(stringResource(R.string.classroom_mute_all)) }
                    }
                }
                LazyColumn(Modifier.fillMaxWidth().heightIn(max = 480.dp)) {
                    items(state.participants, key = { it.userId }) { p ->
                        Row(Modifier.fillMaxWidth().heightIn(min = 56.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            Column(Modifier.weight(1f)) {
                                Text(p.name + (if (p.isLocal) " " + stringResource(R.string.classroom_you) else "") + if (p.isTeacher) " · " + stringResource(R.string.classroom_teacher) else "", fontWeight = FontWeight.SemiBold)
                                Text(
                                    listOf(
                                        stringResource(if (p.micOn) R.string.classroom_mic_on else R.string.classroom_mic_off),
                                        stringResource(if (p.cameraOn) R.string.classroom_camera_on else R.string.classroom_camera_off),
                                        quality(p.quality),
                                    ).joinToString(" · "),
                                    style = MaterialTheme.typography.bodySmall,
                                )
                            }
                            if (state.isTeacher && !p.isLocal) {
                                TextButton(onClick = { run { manager.mute(p.userId) } }, enabled = p.micOn, modifier = Modifier.heightIn(min = 48.dp)) { Text(stringResource(R.string.classroom_mute)) }
                                TextButton(onClick = { confirm = Confirm.Remove(p) }, modifier = Modifier.heightIn(min = 48.dp)) { Text(stringResource(R.string.remove), color = MaterialTheme.colorScheme.error) }
                            }
                        }
                    }
                }
            }
        }
    }

    when (val c = confirm) {
        Confirm.End -> ConfirmDialog(stringResource(R.string.classroom_end_confirm), null, stringResource(R.string.classroom_end), danger = true,
            onConfirm = { confirm = null; run { manager.endClass() } }, onDismiss = { confirm = null })
        Confirm.MuteAll -> ConfirmDialog(stringResource(R.string.classroom_mute_all_confirm), null, stringResource(R.string.classroom_mute_all),
            onConfirm = { confirm = null; run { manager.muteAll() } }, onDismiss = { confirm = null })
        is Confirm.Remove -> ConfirmDialog(stringResource(R.string.classroom_remove_confirm, c.p.name), null, stringResource(R.string.remove), danger = true,
            onConfirm = { confirm = null; run { manager.remove(c.p.userId) } }, onDismiss = { confirm = null })
        null -> Unit
    }
}

@Composable
private fun quality(q: ConnectionQuality): String = when (q) {
    ConnectionQuality.EXCELLENT, ConnectionQuality.GOOD -> "●●●"
    ConnectionQuality.POOR -> "●○○"
    ConnectionQuality.LOST -> "○○○"
    ConnectionQuality.UNKNOWN -> "…"
}

@Composable
private fun Tile(p: RoomParticipant, manager: ClassroomManager, large: Boolean) {
    val ring = if (p.speaking) MaterialTheme.colorScheme.secondary else Color.Transparent
    val description = "${p.name}, ${stringResource(if (p.micOn) R.string.classroom_mic_on else R.string.classroom_mic_off)}"
    Box(
        Modifier
            .then(if (large) Modifier.fillMaxSize() else Modifier.aspectRatio(1f))
            .clip(RoundedCornerShape(12.dp))
            .background(MaterialTheme.colorScheme.surfaceVariant)
            .border(3.dp, ring, RoundedCornerShape(12.dp))
            .semantics { contentDescription = description },
        contentAlignment = Alignment.Center,
    ) {
        val video = p.video
        if (video != null) {
            VideoTrackView(videoTrack = video, passedRoom = manager.currentRoom, mirror = p.isLocal, modifier = Modifier.fillMaxSize())
        } else {
            Box(
                Modifier.fillMaxHeight(if (large) 0.5f else 0.6f).aspectRatio(1f).clip(CircleShape).background(MaterialTheme.colorScheme.primary),
                contentAlignment = Alignment.Center,
            ) {
                Text(p.name.take(1).uppercase(), color = Color.White, style = if (large) MaterialTheme.typography.displaySmall else MaterialTheme.typography.titleLarge)
            }
        }
        Surface(
            color = Color.Black.copy(alpha = 0.55f),
            shape = RoundedCornerShape(topEnd = 8.dp),
            modifier = Modifier.align(Alignment.BottomStart),
        ) {
            Text(
                p.name + if (p.micOn) "" else " 🔇",
                color = Color.White,
                style = MaterialTheme.typography.labelMedium,
                modifier = Modifier.padding(horizontal = 6.dp, vertical = 2.dp),
            )
        }
    }
}
