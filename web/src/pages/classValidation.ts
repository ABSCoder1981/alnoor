import type { SaveClass } from '../api/types'

/** Client-side checks mirror the server (PRD 16.4); the server stays the authority. */
export function validateClass(value: SaveClass): string | null {
  const title = value.title.trim()
  if (title.length < 2 || title.length > 100) return 'The title must be 2–100 characters.'
  if ((value.description ?? '').length > 500) return 'The description must be at most 500 characters.'
  if (value.defaultDurationMinutes < 10 || value.defaultDurationMinutes > 180) return 'The duration must be 10–180 minutes.'
  for (const [i, s] of value.schedule.entries()) {
    if (!/^([01]\d|2[0-3]):[0-5]\d$/.test(s.startTimeLocal)) return `Time ${i + 1}: use 24-hour time, e.g. 17:00.`
    if (s.durationMinutes < 10 || s.durationMinutes > 180) return `Time ${i + 1}: the duration must be 10–180 minutes.`
  }
  return null
}
