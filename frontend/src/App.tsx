import { useAuth0 } from '@auth0/auth0-react'
import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { Link, Navigate, Route, Routes, useLocation, useNavigate } from 'react-router-dom'

import { ApiProblemError } from './api/client'
import { getIdentity, type Identity } from './api/identity'
import {
  bookAppointment,
  cancelAppointment,
  getAppointments,
  getAvailability,
  getClinicians,
  type Appointment,
  type Availability,
  type Clinician,
} from './api/scheduling'
import { DoctorWorklistPage } from './features/doctor/DoctorWorklistPage'
import { ConsultationPage } from './features/doctor/ConsultationPage'
import { NavigationGuardProvider } from './navigation/NavigationGuard'
import './App.css'

const careStages = ['Appointment', 'Consultation', 'Prescription', 'Pharmacy'] as const

function LoadingScreen({ message }: { message: string }) {
  return (
    <main className="centered-state" aria-live="polite" aria-busy="true">
      <span className="loading-orbit" aria-hidden="true" />
      <p className="eyebrow">Preparing your workspace</p>
      <h1>{message}</h1>
    </main>
  )
}

function AppHeader() {
  const { isAuthenticated, logout } = useAuth0()

  return (
    <header className="site-header">
      <Link className="brand-lockup" to={isAuthenticated ? '/app' : '/'} aria-label="Harbor Care home">
        <span className="brand-mark" aria-hidden="true" />
        <span>
          <strong>Harbor Care</strong>
          <small>Coordination portal</small>
        </span>
      </Link>
      <span className="simulation-label">Synthetic care demo</span>
      {isAuthenticated && (
        <button
          className="text-button"
          type="button"
          onClick={() => logout({ logoutParams: { returnTo: window.location.origin } })}
        >
          Sign out
        </button>
      )}
    </header>
  )
}

function LandingPage() {
  const { isAuthenticated, loginWithRedirect } = useAuth0()
  const navigate = useNavigate()

  return (
    <main id="main-content" className="landing-page">
      <section className="hero" aria-labelledby="page-title">
        <div className="hero__copy">
          <p className="eyebrow">A coordinated-care portfolio project</p>
          <h1 id="page-title">
            Care moves better when the <em>next step</em> is clear.
          </h1>
          <p className="hero__summary">
            Explore a fictional patient journey from appointment scheduling to pharmacy pickup,
            built with React, ASP.NET Core, PostgreSQL, and Auth0.
          </p>
          <div className="hero__actions">
            <button
              className="primary-button"
              type="button"
              onClick={() =>
                isAuthenticated
                  ? navigate('/app')
                  : loginWithRedirect({ appState: { returnTo: '/app' } })
              }
            >
              {isAuthenticated ? 'Open secure workspace' : 'Enter the care demo'}
            </button>
            <a className="secondary-link" href="#care-relay">
              See how care moves
            </a>
          </div>
          <p className="synthetic-note">
            <span aria-hidden="true">◆</span>
            Every person and health detail shown here is fictional.
          </p>
        </div>

        <div className="hero__artifact" aria-label="Example patient care itinerary">
          <div className="artifact-ticket">
            <p className="eyebrow">Next handoff</p>
            <time dateTime="2030-09-08T16:00:00Z">Tue 08 · 9:00 AM</time>
            <h2>Visit with Dr. Maya Chen</h2>
            <p>Family Medicine · 45 minutes</p>
            <span className="status-pill status-pill--scheduled">Scheduled</span>
          </div>
          <span className="artifact-caption">One calm view of what happens next.</span>
        </div>
      </section>

      <section id="care-relay" className="relay-section" aria-labelledby="relay-title">
        <div>
          <p className="eyebrow">The care relay</p>
          <h2 id="relay-title">Context travels forward. Each person sees their part.</h2>
        </div>
        <ol className="relay-list">
          {careStages.map((stage, index) => (
            <li key={stage}>
              <span>{String(index + 1).padStart(2, '0')}</span>
              <strong>{stage}</strong>
            </li>
          ))}
        </ol>
      </section>
    </main>
  )
}

