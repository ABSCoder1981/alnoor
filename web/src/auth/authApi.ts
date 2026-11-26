import { config } from '../config'

export type UserRole = 'Teacher' | 'Student' | 'Admin'

export interface AuthUser {
  id: string
  fullName: string
  role: UserRole
  mustChangePassword: boolean
}

interface AuthResponse {
  accessToken: string
  accessTokenExpiresAt: string
  user: AuthUser
}

/** An API error with the stable machine-readable code from the server (PRD 16.1). */
export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly fieldErrors: Record<string, string[]>
  readonly lockoutEndsAt?: string

  constructor(status: number, code: string, message: string, fieldErrors: Record<string, string[]> = {}, lockoutEndsAt?: string) {
    super(message)
    this.status = status
    this.code = code
    this.fieldErrors = fieldErrors
    this.lockoutEndsAt = lockoutEndsAt
  }
}

export const NETWORK_ERROR = 'NETWORK_ERROR'

// The access token lives only in memory (PRD 17.1). The refresh token is an HttpOnly cookie the page cannot read.
let accessToken: string | null = null
let refreshInFlight: Promise<AuthUser | null> | null = null
let onSessionExpired: (() => void) | null = null

export function setSessionExpiredHandler(handler: (() => void) | null) {
  onSessionExpired = handler
}

export function clearAccessToken() {
  accessToken = null
}

async function send(path: string, init: RequestInit): Promise<Response> {
  try {
    return await fetch(`${config.apiBaseUrl}${path}`, init)
  } catch {
    throw new ApiError(0, NETWORK_ERROR, 'The server cannot be reached. Check your connection and try again.')
  }
}

async function toApiError(response: Response): Promise<ApiError> {
  const problem = (await response.json().catch(() => null)) as {
    code?: string
    title?: string
    errors?: Record<string, string[]>
    lockoutEndsAt?: string
  } | null
  return new ApiError(
    response.status,
    problem?.code ?? 'UNKNOWN',
    problem?.title ?? `The server returned an error (HTTP ${response.status}).`,
    problem?.errors ?? {},
    problem?.lockoutEndsAt,
  )
}

const json = (body: unknown): RequestInit => ({
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(body),
})

/** Auth endpoints send the refresh cookie (credentials: include); everything else uses the bearer token only. */
async function authPost(path: string, body: unknown): Promise<Response> {
  return send(path, { ...json(body), credentials: 'include' })
}

export async function login(phoneNumber: string, password: string): Promise<AuthUser> {
  const response = await authPost('/api/auth/login', {
    phoneNumber,
    password,
    clientType: 'Web',
    deviceInfo: navigator.userAgent.slice(0, 100),
  })
  if (!response.ok) throw await toApiError(response)
  const body = (await response.json()) as AuthResponse
  accessToken = body.accessToken
  return body.user
}

/** Uses the refresh cookie to get a new access token. Returns null when there is no valid session. */
export function refreshSession(): Promise<AuthUser | null> {
  refreshInFlight ??= (async () => {
    try {
      const response = await authPost('/api/auth/refresh', { clientType: 'Web' })
      if (!response.ok) {
        accessToken = null
        if (response.status === 401 || response.status === 403) return null
        throw await toApiError(response)
      }
      const body = (await response.json()) as AuthResponse
      accessToken = body.accessToken
      return body.user
    } finally {
      refreshInFlight = null
    }
  })()
  return refreshInFlight
}

/**
 * Calls a protected API. On 401 it refreshes once and retries; if that fails the session has ended.
 */
export async function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const attempt = () => {
    const headers = new Headers(init.headers)
    if (accessToken) headers.set('Authorization', `Bearer ${accessToken}`)
    return send(path, { ...init, headers, credentials: path.startsWith('/api/auth/') ? 'include' : 'omit' })
  }

  let response = await attempt()
  // Only an expired/invalid access token (code UNAUTHENTICATED) is a reason to refresh. Other 401s, such as a
  // wrong current password when changing it, are returned to the caller unchanged.
  const code =
    response.status === 401
      ? ((await response.clone().json().catch(() => null)) as { code?: string } | null)?.code
      : undefined
  if (response.status === 401 && code === 'UNAUTHENTICATED') {
    const user = await refreshSession()
    if (!user) {
      onSessionExpired?.()
      throw new ApiError(401, 'UNAUTHENTICATED', 'Your session has ended. Sign in again.')
    }
    response = await attempt()
  }
  return response
}

export async function logout(): Promise<void> {
  try {
    if (accessToken) await apiFetch('/api/auth/logout', json({}))
  } finally {
    accessToken = null
  }
}

export async function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  // The refresh cookie travels with the request, so the server keeps this session and signs out the others.
  const response = await apiFetch('/api/auth/password', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ currentPassword, newPassword }),
  })
  if (!response.ok) throw await toApiError(response)
}
