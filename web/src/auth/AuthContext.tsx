import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import * as api from './authApi'
import type { AuthUser } from './authApi'

import { AuthContext, type AuthContextValue, type AuthState } from './useAuth'

const teachersOnly = 'The web app is for teachers. Students use the ALNoor Android app.'

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({ status: 'loading' })

  const accept = useCallback(async (user: AuthUser | null) => {
    if (!user) {
      setState({ status: 'signedOut' })
    } else if (user.role !== 'Teacher') {
      // A UI convenience only: the API enforces roles on every request.
      await api.logout().catch(() => undefined)
      setState({ status: 'signedOut', notice: teachersOnly })
    } else {
      setState({ status: 'signedIn', user })
    }
  }, [])

  // Restore the session from the HttpOnly refresh cookie when the page loads.
  useEffect(() => {
    api.setSessionExpiredHandler(() => setState({ status: 'signedOut', notice: 'Your session has ended. Sign in again.' }))
    api
      .refreshSession()
      .then(accept)
      .catch(() => setState({ status: 'signedOut' }))
    return () => api.setSessionExpiredHandler(null)
  }, [accept])

  const value = useMemo<AuthContextValue>(
    () => ({
      state,
      login: async (phoneNumber, password) => accept(await api.login(phoneNumber, password)),
      logout: async () => {
        await api.logout().catch(() => undefined)
        setState({ status: 'signedOut' })
      },
      passwordChanged: () =>
        setState((current) =>
          current.status === 'signedIn' ? { status: 'signedIn', user: { ...current.user, mustChangePassword: false } } : current,
        ),
    }),
    [state, accept],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
