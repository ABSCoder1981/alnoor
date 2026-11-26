import '@livekit/components-styles'
import { LiveKitRoom, RoomAudioRenderer } from '@livekit/components-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { DisconnectReason } from 'livekit-client'
import { useMemo, useState } from 'react'
import { Link, useParams } from 'react-router'
import { errorText } from '../api/client'
import { sessionsApi } from '../api/endpoints'
import type { JoinInfo } from '../api/types'
import { QueryState } from '../components/QueryState'
import { buildRoomOptions, defaultAudioSettings } from './audioSettings'
import { DeviceCheck, type DeviceChoice } from './DeviceCheck'
import { TeacherRoom } from './TeacherRoom'

type Phase =
  | { kind: 'check' }
  | { kind: 'live'; join: JoinInfo; devices: DeviceChoice }
  | { kind: 'ended'; message: string }
  | { kind: 'left' }

/** Teacher live classroom (PRD 11.3 screens 5–7). */
export function ClassroomPage() {
  const { sessionId = '' } = useParams()
  const queryClient = useQueryClient()
  const session = useQuery({ queryKey: ['session', sessionId], queryFn: () => sessionsApi.get(sessionId) })
  const [phase, setPhase] = useState<Phase>({ kind: 'check' })

  const join = useMutation({
    mutationFn: () => sessionsApi.join(sessionId),
  })

  const roomOptions = useMemo(() => {
    // Audio settings: PRD 18.2 starting values until docs/audio-decisions.md records tested defaults.
    const options = buildRoomOptions(defaultAudioSettings)
    if (phase.kind === 'live' && phase.devices.speakerId) {
      options.audioOutput = { deviceId: phase.devices.speakerId }
    }
    return options
  }, [phase])

  function onDisconnected(reason?: DisconnectReason) {
    void queryClient.invalidateQueries({ queryKey: ['classes'] })
    if (reason === DisconnectReason.ROOM_DELETED) setPhase({ kind: 'ended', message: 'The class has ended.' })
    else if (reason === DisconnectReason.PARTICIPANT_REMOVED) setPhase({ kind: 'ended', message: 'You were disconnected from the class.' })
    else if (reason === DisconnectReason.CLIENT_INITIATED) setPhase({ kind: 'left' })
    else setPhase({ kind: 'ended', message: 'The connection to the class was lost.' })
  }

  return (
    <QueryState isLoading={session.isLoading} error={session.error} onRetry={() => void session.refetch()}>
      {session.data &&
        (session.data.status === 'Ended' || phase.kind === 'ended' ? (
          <section className="card" role="status">
            <h2>Class ended</h2>
            <p>{phase.kind === 'ended' ? phase.message : 'This class has ended.'}</p>
            <Link className="button" to={`/classes/${session.data.classId}`}>
              Back to class
            </Link>
          </section>
        ) : phase.kind === 'left' ? (
          <section className="card" role="status">
            <h2>You left the class</h2>
            <p>The class is still running for your students.</p>
            <div className="actions">
              <button type="button" onClick={() => setPhase({ kind: 'check' })}>
                Rejoin
              </button>
              <Link to={`/classes/${session.data.classId}`}>Back to class</Link>
            </div>
          </section>
        ) : phase.kind === 'check' ? (
          <>
            <DeviceCheck
              title={session.data.classTitle}
              joining={join.isPending}
              onJoin={(devices) => join.mutate(undefined, { onSuccess: (info) => setPhase({ kind: 'live', join: info, devices }) })}
            />
            {join.error && (
              <p className="error" role="alert">
                {errorText(join.error)}
              </p>
            )}
          </>
        ) : (
          <section className="card room" data-lk-theme="default">
            <LiveKitRoom
              serverUrl={phase.join.livekitUrl}
              token={phase.join.token}
              connect
              options={roomOptions}
              // Teacher joins with the mic on (PRD C5); camera off by default.
              audio={phase.devices.microphoneId ? { deviceId: phase.devices.microphoneId } : true}
              video={false}
              onDisconnected={onDisconnected}
            >
              <TeacherRoom session={session.data} />
              <RoomAudioRenderer />
            </LiveKitRoom>
          </section>
        ))}
    </QueryState>
  )
}
