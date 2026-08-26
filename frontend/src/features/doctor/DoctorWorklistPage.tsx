import { useAuth0 } from '@auth0/auth0-react'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'

import { ApiProblemError } from '../../api/client'
import {
  getClinicalWorklist,
  startConsultation,
  type AppointmentStatus,
  type DoctorWorklistItem,
  type DoctorWorklistPage,
} from '../../api/clinical'
import { getIdentity, type Identity } from '../../api/identity'
import { ClinicalErrorNotice } from './ClinicalFeedback'
import './doctor.css'

const worklistFilters: ReadonlyArray<{
  label: string
  value?: AppointmentStatus
}> = [
  { label: 'All visits' },
  { label: 'Scheduled', value: 'Scheduled' },
  { label: 'In progress', value: 'InProgress' },
  { label: 'Completed', value: 'Completed' },
]

function createWorklistWindow() {
  const from = new Date()
  from.setHours(0, 0, 0, 0)
  from.setDate(from.getDate() - 14)

  const to = new Date()
  to.setHours(23, 59, 59, 999)
  to.setDate(to.getDate() + 31)
  return { from, to }
}

function formatVisitTime(value: string) {
  const date = new Date(value)
  return {
    day: new Intl.DateTimeFormat('en-US', {
      weekday: 'short',
      month: 'short',
      day: 'numeric',
    }).format(date),
    time: new Intl.DateTimeFormat('en-US', {
      hour: 'numeric',
      minute: '2-digit',
      timeZoneName: 'short',
    }).format(date),
  }
}

function VisitAction({
  item,
  isStarting,
  onStart,
}: {
  item: DoctorWorklistItem
  isStarting: boolean
  onStart: (item: DoctorWorklistItem) => void
}) {
  if (item.appointmentStatus === 'Scheduled') {
    return (
      <button
        className="primary-button clinical-action"
        type="button"
        disabled={isStarting}
        onClick={() => onStart(item)}
      >
        {isStarting ? 'Opening visit…' : 'Start consultation'}
      </button>
    )
  }

  if (item.consultation) {
    return (
      <Link
        className="secondary-button clinical-action"
        to={`/app/doctor/consultations/${item.consultation.id}`}
      >
        {item.consultation.status === 'Draft' ? 'Continue consultation' : 'Review consultation'}
      </Link>
    )
  }

  return <span className="worklist-row__closed">No action needed</span>
}

