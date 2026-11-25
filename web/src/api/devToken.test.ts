import { afterEach, describe, expect, it, vi } from 'vitest'
import { DevTokenError, requestDevToken } from './devToken'

const request = { name: 'Aisha', room: 'poc', role: 'student' as const }

function mockFetch(implementation: () => Promise<Response>) {
  vi.stubGlobal('fetch', vi.fn(implementation))
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('requestDevToken', () => {
  it('returns the connection details on success', async () => {
    const body = { url: 'ws://x:7880', token: 'a.b.c', identity: 'dev-1', expiresAt: '2026-11-25T10:05:00Z' }
    mockFetch(async () => new Response(JSON.stringify(body), { status: 200 }))

    await expect(requestDevToken(request)).resolves.toEqual(body)
  })

  it('explains when the API is unreachable', async () => {
    mockFetch(async () => {
      throw new TypeError('Failed to fetch')
    })

    await expect(requestDevToken(request)).rejects.toThrow(/cannot be reached/)
  })

  it('explains when the endpoint is missing (API not in Development)', async () => {
    mockFetch(async () => new Response(null, { status: 404 }))

    await expect(requestDevToken(request)).rejects.toThrow(/Development environment/)
  })

  it('shows validation messages from ProblemDetails', async () => {
    const problem = { status: 400, code: 'VALIDATION_FAILED', errors: { Room: ['Room may contain only a-z.'] } }
    mockFetch(async () => new Response(JSON.stringify(problem), { status: 400 }))

    const error = await requestDevToken(request).catch((e: unknown) => e)
    expect(error).toBeInstanceOf(DevTokenError)
    expect((error as Error).message).toBe('Room may contain only a-z.')
  })

  it('reports other server errors without details', async () => {
    mockFetch(async () => new Response('stack trace here', { status: 500 }))

    await expect(requestDevToken(request)).rejects.toThrow('The API returned an error (HTTP 500).')
  })
})
