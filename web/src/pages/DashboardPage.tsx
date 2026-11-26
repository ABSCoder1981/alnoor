import { useMutation, useQuery } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router'
import { errorText } from '../api/client'
import { classesApi, sessionsApi } from '../api/endpoints'
import { QueryState } from '../components/QueryState'
import { formatDateTime } from '../format'

/** Teacher dashboard (PRD 11.3 screen 2 / 11.2 screen 3): live banner, upcoming classes, quick Start. */
export function DashboardPage() {
  const navigate = useNavigate()
  const classes = useQuery({ queryKey: ['classes', false], queryFn: () => classesApi.list(false), refetchInterval: 30_000 })
  const start = useMutation({
    mutationFn: (classId: string) => sessionsApi.start(classId),
    onSuccess: (session) => navigate(`/classroom/${session.id}`),
  })

  const live = classes.data?.filter((c) => c.liveSessionId) ?? []
  const upcoming = [...(classes.data ?? [])]
    .filter((c) => !c.liveSessionId)
    .sort((a, b) => (a.nextOccurrences[0] ?? '9999').localeCompare(b.nextOccurrences[0] ?? '9999'))

  return (
    <>
      {live.map((c) => (
        <section key={c.id} className="card live-banner" aria-live="polite">
          <h2>{c.title} is live</h2>
          <Link className="button" to={`/classroom/${c.liveSessionId}`}>
            Return to class
          </Link>
        </section>
      ))}

      <section className="card">
        <div className="section-header">
          <h2>Your classes</h2>
          <Link to="/classes/new">New class</Link>
        </div>
        {start.error && (
          <p className="error" role="alert">
            {errorText(start.error)}
          </p>
        )}
        <QueryState
          isLoading={classes.isLoading}
          error={classes.error}
          onRetry={() => void classes.refetch()}
          isEmpty={classes.data?.length === 0}
          empty={
            <p>
              No classes yet. <Link to="/classes/new">Create your first class</Link>.
            </p>
          }
        >
          <ul className="list">
            {upcoming.map((c) => (
              <li key={c.id} className="list-row">
                <div>
                  <Link to={`/classes/${c.id}`} className="strong">
                    {c.title}
                  </Link>
                  <div className="muted">
                    {c.nextOccurrences[0] ? `Next: ${formatDateTime(c.nextOccurrences[0])}` : 'No weekly time set'} · {c.studentCount}{' '}
                    student{c.studentCount === 1 ? '' : 's'}
                  </div>
                </div>
                <button type="button" onClick={() => start.mutate(c.id)} disabled={start.isPending}>
                  {start.isPending && start.variables === c.id ? 'Starting…' : 'Start class'}
                </button>
              </li>
            ))}
          </ul>
        </QueryState>
      </section>
    </>
  )
}
