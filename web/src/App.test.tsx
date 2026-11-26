import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'
import { clearAccessToken } from './auth/authApi'
import { AuthProvider } from './auth/AuthContext'

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

let routes: Record<string, () => Response>
let fetchMock: ReturnType<typeof vi.fn>

function renderApp() {
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter>
        <AuthProvider>
          <App />
        </AuthProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  clearAccessToken()
  routes = {
    '/health': () => new Response('Healthy', { status: 200 }),
    '/api/auth/refresh': () => json(401, { code: 'INVALID_REFRESH_TOKEN' }),
    '/api/classes': () => json(200, []),
  }
  fetchMock = vi.fn(async (url: string) => {
    const path = new URL(url).pathname
    const handler = routes[path]
    return handler ? handler() : json(404, { code: 'NOT_FOUND' })
  })
  vi.stubGlobal('fetch', fetchMock)
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const teacher = { id: 't1', fullName: 'Ustadh Ahmed', role: 'Teacher', mustChangePassword: false }

describe('App authentication flow', () => {
  it('shows the login form when there is no session', async () => {
    renderApp()

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeTruthy()
    expect(screen.getByLabelText('Phone number')).toBeTruthy()
  })

  it('validates empty fields before calling the server', async () => {
    renderApp()
    await userEvent.click(await screen.findByRole('button', { name: 'Sign in' }))

    expect((await screen.findByRole('alert')).textContent).toBe('Enter your phone number.')
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes('/api/auth/login'))).toBe(false)
  })

  it('shows a clear error for wrong credentials and clears the password', async () => {
    routes['/api/auth/login'] = () => json(401, { code: 'INVALID_CREDENTIALS', title: 'x' })
    renderApp()

    await userEvent.type(await screen.findByLabelText('Phone number'), '9800000001')
    await userEvent.type(screen.getByLabelText('Password'), 'wrong-password')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect((await screen.findByRole('alert')).textContent).toBe('The phone number or password is incorrect.')
    expect((screen.getByLabelText('Password') as HTMLInputElement).value).toBe('')
    const body = JSON.parse(fetchMock.mock.calls.find(([url]) => String(url).includes('/api/auth/login'))![1].body)
    expect(body.phoneNumber).toBe('+919800000001')
  })

  it('signs a teacher in', async () => {
    routes['/api/auth/login'] = () => json(200, { accessToken: 'a', accessTokenExpiresAt: 'x', user: teacher })
    renderApp()

    await userEvent.type(await screen.findByLabelText('Phone number'), '9800000001')
    await userEvent.type(screen.getByLabelText('Password'), 'right-password')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByText('Signed in as Ustadh Ahmed')).toBeTruthy()
  })

  it('forces a password change after a temporary password', async () => {
    routes['/api/auth/refresh'] = () => json(200, { accessToken: 'a', accessTokenExpiresAt: 'x', user: { ...teacher, mustChangePassword: true } })
    renderApp()

    expect(await screen.findByRole('heading', { name: 'Choose a new password' })).toBeTruthy()
    expect(screen.queryByRole('heading', { name: 'Welcome' })).toBeNull()
  })

  it('does not keep students signed in on the teacher web app', async () => {
    routes['/api/auth/login'] = () => json(200, { accessToken: 'a', accessTokenExpiresAt: 'x', user: { ...teacher, role: 'Student' } })
    routes['/api/auth/logout'] = () => new Response(null, { status: 204 })
    renderApp()

    await userEvent.type(await screen.findByLabelText('Phone number'), '9800000002')
    await userEvent.type(screen.getByLabelText('Password'), 'student-password')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect((await screen.findByRole('status')).textContent).toContain('Students use the ALNoor Android app')
  })
})
