import {
  ConnectionQualityIndicator,
  GridLayout,
  ParticipantContext,
  ParticipantTile,
  TrackToggle,
  useConnectionState,
  useIsSpeaking,
  useLocalParticipant,
  useParticipants,
  useRoomContext,
  useTracks,
} from '@livekit/components-react'
import { useMutation } from '@tanstack/react-query'
import { ConnectionState, RoomEvent, Track, type Participant, type RemoteTrackPublication } from 'livekit-client'
import { useEffect, useState } from 'react'
import { errorText } from '../api/client'
import { sessionsApi } from '../api/endpoints'
import type { Session } from '../api/types'
import { ConfirmDialog } from '../components/ConfirmDialog'

const stateLabels: Partial<Record<ConnectionState, string>> = {
  [ConnectionState.Connecting]: 'Joining…',
  [ConnectionState.Connected]: 'Connected',
  [ConnectionState.Reconnecting]: 'Reconnecting…',
  [ConnectionState.SignalReconnecting]: 'Reconnecting…',
  [ConnectionState.Disconnected]: 'Disconnected',
}

type Confirm = { kind: 'end' } | { kind: 'muteAll' } | { kind: 'remove'; userId: string; name: string } | null

/** Teacher's view of the live class: video, participants with moderation, and class controls (PRD 7.4, 11.3). */
export function TeacherRoom({ session }: { session: Session }) {
  const room = useRoomContext()
  const state = useConnectionState()
  const [audioOnly, setAudioOnly] = useState(false)
  const [confirm, setConfirm] = useState<Confirm>(null)
  const [notices, setNotices] = useState<string[]>([])
  const cameraTracks = useTracks([{ source: Track.Source.Camera, withPlaceholder: false }])

  // Join/leave notices (FR-LIVE-08), kept short and announced to screen readers.
  useEffect(() => {
    const add = (text: string) => setNotices((n) => [...n.slice(-3), text])
    const joined = (p: Participant) => add(`${p.name || 'Someone'} joined`)
    const left = (p: Participant) => add(`${p.name || 'Someone'} left`)
    room.on(RoomEvent.ParticipantConnected, joined)
    room.on(RoomEvent.ParticipantDisconnected, left)
    return () => {
      room.off(RoomEvent.ParticipantConnected, joined)
      room.off(RoomEvent.ParticipantDisconnected, left)
    }
  }, [room])

  // Audio-first (PRD 18.3): stop receiving video and turn own camera off.
  useEffect(() => {
    const apply = (pub: RemoteTrackPublication) => {
      if (pub.kind === Track.Kind.Video) pub.setSubscribed(!audioOnly)
    }
    room.remoteParticipants.forEach((p) => p.trackPublications.forEach(apply))
    if (audioOnly) void room.localParticipant.setCameraEnabled(false)
    room.on(RoomEvent.TrackPublished, apply)
    return () => {
      room.off(RoomEvent.TrackPublished, apply)
    }
  }, [audioOnly, room])

  const end = useMutation({ mutationFn: () => sessionsApi.end(session.id), onSuccess: () => setConfirm(null) })
  const muteAll = useMutation({ mutationFn: () => sessionsApi.muteAll(session.id), onSuccess: () => setConfirm(null) })
  const remove = useMutation({ mutationFn: (userId: string) => sessionsApi.remove(session.id, userId), onSuccess: () => setConfirm(null) })

  return (
    <>
      <header className="room-header">
        <h2>{session.classTitle}</h2>
        <span className={`status status-${state}`} aria-live="polite">
          {stateLabels[state] ?? state}
        </span>
      </header>

      <ul className="notices" aria-live="polite">
        {notices.map((n, i) => (
          <li key={i}>{n}</li>
        ))}
      </ul>

      <div className="room-layout">
        <div className="room-main">
          {!audioOnly && cameraTracks.length > 0 ? (
            <div className="video-grid">
              <GridLayout tracks={cameraTracks}>
                <ParticipantTile />
              </GridLayout>
            </div>
          ) : (
            <p className="muted">{audioOnly ? 'Audio only: video is off to keep the sound clear.' : 'No cameras are on.'}</p>
          )}

          <div className="controls" role="toolbar" aria-label="Classroom controls">
            <TrackToggle source={Track.Source.Microphone} showIcon>
              Mic
            </TrackToggle>
            <TrackToggle source={Track.Source.Camera} showIcon disabled={audioOnly}>
              Camera
            </TrackToggle>
            <button type="button" className="secondary" aria-pressed={audioOnly} onClick={() => setAudioOnly(!audioOnly)}>
              {audioOnly ? 'Audio only: on' : 'Audio only: off'}
            </button>
            <button type="button" className="secondary" onClick={() => room.disconnect()}>
              Leave (class continues)
            </button>
            <button type="button" className="danger" onClick={() => setConfirm({ kind: 'end' })}>
              End class
            </button>
          </div>
        </div>

        <aside className="participants-panel" aria-label="Participants">
          <ParticipantPanel
            onMuteAll={() => setConfirm({ kind: 'muteAll' })}
            onRemove={(userId, name) => setConfirm({ kind: 'remove', userId, name })}
            sessionId={session.id}
          />
        </aside>
      </div>

      <ConfirmDialog
        open={confirm?.kind === 'end'}
        title="End the class for everyone?"
        confirmLabel="End class"
        danger
        busy={end.isPending}
        onCancel={() => setConfirm(null)}
        onConfirm={() => end.mutate()}
      >
        <p>All students are disconnected and see “Class ended”.</p>
        {end.error && <p className="error">{errorText(end.error)}</p>}
      </ConfirmDialog>
      <ConfirmDialog
        open={confirm?.kind === 'muteAll'}
        title="Mute all students?"
        confirmLabel="Mute all"
        busy={muteAll.isPending}
        onCancel={() => setConfirm(null)}
        onConfirm={() => muteAll.mutate()}
      >
        <p>Each student sees “The teacher muted everyone” and can unmute themselves when asked.</p>
        {muteAll.error && <p className="error">{errorText(muteAll.error)}</p>}
      </ConfirmDialog>
      <ConfirmDialog
        open={confirm?.kind === 'remove'}
        title={`Remove ${confirm?.kind === 'remove' ? confirm.name : ''} from this class?`}
        confirmLabel="Remove"
        danger
        busy={remove.isPending}
        onCancel={() => setConfirm(null)}
        onConfirm={() => confirm?.kind === 'remove' && remove.mutate(confirm.userId)}
      >
        <p>They are disconnected and cannot rejoin this session. They can join the next one.</p>
        {remove.error && <p className="error">{errorText(remove.error)}</p>}
      </ConfirmDialog>
    </>
  )
}

