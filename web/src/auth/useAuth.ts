import { createContext, useContext } from 'react'
import type { AuthUser } from './authApi'

export type AuthState =
  | { status: 'loading' }
  | { status: 'signedOut'; notice?: string }
  | { status: 'signedIn'; user: AuthUser }

export interface AuthContextValue {
  state: AuthState
  login: (phoneNumber: string, password: string) => Promise<void>
  logout: () => Promise<void>
  passwordChanged: () => void
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext)
  if (!value) throw new Error('useAuth must be used inside <AuthProvider>.')
  return value
}