export function DoctorWorklistPage() {
  const { getAccessTokenSilently } = useAuth0()
  const navigate = useNavigate()
  const window = useMemo(createWorklistWindow, [])
  const [identity, setIdentity] = useState<Identity | null>(null)
  const [worklist, setWorklist] = useState<DoctorWorklistPage | null>(null)
  const [filter, setFilter] = useState<AppointmentStatus | undefined>()
  const [isLoading, setIsLoading] = useState(true)
  const [startingAppointmentId, setStartingAppointmentId] = useState<number | null>(null)
  const [error, setError] = useState<unknown>(null)

  const loadWorklist = useCallback(() => {
    const controller = new AbortController()
    setIsLoading(true)
    setError(null)

    getAccessTokenSilently()
      .then(async (token) => {
        const currentIdentity = await getIdentity(token, controller.signal)
        setIdentity(currentIdentity)

        if (currentIdentity.role.toLowerCase() !== 'doctor') return null

        return getClinicalWorklist(
          token,
          { from: window.from, to: window.to, status: filter },
          controller.signal,
        )
      })
      .then((page) => {
        if (page) setWorklist(page)
      })
      .catch((requestError: unknown) => {
        if (!controller.signal.aborted) setError(requestError)
      })
      .finally(() => {
        if (!controller.signal.aborted) setIsLoading(false)
      })

    return () => controller.abort()
  }, [filter, getAccessTokenSilently, window.from, window.to])

  useEffect(loadWorklist, [loadWorklist])

  async function handleStart(item: DoctorWorklistItem) {
    setStartingAppointmentId(item.appointmentId)
    setError(null)

    try {
      const token = await getAccessTokenSilently()
      const consultation = await startConsultation(
        token,
        item.appointmentId,
        item.appointmentVersion,
      )
      navigate(`/app/doctor/consultations/${consultation.id}`, {
        state: { notice: `Consultation started for ${item.patientDisplayName}.` },
      })
    } catch (requestError) {
      setError(requestError)
      if (requestError instanceof ApiProblemError && requestError.status === 409) {
        loadWorklist()
      }
    } finally {
      setStartingAppointmentId(null)
    }
  }

  const scheduledCount = worklist?.items.filter(
    (item) => item.appointmentStatus === 'Scheduled',
  ).length ?? 0
  const inProgressCount = worklist?.items.filter(
    (item) => item.appointmentStatus === 'InProgress',
  ).length ?? 0

  if (!isLoading && identity && identity.role.toLowerCase() !== 'doctor') {
    return (
      <main id="main-content" className="centered-state">
        <p className="eyebrow">Role-specific workspace</p>
        <h1>This care queue is for doctor demo accounts.</h1>
        <p>Your signed role determines which fictional records and actions are available.</p>
        <Link className="primary-button" to="/app">
          Return to your workspace
        </Link>
      </main>
    )
  }

  return (
    <main id="main-content" className="doctor-workspace">
      <header className="doctor-intro">
        <div>
          <p className="eyebrow">Doctor workspace</p>
          <h1>Care queue</h1>
          <p>
            {identity ? `Good day, ${identity.displayName.split(' ')[0]}. ` : ''}
            Pick up the next visit without losing the handoff.
          </p>
        </div>
        <span className="timezone-note">Times shown in your local timezone</span>
      </header>

      <section className="worklist-summary" aria-label="Worklist summary">
        <div>
          <span>Visible visits</span>
          <strong>{worklist?.totalItems ?? '—'}</strong>
        </div>
        <div>
          <span>Ready to start</span>
          <strong>{scheduledCount}</strong>
        </div>
        <div>
          <span>In progress</span>
          <strong>{inProgressCount}</strong>
        </div>
        <p>
          Showing 14 days of recent context and the next 31 days of scheduled care.
        </p>
      </section>

      <section className="worklist-board" aria-labelledby="worklist-title" aria-busy={isLoading}>
        <div className="worklist-board__heading">
          <div>
            <p className="eyebrow">Assigned visits</p>
            <h2 id="worklist-title">Your clinical handoffs</h2>
          </div>
          <div className="worklist-filters" aria-label="Filter visits">
            {worklistFilters.map((option) => (
              <button
                key={option.label}
                type="button"
                aria-pressed={filter === option.value}
                onClick={() => setFilter(option.value)}
              >
                {option.label}
              </button>
            ))}
          </div>
        </div>

        {error !== null && (
          <ClinicalErrorNotice
            error={error}
            title="The care queue needs a refresh."
            actionLabel="Refresh worklist"
            onAction={loadWorklist}
          />
        )}

        {isLoading ? (
          <div className="clinical-loading" aria-live="polite">
            <span className="loading-orbit" aria-hidden="true" />
            <p>Organizing assigned visits…</p>
          </div>
        ) : worklist?.items.length ? (
          <div className="worklist-rows">
            {worklist.items.map((item) => {
              const when = formatVisitTime(item.startsAtUtc)
              return (
                <article className="worklist-row" key={item.appointmentId}>
                  <time dateTime={item.startsAtUtc} className="worklist-row__time">
                    <strong>{when.day}</strong>
                    <span>{when.time}</span>
                  </time>
                  <div className="worklist-row__patient">
                    <span
                      className={`status-pill status-pill--${item.appointmentStatus.toLowerCase()}`}
                    >
                      {item.appointmentStatus === 'InProgress'
                        ? 'In progress'
                        : item.appointmentStatus}
                    </span>
                    <h3>{item.patientDisplayName}</h3>
                    <p>{item.reason}</p>
                  </div>
                  <VisitAction
                    item={item}
                    isStarting={startingAppointmentId === item.appointmentId}
                    onStart={handleStart}
                  />
                </article>
              )
            })}
          </div>
        ) : (
          <div className="empty-state compact">
            <h3>No visits match this view</h3>
            <p>Choose another status or refresh when new appointments are assigned.</p>
          </div>
        )}
      </section>
    </main>
  )
}
