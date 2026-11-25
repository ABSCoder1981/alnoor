package com.alnoor.app.ui.poc

import android.Manifest
import android.content.pm.PackageManager
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.Checkbox
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.alnoor.app.livekit.AudioTestSettings
import io.livekit.android.compose.ui.VideoTrackView
import io.livekit.android.room.participant.ConnectionQuality

/** Phase 2 proof of concept (debug builds only): join a LiveKit room and compare audio settings. */
@Composable
fun PocScreen(viewModel: PocViewModel = viewModel()) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    Scaffold { padding ->
        val room = state.room
        if (room == null) {
            JoinScreen(state, viewModel, Modifier.padding(padding))
        } else {
            RoomScreen(room, state.error, viewModel, Modifier.padding(padding))
        }
    }
}

@Composable
private fun JoinScreen(state: PocUiState, viewModel: PocViewModel, modifier: Modifier) {
    val form = state.form
    val micPermission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) viewModel.join() else viewModel.onMicPermissionDenied()
    }
    val context = LocalContext.current
    val hasMic = ContextCompat.checkSelfPermission(context, Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED

    Column(
        modifier = modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Text("LiveKit test room", style = MaterialTheme.typography.headlineSmall, color = MaterialTheme.colorScheme.primary)
        Text("Phase 2 – development build only", style = MaterialTheme.typography.bodySmall)

        OutlinedTextField(
            value = form.apiBaseUrl,
            onValueChange = { v -> viewModel.updateForm { it.copy(apiBaseUrl = v) } },
            label = { Text("API address") },
            supportingText = { Text("Emulator: http://10.0.2.2:5080 · Phone: http://<PC Wi-Fi IP>:5080") },
            singleLine = true,
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
            modifier = Modifier.fillMaxWidth(),
        )
        OutlinedTextField(
            value = form.name,
            onValueChange = { v -> viewModel.updateForm { it.copy(name = v.take(50)) } },
            label = { Text("Your name") },
            singleLine = true,
            modifier = Modifier.fillMaxWidth(),
        )
        OutlinedTextField(
            value = form.room,
            onValueChange = { v -> viewModel.updateForm { it.copy(room = v.lowercase().take(40)) } },
            label = { Text("Room") },
            singleLine = true,
            modifier = Modifier.fillMaxWidth(),
        )

        Text("Role", fontWeight = FontWeight.SemiBold)
        PocRole.entries.forEach { role ->
            LabeledRow(
                selected = form.role == role,
                label = if (role == PocRole.Teacher) "Teacher (joins with mic on)" else "Student (joins muted)",
                onClick = { viewModel.updateForm { it.copy(role = role) } },
                radio = true,
            )
        }

        Text("Audio settings to test", fontWeight = FontWeight.SemiBold)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            AudioTestSettings.BITRATE_OPTIONS.forEach { kbps ->
                val selected = form.settings.bitrateKbps == kbps
                val select = { viewModel.updateForm { it.copy(settings = it.settings.copy(bitrateKbps = kbps)) } }
                if (selected) Button(onClick = select, modifier = Modifier.heightIn(min = 48.dp)) { Text("$kbps kbps") }
                else OutlinedButton(onClick = select, modifier = Modifier.heightIn(min = 48.dp)) { Text("$kbps kbps") }
            }
        }
        LabeledRow(form.settings.dtx, "DTX (silence suppression)", { viewModel.updateForm { it.copy(settings = it.settings.copy(dtx = !it.settings.dtx)) } })
        LabeledRow(form.settings.noiseSuppression, "Noise suppression", { viewModel.updateForm { it.copy(settings = it.settings.copy(noiseSuppression = !it.settings.noiseSuppression)) } })
        LabeledRow(form.settings.autoGainControl, "Auto gain control", { viewModel.updateForm { it.copy(settings = it.settings.copy(autoGainControl = !it.settings.autoGainControl)) } })

        state.error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
        Text(
            "The microphone is needed so the teacher and class can hear your recitation.",
            style = MaterialTheme.typography.bodySmall,
        )
        Button(
            onClick = { if (hasMic) viewModel.join() else micPermission.launch(Manifest.permission.RECORD_AUDIO) },
            enabled = !state.joining,
            modifier = Modifier
                .fillMaxWidth()
                .heightIn(min = 56.dp),
        ) {
            Text(if (state.joining) "Joining…" else "Join")
        }
    }
}

