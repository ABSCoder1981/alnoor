import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, apiFetch, clearAccessToken, login, NETWORK_ERROR, refreshSession, setSessionExpiredHandler } from './authApi'

const user = { id: 'u1', fullName: 'Ustadh Ahmed', role: 'Teacher', mustChangePassword: false }
const ok = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
const problem = (status: number, body: unknown) => new Response(JSON.stringify(body), { status })

let fetchMock: ReturnType<typeof vi.fn>

beforeEach(() => {
  clearAccessToken()
  fetchMock = vi.fn()
  vi.stubGlobal('fetch', fetchMock)
})

afterEach(() => {
  vi.unstubAllGlobals()
  setSessionExpiredHandler(null)
})

describe('login', () => {
  it('asks for cookie delivery and keeps the access token in memory', async () => {
    fetchMock.mockResolvedValueOnce(ok({ accessToken: 'access-1', accessTokenExpiresAt: 'x', user }))
    fetchMock.mockResolvedValueOnce(new Response('{}', { status: 200 }))

    await expect(login('+919800000001', 'secret-pass')).resolves.toEqual(user)
    const [, init] = fetchMock.mock.calls[0]
    expect(init.credentials).toBe('include')
    expect(JSON.parse(init.body)).toMatchObject({ phoneNumber: '+919800000001', clientType: 'Web' })

    await apiFetch('/api/classes')
    const headers = fetchMock.mock.calls[1][1].headers as Headers
    expect(headers.get('Authorization')).toBe('Bearer access-1')
    expect(fetchMock.mock.calls[1][1].credentials).toBe('omit')
  })

  it('turns problem details into ApiError with code and lockout time', async () => {
    fetchMock.mockResolvedValueOnce(problem(423, { code: 'ACCOUNT_LOCKED', title: 'Locked', lockoutEndsAt: '2026-11-25T10:15:00Z' }))

    const error = await login('+919800000001', 'x').catch((e: unknown) => e)
    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).code).toBe('ACCOUNT_LOCKED')
    expect((error as ApiError).lockoutEndsAt).toBe('2026-11-25T10:15:00Z')
  })

  it('reports an unreachable server as a network error', async () => {
    fetchMock.mockRejectedValueOnce(new TypeError('Failed to fetch'))

    await expect(login('+919800000001', 'x')).rejects.toMatchObject({ code: NETWORK_ERROR })
  })
})

describe('apiFetch', () => {
  it('refreshes once on 401 and retries with the new token', async () => {
    fetchMock
      .mockResolvedValueOnce(problem(401, { code: 'UNAUTHENTICATED' }))
      .mockResolvedValueOnce(ok({ accessToken: 'access-2', accessTokenExpiresAt: 'x', user }))
      .mockResolvedValueOnce(new Response('[]', { status: 200 }))

    const response = await apiFetch('/api/classes')

    expect(response.status).toBe(200)
    expect(fetchMock).toHaveBeenCalledTimes(3)
    expect(fetchMock.mock.calls[1][0]).toContain('/api/auth/refresh')
    expect((fetchMock.mock.calls[2][1].headers as Headers).get('Authorization')).toBe('Bearer access-2')
  })

  it('ends the session when refresh fails', async () => {
    const expired = vi.fn()
    setSessionExpiredHandler(expired)
    fetchMock
      .mockResolvedValueOnce(problem(401, { code: 'UNAUTHENTICATED' }))
      .mockResolvedValueOnce(problem(401, { code: 'INVALID_REFRESH_TOKEN' }))

    await expect(apiFetch('/api/classes')).rejects.toMatchObject({ status: 401, code: 'UNAUTHENTICATED' })
    expect(expired).toHaveBeenCalledOnce()
  })

  it('does not refresh for other 401s such as a wrong current password', async () => {
    const expired = vi.fn()
    setSessionExpiredHandler(expired)
    fetchMock.mockResolvedValueOnce(problem(401, { code: 'INVALID_CREDENTIALS', title: 'The current password is incorrect.' }))

    const response = await apiFetch('/api/auth/password', { method: 'PUT' })

    expect(response.status).toBe(401)
    expect(fetchMock).toHaveBeenCalledOnce()
    expect(expired).not.toHaveBeenCalled()
  })
})

describe('refreshSession', () => {
  it('shares one request between parallel callers', async () => {
    fetchMock.mockResolvedValueOnce(ok({ accessToken: 'a', accessTokenExpiresAt: 'x', user }))

    const [a, b] = await Promise.all([refreshSession(), refreshSession()])

    expect(a).toEqual(user)
    expect(b).toEqual(user)
    expect(fetchMock).toHaveBeenCalledOnce()
  })

  it('returns null when there is no session', async () => {
    fetchMock.mockResolvedValueOnce(problem(401, { code: 'INVALID_REFRESH_TOKEN' }))

    await expect(refreshSession()).resolves.toBeNull()
  })
})
