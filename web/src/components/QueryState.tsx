import type { ReactNode } from 'react'
import { errorText } from '../api/client'

/** Loading / error / empty states for a query (CLAUDE.md: every data view has all three). */
export function QueryState({
  isLoading,
  error,
  isEmpty,
  empty,
  onRetry,
  children,
}: {
  isLoading: boolean
  error: unknown
  isEmpty?: boolean
  empty?: ReactNode
  onRetry?: () => void
  children: ReactNode
}) {
  if (isLoading) return <p aria-live="polite">Loading…</p>
  if (error)
    return (
      <div role="alert" className="error-box">
        <p className="error">{errorText(error)}</p>
        {onRetry && (
          <button type="button" className="secondary" onClick={onRetry}>
            Try again
          </button>
        )}
      </div>
    )
  if (isEmpty) return <>{empty}</>
  return <>{children}</>
}
