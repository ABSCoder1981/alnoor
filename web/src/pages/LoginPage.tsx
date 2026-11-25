import { useState, type FormEvent } from 'react'
import { useAuth } from '../auth/useAuth'
import { loginErrorMessage } from '../auth/messages'
import { countryCodes, toInternational } from '../auth/phone'
import { ServerStatus } from '../components/ServerStatus'

export function LoginPage({ notice }: { notice?: string }) {
  const { login } = useAuth()
  const [countryCode, setCountryCode] = useState<string>(countryCodes[0].code)
  const [number, setNumber] = useState('')
  const [password, setPassword] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const phone = toInternational(countryCode, number)
    if (!phone) {
      setError('Enter your phone number.')
      return
    }
    if (!password) {
      setError('Enter your password.')
      return
    }
    setError(null)
    setSubmitting(true)
    try {
      await login(phone, password)
    } catch (e) {
      setError(loginErrorMessage(e))
      setPassword('')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <main className="shell auth">
      <header>
        <h1>ALNoor</h1>
        <p className="subtitle">Online Quran Classroom – Teacher</p>
      </header>

      <section className="card">
        <h2>Sign in</h2>
        {notice && (
          <p className="notice" role="status">
            {notice}
          </p>
        )}
        <form onSubmit={handleSubmit} className="form" noValidate>
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
              Phone number
              <input
                type="tel"
                inputMode="tel"
                autoComplete="tel-national"
                value={number}
                onChange={(e) => setNumber(e.target.value)}
                maxLength={20}
              />
            </label>
          </div>
          <label>
            Password
            <input
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              maxLength={128}
            />
          </label>
          {error && (
            <p className="error" role="alert">
              {error}
            </p>
          )}
          <button type="submit" disabled={submitting}>
            {submitting ? 'Signing in…' : 'Sign in'}
          </button>
          <p className="hint">Forgot your password? Ask the person who manages ALNoor to reset it.</p>
        </form>
      </section>

      <ServerStatus />
    </main>
  )
}
