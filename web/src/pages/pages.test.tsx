import { cleanup, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { ClassSummary } from '../api/types'
import { generateTemporaryPassword } from '../format'
import { json, renderWithApi } from '../test/renderWithApi'
import { validateClass } from './classValidation'
import { ClassDetailPage } from './ClassPages'
import { DashboardPage } from './DashboardPage'
import { StudentsPage } from './StudentsPage'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const cls = (overrides: Partial<ClassSummary> = {}): ClassSummary => ({
  id: 'c1',
  title: 'Tajweed A',
  description: null,
  timeZoneId: 'Asia/Kolkata',
  defaultDurationMinutes: 60,
  isActive: true,
  schedule: [{ dayOfWeek: 1, startTimeLocal: '17:00', durationMinutes: 60 }],
  nextOccurrences: ['2026-11-30T11:30:00Z'],
  studentCount: 2,
  liveSessionId: null,
  teacher: { id: 't1', fullName: 'Ustadh' },
  students: null,
  ...overrides,
})

describe('DashboardPage', () => {
  it('shows a live banner and upcoming classes, and starts a class', async () => {
    const { calls } = renderWithApi(<DashboardPage />, {
      handlers: {
        'GET /api/classes': () => json(200, [cls(), cls({ id: 'c2', title: 'Hifz B', liveSessionId: 's9' })]),
        'POST /api/classes/c1/sessions': () => json(201, { id: 's1', classId: 'c1', classTitle: 'Tajweed A', status: 'Live', startedAt: 'x', endedAt: null, endReason: null }),
      },
    })

    expect(await screen.findByRole('heading', { name: 'Hifz B is live' })).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Return to class' }).getAttribute('href')).toBe('/classroom/s9')

    await userEvent.click(screen.getByRole('button', { name: 'Start class' }))
    expect(await screen.findByText('navigated')).toBeTruthy()
    expect(calls('POST /api/classes/c1/sessions')).toHaveLength(1)
  })

  it('shows the server error when starting fails', async () => {
    renderWithApi(<DashboardPage />, {
      handlers: {
        'GET /api/classes': () => json(200, [cls()]),
        'POST /api/classes/c1/sessions': () => json(502, { code: 'RTC_UNAVAILABLE', title: 'The live classroom service is not available. Try again in a moment.' }),
      },
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Start class' }))
    expect((await screen.findByRole('alert')).textContent).toContain('live classroom service is not available')
  })

  it('has an empty state', async () => {
    renderWithApi(<DashboardPage />, { handlers: { 'GET /api/classes': () => json(200, []) } })

    expect(await screen.findByText(/No classes yet/)).toBeTruthy()
  })
})

describe('StudentsPage', () => {
  const student = { id: 's1', fullName: 'Aisha Khan', phoneNumber: '+919800000001', isActive: true, mustChangePassword: false, classCount: 1 }

  it('adds a student and shows the temporary password once', async () => {
    let created: Record<string, string> | null = null
    renderWithApi(<StudentsPage />, {
      handlers: {
        'GET /api/students': () => json(200, []),
        'POST /api/auth/register': (init) => {
          created = JSON.parse(String(init.body))
          return json(201, { ...student, mustChangePassword: true })
        },
      },
    })

    await userEvent.type(await screen.findByLabelText('Full name'), 'Aisha Khan')
    await userEvent.type(screen.getByLabelText(/Phone number/), '98000 00001')
    await userEvent.click(screen.getByRole('button', { name: 'Add student' }))

    const notice = await screen.findByRole('status')
    expect(within(notice).getByRole('heading').textContent).toBe('Temporary password for Aisha Khan')
    expect(created!.phoneNumber).toBe('+919800000001')
    expect(within(notice).getByText(created!.temporaryPassword)).toBeTruthy()

    await userEvent.click(within(notice).getByRole('button', { name: 'Done' }))
    expect(screen.queryByText(created!.temporaryPassword)).toBeNull()
  })

  it('asks for confirmation before deactivating', async () => {
    const { calls } = renderWithApi(<StudentsPage />, {
      handlers: {
        'GET /api/students': () => json(200, [student]),
        'PUT /api/students/s1/status': () => json(200, { ...student, isActive: false }),
      },
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Deactivate' }))
    const dialog = screen.getByRole('dialog')
    expect(within(dialog).getByText(/removed from any live class/)).toBeTruthy()
    expect(calls('PUT /api/students/s1/status')).toHaveLength(0)

    await userEvent.click(within(dialog).getByRole('button', { name: 'Deactivate' }))
    await vi.waitFor(() => expect(calls('PUT /api/students/s1/status')).toHaveLength(1))
  })

  it('validates before calling the server', async () => {
    const { calls } = renderWithApi(<StudentsPage />, { handlers: { 'GET /api/students': () => json(200, []) } })

    await userEvent.click(await screen.findByRole('button', { name: 'Add student' }))

    expect((await screen.findByRole('alert')).textContent).toContain('full name')
    expect(calls('POST /api/auth/register')).toHaveLength(0)
  })
})

describe('ClassDetailPage enrollment', () => {
  it('limits selection to the free places', async () => {
    const enrolled = Array.from({ length: 8 }, (_, i) => ({ id: `e${i}`, fullName: `Enrolled ${i}`, enrollmentStatus: 'Active' as const }))
    renderWithApi(<ClassDetailPage />, {
      path: '/classes/c1',
      route: '/classes/:classId',
      handlers: {
        'GET /api/classes/c1': () => json(200, cls({ students: enrolled, studentCount: 8 })),
        'GET /api/students': () =>
          json(200, [
            { id: 'a', fullName: 'Available A', phoneNumber: '+919800000011', isActive: true, mustChangePassword: false },
            { id: 'b', fullName: 'Available B', phoneNumber: '+919800000012', isActive: true, mustChangePassword: false },
          ]),
      },
    })

    expect(await screen.findByRole('heading', { name: 'Students (8/9)' })).toBeTruthy()
    const a = await screen.findByRole('checkbox', { name: /Available A/ })
    const b = screen.getByRole('checkbox', { name: /Available B/ })
    await userEvent.click(a)
    expect((b as HTMLInputElement).disabled).toBe(true) // Only 1 place left.
  })
})

describe('validateClass', () => {
  const base = { title: 'Tajweed', description: null, timeZoneId: 'Asia/Kolkata', defaultDurationMinutes: 60, schedule: [] }

  it('accepts a valid class', () => expect(validateClass(base)).toBeNull())
  it('rejects a short title', () => expect(validateClass({ ...base, title: 'T' })).toContain('2–100'))
  it('rejects a bad time', () =>
    expect(validateClass({ ...base, schedule: [{ dayOfWeek: 1, startTimeLocal: '25:00', durationMinutes: 60 }] })).toContain('24-hour'))
  it('rejects a long slot', () =>
    expect(validateClass({ ...base, schedule: [{ dayOfWeek: 1, startTimeLocal: '17:00', durationMinutes: 300 }] })).toContain('10–180'))
})

describe('generateTemporaryPassword', () => {
  it('is readable and at least 8 characters', () => {
    for (let i = 0; i < 20; i++) {
      const p = generateTemporaryPassword()
      expect(p).toMatch(/^[a-z]+-\d{4}-[a-z]+$/)
      expect(p.length).toBeGreaterThanOrEqual(8)
    }
  })
})
