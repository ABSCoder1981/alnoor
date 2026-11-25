import { config } from '../config'

export type ParticipantRole = 'teacher' | 'student'

export interface DevTokenRequest {
  name: string
  room: string
  role: ParticipantRole
}

export interface DevTokenResponse {
  url: string
  token: string
  identity: string
  expiresAt: string
}

export class DevTokenError extends Error {}

/**
 * Phase 2 only: asks the API's Development-only endpoint for a LiveKit token.
 * Replaced by the authorised session join endpoint in Phase 4.
 */
export async function requestDevToken(request: DevTokenRequest): Promise<DevTokenResponse> {
  let response: Response
  try {
    response = await fetch(`${config.apiBaseUrl}/api/dev/livekit-token`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    })
  } catch {
    throw new DevTokenError('The API cannot be reached. Check that it is running.')
  }

  if (response.ok) {
    return (await response.json()) as DevTokenResponse
  }
  if (response.status === 404) {
    throw new DevTokenError('The test token endpoint is not available. Run the API in the Development environment.')
  }
  if (response.status === 400) {
    const problem = (await response.json().catch(() => null)) as { errors?: Record<string, string[]> } | null
    const messages = problem?.errors ? Object.values(problem.errors).flat() : []
    throw new DevTokenError(messages.length > 0 ? messages.join(' ') : 'The request was not valid.')
  }
  throw new DevTokenError(`The API returned an error (HTTP ${response.status}).`)
}