function ProtectedRoute({ children }: { children: ReactNode }) {
  const { isAuthenticated, isLoading, loginWithRedirect } = useAuth0()

  if (isLoading) {
    return <LoadingScreen message="Confirming your secure session" />
  }

  if (!isAuthenticated) {
    return (
      <main id="main-content" className="centered-state">
        <p className="eyebrow">Secure workspace</p>
        <h1>Sign in to continue your care journey.</h1>
        <p>Your Auth0 session protects the fictional appointments in this demonstration.</p>
        <button
          className="primary-button"
          type="button"
          onClick={() => loginWithRedirect({ appState: { returnTo: window.location.pathname } })}
        >
          Sign in with Auth0
        </button>
      </main>
    )
  }

  return children
}

function WorkspaceIndexPage() {
  const { getAccessTokenSilently } = useAuth0()
  const [identity, setIdentity] = useState<Identity | null>(null)
  const [error, setError] = useState<unknown>(null)

  useEffect(() => {
    const controller = new AbortController()
    getAccessTokenSilently()
      .then((token) => getIdentity(token, controller.signal))
      .then(setIdentity)
      .catch((requestError: unknown) => {
        if (!controller.signal.aborted) setError(requestError)
      })

    return () => controller.abort()
  }, [getAccessTokenSilently])

  if (error !== null) {
    return (
      <main id="main-content" className="centered-state">
        <ErrorNotice error={error} />
      </main>
    )
  }

  if (!identity) return <LoadingScreen message="Finding your care workspace" />

  const role = identity.role.toLowerCase()
  if (role === 'doctor') return <Navigate to="/app/doctor" replace />
  if (role === 'patient') return <DashboardPage />

  return (
    <main id="main-content" className="centered-state">
      <p className="eyebrow">Role-specific workspace</p>
      <h1>The {role} experience is not part of this milestone yet.</h1>
      <p>
        Your signed role was recognized. A later workflow will connect this demo account to
        its own task-focused workspace.
      </p>
    </main>
  )
}

function AuthCallbackPage() {
  const { error, isAuthenticated, loginWithRedirect } = useAuth0()

  if (isAuthenticated) {
    return <Navigate to="/app" replace />
  }

  return (
    <main id="main-content" className="centered-state">
      <p className="eyebrow">Secure sign in</p>
      <h1>Sign in was not completed.</h1>
      <p>{error?.message ?? 'The login response could not be connected to a patient session.'}</p>
      <button
        className="primary-button"
        type="button"
        onClick={() => loginWithRedirect({ appState: { returnTo: '/app' } })}
      >
        Try signing in again
      </button>
    </main>
  )
}