function ParticipantPanel({ sessionId, onMuteAll, onRemove }: { sessionId: string; onMuteAll: () => void; onRemove: (userId: string, name: string) => void }) {
  const participants = useParticipants()
  const { localParticipant } = useLocalParticipant()
  const mute = useMutation({ mutationFn: (userId: string) => sessionsApi.mute(sessionId, userId) })
  const ordered = [...participants].sort((a, b) => Number(b.isLocal) - Number(a.isLocal) || (a.name ?? '').localeCompare(b.name ?? ''))

  return (
    <>
      <div className="section-header">
        <h3>Participants ({participants.length})</h3>
        <button type="button" className="secondary" onClick={onMuteAll} disabled={participants.length <= 1}>
          Mute all
        </button>
      </div>
      {mute.error && (
        <p className="error" role="alert">
          {errorText(mute.error)}
        </p>
      )}
      <ul className="participants">
        {ordered.map((p) => (
          <ParticipantContext.Provider key={p.identity} value={p}>
            <ParticipantRow
              participant={p}
              isSelf={p.identity === localParticipant.identity}
              muting={mute.isPending && mute.variables === p.identity}
              onMute={() => mute.mutate(p.identity)}
              onRemove={() => onRemove(p.identity, p.name || 'this student')}
            />
          </ParticipantContext.Provider>
        ))}
      </ul>
    </>
  )
}

function ParticipantRow({
  participant,
  isSelf,
  muting,
  onMute,
  onRemove,
}: {
  participant: Participant
  isSelf: boolean
  muting: boolean
  onMute: () => void
  onRemove: () => void
}) {
  const speaking = useIsSpeaking(participant)
  const micOn = participant.isMicrophoneEnabled
  return (
    <li className={speaking ? 'speaking' : undefined}>
      <span className="name">
        {participant.name || 'Participant'}
        {isSelf ? ' (you)' : ''}
      </span>
      <span className={micOn ? 'ok' : 'error'}>{micOn ? 'Mic on' : 'Mic off'}</span>
      {participant.isCameraEnabled && <span>Camera on</span>}
      <ConnectionQualityIndicator />
      {!isSelf && (
        <span className="row-actions">
          <button type="button" className="secondary small" onClick={onMute} disabled={!micOn || muting}>
            {muting ? 'Muting…' : 'Mute'}
          </button>
          <button type="button" className="secondary small danger-text" onClick={onRemove}>
            Remove
          </button>
        </span>
      )}
    </li>
  )
}
