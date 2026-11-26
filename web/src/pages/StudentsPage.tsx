import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { errorText } from '../api/client'
import { studentsApi } from '../api/endpoints'
import type { Student } from '../api/types'
import { countryCodes, toInternational } from '../auth/phone'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { QueryState } from '../components/QueryState'
import { generateTemporaryPassword } from '../format'

type Pending =
  | { kind: 'status'; student: Student }
  | { kind: 'reset'; student: Student }
  | null

/** Student management (PRD 11.3 screen 4, 16.3). */
export function StudentsPage() {
  const queryClient = useQueryClient()
  const [search, setSearch] = useState('')
  const students = useQuery({ queryKey: ['students', search], queryFn: () => studentsApi.list(search.trim()) })
  const [pending, setPending] = useState<Pending>(null)
  const [editing, setEditing] = useState<Student | null>(null)
  const [shownPassword, setShownPassword] = useState<{ name: string; password: string } | null>(null)
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['students'] })

  const setActive = useMutation({
    mutationFn: (s: Student) => studentsApi.setActive(s.id, !s.isActive),
    onSuccess: async () => {
      setPending(null)
      await refresh()
    },
  })
  const reset = useMutation({
    mutationFn: async (s: Student) => {
      const password = generateTemporaryPassword()
      await studentsApi.resetPassword(s.id, password)
      return { name: s.fullName, password }
    },
    onSuccess: (shown) => {
      setPending(null)
      setShownPassword(shown)
    },
  })

  return (
    <>
      <AddStudentCard onCreated={(shown) => { setShownPassword(shown); void refresh() }} />

      {shownPassword && <TemporaryPasswordNotice {...shownPassword} onDone={() => setShownPassword(null)} />}

      <section className="card">
        <h2>Students</h2>
        <label>
          Search by name or phone
          <input type="search" value={search} onChange={(e) => setSearch(e.target.value)} maxLength={100} />
        </label>
        <QueryState
          isLoading={students.isLoading}
          error={students.error}
          onRetry={() => void students.refetch()}
          isEmpty={students.data?.length === 0}
          empty={<p className="muted">{search ? 'No students match.' : 'No students yet.'}</p>}
        >
          <ul className="list">
            {students.data?.map((s) => (
              <li key={s.id} className="list-row">
                {editing?.id === s.id ? (
                  <EditStudentForm student={s} onDone={() => { setEditing(null); void refresh() }} />
                ) : (
                  <>
                    <div>
                      <span className="strong">{s.fullName}</span> {!s.isActive && <span className="badge">Deactivated</span>}
                      {s.mustChangePassword && <span className="badge">Temporary password</span>}
                      <div className="muted">
                        {s.phoneNumber} · {s.classCount ?? 0} class{s.classCount === 1 ? '' : 'es'}
                      </div>
                    </div>
                    <div className="actions">
                      <button type="button" className="secondary" onClick={() => setEditing(s)}>
                        Edit
                      </button>
                      <button type="button" className="secondary" onClick={() => setPending({ kind: 'reset', student: s })}>
                        Reset password
                      </button>
                      <button type="button" className={s.isActive ? 'secondary danger-text' : 'secondary'} onClick={() => setPending({ kind: 'status', student: s })}>
                        {s.isActive ? 'Deactivate' : 'Activate'}
                      </button>
                    </div>
                  </>
                )}
              </li>
            ))}
          </ul>
        </QueryState>
      </section>

      <ConfirmDialog
        open={pending?.kind === 'status'}
        title={pending?.student.isActive ? `Deactivate ${pending.student.fullName}?` : `Activate ${pending?.student.fullName ?? ''}?`}
        confirmLabel={pending?.student.isActive ? 'Deactivate' : 'Activate'}
        danger={pending?.student.isActive}
        busy={setActive.isPending}
        onCancel={() => setPending(null)}
        onConfirm={() => pending && setActive.mutate(pending.student)}
      >
        <p>
          {pending?.student.isActive
            ? 'They are signed out everywhere, cannot sign in or join classes, and are removed from any live class.'
            : 'They can sign in and join their classes again.'}
        </p>
        {setActive.error && <p className="error">{errorText(setActive.error)}</p>}
      </ConfirmDialog>

      <ConfirmDialog
        open={pending?.kind === 'reset'}
        title={`Reset the password of ${pending?.student.fullName ?? ''}?`}
        confirmLabel="Reset password"
        busy={reset.isPending}
        onCancel={() => setPending(null)}
        onConfirm={() => pending && reset.mutate(pending.student)}
      >
        <p>A new temporary password is created and shown once. They must choose their own password when they next sign in.</p>
        {reset.error && <p className="error">{errorText(reset.error)}</p>}
      </ConfirmDialog>
    </>
  )
}

