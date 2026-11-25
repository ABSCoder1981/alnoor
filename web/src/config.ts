// Build-time configuration. Only public values belong here: anything prefixed
// VITE_ is embedded in the browser bundle, so never put secrets in these variables.
const rawApiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080'

export const config = {
  apiBaseUrl: rawApiBaseUrl.replace(/\/+$/, ''),
} as const
