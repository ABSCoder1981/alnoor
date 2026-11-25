import { useEffect, useState } from 'react'
import { fetchHealth, type HealthResult } from './api/health'

type ViewState = { state: 'loading' } | HealthResult

function App() {
  const [health, setHealth] = useState<ViewState>({ state: 'loading' })
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    fetchHealth(controller.signal)
      .then(setHealth)
      .catch((error: unknown) => {
        if (!(error instanceof DOMException && error.name === 'AbortError')) {
          setHealth({ state: 'unreachable' })
        }
      })
    return () => controller.abort()
  }, [attempt])

  return (
    <main className="shell">
      <header>
        <h1>ALNoor</h1>
        <p className="subtitle">Online Quran Classroom – Teacher</p>
      </header>

      <section className="card" aria-live="polite">
        <h2>Server status</h2>
        {health.state === 'loading' && <p>Checking the server…</p>}
        {health.state === 'healthy' && <p className="ok">Connected ({health.status})</p>}
        {health.state === 'unhealthy' && (
          <p className="error">The server responded but is not healthy ({health.status}).</p>
        )}
        {health.state === 'unreachable' && (
          <p className="error">The server cannot be reached. Check that the API is running.</p>
        )}
        {health.state !== 'loading' && (
          <button type="button" onClick={() => {
              setHealth({ state: 'loading' })
              setAttempt((n) => n + 1)
            }}>
            Check again
          </button>
        )}
      </section>
    </main>
  )
}

export default App
