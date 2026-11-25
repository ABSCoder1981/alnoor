import { describe, expect, it } from 'vitest'
import { ApiError } from './authApi'
import { loginErrorMessage } from './messages'
import { toInternational } from './phone'

describe('toInternational', () => {
  it.each([
    ['+91', '98765 43210', '+919876543210'],
    ['+91', '098765-43210', '+919876543210'],
    ['+44', '(07911) 123456', '+447911123456'],
    ['+91', '   ', ''],
  ])('%s + %s -> %s', (code, number, expected) => {
    expect(toInternational(code, number)).toBe(expected)
  })
})

describe('loginErrorMessage', () => {
  it('explains a lockout in minutes', () => {
    const now = new Date('2026-11-25T10:00:00Z')
    const error = new ApiError(423, 'ACCOUNT_LOCKED', 'Locked', {}, '2026-11-25T10:14:10Z')

    expect(loginErrorMessage(error, now)).toBe('Too many failed attempts. Try again in 15 minutes.')
  })

  it('uses one message for wrong password and unknown phone', () => {
    expect(loginErrorMessage(new ApiError(401, 'INVALID_CREDENTIALS', 'x'))).toBe('The phone number or password is incorrect.')
  })

  it('shows the phone validation message from the server', () => {
    const error = new ApiError(400, 'VALIDATION_FAILED', 'x', { phoneNumber: ['Enter the phone number with its country code.'] })

    expect(loginErrorMessage(error)).toBe('Enter the phone number with its country code.')
  })

  it('never shows internal details for unexpected errors', () => {
    expect(loginErrorMessage(new ApiError(500, 'INTERNAL_ERROR', 'NullReferenceException at ...'))).toBe('Something went wrong. Please try again.')
    expect(loginErrorMessage(new Error('boom'))).toBe('Something went wrong. Please try again.')
  })
})
