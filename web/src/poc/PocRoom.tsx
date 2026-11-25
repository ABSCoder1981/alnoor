import {
  ConnectionQualityIndicator,
  ConnectionStateToast,
  DisconnectButton,
  GridLayout,
  LiveKitRoom,
  ParticipantTile,
  RoomAudioRenderer,
  TrackToggle,
  useConnectionState,
  useIsSpeaking,
  useLocalParticipant,
  useParticipants,
  useRoomContext,
  useTracks,
  ParticipantContext,
} from '@livekit/components-react'
import {
  ConnectionState,
  RoomEvent,
  Track,
  type Participant,
  type RemoteTrackPublication,
  type RoomOptions,
} from 'livekit-client'
import { useEffect, useState } from 'react'
import type { DevTokenResponse, ParticipantRole } from '../api/devToken'

interface Props {
  connection: DevTokenResponse
  role: ParticipantRole
  roomOptions: RoomOptions
  settingsLabel: string
  onLeave: () => void
}

export function PocRoom({ connection, role, roomOptions, settingsLabel, onLeave }: Props) {
  const [error, setError] = useState<string | null>(null)

  return (
    <section className="card room" data-lk-theme="default">
      <LiveKitRoom
        serverUrl={connection.url}
        token={connection.token}
        connect
        options={roomOptions}
        audio={role === 'teacher'}
        video={false}
        onDisconnected={onLeave}
        onError={(e) => setError(e.message)}
      >
        <RoomHeader settingsLabel={settingsLabel} />
        {error && (
          <p className="error" role="alert">
            {error}
          </p>
        )}
        <RoomBody />
        <RoomAudioRenderer />
        <ConnectionStateToast />
      </LiveKitRoom>
    </section>
  )
}

const stateLabels: Record<ConnectionState, string> = {
  [ConnectionState.Connecting]: 'Joining…',
  [ConnectionState.Connected]: 'Connected',
  [ConnectionState.Reconnecting]: 'Reconnecting…',
  [ConnectionState.SignalReconnecting]: 'Reconnecting…',
  [ConnectionState.Disconnected]: 'Disconnected',
}

function RoomHeader({ settingsLabel }: { settingsLabel: string }) {
  const state = useConnectionState()
  return (
    <header className="room-header">
      <span className={`status status-${state}`} aria-live="polite">
        {stateLabels[state] ?? state}
      </span>
      <span className="settings-label">Testing: {settingsLabel}</span>
    </header>
  )
}

/** Audio-first: stop receiving all remote video and turn own camera off (PRD 18.3). */
function useAudioOnly(audioOnly: boolean) {
  const room = useRoomContext()
  const { localParticipant } = useLocalParticipant()

  useEffect(() => {
    const apply = (publication: RemoteTrackPublication) => {
      if (publication.kind === Track.Kind.Video) publication.setSubscribed(!audioOnly)
    }
    room.remoteParticipants.forEach((p) => p.trackPublications.forEach(apply))
    if (audioOnly) void localParticipant.setCameraEnabled(false)

    room.on(RoomEvent.TrackPublished, apply)
    return () => {
      room.off(RoomEvent.TrackPublished, apply)
    }
  }, [audioOnly, room, localParticipant])
}

function RoomBody() {
  const [audioOnly, setAudioOnly] = useState(false)
  useAudioOnly(audioOnly)
  const cameraTracks = useTracks([{ source: Track.Source.Camera, withPlaceholder: false }])

  return (
    <>
      {!audioOnly && cameraTracks.length > 0 && (
        <div className="video-grid">
          <GridLayout tracks={cameraTracks}>
            <ParticipantTile />
          </GridLayout>
        </div>
      )}
      <ParticipantList />
      <div className="controls" role="toolbar" aria-label="Classroom controls">
        <TrackToggle source={Track.Source.Microphone} showIcon>
          Mic
        </TrackToggle>
        <TrackToggle source={Track.Source.Camera} showIcon disabled={audioOnly}>
          Camera
        </TrackToggle>
        <button type="button" aria-pressed={audioOnly} onClick={() => setAudioOnly(!audioOnly)}>
          {audioOnly ? 'Audio only: ON' : 'Audio only: off'}
        </button>
        <DisconnectButton>Leave</DisconnectButton>
      </div>
    </>
  )
}

function ParticipantList() {
  const participants = useParticipants()
  return (
    <div>
      <h3>Participants ({participants.length})</h3>
      <ul className="participants">
        {participants.map((p) => (
          <ParticipantContext.Provider key={p.identity} value={p}>
            <ParticipantRow participant={p} />
          </ParticipantContext.Provider>
        ))}
      </ul>
    </div>
  )
}

function ParticipantRow({ participant }: { participant: Participant }) {
  const speaking = useIsSpeaking(participant)
  const micOn = participant.isMicrophoneEnabled
  const roleLabel = participant.attributes.role === 'teacher' ? 'Teacher' : 'Student'
  return (
    <li className={speaking ? 'speaking' : undefined}>
      <span className="name">
        {participant.name || participant.identity}
        {participant.isLocal ? ' (you)' : ''}
      </span>
      <span className="role">{roleLabel}</span>
      <span className={micOn ? 'ok' : 'error'}>{micOn ? 'Mic on' : 'Mic off'}</span>
      <span>{participant.isCameraEnabled ? 'Camera on' : 'Camera off'}</span>
      <ConnectionQualityIndicator />
    </li>
  )
}
