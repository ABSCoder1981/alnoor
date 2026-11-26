import { describe, expect, it } from 'vitest'
import { buildRoomOptions, defaultAudioSettings, describeSettings } from './audioSettings'

describe('buildRoomOptions', () => {
  it('uses the PRD 18.2 starting values by default', () => {
    const options = buildRoomOptions(defaultAudioSettings)

    expect(options.publishDefaults?.audioPreset?.maxBitrate).toBe(32_000)
    expect(options.publishDefaults?.dtx).toBe(true)
    expect(options.publishDefaults?.red).toBe(true)
    expect(options.audioCaptureDefaults?.echoCancellation).toBe(true)
    expect(options.audioCaptureDefaults?.autoGainControl).toBe(false)
    expect(options.adaptiveStream).toBe(true)
    expect(options.dynacast).toBe(true)
    expect(options.videoCaptureDefaults?.resolution).toEqual({ width: 640, height: 360, frameRate: 15 })
  })

  it('applies the tester-selected audio settings', () => {
    const options = buildRoomOptions({ bitrateKbps: 48, dtx: false, noiseSuppression: false, autoGainControl: true })

    expect(options.publishDefaults?.audioPreset?.maxBitrate).toBe(48_000)
    expect(options.publishDefaults?.dtx).toBe(false)
    expect(options.audioCaptureDefaults?.noiseSuppression).toBe(false)
    expect(options.audioCaptureDefaults?.autoGainControl).toBe(true)
    expect(options.audioCaptureDefaults?.echoCancellation).toBe(true)
  })
})

describe('describeSettings', () => {
  it('produces a label testers can record', () => {
    expect(describeSettings(defaultAudioSettings)).toBe('32 kbps · DTX on · NS on · AGC off')
  })
})
