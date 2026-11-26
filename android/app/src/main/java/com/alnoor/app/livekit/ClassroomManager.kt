package com.alnoor.app.livekit

import android.app.Application
import com.alnoor.app.data.api.AlNoorApi
import com.alnoor.app.data.auth.ApiException
import io.livekit.android.LiveKit
import io.livekit.android.events.DisconnectReason
import io.livekit.android.events.RoomEvent
import io.livekit.android.events.collect
import io.livekit.android.room.Room
import io.livekit.android.room.participant.ConnectionQuality
import io.livekit.android.room.participant.Participant
import io.livekit.android.room.track.RemoteTrackPublication
import io.livekit.android.room.track.Track
import io.livekit.android.room.track.VideoTrack
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

data class RoomParticipant(
    val userId: String,
    val name: String,
    val isLocal: Boolean,
    val isTeacher: Boolean,
    val micOn: Boolean,
    val cameraOn: Boolean,
    val speaking: Boolean,
    val quality: ConnectionQuality,
    val video: VideoTrack?,
)

enum class ClassroomStatus { Idle, Joining, Connected, Reconnecting, Ended }

/** Why the class screen closed (PRD 11.2 screen 12: "Class ended", with a different message on removal). */
enum class EndReason { ClassEnded, Removed, Left, ConnectionLost, Failed }

/** One-off messages for the user (FR-LIVE-05/05a). */
enum class ClassroomNotice { MutedByTeacher, MutedEveryone }

data class ClassroomState(
    val status: ClassroomStatus = ClassroomStatus.Idle,
    val sessionId: String? = null,
    val classTitle: String = "",
    val isTeacher: Boolean = false,
    val participants: List<RoomParticipant> = emptyList(),
    val micOn: Boolean = false,
    val cameraOn: Boolean = false,
    val audioOnly: Boolean = false,
    val notice: ClassroomNotice? = null,
    val endReason: EndReason? = null,
    val error: ApiException? = null,
    val joinedAtMillis: Long? = null,
    val endedAtMillis: Long? = null,
)

/**
 * Owns the LiveKit room for the whole class (PRD 12.3): it lives in the application, not in a screen, so rotating
 * or leaving the screen does not drop the call. [ClassroomService] keeps the process in the foreground.
 */