function formatDateTime(value: string): { date: string; time: string } {
  const date = new Date(value)
  return {
    date: new Intl.DateTimeFormat('en-US', {
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

function ErrorNotice({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const traceId = error instanceof ApiProblemError ? error.traceId : undefined
  const message = error instanceof Error ? error.message : 'The request could not be completed.'

  return (
    <div className="error-notice" role="alert">
      <strong>We could not update the care itinerary.</strong>
      <p>{message}</p>
      {traceId && <small>Support reference: {traceId}</small>}
      {onRetry && (
        <button className="secondary-button" type="button" onClick={onRetry}>
          Try again
        </button>
      )}
    </div>
  )
}

function AppointmentCard({
  appointment,
  onCancel,
}: {
  appointment: Appointment
  onCancel?: (appointment: Appointment) => void
}) {
  const when = formatDateTime(appointment.startsAtUtc)

  return (
    <article className={`appointment-card appointment-card--${appointment.status.toLowerCase()}`}>
      <div className="appointment-card__time">
        <time dateTime={appointment.startsAtUtc}>
          <strong>{when.date}</strong>
          <span>{when.time}</span>
        </time>
      </div>
      <div className="appointment-card__body">
        <div>
          <span className={`status-pill status-pill--${appointment.status.toLowerCase()}`}>
            {appointment.status}
          </span>
          <h3>{appointment.clinicianDisplayName}</h3>
          <p>{appointment.clinicianSpecialty}</p>
        </div>
        <p className="appointment-reason">{appointment.reason}</p>
      </div>
      {onCancel && appointment.status === 'Scheduled' && new Date(appointment.startsAtUtc) > new Date() && (
        <button className="text-button" type="button" onClick={() => onCancel(appointment)}>
          Cancel visit
        </button>
      )}
    </article>
  )
}

function DashboardPage() {
  const { getAccessTokenSilently } = useAuth0()
  const location = useLocation()
  const [identity, setIdentity] = useState<Identity | null>(null)
  const [appointments, setAppointments] = useState<Appointment[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<unknown>(null)
  const [refreshKey, setRefreshKey] = useState(0)
  const [cancelling, setCancelling] = useState<Appointment | null>(null)
  const [cancellationReason, setCancellationReason] = useState('')
  const [isSaving, setIsSaving] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    setIsLoading(true)
    setError(null)

    getAccessTokenSilently()
      .then((token) =>
        Promise.all([
          getIdentity(token, controller.signal),
          getAppointments(token, controller.signal),
        ]),
      )
      .then(([currentIdentity, appointmentPage]) => {
        setIdentity(currentIdentity)
        setAppointments(appointmentPage.items)
      })
      .catch((requestError: unknown) => {
        if (!controller.signal.aborted) setError(requestError)
      })
      .finally(() => {
        if (!controller.signal.aborted) setIsLoading(false)
      })

    return () => controller.abort()
  }, [getAccessTokenSilently, refreshKey])

  const upcoming = appointments.filter(
    (appointment) =>
      appointment.status === 'Scheduled' && new Date(appointment.startsAtUtc) > new Date(),
  )
  const history = appointments.filter((appointment) => !upcoming.includes(appointment))
  const nextAppointment = upcoming[0]

  async function confirmCancellation() {
    if (!cancelling) return

    setIsSaving(true)
    setError(null)
    try {
      const token = await getAccessTokenSilently()
      const cancelled = await cancelAppointment(token, cancelling, cancellationReason)
      setAppointments((current) =>
        current.map((appointment) => (appointment.id === cancelled.id ? cancelled : appointment)),
      )
      setCancelling(null)
      setCancellationReason('')
    } catch (requestError) {
      setError(requestError)
      if (requestError instanceof ApiProblemError && requestError.status === 409) {
        setCancelling(null)
        setCancellationReason('')
        setRefreshKey((key) => key + 1)
      }
    } finally {
      setIsSaving(false)
    }
  }

  if (isLoading) return <LoadingScreen message="Building your care itinerary" />

  return (
    <main id="main-content" className="workspace">
      <section className="workspace-intro">
        <div>
          <p className="eyebrow">Patient workspace</p>
          <h1>Good day, {identity?.displayName.split(' ')[0] ?? 'patient'}.</h1>
          <p>Here is what is next in your fictional care journey.</p>
        </div>
        <Link className="primary-button" to="/app/appointments/new">
          Book a visit
        </Link>
      </section>

      {typeof location.state === 'object' && location.state && 'notice' in location.state && (
        <output className="success-notice">
          {String(location.state.notice)}
        </output>
      )}
      {error !== null && (
        <ErrorNotice error={error} onRetry={() => setRefreshKey((key) => key + 1)} />
      )}

      <section className="next-visit" aria-labelledby="next-visit-title">
        <div className="section-heading">
          <div>
            <p className="eyebrow">Next visit</p>
            <h2 id="next-visit-title">Your next handoff</h2>
          </div>
          <span className="timezone-note">Times shown in your local timezone</span>
        </div>
        {nextAppointment ? (
          <AppointmentCard appointment={nextAppointment} onCancel={setCancelling} />
        ) : (
          <div className="empty-state">
            <span aria-hidden="true">○</span>
            <h3>No upcoming visit</h3>
            <p>Choose a clinician and a time that works for you.</p>
            <Link className="secondary-button" to="/app/appointments/new">
              Find a time
            </Link>
          </div>
        )}
      </section>

      <section className="itinerary" aria-labelledby="itinerary-title">
        <div className="section-heading">
          <div>
            <p className="eyebrow">Care itinerary</p>
            <h2 id="itinerary-title">Appointments</h2>
          </div>
          <span>{appointments.length} total</span>
        </div>
        <div className="itinerary__track">
          {appointments.length === 0 ? (
            <p className="muted-copy">Your booked and completed visits will appear here.</p>
          ) : (
            <>
              {upcoming.slice(1).map((appointment) => (
                <AppointmentCard
                  key={appointment.id}
                  appointment={appointment}
                  onCancel={setCancelling}
                />
              ))}
              {history.map((appointment) => (
                <AppointmentCard key={appointment.id} appointment={appointment} />
              ))}
            </>
          )}
        </div>
      </section>

      {cancelling && (
        <section className="action-panel" aria-labelledby="cancel-title">
          <div>
            <p className="eyebrow">Confirm change</p>
            <h2 id="cancel-title">Cancel this visit?</h2>
            <p>
              {formatDateTime(cancelling.startsAtUtc).date} with{' '}
              {cancelling.clinicianDisplayName}
            </p>
          </div>
          <label>
            Reason <span>(optional)</span>
            <textarea
              value={cancellationReason}
              maxLength={500}
              onChange={(event) => setCancellationReason(event.target.value)}
            />
          </label>
          <div className="button-row">
            <button className="danger-button" type="button" disabled={isSaving} onClick={confirmCancellation}>
              {isSaving ? 'Cancelling…' : 'Cancel visit'}
            </button>
            <button className="text-button" type="button" onClick={() => setCancelling(null)}>
              Keep visit
            </button>
          </div>
        </section>
      )}
    </main>
  )
}

function BookingPage() {
  const { getAccessTokenSilently } = useAuth0()
  const navigate = useNavigate()
  const [clinicians, setClinicians] = useState<Clinician[]>([])
  const [selectedClinician, setSelectedClinician] = useState<Clinician | null>(null)
  const [availability, setAvailability] = useState<Availability[]>([])
  const [selectedTime, setSelectedTime] = useState<Availability | null>(null)
  const [reason, setReason] = useState('')
  const [isLoading, setIsLoading] = useState(true)
  const [isSaving, setIsSaving] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const availabilityRequest = useRef<AbortController | null>(null)

  const loadClinicians = useCallback(() => {
    const controller = new AbortController()
    setIsLoading(true)
    setError(null)
    getAccessTokenSilently()
      .then((token) => getClinicians(token, controller.signal))
      .then(setClinicians)
      .catch((requestError: unknown) => {
        if (!controller.signal.aborted) setError(requestError)
      })
      .finally(() => {
        if (!controller.signal.aborted) setIsLoading(false)
      })
    return () => controller.abort()
  }, [getAccessTokenSilently])

  useEffect(loadClinicians, [loadClinicians])

  useEffect(
    () => () => {
      availabilityRequest.current?.abort()
    },
    [],
  )

  const selectedWhen = useMemo(
    () => (selectedTime ? formatDateTime(selectedTime.startsAtUtc) : null),
    [selectedTime],
  )

  async function chooseClinician(clinician: Clinician) {
    availabilityRequest.current?.abort()
    const controller = new AbortController()
    availabilityRequest.current = controller
    setSelectedClinician(clinician)
    setSelectedTime(null)
    setAvailability([])
    setIsLoading(true)
    setError(null)

    try {
      const token = await getAccessTokenSilently()
      const from = new Date()
      const to = new Date(from.getTime() + 31 * 24 * 60 * 60 * 1000)
      const openTimes = await getAvailability(
        token,
        clinician.id,
        from,
        to,
        controller.signal,
      )
      if (availabilityRequest.current === controller) {
        setAvailability(openTimes)
      }
    } catch (requestError) {
      if (!controller.signal.aborted) {
        setError(requestError)
      }
    } finally {
      if (availabilityRequest.current === controller) {
        availabilityRequest.current = null
        setIsLoading(false)
      }
    }
  }

  async function submitBooking(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!selectedTime || !reason.trim()) return

    setIsSaving(true)
    setError(null)
    try {
      const token = await getAccessTokenSilently()
      await bookAppointment(token, selectedTime, reason)
      navigate('/app', {
        replace: true,
        state: { notice: 'Visit booked. Your care itinerary has been updated.' },
      })
    } catch (requestError) {
      setError(requestError)
      if (requestError instanceof ApiProblemError && requestError.status === 409 && selectedClinician) {
        await chooseClinician(selectedClinician)
      }
    } finally {
      setIsSaving(false)
    }
  }

  return (
    <main id="main-content" className="booking-page">
      <div className="booking-heading">
        <Link className="back-link" to="/app">
          ← Care itinerary
        </Link>
        <p className="eyebrow">Book a visit</p>
        <h1>Choose the care, then the time.</h1>
        <p>Three short steps. All appointment times are displayed in your local timezone.</p>
      </div>

      {error !== null && <ErrorNotice error={error} onRetry={loadClinicians} />}

      <form className="booking-flow" onSubmit={submitBooking}>
        <fieldset className="booking-step">
          <legend>
            <span>1</span>
            Choose a clinician
          </legend>
          <div className="choice-grid">
            {clinicians.map((clinician) => (
              <label
                key={clinician.id}
                className={selectedClinician?.id === clinician.id ? 'choice-card is-selected' : 'choice-card'}
              >
                <input
                  type="radio"
                  name="clinician"
                  checked={selectedClinician?.id === clinician.id}
                  onChange={() => chooseClinician(clinician)}
                />
                <strong>{clinician.displayName}</strong>
                <span>{clinician.specialty}</span>
              </label>
            ))}
          </div>
        </fieldset>

        <fieldset className="booking-step" disabled={!selectedClinician || isLoading}>
          <legend>
            <span>2</span>
            Choose a time
          </legend>
          {isLoading && selectedClinician ? (
            <p className="muted-copy" aria-live="polite">Finding open times…</p>
          ) : availability.length > 0 ? (
            <div className="time-grid">
              {availability.map((slot) => {
                const when = formatDateTime(slot.startsAtUtc)
                return (
                  <label key={slot.id} className={selectedTime?.id === slot.id ? 'time-card is-selected' : 'time-card'}>
                    <input
                      type="radio"
                      name="time"
                      checked={selectedTime?.id === slot.id}
                      onChange={() => setSelectedTime(slot)}
                    />
                    <strong>{when.date}</strong>
                    <span>{when.time}</span>
                  </label>
                )
              })}
            </div>
          ) : selectedClinician ? (
            <div className="empty-state compact">
              <h3>No open times in the next 31 days</h3>
              <p>Choose another clinician to continue.</p>
            </div>
          ) : (
            <p className="muted-copy">Choose a clinician to see their available times.</p>
          )}
        </fieldset>

        <fieldset className="booking-step" disabled={!selectedTime}>
          <legend>
            <span>3</span>
            Tell us the reason
          </legend>
          <label className="reason-field">
            What would you like to discuss?
            <textarea
              required
              minLength={1}
              maxLength={500}
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              placeholder="Example: Annual wellness visit"
            />
            <small>{reason.length}/500 characters</small>
          </label>
        </fieldset>

        <aside className="booking-summary" aria-live="polite">
          <div>
            <p className="eyebrow">Your selection</p>
            <strong>{selectedClinician?.displayName ?? 'Choose a clinician'}</strong>
            <span>
              {selectedWhen ? `${selectedWhen.date} · ${selectedWhen.time}` : 'Choose an available time'}
            </span>
          </div>
          <button
            className="primary-button"
            type="submit"
            disabled={!selectedTime || !reason.trim() || isSaving}
          >
            {isSaving ? 'Booking visit…' : 'Book this visit'}
          </button>
        </aside>
      </form>
    </main>
  )
}

function App() {
  const { isLoading } = useAuth0()

  if (isLoading) return <LoadingScreen message="Opening Harbor Care" />

  return (
    <NavigationGuardProvider>
      <div className="app-shell">
      <a className="skip-link" href="#main-content">
        Skip to main content
      </a>
      <AppHeader />
      <Routes>
        <Route path="/" element={<LandingPage />} />
        <Route path="/auth/callback" element={<AuthCallbackPage />} />
        <Route
          path="/app"
          element={
            <ProtectedRoute>
              <WorkspaceIndexPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/app/doctor"
          element={
            <ProtectedRoute>
              <DoctorWorklistPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/app/doctor/consultations/:consultationId"
          element={
            <ProtectedRoute>
              <ConsultationPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/app/appointments/new"
          element={
            <ProtectedRoute>
              <BookingPage />
            </ProtectedRoute>
          }
        />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
      <footer className="site-footer">
        <p>Educational portfolio demonstration · Synthetic data only · Not for clinical use</p>
      </footer>
      </div>
    </NavigationGuardProvider>
  )
}

export default App
