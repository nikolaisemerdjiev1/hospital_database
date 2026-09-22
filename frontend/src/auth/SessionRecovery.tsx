import { useAuth0 } from '@auth0/auth0-react'
import { useEffect, useRef, useState, type PropsWithChildren } from 'react'
import { useLocation } from 'react-router-dom'

const recoveryKey = 'harbor-care:session-recovery'

// This tab-local hint can request authentication; it never proves identity or grants access.
export function rememberSession() {
  try { sessionStorage.setItem(recoveryKey, '1') } catch { /* Interactive sign-in remains available. */ }
}

export function forgetSession() {
  try { sessionStorage.removeItem(recoveryKey) } catch { /* No storage means no automatic recovery. */ }
}

function consumeSessionHint(): boolean {
  try {
    const remembered = sessionStorage.getItem(recoveryKey) === '1'
    sessionStorage.removeItem(recoveryKey)
    return remembered
  } catch {
    return false
  }
}

export function SessionRecovery({ children }: PropsWithChildren) {
  const { isAuthenticated, isLoading, loginWithRedirect } = useAuth0()
  const location = useLocation()
  const attempted = useRef(false)
  const [recovering, setRecovering] = useState(false)

  useEffect(() => {
    if (isLoading) return
    if (isAuthenticated) {
      rememberSession()
      return
    }
    const protectedPath = location.pathname === '/app' || location.pathname.startsWith('/app/')
    if (!protectedPath || attempted.current || !consumeSessionHint()) return

    attempted.current = true
    setRecovering(true)
    // A top-level request can use Auth0's first-party session when iframe recovery fails.
    // Consume the hint before leaving to prevent retry loops on error or cancellation.
    void loginWithRedirect({
      appState: { returnTo: `${location.pathname}${location.search}${location.hash}` },
    }).catch(() => setRecovering(false))
  }, [isAuthenticated, isLoading, loginWithRedirect, location.pathname, location.search, location.hash])

  if (recovering && !isAuthenticated) {
    return (
      <main id="main-content" className="centered-state" aria-busy="true">
        <h1>Restoring your secure session</h1>
        <output>Returning through secure sign-in to reopen your workspace.</output>
      </main>
    )
  }

  return children
}
