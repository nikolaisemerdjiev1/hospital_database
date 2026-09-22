import { useAuth0 } from '@auth0/auth0-react'
import { useRef, useState } from 'react'
import { Link } from 'react-router-dom'

import { readinessAttempts } from './readiness'
import { useDemoReadiness } from './useDemoReadiness'
import './LandingPage.css'

const accounts = [
  { role: 'patient', name: 'Patient', person: 'Avery Brooks', email: 'care.relay.demo+patient@gmail.com',
    title: 'See care from the patient side.', description: 'Book a visit, follow your appointments, and see when a prescription is ready for pickup.', action: 'Book a visit' },
  { role: 'doctor', name: 'Doctor', person: 'Dr. Maya Chen', email: 'care.relay.demo+doctor@gmail.com',
    title: 'Turn a visit into a care plan.', description: 'Open the care queue, complete a consultation, search medications, and issue a prescription.', action: 'Complete a consultation' },
  { role: 'pharmacist', name: 'Pharmacist', person: 'Alex Morgan, PharmD', email: 'care.relay.demo+pharmacist@gmail.com',
    title: 'Carry the plan through to pickup.', description: 'Claim a prescription, review its details, then prepare and dispense it with a recorded handoff.', action: 'Prepare a prescription' },
] as const

export function LandingPage() {
  const { isAuthenticated, isLoading, loginWithRedirect } = useAuth0()
  const readiness = useDemoReadiness()
  const [signingIn, setSigningIn] = useState<string | null>(null)
  const loginPending = useRef(false)
  const [loginError, setLoginError] = useState(false)
  const [showPassword, setShowPassword] = useState(false)
  const [copyMessage, setCopyMessage] = useState('')
  const password: string = import.meta.env.VITE_DEMO_PASSWORD ?? ''

  function focusSection(sectionId: string, headingId: string) {
    document.getElementById(sectionId)?.scrollIntoView({ block: 'start' })
    document.getElementById(headingId)?.focus({ preventScroll: true })
  }

  async function signIn(account: typeof accounts[number]) {
    if (loginPending.current || isLoading || readiness.status !== 'ready') return
    loginPending.current = true
    setSigningIn(account.role)
    setLoginError(false)
    try {
      await loginWithRedirect({
        appState: { returnTo: '/app' },
        authorizationParams: { login_hint: account.email, prompt: 'login' },
      })
    } catch {
      setLoginError(true)
    } finally {
      loginPending.current = false
      setSigningIn(null)
    }
  }

  async function copyPassword() {
    try {
      await navigator.clipboard.writeText(password)
      setCopyMessage('Demo password copied.')
    } catch {
      setShowPassword(true)
      setCopyMessage('Copy is unavailable. Select the displayed demo password and copy it manually.')
    }
  }

  return (
    <main id="main-content" className="landing-page demo-landing">
      <section className="hero" aria-labelledby="page-title">
        <div className="hero__copy">
          <p className="eyebrow">A coordinated-care portfolio project</p>
          <h1 id="page-title">Care moves better when the <em>next step</em> is clear.</h1>
          <p className="hero__summary">
            Follow one fictional care journey from the first appointment to prescription pickup.
            Step into three connected workspaces and see how each handoff reaches the next person.
          </p>
          <div className="hero__actions">
            <Link className="primary-button" to="#demo-roles"
              onClick={() => focusSection('demo-roles', 'demo-roles-title')}>Choose your role</Link>
            <Link className="secondary-link" to="#care-relay"
              onClick={() => focusSection('care-relay', 'relay-title')}>See how care moves</Link>
          </div>
          <p className="synthetic-note">Every person and health detail shown here is fictional. Not for clinical use.</p>
        </div>
        <aside className="hero__artifact" aria-label="An example care journey">
          <div className="artifact-ticket">
            <p className="eyebrow">One connected journey</p>
            <h2>Different roles.<br />Shared next steps.</h2>
            <ol className="demo-handoff">
              {accounts.map((account) => (
                <li key={account.role}>
                  <span className="eyebrow">{account.name}</span>
                  <strong>{account.action}</strong>
                  <span>{account.person}</span>
                </li>
              ))}
            </ol>
            <p className="demo-handoff-return">Back to the patient: ready for pickup.</p>
          </div>
          <span className="artifact-caption">A fictional example of the workflow you can explore.</span>
        </aside>
      </section>

      <section id="demo-roles" className="demo-entry" aria-labelledby="demo-roles-title">
        <div className="demo-section-heading">
          <div>
            <p className="eyebrow">Explore the shared demo</p>
            <h2 id="demo-roles-title" tabIndex={-1}>Choose a part in the care journey.</h2>
          </div>
          <p>Start as a patient, then follow the handoff as a doctor or pharmacist.</p>
        </div>

        <div className={`demo-readiness demo-readiness--${readiness.status}`}>
          <output id="demo-access-status" aria-live="polite" aria-atomic="true">
            <strong>{readiness.status === 'ready' ? 'The demo is ready.'
              : readiness.status === 'checking' ? 'Waking the demo workspace…' : 'The demo is taking longer than expected.'}</strong>
            <span>{readiness.status === 'ready' ? 'Choose an account below to sign in.'
              : readiness.status === 'checking'
                ? `The service may be asleep between visits. Check ${readiness.attempt} of ${readinessAttempts}; you can explore this page while it starts.`
                : readiness.coolingDown ? 'The service asked us to wait. Retry will become available shortly.'
                  : 'Automatic checks have stopped. Try again shortly; the page and account guidance remain available.'}</span>
          </output>
          <button className="secondary-button" type="button" onClick={readiness.retry}
            disabled={readiness.status === 'checking' || readiness.coolingDown}>
            {readiness.status === 'checking' ? 'Checking availability…'
              : readiness.status === 'ready' ? 'Check again' : 'Try again'}
          </button>
        </div>

        {isAuthenticated && <p className="demo-session-note">
          You are signed in. <Link to="/app">Open your current workspace</Link>, or choose a card to sign in with another account.
        </p>}
        {isLoading && <output>Checking your existing sign-in. You can explore the demo below.</output>}
        {loginError && <div className="error-notice" role="alert">
          Sign-in could not start. Choose your role again to retry.
        </div>}

        <div className="demo-role-grid">
          {accounts.map((account) => (
            <article className="demo-role-card" key={account.role} aria-labelledby={`demo-${account.role}`}>
              <p className="eyebrow">{account.name} workspace</p>
              <h3 id={`demo-${account.role}`}>{account.title}</h3>
              <p>{account.description}</p>
              <div className="demo-account">
                <span>Demo account email</span>
                <code>{account.email}</code>
              </div>
              <button className="primary-button" type="button" aria-describedby="demo-access-status demo-login-guidance"
                disabled={readiness.status !== 'ready' || Boolean(signingIn) || isLoading}
                onClick={() => void signIn(account)}>
                {signingIn === account.role ? 'Opening secure sign-in…' : `Sign in as ${account.role}`}
              </button>
            </article>
          ))}
        </div>

        <div className="demo-password-panel">
          <div>
            <h3>One public password. Three demo accounts.</h3>
            <p id="demo-login-guidance">Use the selected email and the shared demo password in the sign-in form.
              Choose email and password rather than a social sign-in button.</p>
            <p>This password is intentionally public and only for these fictional accounts.</p>
          </div>
          {password ? <div className="demo-password-controls">
            <button className="secondary-button" type="button" aria-expanded={showPassword}
              aria-controls="demo-password" onClick={() => setShowPassword((shown) => !shown)}>
              {showPassword ? 'Hide demo password' : 'Show demo password'}
            </button>
            <button className="secondary-button" type="button" onClick={() => void copyPassword()}>Copy demo password</button>
            <div id="demo-password" hidden={!showPassword}><code>{showPassword ? password : ''}</code></div>
            <output className="demo-copy-feedback">{copyMessage}</output>
          </div> : <p className="demo-password-missing">
            The demo password is not published here yet. Use the project-only password supplied by the demo owner.
          </p>}
        </div>
      </section>

      <section id="care-relay" className="relay-section" aria-labelledby="relay-title">
        <div>
          <p className="eyebrow">The care relay</p>
          <h2 id="relay-title" tabIndex={-1}>Context travels forward. Each person sees their part.</h2>
        </div>
        <ol className="demo-relay-steps">
          <li><strong>Book an appointment</strong><span>The patient chooses a clinician and an available time.</span></li>
          <li><strong>Complete the consultation</strong><span>The doctor records the visit and searches RxNorm to issue a prescription.</span></li>
          <li><strong>Prepare and dispense</strong><span>The pharmacist claims the handoff and updates its fulfillment.</span></li>
          <li><strong>Follow the pickup status</strong><span>The patient sees a clear, appropriate update.</span></li>
        </ol>
      </section>

      <section className="demo-details" aria-labelledby="demo-details-title">
        <div>
          <p className="eyebrow">Before you explore</p>
          <h2 id="demo-details-title">Shared accounts. Fictional care.</h2>
          <p>Visitors share the same records. Another visitor may change an appointment or prescription while you explore.
            Demo records can be reset, so changes are temporary. Never enter personal or real patient information.</p>
          <p>This portfolio simulation is not a clinical system and makes no healthcare compliance claim.
            Availability is best effort; the service may need time to wake up.</p>
        </div>
        <div className="demo-engineering">
          <p className="eyebrow">Behind the handoff</p>
          <h3>A complete full-stack journey.</h3>
          <p>React &amp; TypeScript · ASP.NET Core · PostgreSQL · Auth0 · RxNorm</p>
          <p>Role-based access, transactional workflows, and an audit trail connect the three workspaces.</p>
          <a className="secondary-link" href="https://github.com/nikolaisemerdjiev1/hospital_database">Explore the source and architecture</a>
        </div>
      </section>
    </main>
  )
}
