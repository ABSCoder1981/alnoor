import { useState, type FormEvent } from 'react'
import { ApiError, changePassword } from '../auth/authApi'
import { useAuth } from '../auth/useAuth'

const MIN_LENGTH = 8

export function ChangePasswordPage({ forced }: { forced: boolean }) {
  const { passwordChanged, logout } = useAuth()
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [repeat, setRepeat] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (next.length < MIN_LENGTH) return setError(`The new password must be at least ${MIN_LENGTH} characters.`)
    if (next !== repeat) return setError('The new passwords do not match.')
    if (next === current) return setError('The new password must be different from the current one.')

    setError(null)
    setSubmitting(true)
    try {
      await changePassword(current, next)
      setDone(true)
      passwordChanged()
    } catch (e) {
      if (e instanceof ApiError && e.code === 'INVALID_CREDENTIALS') setError('The current password is incorrect.')
      else if (e instanceof ApiError && e.fieldErrors.newPassword) setError(e.fieldErrors.newPassword[0])
      else setError(e instanceof ApiError ? e.message : 'Something went wrong. Please try again.')
    } finally {
      setSubmitting(false)
    }
  }

  if (done && !forced) {
    return (
      <section className="card">
        <h2>Password changed</h2>
        <p className="ok">Your password was changed. Other devices have been signed out.</p>
      </section>
    )
  }

  return (
    <section className="card">
      <h2>{forced ? 'Choose a new password' : 'Change password'}</h2>
      {forced && <p>You signed in with a temporary password. Choose your own password to continue.</p>}
      <form onSubmit={handleSubmit} className="form" noValidate>
        <label>
          Current password
          <input type="password" autoComplete="current-password" value={current} onChange={(e) => setCurrent(e.target.value)} maxLength={128} />
        </label>
        <label>
          New password (at least {MIN_LENGTH} characters)
          <input type="password" autoComplete="new-password" value={next} onChange={(e) => setNext(e.target.value)} maxLength={128} />
        </label>
        <label>
          Repeat new password
          <input type="password" autoComplete="new-password" value={repeat} onChange={(e) => setRepeat(e.target.value)} maxLength={128} />
        </label>
        {error && (
          <p className="error" role="alert">
            {error}
          </p>
        )}
        <button type="submit" disabled={submitting}>
          {submitting ? 'Saving…' : 'Save password'}
        </button>
        {forced && (
          <button type="button" className="secondary" onClick={() => void logout()}>
            Sign out
          </button>
        )}
      </form>
    </section>
  )
}
