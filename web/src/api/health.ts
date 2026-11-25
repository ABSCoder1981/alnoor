import { config } from '../config'

export type HealthResult =
  | { state: 'healthy'; status: string }
  | { state: 'unhealthy'; status: string }
  | { state: 'unreachable' }

/** Calls the backend's public GET /health endpoint (no auth, no personal data). */
export async function fetchHealth(signal?: AbortSignal): Promise<HealthResult> {
  let response: Response
  try {
    response = await fetch(`${config.apiBaseUrl}/health`, { signal })
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') throw error
    return { state: 'unreachable' }
  }
  const status = (await response.text()).trim() || `HTTP ${response.status}`
  return response.ok ? { state: 'healthy', status } : { state: 'unhealthy', status }
}
