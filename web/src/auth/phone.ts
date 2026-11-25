/** Country codes offered on the login screen. The server validates the full number (E.164). */
export const countryCodes = [
  { code: '+91', label: 'India (+91)' },
  { code: '+92', label: 'Pakistan (+92)' },
  { code: '+880', label: 'Bangladesh (+880)' },
  { code: '+971', label: 'UAE (+971)' },
  { code: '+966', label: 'Saudi Arabia (+966)' },
  { code: '+44', label: 'United Kingdom (+44)' },
  { code: '+1', label: 'USA / Canada (+1)' },
] as const

/**
 * Joins a country code and the number typed by the user: keeps digits only and drops a leading 0
 * (trunk prefix, e.g. 09876543210 in India). Returns '' when there are no digits.
 */
export function toInternational(countryCode: string, nationalNumber: string): string {
  const digits = nationalNumber.replace(/\D/g, '').replace(/^0+/, '')
  return digits ? `${countryCode}${digits}` : ''
}
