import { VideoPresets, type RoomOptions } from 'livekit-client'

/**
 * Settings compared in the Phase 2 recitation tests (PRD section 18.2 / 18.5).
 * Echo cancellation always stays on (essential on phone speakers); RED always on (packet-loss resilience).
 */
export interface AudioTestSettings {
  bitrateKbps: 24 | 32 | 48
  dtx: boolean
  noiseSuppression: boolean
  autoGainControl: boolean
}

export const bitrateOptions = [24, 32, 48] as const

/** PRD 18.2 starting values: 32 kbps, DTX on, noise suppression on (to be tested), AGC off (preferred). */
export const defaultAudioSettings: AudioTestSettings = {
  bitrateKbps: 32,
  dtx: true,
  noiseSuppression: true,
  autoGainControl: false,
}

/** Video: 640x360 @ 15 fps, VP8 simulcast 360p/180p, adaptive stream + dynacast (PRD 18.2). */
export function buildRoomOptions(settings: AudioTestSettings): RoomOptions {
  return {
    adaptiveStream: true,
    dynacast: true,
    audioCaptureDefaults: {
      echoCancellation: true,
      noiseSuppression: settings.noiseSuppression,
      autoGainControl: settings.autoGainControl,
      channelCount: 1,
    },
    videoCaptureDefaults: {
      resolution: { width: 640, height: 360, frameRate: 15 },
    },
    publishDefaults: {
      audioPreset: { maxBitrate: settings.bitrateKbps * 1000 },
      dtx: settings.dtx,
      red: true,
      forceStereo: false,
      videoCodec: 'vp8',
      simulcast: true,
      videoSimulcastLayers: [VideoPresets.h180],
    },
  }
}

/** Short label shown in the room so testers can note which combination they rated. */
export function describeSettings(settings: AudioTestSettings): string {
  const onOff = (value: boolean) => (value ? 'on' : 'off')
  return `${settings.bitrateKbps} kbps · DTX ${onOff(settings.dtx)} · NS ${onOff(settings.noiseSuppression)} · AGC ${onOff(settings.autoGainControl)}`
}