@Composable
private fun LabeledRow(selected: Boolean, label: String, onClick: () -> Unit, radio: Boolean = false) {
    val rowModifier = if (radio) {
        Modifier.selectable(selected = selected, onClick = onClick, role = Role.RadioButton)
    } else {
        Modifier.toggleable(value = selected, onValueChange = { onClick() }, role = Role.Checkbox)
    }
    Row(
        modifier = rowModifier
            .fillMaxWidth()
            .heightIn(min = 48.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (radio) RadioButton(selected = selected, onClick = null) else Checkbox(checked = selected, onCheckedChange = null)
        Spacer(Modifier.width(8.dp))
        Text(label)
    }
}

@Composable
private fun RoomScreen(room: RoomUi, error: String?, viewModel: PocViewModel, modifier: Modifier) {
    val cameraPermission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) viewModel.toggleCamera()
    }
    val context = LocalContext.current

    Column(modifier = modifier.fillMaxSize().padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            val (label, color) = when (room.connection) {
                ConnectionUi.Joining -> "Joining…" to MaterialTheme.colorScheme.onSurface
                ConnectionUi.Connected -> "Connected" to MaterialTheme.colorScheme.primary
                ConnectionUi.Reconnecting -> "Reconnecting…" to MaterialTheme.colorScheme.secondary
                ConnectionUi.Disconnected -> "Disconnected" to MaterialTheme.colorScheme.error
            }
            Text(label, color = color, fontWeight = FontWeight.Bold)
        }
        Text("Testing: ${room.settingsLabel}", style = MaterialTheme.typography.bodySmall)
        error?.let { Text(it, color = MaterialTheme.colorScheme.error) }

        if (!room.audioOnly && room.videoTracks.isNotEmpty()) {
            room.videoTracks.take(2).forEach { track ->
                VideoTrackView(
                    videoTrack = track,
                    passedRoom = viewModel.currentRoom,
                    modifier = Modifier.fillMaxWidth().aspectRatio(16f / 9f),
                )
            }
        }

        Text("Participants (${room.participants.size})", fontWeight = FontWeight.SemiBold)
        LazyColumn(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            items(room.participants, key = { it.identity }) { ParticipantRow(it) }
        }

        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
            val big = Modifier.weight(1f).heightIn(min = 64.dp)
            FilledTonalButton(onClick = viewModel::toggleMic, modifier = big) { Text(if (room.micOn) "Mic on" else "Mic off") }
            FilledTonalButton(
                onClick = {
                    val granted = ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED
                    if (granted || room.cameraOn) viewModel.toggleCamera() else cameraPermission.launch(Manifest.permission.CAMERA)
                },
                enabled = !room.audioOnly,
                modifier = big,
            ) { Text(if (room.cameraOn) "Cam on" else "Cam off") }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
            val big = Modifier.weight(1f).heightIn(min = 64.dp)
            OutlinedButton(onClick = viewModel::toggleAudioOnly, modifier = big) { Text(if (room.audioOnly) "Audio only: ON" else "Audio only: off") }
            Button(onClick = viewModel::leave, modifier = big) { Text("Leave") }
        }
    }
}

@Composable
private fun ParticipantRow(p: ParticipantUi) {
    Card(
        border = if (p.speaking) BorderStroke(2.dp, MaterialTheme.colorScheme.secondary) else null,
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(Modifier.padding(12.dp)) {
            Text(
                p.name + (if (p.isLocal) " (you)" else "") + if (p.isTeacher) " · Teacher" else " · Student",
                fontWeight = FontWeight.SemiBold,
            )
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Text(if (p.micOn) "Mic on" else "Mic off", color = if (p.micOn) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.error)
                Text(if (p.cameraOn) "Cam on" else "Cam off")
                Text(
                    when (p.quality) {
                        ConnectionQuality.EXCELLENT, ConnectionQuality.GOOD -> "Connection good"
                        ConnectionQuality.POOR -> "Connection poor"
                        ConnectionQuality.LOST -> "Connection lost"
                        ConnectionQuality.UNKNOWN -> "Connection …"
                    },
                )
            }
        }
    }
}
