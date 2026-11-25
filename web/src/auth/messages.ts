import { ApiError, NETWORK_ERROR } from './authApi'

/** Clear, user-facing text for login errors (US-01: "wrong → clear error; locked after repeated failures"). */
export function loginErrorMessage(error: unknown, now: Date = new Date()): string {
  if (!(error instanceof ApiError)) return 'Something went wrong. Please try again.'
  switch (error.code) {
    case 'INVALID_CREDENTIALS':
      return 'The phone number or password is incorrect.'
    case 'ACCOUNT_LOCKED': {
      if (!error.lockoutEndsAt) return 'Too many failed attempts. Please wait and try again.'
      const minutes = Math.max(1, Math.ceil((new Date(error.lockoutEndsAt).getTime() - now.getTime()) / 60_000))
      return `Too many failed attempts. Try again in ${minutes} minute${minutes === 1 ? '' : 's'}.`
    }
    case 'ACCOUNT_DEACTIVATED':
      return 'This account has been deactivated.'
    case 'RATE_LIMITED':
      return 'Too many login attempts. Wait a minute and try again.'
    case 'VALIDATION_FAILED':
      return error.fieldErrors.phoneNumber?.[0] ?? 'Check the phone number and password.'
    case NETWORK_ERROR:
      return error.message
    default:
      return 'Something went wrong. Please try again.'
  }
}
