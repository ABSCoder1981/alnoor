import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { errorText } from '../api/client'
import { classesApi, sessionsApi, studentsApi } from '../api/endpoints'
import type { ClassSummary, SaveClass } from '../api/types'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { QueryState } from '../components/QueryState'
import { dayNames, formatDateTime } from '../format'
import { ClassForm } from './ClassForm'

const MAX_STUDENTS = 9

export function ClassListPage() {
  const [showArchived, setShowArchived] = useState(false)
  const classes = useQuery({ queryKey: ['classes', showArchived], queryFn: () => classesApi.list(showArchived) })

  return (
    <section className="card">
      <div className="section-header">
        <h2>Classes</h2>
        <Link to="/classes/new">New class</Link>
      </div>
      <label className="check">
        <input type="checkbox" checked={showArchived} onChange={(e) => setShowArchived(e.target.checked)} /> Show archived classes
      </label>
      <QueryState isLoading={classes.isLoading} error={classes.error} onRetry={() => void classes.refetch()} isEmpty={classes.data?.length === 0} empty={<p>No classes.</p>}>
        <ul className="list">
          {classes.data?.map((c) => (
            <li key={c.id} className="list-row">
              <div>
                <Link to={`/classes/${c.id}`} className="strong">
                  {c.title}
                </Link>
                {!c.isActive && <span className="badge">Archived</span>}
                {c.liveSessionId && <span className="badge live">Live</span>}
                <div className="muted">
                  {c.schedule.map((s) => `${dayNames[s.dayOfWeek].slice(0, 3)} ${s.startTimeLocal}`).join(', ') || 'No weekly time'} · {c.studentCount}/
                  {MAX_STUDENTS} students
                </div>
              </div>
            </li>
          ))}
        </ul>
      </QueryState>
    </section>
  )
}

export function NewClassPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const create = useMutation({
    mutationFn: (value: SaveClass) => classesApi.create(value),
    onSuccess: async (created) => {
      await queryClient.invalidateQueries({ queryKey: ['classes'] })
      navigate(`/classes/${created.id}`)
    },
  })

  return (
    <section className="card">
      <h2>New class</h2>
      <ClassForm submitLabel="Create class" busy={create.isPending} serverError={create.error ? errorText(create.error) : null} onSubmit={create.mutate} />
    </section>
  )
}

export function ClassDetailPage() {
  const { classId = '' } = useParams()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const cls = useQuery({ queryKey: ['class', classId], queryFn: () => classesApi.get(classId) })
  const [editing, setEditing] = useState(false)
  const [archiveOpen, setArchiveOpen] = useState(false)

  const refresh = () => Promise.all([queryClient.invalidateQueries({ queryKey: ['class', classId] }), queryClient.invalidateQueries({ queryKey: ['classes'] })])

  const save = useMutation({
    mutationFn: (value: SaveClass) => classesApi.update(classId, value),
    onSuccess: async () => {
      setEditing(false)
      setArchiveOpen(false)
      await refresh()
    },
  })
  const start = useMutation({ mutationFn: () => sessionsApi.start(classId), onSuccess: (s) => navigate(`/classroom/${s.id}`) })

  const toSave = (c: ClassSummary, patch: Partial<SaveClass> = {}): SaveClass => ({
    title: c.title,
    description: c.description,
    timeZoneId: c.timeZoneId,
    defaultDurationMinutes: c.defaultDurationMinutes,
    schedule: c.schedule,
    isActive: c.isActive,
    ...patch,
  })

  return (
    <QueryState isLoading={cls.isLoading} error={cls.error} onRetry={() => void cls.refetch()}>
      {cls.data && (
        <>
          <section className="card">
            <div className="section-header">
              <h2>
                {cls.data.title} {!cls.data.isActive && <span className="badge">Archived</span>}
              </h2>
              {cls.data.liveSessionId ? (
                <Link className="button" to={`/classroom/${cls.data.liveSessionId}`}>
                  Return to live class
                </Link>
              ) : (
                cls.data.isActive && (
                  <button type="button" onClick={() => start.mutate()} disabled={start.isPending}>
                    {start.isPending ? 'Starting…' : 'Start class'}
                  </button>
                )
              )}
            </div>
            {start.error && (
              <p className="error" role="alert">
                {errorText(start.error)}
              </p>
            )}

            {editing ? (
              <ClassForm
                initial={toSave(cls.data)}
                submitLabel="Save changes"
                busy={save.isPending}
                serverError={save.error ? errorText(save.error) : null}
                onSubmit={save.mutate}
              />
            ) : (
              <>
                {cls.data.description && <p>{cls.data.description}</p>}
                <p className="muted">Time zone: {cls.data.timeZoneId}</p>
                <h3>Weekly times</h3>
                {cls.data.schedule.length === 0 ? (
                  <p className="muted">None</p>
                ) : (
                  <ul>
                    {cls.data.schedule.map((s, i) => (
                      <li key={i}>
                        {dayNames[s.dayOfWeek]} {s.startTimeLocal} ({s.durationMinutes} min)
                      </li>
                    ))}
                  </ul>
                )}
                {cls.data.nextOccurrences.length > 0 && (
                  <p className="muted">Next (your time): {cls.data.nextOccurrences.slice(0, 3).map((t) => formatDateTime(t)).join(' · ')}</p>
                )}
                <div className="actions">
                  <button type="button" className="secondary" onClick={() => setEditing(true)}>
                    Edit
                  </button>
                  <button type="button" className="secondary" onClick={() => setArchiveOpen(true)}>
                    {cls.data.isActive ? 'Archive class' : 'Reactivate class'}
                  </button>
                </div>
              </>
            )}
          </section>

          <EnrollmentSection cls={cls.data} onChanged={refresh} />

          <ConfirmDialog
            open={archiveOpen}
            title={cls.data.isActive ? 'Archive this class?' : 'Reactivate this class?'}
            confirmLabel={cls.data.isActive ? 'Archive' : 'Reactivate'}
            danger={cls.data.isActive}
            busy={save.isPending}
            onCancel={() => setArchiveOpen(false)}
            onConfirm={() => save.mutate(toSave(cls.data!, { isActive: !cls.data!.isActive }))}
          >
            <p>{cls.data.isActive ? 'Students will no longer see it. You can reactivate it later.' : 'Enrolled students will see it again.'}</p>
            {save.error && <p className="error">{errorText(save.error)}</p>}
          </ConfirmDialog>
        </>
      )}
    </QueryState>
  )
}

