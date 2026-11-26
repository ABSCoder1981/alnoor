import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import type { ReactElement } from 'react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { vi } from 'vitest'

export type Handler = (init: RequestInit & { url: URL }) => Response | Promise<Response>

export const json = (status: number, body?: unknown) =>
  new Response(body === undefined ? null : JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

/**
 * Renders a page with React Query and a router, and a fake API: handlers are keyed by "METHOD /path".
 * Returns the fetch mock so tests can assert which calls were made.
 */
export function renderWithApi(ui: ReactElement, opts: { path?: string; route?: string; handlers: Record<string, Handler> }) {
  const fetchMock = vi.fn(async (input: string, init: RequestInit = {}) => {
    const url = new URL(input)
    const key = `${(init.method ?? 'GET').toUpperCase()} ${url.pathname}`
    const handler = opts.handlers[key]
    return handler ? handler({ ...init, url }) : json(404, { code: 'NOT_FOUND', title: `No handler for ${key}` })
  })
  vi.stubGlobal('fetch', fetchMock)

  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[opts.path ?? '/']}>
        <Routes>
          <Route path={opts.route ?? '/'} element={ui} />
          <Route path="*" element={<p>navigated</p>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )

  const calls = (key: string) =>
    fetchMock.mock.calls.filter(([url, init]) => `${((init as RequestInit | undefined)?.method ?? 'GET').toUpperCase()} ${new URL(url).pathname}` === key)
  return { fetchMock, calls }
}
