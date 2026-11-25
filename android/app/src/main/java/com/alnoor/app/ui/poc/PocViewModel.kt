package com.alnoor.app.ui.poc

import android.app.Application
import android.content.Context
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.alnoor.app.BuildConfig
import com.alnoor.app.data.DevTokenRepository
import com.alnoor.app.data.DevTokenRequest
import com.alnoor.app.data.DevTokenResult
import com.alnoor.app.livekit.AudioTestSettings
import com.alnoor.app.livekit.toRoomOptions
import io.livekit.android.LiveKit
import io.livekit.android.events.RoomEvent
import io.livekit.android.events.collect
import io.livekit.android.room.Room
import io.livekit.android.room.participant.ConnectionQuality
import io.livekit.android.room.participant.Participant
import io.livekit.android.room.track.RemoteTrackPublication
import io.livekit.android.room.track.Track
import io.livekit.android.room.track.VideoTrack
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

enum class PocRole(val apiValue: String) { Teacher("teacher"), Student("student") }

data class JoinForm(
    val apiBaseUrl: String,
    val name: String = "",
    val room: String = "poc",
    val role: PocRole = PocRole.Student,
    val settings: AudioTestSettings = AudioTestSettings(),
)

data class ParticipantUi(
    val identity: String,
    val name: String,
    val isLocal: Boolean,
    val isTeacher: Boolean,
    val micOn: Boolean,
    val cameraOn: Boolean,
    val speaking: Boolean,
    val quality: ConnectionQuality,
)

enum class ConnectionUi { Joining, Connected, Reconnecting, Disconnected }

data class RoomUi(
    val connection: ConnectionUi = ConnectionUi.Joining,
    val participants: List<ParticipantUi> = emptyList(),
    val videoTracks: List<VideoTrack> = emptyList(),
    val micOn: Boolean = false,
    val cameraOn: Boolean = false,
    val audioOnly: Boolean = false,
    val settingsLabel: String = "",
)

data class PocUiState(
    val form: JoinForm,
    val joining: Boolean = false,
    val error: String? = null,
    val room: RoomUi? = null,
)

/**
 * Phase 2 proof of concept. Owns the LiveKit Room so screen rotation does not drop the call.
 * (Phase 4 moves room ownership into a foreground-service-scoped ClassroomManager, PRD 12.3 / 20.4.)
 */
class PocViewModel(application: Application) : AndroidViewModel(application) {
    private val prefs = application.getSharedPreferences("poc", Context.MODE_PRIVATE)
    private val tokens = DevTokenRepository()
    private var room: Room? = null
    private var eventsJob: Job? = null

    private val _state = MutableStateFlow(
        PocUiState(
            form = JoinForm(
                apiBaseUrl = prefs.getString(KEY_API_URL, null) ?: BuildConfig.DEFAULT_DEV_API_BASE_URL,
                name = prefs.getString(KEY_NAME, null).orEmpty(),
            ),
        ),
    )
    val state: StateFlow<PocUiState> = _state.asStateFlow()

    /** The connected room, for video rendering only. */
    val currentRoom: Room? get() = room

    fun updateForm(transform: (JoinForm) -> JoinForm) = _state.update { it.copy(form = transform(it.form), error = null) }

    fun join() {
        val form = _state.value.form
        val name = form.name.trim()
        when {
            name.isEmpty() -> return showError("Enter your name.")
            name.length > 50 -> return showError("Name must be at most 50 characters.")
            !ROOM_PATTERN.matches(form.room) -> return showError("Room may contain only a-z, 0-9 and '-' (max 40).")
        }
        prefs.edit().putString(KEY_API_URL, form.apiBaseUrl.trim()).putString(KEY_NAME, name).apply()
        _state.update { it.copy(joining = true, error = null) }

        viewModelScope.launch {
            val result = tokens.requestToken(form.apiBaseUrl, DevTokenRequest(name, form.room, form.role.apiValue))
            if (result is DevTokenResult.Failure) {
                _state.update { it.copy(joining = false, error = result.message) }
                return@launch
            }
            val connection = (result as DevTokenResult.Success).response
            val newRoom = LiveKit.create(getApplication(), form.settings.toRoomOptions())
            room = newRoom
            _state.update { it.copy(joining = false, room = RoomUi(settingsLabel = form.settings.describe())) }
            eventsJob = launch { newRoom.events.collect { event -> onRoomEvent(newRoom, event) } }

            try {
                newRoom.connect(connection.url, connection.token)
                // Students join muted (PRD C5 / FR-LIVE-03); the teacher starts with the mic on for testing.
                if (form.role == PocRole.Teacher) newRoom.localParticipant.setMicrophoneEnabled(true)
                refresh(newRoom)
            } catch (e: Exception) {
                releaseRoom()
                _state.update { it.copy(room = null, error = "Could not connect to LiveKit at ${connection.url}: ${e.message}") }
            }
        }
    }

