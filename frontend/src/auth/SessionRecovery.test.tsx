import { StrictMode } from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { forgetSession, rememberSession, SessionRecovery } from './SessionRecovery'

const auth = vi.hoisted(() => ({
  isAuthenticated: false,
  isLoading: false,
  loginWithRedirect: vi.fn<() => Promise<void>>(),
}))
vi.mock('@auth0/auth0-react', () => ({ useAuth0: () => auth }))

function mount(path = '/app/doctor') {
  const router = createMemoryRouter([
    { path: '*', element: <SessionRecovery><p>Normal application</p></SessionRecovery> },
  ], { initialEntries: [path] })
  return render(<StrictMode><RouterProvider router={router} /></StrictMode>)
}

beforeEach(() => {
  sessionStorage.clear()
  auth.isAuthenticated = false
  auth.isLoading = false
  auth.loginWithRedirect.mockReset().mockResolvedValue(undefined)
})
afterEach(() => {
  vi.restoreAllMocks()
  sessionStorage.clear()
})

describe('tab session recovery', () => {
  it('requests authentication once after cache loss and preserves the complete route', () => {
    rememberSession()
    mount('/app/pharmacy?view=ready#orders')
    expect(auth.loginWithRedirect).toHaveBeenCalledExactlyOnceWith({
      appState: { returnTo: '/app/pharmacy?view=ready#orders' },
    })
    expect(screen.getByRole('heading', { name: 'Restoring your secure session' })).toBeInTheDocument()
    expect(screen.queryByText('Normal application')).not.toBeInTheDocument()
    expect(sessionStorage.length).toBe(0)
  })

  it('stops after a failed redirect and does not retry on remount', async () => {
    rememberSession()
    auth.loginWithRedirect.mockRejectedValue(new Error('Redirect unavailable'))
    const first = mount()
    await waitFor(() => expect(screen.getByText('Normal application')).toBeInTheDocument())
    first.unmount()
    mount()
    expect(auth.loginWithRedirect).toHaveBeenCalledTimes(1)
  })

  it.each(['/', '/auth/callback?error=login_required', '/application'])('does not redirect from %s', (path) => {
    rememberSession()
    mount(path)
    expect(auth.loginWithRedirect).not.toHaveBeenCalled()
  })

  it('leaves first-time visitors and signed-out users in the normal sign-in flow', () => {
    const first = mount()
    expect(auth.loginWithRedirect).not.toHaveBeenCalled()
    first.unmount()
    rememberSession()
    forgetSession()
    mount()
    expect(auth.loginWithRedirect).not.toHaveBeenCalled()
  })

  it('remembers SDK-authenticated sessions using only a non-secret flag', () => {
    auth.isAuthenticated = true
    mount()
    expect(sessionStorage.length).toBe(1)
    expect(sessionStorage.getItem('harbor-care:session-recovery')).toBe('1')
    expect(auth.loginWithRedirect).not.toHaveBeenCalled()
  })

  it('waits for SDK initialization before attempting recovery', () => {
    rememberSession()
    auth.isLoading = true
    mount()
    expect(auth.loginWithRedirect).not.toHaveBeenCalled()
    expect(sessionStorage.length).toBe(1)
  })

  it('falls back to normal sign-in if browser storage is unavailable', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('Storage blocked') })
    mount()
    expect(screen.getByText('Normal application')).toBeInTheDocument()
    expect(auth.loginWithRedirect).not.toHaveBeenCalled()
  })
})
