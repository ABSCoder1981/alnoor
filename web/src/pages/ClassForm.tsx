import { useState, type FormEvent } from 'react'
import type { SaveClass, ScheduleEntry } from '../api/types'
import { browserTimeZone, dayNames, timeZones } from '../format'
import { validateClass } from './classValidation'

interface Props {
  initial?: SaveClass
  submitLabel: string
  busy: boolean
  serverError: string | null
  onSubmit: (value: SaveClass) => void
}

const MAX_SLOTS = 14

export function ClassForm({ initial, submitLabel, busy, serverError, onSubmit }: Props) {
  const [title, setTitle] = useState(initial?.title ?? '')
  const [description, setDescription] = useState(initial?.description ?? '')
  const [timeZoneId, setTimeZoneId] = useState(initial?.timeZoneId ?? browserTimeZone())
  const [duration, setDuration] = useState(initial?.defaultDurationMinutes ?? 60)
  const [schedule, setSchedule] = useState<ScheduleEntry[]>(initial?.schedule ?? [])
  const [error, setError] = useState<string | null>(null)

  const updateSlot = (index: number, patch: Partial<ScheduleEntry>) =>
    setSchedule((rows) => rows.map((row, i) => (i === index ? { ...row, ...patch } : row)))

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const value: SaveClass = {
      title: title.trim(),
      description: description.trim() || null,
      timeZoneId,
      defaultDurationMinutes: duration,
      schedule,
      isActive: initial?.isActive,
    }
    const problem = validateClass(value)
    setError(problem)
    if (!problem) onSubmit(value)
  }

  return (
    <form className="form" onSubmit={handleSubmit} noValidate>
      <label>
        Title
        <input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={100} required />
      </label>
      <label>
        Description (optional)
        <textarea value={description} onChange={(e) => setDescription(e.target.value)} maxLength={500} rows={2} />
      </label>
      <div className="row-2">
        <label>
          Time zone
          <select value={timeZoneId} onChange={(e) => setTimeZoneId(e.target.value)}>
            {timeZones().map((z) => (
              <option key={z} value={z}>
                {z}
              </option>
            ))}
          </select>
        </label>
        <label>
          Usual length (minutes)
          <input type="number" min={10} max={180} value={duration} onChange={(e) => setDuration(Number(e.target.value))} />
        </label>
      </div>

      <fieldset>
        <legend>Weekly times</legend>
        {schedule.length === 0 && <p className="muted">No weekly times. Students will see "Waiting for teacher" until you start.</p>}
        {schedule.map((slot, i) => (
          <div key={i} className="slot-row">
            <label>
              Day
              <select value={slot.dayOfWeek} onChange={(e) => updateSlot(i, { dayOfWeek: Number(e.target.value) })}>
                {dayNames.map((d, n) => (
                  <option key={d} value={n}>
                    {d}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Start
              <input type="time" value={slot.startTimeLocal} onChange={(e) => updateSlot(i, { startTimeLocal: e.target.value })} />
            </label>
            <label>
              Minutes
              <input
                type="number"
                min={10}
                max={180}
                value={slot.durationMinutes}
                onChange={(e) => updateSlot(i, { durationMinutes: Number(e.target.value) })}
              />
            </label>
            <button type="button" className="secondary" onClick={() => setSchedule((rows) => rows.filter((_, n) => n !== i))} aria-label={`Remove ${dayNames[slot.dayOfWeek]} ${slot.startTimeLocal}`}>
              Remove
            </button>
          </div>
        ))}
        {schedule.length < MAX_SLOTS && (
          <button
            type="button"
            className="secondary"
            onClick={() => setSchedule((rows) => [...rows, { dayOfWeek: 1, startTimeLocal: '17:00', durationMinutes: duration }])}
          >
            Add weekly time
          </button>
        )}
      </fieldset>

      {(error ?? serverError) && (
        <p className="error" role="alert">
          {error ?? serverError}
        </p>
      )}
      <button type="submit" disabled={busy}>
        {busy ? 'Saving…' : submitLabel}
      </button>
    </form>
  )
}
