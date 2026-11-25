import { useEffect, useState } from 'react'
import { fetchHealth, type HealthResult } from '../api/health'

type ViewState = { state: 'loading' } | HealthResult

/** Small server-reachability indicator (moved from the Phase 1 shell). */
export function ServerStatus() {
  const [health, setHealth] = useState<ViewState>({ state: 'loading' })

  useEffect(() => {
    const controller = new AbortController()
    fetchHealth(controller.signal)
      .then(setHealth)
      .catch((error: unknown) => {
        if (!(error instanceof DOMException && error.name === 'AbortError')) setHealth({ state: 'unreachable' })
      })
    return () => controller.abort()
  }, [])

  return (
    <p className="server-status" aria-live="polite">
      Server:{' '}
      {health.state === 'loading' && 'checking…'}
      {health.state === 'healthy' && <span className="ok">connected</span>}
      {health.state === 'unhealthy' && <span className="error">not healthy ({health.status})</span>}
      {health.state === 'unreachable' && <span className="error">cannot be reached</span>}
    </p>
  )
}
