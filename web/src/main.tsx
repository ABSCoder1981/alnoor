import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter } from 'react-router'
import './index.css'
import App from './App.tsx'
import { AuthProvider } from './auth/AuthContext'

// Retry once for network blips; never retry client errors (4xx) such as validation or permission failures.
const isClientError = (error: unknown) => {
  const status = (error as { status?: number }).status ?? 0
  return status >= 400 && status < 500
}
const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: (count, error) => count < 1 && !isClientError(error), staleTime: 10_000 } },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AuthProvider>
          <App />
        </AuthProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
)