class ClassroomManager(private val app: Application, private val api: AlNoorApi) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val json = Json { ignoreUnknownKeys = true }
    private var room: Room? = null
    private var eventsJob: Job? = null
    private var selfMuting = false

    private val _state = MutableStateFlow(ClassroomState())
    val state: StateFlow<ClassroomState> = _state.asStateFlow()

    val currentRoom: Room? get() = room

    val isActive: Boolean get() = _state.value.status in setOf(ClassroomStatus.Joining, ClassroomStatus.Connected, ClassroomStatus.Reconnecting)

    /** Joins via our API (all checks happen there), then connects to LiveKit. Teacher joins with mic on; students muted (C5). */
    suspend fun join(sessionId: String, classTitle: String) {
        if (isActive && _state.value.sessionId == sessionId) return
        if (isActive) leave()
        _state.value = ClassroomState(status = ClassroomStatus.Joining, sessionId = sessionId, classTitle = classTitle)

        val info = try {
            api.require { join(sessionId) }
        } catch (e: ApiException) {
            _state.update { it.copy(status = ClassroomStatus.Ended, endReason = EndReason.Failed, error = e) }
            return
        }

        val isTeacher = info.role == "Teacher"
        val newRoom = LiveKit.create(app, AudioTestSettings().toRoomOptions())
        room = newRoom
        _state.update { it.copy(isTeacher = isTeacher) }
        eventsJob = scope.launch { newRoom.events.collect { onEvent(newRoom, it) } }

        try {
            newRoom.connect(info.livekitUrl, info.token)
            if (isTeacher) newRoom.localParticipant.setMicrophoneEnabled(true)
            _state.update { it.copy(status = ClassroomStatus.Connected, joinedAtMillis = System.currentTimeMillis()) }
            refresh(newRoom)
        } catch (e: Exception) {
            release()
            _state.update {
                it.copy(status = ClassroomStatus.Ended, endReason = EndReason.Failed, error = ApiException(0, "RTC_CONNECT_FAILED", e.message ?: "Could not connect."))
            }
        }
    }

    fun toggleMic() = withRoom { r ->
        val enable = !r.localParticipant.isMicrophoneEnabled
        selfMuting = !enable
        r.localParticipant.setMicrophoneEnabled(enable)
        _state.update { it.copy(notice = null) }
        refresh(r)
    }

    /** Camera permission must already be granted (asked by the screen when the user taps Camera). */
    fun toggleCamera() = withRoom { r ->
        if (_state.value.audioOnly) return@withRoom
        r.localParticipant.setCameraEnabled(!r.localParticipant.isCameraEnabled)
        refresh(r)
    }

    /** Audio-first (PRD 18.3, FR-LIVE-12): stop receiving video and turn the camera off. */
    fun toggleAudioOnly() = withRoom { r ->
        val audioOnly = !_state.value.audioOnly
        _state.update { it.copy(audioOnly = audioOnly) }
        applyAudioOnly(r)
        if (audioOnly) r.localParticipant.setCameraEnabled(false)
        refresh(r)
    }

    fun clearNotice() = _state.update { it.copy(notice = null) }

    /** Leaves; the class continues for others (FR-LIVE-11). */
    fun leave() {
        val id = _state.value.sessionId
        if (id != null && isActive) scope.launch { runCatching { api.call { leave(id) } } }
        release()
        _state.update { it.copy(status = ClassroomStatus.Ended, endReason = EndReason.Left, endedAtMillis = System.currentTimeMillis()) }
    }

    /** Teacher only: ends the class for everyone (FR-SES-03). The server deletes the room, which disconnects us. */
    suspend fun endClass() {
        val id = _state.value.sessionId ?: return
        api.call { end(id) }
    }

    suspend fun mute(userId: String) {
        _state.value.sessionId?.let { id -> api.call { mute(id, userId) } }
    }

    suspend fun muteAll(): Int = _state.value.sessionId?.let { id -> api.call { muteAll(id) }?.mutedCount } ?: 0

    suspend fun remove(userId: String) {
        _state.value.sessionId?.let { id -> api.call { remove(id, userId) } }
    }

    /** Forget a finished class so the next one starts clean. */
    fun reset() {
        if (!isActive) _state.value = ClassroomState()
    }

    private fun onEvent(r: Room, event: RoomEvent) {
        when (event) {
            is RoomEvent.Reconnecting -> _state.update { it.copy(status = ClassroomStatus.Reconnecting) }
            is RoomEvent.Reconnected -> _state.update { it.copy(status = ClassroomStatus.Connected) }
            is RoomEvent.TrackPublished -> applyAudioOnly(r)
            is RoomEvent.TrackMuted -> if (event.participant == r.localParticipant && event.publication.source == Track.Source.MICROPHONE) {
                if (!selfMuting && _state.value.notice == null) _state.update { it.copy(notice = ClassroomNotice.MutedByTeacher) }
            }
            is RoomEvent.DataReceived -> if (event.topic == NOTICE_TOPIC && !_state.value.isTeacher) {
                val type = runCatching { json.parseToJsonElement(event.data.decodeToString()).jsonObject["type"]?.jsonPrimitive?.content }.getOrNull()
                when (type) {
                    "muted" -> _state.update { it.copy(notice = ClassroomNotice.MutedByTeacher) }
                    "muted_all" -> _state.update { it.copy(notice = ClassroomNotice.MutedEveryone) }
                }
            }
            is RoomEvent.Disconnected -> {
                val reason = when (event.reason) {
                    DisconnectReason.ROOM_DELETED, DisconnectReason.ROOM_CLOSED -> EndReason.ClassEnded
                    DisconnectReason.PARTICIPANT_REMOVED -> EndReason.Removed
                    DisconnectReason.CLIENT_INITIATED -> EndReason.Left
                    else -> EndReason.ConnectionLost // Phase 5: automatic rejoin through the API (PRD 20.2).
                }
                release()
                _state.update { it.copy(status = ClassroomStatus.Ended, endReason = reason, endedAtMillis = System.currentTimeMillis()) }
                return
            }
            else -> Unit
        }
        if (event is RoomEvent.TrackMuted || event is RoomEvent.TrackUnmuted) selfMuting = false
        refresh(r)
    }

    private fun applyAudioOnly(r: Room) {
        val subscribe = !_state.value.audioOnly
        r.remoteParticipants.values
            .flatMap { it.trackPublications.values }
            .filterIsInstance<RemoteTrackPublication>()
            .filter { it.kind == Track.Kind.VIDEO }
            .forEach { it.setSubscribed(subscribe) }
    }

    private fun refresh(r: Room) {
        val audioOnly = _state.value.audioOnly
        val all = listOf<Participant>(r.localParticipant) + r.remoteParticipants.values
        val ui = all.map { p ->
            val isTeacher = p.attributes["role"] == "teacher"
            val camera = p.getTrackPublication(Track.Source.CAMERA)?.takeIf { !it.muted }?.track as? VideoTrack
            RoomParticipant(
                userId = p.identity?.value.orEmpty(),
                name = p.name?.takeIf { it.isNotBlank() } ?: "—",
                isLocal = p == r.localParticipant,
                isTeacher = isTeacher,
                micOn = p.isMicrophoneEnabled,
                cameraOn = p.isCameraEnabled,
                speaking = p.isSpeaking,
                quality = p.connectionQuality,
                video = if (audioOnly && p != r.localParticipant) null else camera,
            )
        }.sortedWith(compareByDescending<RoomParticipant> { it.isTeacher }.thenBy { it.name })
        _state.update {
            it.copy(
                participants = ui,
                micOn = r.localParticipant.isMicrophoneEnabled,
                cameraOn = r.localParticipant.isCameraEnabled,
            )
        }
    }

    private fun withRoom(action: suspend (Room) -> Unit) {
        val r = room ?: return
        scope.launch { runCatching { action(r) } }
    }

    private fun release() {
        eventsJob?.cancel()
        eventsJob = null
        room?.disconnect()
        room?.release()
        room = null
    }

    companion object {
        const val NOTICE_TOPIC = "alnoor.moderation"
    }
}
