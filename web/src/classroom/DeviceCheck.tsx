import { useEffect, useRef, useState } from 'react'

export interface DeviceChoice {
  microphoneId?: string
  speakerId?: string
}

/**
 * Waiting room (PRD 11.3 screen 5): choose microphone and speaker, see a live mic level, then join.
 * The microphone is requested only after the teacher clicks, with an explanation (PRD 21.1 device permissions).
 */
export function DeviceCheck({ title, joining, onJoin }: { title: string; joining: boolean; onJoin: (choice: DeviceChoice) => void }) {
  const [stream, setStream] = useState<MediaStream | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [mics, setMics] = useState<MediaDeviceInfo[]>([])
  const [speakers, setSpeakers] = useState<MediaDeviceInfo[]>([])
  const [choice, setChoice] = useState<DeviceChoice>({})
  const [level, setLevel] = useState(0)
  const frame = useRef<number | null>(null)

  async function enable(microphoneId?: string) {
    setError(null)
    stream?.getTracks().forEach((t) => t.stop())
    try {
      const next = await navigator.mediaDevices.getUserMedia({ audio: microphoneId ? { deviceId: { exact: microphoneId } } : true })
      setStream(next)
      const devices = await navigator.mediaDevices.enumerateDevices()
      setMics(devices.filter((d) => d.kind === 'audioinput'))
      setSpeakers(devices.filter((d) => d.kind === 'audiooutput'))
      setChoice((c) => ({ ...c, microphoneId: microphoneId ?? next.getAudioTracks()[0]?.getSettings().deviceId }))
    } catch {
      setError('The microphone is blocked. Allow microphone access for this site in the browser (lock icon in the address bar), then try again.')
    }
  }

  // Live level meter from the selected microphone.
  useEffect(() => {
    if (!stream) return
    const context = new AudioContext()
    const analyser = context.createAnalyser()
    analyser.fftSize = 512
    context.createMediaStreamSource(stream).connect(analyser)
    const data = new Uint8Array(analyser.frequencyBinCount)
    const tick = () => {
      analyser.getByteTimeDomainData(data)
      let peak = 0
      for (const v of data) peak = Math.max(peak, Math.abs(v - 128))
      setLevel(Math.min(1, peak / 64))
      frame.current = requestAnimationFrame(tick)
    }
    tick()
    return () => {
      if (frame.current) cancelAnimationFrame(frame.current)
      void context.close()
    }
  }, [stream])

  useEffect(() => () => stream?.getTracks().forEach((t) => t.stop()), [stream])

  return (
    <section className="card">
      <h2>{title}</h2>
      <p>Check your microphone before you join. Your camera stays off until you turn it on.</p>
      {!stream ? (
        <button type="button" onClick={() => void enable()}>
          Check microphone
        </button>
      ) : (
        <div className="form">
          <label>
            Microphone
            <select value={choice.microphoneId} onChange={(e) => void enable(e.target.value)}>
              {mics.map((m, i) => (
                <option key={m.deviceId} value={m.deviceId}>
                  {m.label || `Microphone ${i + 1}`}
                </option>
              ))}
            </select>
          </label>
          <div className="meter" role="meter" aria-label="Microphone level" aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(level * 100)}>
            <div className="meter-fill" style={{ width: `${Math.round(level * 100)}%` }} />
          </div>
          <p className="muted">Speak or recite: the bar should move.</p>
          {speakers.length > 0 && (
            <label>
              Speaker
              <select value={choice.speakerId} onChange={(e) => setChoice((c) => ({ ...c, speakerId: e.target.value }))}>
                {speakers.map((s, i) => (
                  <option key={s.deviceId} value={s.deviceId}>
                    {s.label || `Speaker ${i + 1}`}
                  </option>
                ))}
              </select>
            </label>
          )}
          <button
            type="button"
            disabled={joining}
            onClick={() => {
              stream.getTracks().forEach((t) => t.stop())
              onJoin(choice)
            }}
          >
            {joining ? 'Joining…' : 'Join class'}
          </button>
        </div>
      )}
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
    </section>
  )
}
