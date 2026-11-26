import { ApiError, apiFetch } from '../auth/authApi'

/** Calls a protected API and returns parsed JSON; throws ApiError with the server's code on failure. */
export async function request<T>(path: string, init: { method?: string; body?: unknown } = {}): Promise<T> {
  const response = await apiFetch(path, {
    method: init.method ?? 'GET',
    headers: init.body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: init.body === undefined ? undefined : JSON.stringify(init.body),
  })

  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as {
      code?: string
      title?: string
      errors?: Record<string, string[]>
    } | null
    throw new ApiError(response.status, problem?.code ?? 'UNKNOWN', problem?.title ?? `Request failed (HTTP ${response.status}).`, problem?.errors ?? {})
  }

  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

/** A short, user-facing message for an error from {@link request}. Never shows internal details. */
export function errorText(error: unknown): string {
  if (!(error instanceof ApiError)) return 'Something went wrong. Please try again.'
  const first = Object.values(error.fieldErrors)[0]?.[0]
  if (first) return first
  if (error.status >= 500 && error.code !== 'RTC_UNAVAILABLE') return 'Something went wrong on the server. Please try again.'
  return error.message
}
