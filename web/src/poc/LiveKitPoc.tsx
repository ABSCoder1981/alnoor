import '@livekit/components-styles'
import { useState, type FormEvent } from 'react'
import { DevTokenError, requestDevToken, type DevTokenResponse, type ParticipantRole } from '../api/devToken'
import {
  bitrateOptions,
  buildRoomOptions,
  defaultAudioSettings,
  describeSettings,
  type AudioTestSettings,
} from '../classroom/audioSettings'
import { PocRoom } from './PocRoom'

interface Session {
  connection: DevTokenResponse
  role: ParticipantRole
  settings: AudioTestSettings
}

/** Phase 2 proof of concept: join a LiveKit room and compare audio settings (PRD 18.5). */
export default function LiveKitPoc() {
  const [name, setName] = useState('')
  const [room, setRoom] = useState('poc')
  const [role, setRole] = useState<ParticipantRole>('teacher')
  const [settings, setSettings] = useState<AudioTestSettings>(defaultAudioSettings)
  const [joining, setJoining] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [session, setSession] = useState<Session | null>(null)

  async function handleJoin(event: FormEvent) {
    event.preventDefault()
    setError(null)
    if (name.trim().length === 0) {
      setError('Enter your name.')
      return
    }
    if (!/^[a-z0-9-]{1,40}$/.test(room)) {
      setError("Room may contain only a-z, 0-9 and '-' (max 40).")
      return
    }
    setJoining(true)
    try {
      const connection = await requestDevToken({ name: name.trim(), room, role })
      setSession({ connection, role, settings })
    } catch (e) {
      setError(e instanceof DevTokenError ? e.message : 'Something went wrong while joining.')
    } finally {
      setJoining(false)
    }
  }

  if (session) {
    return (
      <PocRoom
        connection={session.connection}
        role={session.role}
        roomOptions={buildRoomOptions(session.settings)}
        settingsLabel={describeSettings(session.settings)}
        onLeave={() => setSession(null)}
      />
    )
  }

  const toggle = (key: 'dtx' | 'noiseSuppression' | 'autoGainControl', label: string) => (
    <label className="check">
      <input
        type="checkbox"
        checked={settings[key]}
        onChange={(e) => setSettings({ ...settings, [key]: e.target.checked })}
      />
      {label}
    </label>
  )

  return (
    <section className="card">
      <h2>LiveKit test room (Phase 2, development only)</h2>
      <form onSubmit={handleJoin} className="form">
        <label>
          Your name
          <input value={name} onChange={(e) => setName(e.target.value)} maxLength={50} autoComplete="given-name" />
        </label>
        <label>
          Room
          <input value={room} onChange={(e) => setRoom(e.target.value.toLowerCase())} maxLength={40} />
        </label>
        <fieldset>
          <legend>Role</legend>
          <label className="check">
            <input type="radio" name="role" checked={role === 'teacher'} onChange={() => setRole('teacher')} />
            Teacher (joins with mic on)
          </label>
          <label className="check">
            <input type="radio" name="role" checked={role === 'student'} onChange={() => setRole('student')} />
            Student (joins muted)
          </label>
        </fieldset>
        <fieldset>
          <legend>Audio settings to test</legend>
          <label>
            Bitrate
            <select
              value={settings.bitrateKbps}
              onChange={(e) =>
                setSettings({ ...settings, bitrateKbps: Number(e.target.value) as AudioTestSettings['bitrateKbps'] })
              }
            >
              {bitrateOptions.map((kbps) => (
                <option key={kbps} value={kbps}>
                  {kbps} kbps
                </option>
              ))}
            </select>
          </label>
          {toggle('dtx', 'DTX (silence suppression)')}
          {toggle('noiseSuppression', 'Noise suppression')}
          {toggle('autoGainControl', 'Auto gain control')}
        </fieldset>
        {error && (
          <p className="error" role="alert">
            {error}
          </p>
        )}
        <button type="submit" disabled={joining}>
          {joining ? 'Joining…' : 'Join'}
        </button>
      </form>
    </section>
  )
}
