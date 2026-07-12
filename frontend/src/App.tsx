import { useEffect, useState } from 'react'

import { apiBaseUrl, getSystemStatus, type SystemStatus } from './api/system'
import './App.css'

const relayStages = [
  {
    number: '01',
    title: 'Appointment',
    description: 'A patient chooses an available visit.',
  },
  {
    number: '02',
    title: 'Consultation',
    description: 'A doctor records the care plan.',
  },
  {
    number: '03',
    title: 'Prescription',
    description: 'Medication is matched through RxNorm.',
  },
  {
    number: '04',
    title: 'Pharmacy',
    description: 'A pharmacist prepares the order.',
  },
] as const

type ApiState =
  | { kind: 'loading' }
  | { kind: 'online'; status: SystemStatus }
  | { kind: 'offline' }

function formatTimestamp(timestamp: string): string {
  return new Intl.DateTimeFormat('en-US', {
    hour: 'numeric',
    minute: '2-digit',
    second: '2-digit',
  }).format(new Date(timestamp))
}

interface SystemStatusPanelProps {
  apiState: ApiState
  onRetry: () => void
}

function SystemStatusPanel({ apiState, onRetry }: SystemStatusPanelProps) {
  const isLoading = apiState.kind === 'loading'

  return (
    <aside className="status-card" aria-labelledby="system-status-title">
      <div className="status-card__heading">
        <div>
          <p className="eyebrow">Foundation check</p>
          <h2 id="system-status-title">System status</h2>
        </div>
        <span className={`status-light status-light--${apiState.kind}`} aria-hidden="true" />
      </div>

      <div className="status-card__body" aria-live="polite" aria-busy={isLoading}>
        {apiState.kind === 'loading' && (
          <div className="status-message">
            <p className="status-message__title">Checking API</p>
            <p>The interface is ready while the service responds.</p>
          </div>
        )}

        {apiState.kind === 'online' && (
          <>
            <div className="status-message">
              <p className="status-message__title">Foundation connected</p>
              <p>The React application and ASP.NET Core API can communicate.</p>
            </div>

            <dl className="status-details">
              <div>
                <dt>Service</dt>
                <dd>{apiState.status.service}</dd>
              </div>
              <div>
                <dt>Environment</dt>
                <dd>{apiState.status.environment}</dd>
              </div>
              <div>
                <dt>Checked</dt>
                <dd>
                  <time dateTime={apiState.status.timestamp}>
                    {formatTimestamp(apiState.status.timestamp)}
                  </time>
                </dd>
              </div>
            </dl>

            <a
              className="contract-link"
              href={`${apiBaseUrl}/openapi/v1.json`}
              target="_blank"
              rel="noreferrer"
            >
              View the API contract <span aria-hidden="true">↗</span>
            </a>
          </>
        )}

        {apiState.kind === 'offline' && (
          <div className="status-message status-message--offline">
            <p className="status-message__title">API not connected</p>
            <p>The web interface is ready, but the local API did not respond.</p>
            <button className="retry-button" type="button" onClick={onRetry}>
              Check again
            </button>
          </div>
        )}
      </div>
    </aside>
  )
}

function App() {
  const [apiState, setApiState] = useState<ApiState>({ kind: 'loading' })
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    let isActive = true
    const controller = new AbortController()
    const timeoutId = window.setTimeout(() => controller.abort(), 12_000)

    setApiState({ kind: 'loading' })

    getSystemStatus(controller.signal)
      .then((status) => {
        if (isActive) {
          setApiState({ kind: 'online', status })
        }
      })
      .catch(() => {
        if (isActive) {
          setApiState({ kind: 'offline' })
        }
      })
      .finally(() => window.clearTimeout(timeoutId))

    return () => {
      isActive = false
      window.clearTimeout(timeoutId)
      controller.abort()
    }
  }, [attempt])

  return (
    <div className="app-shell">
      <a className="skip-link" href="#main-content">
        Skip to main content
      </a>

      <header className="topbar">
        <div className="brand-lockup" aria-label="Hospital Coordination Platform">
          <span className="brand-mark" aria-hidden="true" />
          <span>Hospital Coordination</span>
        </div>
        <span className="simulation-label">Fictional care simulation</span>
      </header>

      <main id="main-content" className="foundation-layout" tabIndex={-1}>
        <section className="intro" aria-labelledby="page-title">
          <p className="eyebrow">Coordinated care platform</p>
          <h1 id="page-title">
            One care plan.
            <span>Four clear handoffs.</span>
          </h1>
          <p className="intro__summary">
            Follow a fictional care journey from appointment booking to pharmacy pickup, with each
            person seeing the next action that belongs to them.
          </p>
          <p className="synthetic-note">
            <span aria-hidden="true">◆</span>
            All people and health information in this portfolio project are synthetic.
          </p>
        </section>

        <SystemStatusPanel
          apiState={apiState}
          onRetry={() => setAttempt((currentAttempt) => currentAttempt + 1)}
        />

        <section className="care-relay" aria-labelledby="care-relay-title">
          <div className="care-relay__heading">
            <div>
              <p className="eyebrow">The care relay</p>
              <h2 id="care-relay-title">One shared journey, four focused workspaces</h2>
            </div>
            <p>Each handoff carries the context forward.</p>
          </div>

          <ol className="relay-track" aria-label="Coordinated care workflow">
            {relayStages.map((stage) => (
              <li key={stage.number} className="relay-stage">
                <span className="relay-stage__marker" aria-hidden="true">
                  {stage.number}
                </span>
                <h3>{stage.title}</h3>
                <p>{stage.description}</p>
              </li>
            ))}
          </ol>
        </section>
      </main>

      <footer className="page-footer">
        <p>Foundation milestone · Built for learning, demonstration, and synthetic data only.</p>
      </footer>
    </div>
  )
}

export default App