function EnrollmentSection({ cls, onChanged }: { cls: ClassSummary; onChanged: () => Promise<unknown> }) {
  const students = useQuery({ queryKey: ['students'], queryFn: () => studentsApi.list() })
  const [selected, setSelected] = useState<string[]>([])
  const [removing, setRemoving] = useState<{ id: string; name: string } | null>(null)
  const enrolled = (cls.students ?? []).filter((s) => s.enrollmentStatus === 'Active')
  const enrolledIds = new Set(enrolled.map((s) => s.id))
  const available = (students.data ?? []).filter((s) => s.isActive && !enrolledIds.has(s.id))
  const places = MAX_STUDENTS - enrolled.length

  const enroll = useMutation({
    mutationFn: () => classesApi.enroll(cls.id, selected),
    onSuccess: async () => {
      setSelected([])
      await onChanged()
    },
  })
  const remove = useMutation({
    mutationFn: (studentId: string) => classesApi.unenroll(cls.id, studentId),
    onSuccess: async () => {
      setRemoving(null)
      await onChanged()
    },
  })

  return (
    <section className="card">
      <h2>
        Students ({enrolled.length}/{MAX_STUDENTS})
      </h2>
      {enrolled.length === 0 ? (
        <p className="muted">No students enrolled yet.</p>
      ) : (
        <ul className="list">
          {enrolled.map((s) => (
            <li key={s.id} className="list-row">
              <span>{s.fullName}</span>
              <button type="button" className="secondary" onClick={() => setRemoving({ id: s.id, name: s.fullName })}>
                Remove
              </button>
            </li>
          ))}
        </ul>
      )}

      <h3>Add students</h3>
      <QueryState
        isLoading={students.isLoading}
        error={students.error}
        isEmpty={available.length === 0}
        empty={
          <p className="muted">
            No other active students. <Link to="/students">Add a student</Link> first.
          </p>
        }
      >
        <fieldset disabled={places <= 0}>
          <legend>{places > 0 ? `Choose up to ${places}` : 'This class is full'}</legend>
          {available.map((s) => (
            <label key={s.id} className="check">
              <input
                type="checkbox"
                checked={selected.includes(s.id)}
                disabled={!selected.includes(s.id) && selected.length >= places}
                onChange={(e) => setSelected((ids) => (e.target.checked ? [...ids, s.id] : ids.filter((x) => x !== s.id)))}
              />
              {s.fullName} <span className="muted">{s.phoneNumber}</span>
            </label>
          ))}
        </fieldset>
        {enroll.error && (
          <p className="error" role="alert">
            {errorText(enroll.error)}
          </p>
        )}
        <button type="button" onClick={() => enroll.mutate()} disabled={selected.length === 0 || enroll.isPending}>
          {enroll.isPending ? 'Adding…' : `Add ${selected.length || ''} to class`}
        </button>
      </QueryState>

      <ConfirmDialog
        open={removing !== null}
        title={`Remove ${removing?.name ?? ''} from this class?`}
        confirmLabel="Remove"
        danger
        busy={remove.isPending}
        onCancel={() => setRemoving(null)}
        onConfirm={() => removing && remove.mutate(removing.id)}
      >
        <p>They will no longer see this class. If the class is live, they are disconnected.</p>
        {remove.error && <p className="error">{errorText(remove.error)}</p>}
      </ConfirmDialog>
    </section>
  )
}
