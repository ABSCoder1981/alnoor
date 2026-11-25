package com.alnoor.app.livekit

import io.livekit.android.RoomOptions
import io.livekit.android.room.participant.AudioTrackPublishDefaults
import io.livekit.android.room.participant.VideoTrackPublishDefaults
import io.livekit.android.room.track.LocalAudioTrackOptions
import io.livekit.android.room.track.LocalVideoTrackOptions
import io.livekit.android.room.track.VideoCaptureParameter
import io.livekit.android.room.track.VideoEncoding

/**
 * Settings compared in the Phase 2 recitation tests (PRD sections 18.2 and 18.5).
 * Echo cancellation always stays on; RED (redundancy) always on.
 */
data class AudioTestSettings(
    val bitrateKbps: Int = 32,
    val dtx: Boolean = true,
    val noiseSuppression: Boolean = true,
    val autoGainControl: Boolean = false,
) {
    init {
        require(bitrateKbps in BITRATE_OPTIONS) { "Unsupported bitrate $bitrateKbps" }
    }

    fun describe(): String {
        fun onOff(value: Boolean) = if (value) "on" else "off"
        return "$bitrateKbps kbps · DTX ${onOff(dtx)} · NS ${onOff(noiseSuppression)} · AGC ${onOff(autoGainControl)}"
    }

    companion object {
        val BITRATE_OPTIONS = listOf(24, 32, 48)
    }
}

/** Video: 640x360 @ 15 fps with simulcast; adaptive stream and dynacast on (PRD 18.2). */
fun AudioTestSettings.toRoomOptions(): RoomOptions = RoomOptions(
    adaptiveStream = true,
    dynacast = true,
    audioTrackCaptureDefaults = audioCaptureOptions(),
    audioTrackPublishDefaults = AudioTrackPublishDefaults(
        audioBitrate = bitrateKbps * 1000,
        dtx = dtx,
        red = true,
    ),
    videoTrackCaptureDefaults = LocalVideoTrackOptions(
        captureParams = VideoCaptureParameter(width = 640, height = 360, maxFps = 15),
    ),
    videoTrackPublishDefaults = VideoTrackPublishDefaults(
        videoEncoding = VideoEncoding(maxBitrate = 500_000, maxFps = 15),
        simulcast = true,
    ),
)

fun AudioTestSettings.audioCaptureOptions(): LocalAudioTrackOptions = LocalAudioTrackOptions(
    noiseSuppression = noiseSuppression,
    echoCancellation = true,
    autoGainControl = autoGainControl,
)
