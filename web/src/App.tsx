import { lazy, Suspense } from 'react'
import { Link, Navigate, Route, Routes } from 'react-router'
import { useAuth } from './auth/useAuth'
import { ChangePasswordPage } from './pages/ChangePasswordPage'
import { LoginPage } from './pages/LoginPage'

// Phase 2 proof of concept. import.meta.env.DEV is false in production builds, so this code is not bundled there.
const LiveKitPoc = import.meta.env.DEV ? lazy(() => import('./poc/LiveKitPoc')) : null

function App() {
  const { state, logout } = useAuth()

  if (state.status === 'loading') {
    return (
      <main className="shell">
        <p aria-live="polite">Loading…</p>
      </main>
    )
  }

  if (state.status === 'signedOut') {
    return <LoginPage notice={state.notice} />
  }

  const { user } = state
  return (
    <main className="shell">
      <header className="app-header">
        <div>
          <h1>ALNoor</h1>
          <p className="subtitle">Signed in as {user.fullName}</p>
        </div>
        <nav aria-label="Main">
          <Link to="/">Home</Link>
          <Link to="/account/password">Password</Link>
          {LiveKitPoc && <Link to="/dev/livekit">LiveKit test</Link>}
          <button type="button" className="secondary" onClick={() => void logout()}>
            Sign out
          </button>
        </nav>
      </header>

      {user.mustChangePassword ? (
        <ChangePasswordPage forced />
      ) : (
        <Routes>
          <Route
            path="/"
            element={
              <section className="card">
                <h2>Welcome</h2>
                <p>Class and student management arrive in the next screens (Phase 4 dashboards).</p>
              </section>
            }
          />
          <Route path="/account/password" element={<ChangePasswordPage forced={false} />} />
          {LiveKitPoc && (
            <Route
              path="/dev/livekit"
              element={
                <Suspense fallback={<p>Loading test room…</p>}>
                  <LiveKitPoc />
                </Suspense>
              }
            />
          )}
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      )}
    </main>
  )
}

export default App