function TemporaryPasswordNotice({ name, password, onDone }: { name: string; password: string; onDone: () => void }) {
  return (
    <section className="card notice" role="status">
      <h2>Temporary password for {name}</h2>
      <p className="temp-password">{password}</p>
      <p>Share it privately with the student or their parent. It is shown only now; they must change it when they first sign in.</p>
      <button type="button" onClick={onDone}>
        Done
      </button>
    </section>
  )
}

function AddStudentCard({ onCreated }: { onCreated: (shown: { name: string; password: string }) => void }) {
  const [name, setName] = useState('')
  const [countryCode, setCountryCode] = useState<string>(countryCodes[0].code)
  const [number, setNumber] = useState('')
  const [error, setError] = useState<string | null>(null)
  const create = useMutation({
    mutationFn: async () => {
      const password = generateTemporaryPassword()
      await studentsApi.create(name.trim(), toInternational(countryCode, number), password)
      return { name: name.trim(), password }
    },
    onSuccess: (shown) => {
      setName('')
      setNumber('')
      onCreated(shown)
    },
    onError: (e) => setError(errorText(e)),
  })

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (name.trim().length < 2) return setError('Enter the student’s full name (at least 2 characters).')
    if (!toInternational(countryCode, number)) return setError('Enter the student’s phone number.')
    setError(null)
    create.mutate()
  }

  return (
    <section className="card">
      <h2>Add student</h2>
      <form className="form" onSubmit={handleSubmit} noValidate>
        <label>
          Full name
          <input value={name} onChange={(e) => setName(e.target.value)} maxLength={100} autoComplete="off" />
        </label>
        <div className="phone-row">
          <label>
            Country
            <select value={countryCode} onChange={(e) => setCountryCode(e.target.value)}>
              {countryCodes.map((c) => (
                <option key={c.code} value={c.code}>
                  {c.label}
                </option>
              ))}
            </select>
          </label>
          <label>
            Phone number (their own; used to sign in)
            <input type="tel" value={number} onChange={(e) => setNumber(e.target.value)} maxLength={20} autoComplete="off" />
          </label>
        </div>
        {error && (
          <p className="error" role="alert">
            {error}
          </p>
        )}
        <button type="submit" disabled={create.isPending}>
          {create.isPending ? 'Adding…' : 'Add student'}
        </button>
      </form>
    </section>
  )
}

function EditStudentForm({ student, onDone }: { student: Student; onDone: () => void }) {
  const [name, setName] = useState(student.fullName)
  const [phone, setPhone] = useState(student.phoneNumber)
  const save = useMutation({ mutationFn: () => studentsApi.update(student.id, name.trim(), phone.trim()), onSuccess: onDone })

  return (
    <form
      className="form inline-form"
      onSubmit={(e) => {
        e.preventDefault()
        save.mutate()
      }}
    >
      <label>
        Full name
        <input value={name} onChange={(e) => setName(e.target.value)} maxLength={100} />
      </label>
      <label>
        Phone (with country code)
        <input type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} maxLength={30} />
      </label>
      {save.error && (
        <p className="error" role="alert">
          {errorText(save.error)}
        </p>
      )}
      <div className="actions">
        <button type="submit" disabled={save.isPending}>
          {save.isPending ? 'Saving…' : 'Save'}
        </button>
        <button type="button" className="secondary" onClick={onDone}>
          Cancel
        </button>
      </div>
    </form>
  )
}
