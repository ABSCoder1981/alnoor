/** Display helpers. Times come from the API in UTC and are shown in the browser's local time (PRD M5). */

export const dayNames = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'] as const

export function formatDateTime(isoUtc: string, locale?: string): string {
  return new Intl.DateTimeFormat(locale, { weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }).format(
    new Date(isoUtc),
  )
}

export function browserTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone || 'Asia/Kolkata'
}

export function timeZones(): string[] {
  const supported = (Intl as unknown as { supportedValuesOf?: (key: string) => string[] }).supportedValuesOf?.('timeZone')
  return supported && supported.length > 0 ? supported : ['Asia/Kolkata', 'Asia/Karachi', 'Asia/Dubai', 'Europe/London', 'UTC']
}

/** A readable temporary password, e.g. "noor-4821-quran": easy to read out, not in the common-password list. */
export function generateTemporaryPassword(random: (max: number) => number = (max) => crypto.getRandomValues(new Uint32Array(1))[0] % max): string {
  const words = ['noor', 'quran', 'sabr', 'ilm', 'huda', 'amal', 'rahma', 'salam', 'barakah', 'iman', 'taqwa', 'shukr']
  const pick = () => words[random(words.length)]
  const digits = String(1000 + random(9000))
  return `${pick()}-${digits}-${pick()}`
}