    fun onMicPermissionDenied() = showError(
        "Microphone permission is needed to join. Allow it in Settings > Apps > ALNoor > Permissions.",
    )

    fun toggleMic() = withRoom { room ->
        room.localParticipant.setMicrophoneEnabled(!room.localParticipant.isMicrophoneEnabled)
        refresh(room)
    }

    fun toggleCamera() = withRoom { room ->
        if (_state.value.room?.audioOnly == true) return@withRoom
        room.localParticipant.setCameraEnabled(!room.localParticipant.isCameraEnabled)
        refresh(room)
    }

    /** Audio-first: stop receiving all remote video and turn own camera off (PRD 18.3). */
    fun toggleAudioOnly() = withRoom { room ->
        val audioOnly = !(_state.value.room?.audioOnly ?: false)
        _state.update { it.copy(room = it.room?.copy(audioOnly = audioOnly)) }
        applyAudioOnly(room)
        if (audioOnly) room.localParticipant.setCameraEnabled(false)
        refresh(room)
    }

    fun leave() {
        releaseRoom()
        _state.update { it.copy(room = null) }
    }

    private fun onRoomEvent(room: Room, event: RoomEvent) {
        when (event) {
            is RoomEvent.TrackPublished -> applyAudioOnly(room)
            is RoomEvent.Disconnected -> {
                releaseRoom()
                _state.update {
                    it.copy(room = null, error = event.error?.let { e -> "Disconnected: ${e.message}" })
                }
                return
            }
            else -> Unit
        }
        refresh(room)
    }

    private fun applyAudioOnly(room: Room) {
        val subscribe = !(_state.value.room?.audioOnly ?: false)
        room.remoteParticipants.values
            .flatMap { it.trackPublications.values }
            .filterIsInstance<RemoteTrackPublication>()
            .filter { it.kind == Track.Kind.VIDEO }
            .forEach { it.setSubscribed(subscribe) }
    }

    private fun refresh(room: Room) {
        val participants = listOf<Participant>(room.localParticipant) + room.remoteParticipants.values
        val connection = when (room.state) {
            Room.State.CONNECTING -> ConnectionUi.Joining
            Room.State.CONNECTED -> ConnectionUi.Connected
            Room.State.RECONNECTING -> ConnectionUi.Reconnecting
            Room.State.DISCONNECTED -> ConnectionUi.Disconnected
        }
        val audioOnly = _state.value.room?.audioOnly ?: false
        _state.update { current ->
            current.copy(
                room = current.room?.copy(
                    connection = connection,
                    participants = participants.map { it.toUi(room) },
                    videoTracks = if (audioOnly) emptyList() else participants.mapNotNull { cameraTrack(it) },
                    micOn = room.localParticipant.isMicrophoneEnabled,
                    cameraOn = room.localParticipant.isCameraEnabled,
                ),
            )
        }
    }

    private fun cameraTrack(participant: Participant): VideoTrack? {
        val publication = participant.getTrackPublication(Track.Source.CAMERA) ?: return null
        if (publication.muted) return null
        return publication.track as? VideoTrack
    }

    private fun Participant.toUi(room: Room) = ParticipantUi(
        identity = identity?.value.orEmpty(),
        name = name?.takeIf { it.isNotBlank() } ?: identity?.value.orEmpty(),
        isLocal = this == room.localParticipant,
        isTeacher = attributes["role"] == "teacher",
        micOn = isMicrophoneEnabled,
        cameraOn = isCameraEnabled,
        speaking = isSpeaking,
        quality = connectionQuality,
    )

    private fun withRoom(action: suspend (Room) -> Unit) {
        val current = room ?: return
        viewModelScope.launch {
            try {
                action(current)
            } catch (e: Exception) {
                showError(e.message ?: "Action failed.")
            }
        }
    }

    private fun showError(message: String) = _state.update { it.copy(error = message) }

    private fun releaseRoom() {
        eventsJob?.cancel()
        eventsJob = null
        room?.disconnect()
        room?.release()
        room = null
    }

    override fun onCleared() {
        releaseRoom()
        super.onCleared()
    }

    private companion object {
        const val KEY_API_URL = "api_base_url"
        const val KEY_NAME = "name"
        val ROOM_PATTERN = Regex("^[a-z0-9-]{1,40}$")
    }
}
