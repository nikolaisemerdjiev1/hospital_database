import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { LandingPage } from './LandingPage'

const auth = vi.hoisted(() => ({
  isAuthenticated: false,
  isLoading: false,
  loginWithRedirect: vi.fn<(options?: unknown) => Promise<void>>(),
}))
vi.mock('@auth0/auth0-react', () => ({ useAuth0: () => auth }))

const fetchMock = vi.fn<typeof fetch>()
function renderLanding() { return render(<MemoryRouter><LandingPage /></MemoryRouter>) }

beforeEach(() => {
  auth.isAuthenticated = false
  auth.isLoading = false
  auth.loginWithRedirect.mockReset().mockResolvedValue(undefined)
  fetchMock.mockReset().mockImplementation(async () => new Response('Healthy'))
  vi.stubGlobal('fetch', fetchMock)
  vi.stubEnv('VITE_DEMO_PASSWORD', '')
})
afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
  vi.unstubAllEnvs()
})

describe('recruiter landing page', () => {
  it('moves keyboard focus to the role heading when following the section link', async () => {
    const user = userEvent.setup()
    renderLanding()
    const section = screen.getByRole('region', { name: 'Choose a part in the care journey.' })
    section.scrollIntoView = vi.fn<Element['scrollIntoView']>()
    await user.tab()
    expect(screen.getByRole('link', { name: 'Choose your role' })).toHaveFocus()
    await user.keyboard('{Enter}')
    expect(screen.getByRole('heading', { name: 'Choose a part in the care journey.' })).toHaveFocus()
    expect(section.scrollIntoView).toHaveBeenCalledWith({ block: 'start' })
  })

  it('shows useful shared-demo guidance while readiness is pending', () => {
    fetchMock.mockImplementation((_url, init) => new Promise((_resolve, reject) => {
      init?.signal?.addEventListener('abort', () => reject(init.signal?.reason))
    }))
    renderLanding()
    expect(screen.getByRole('heading', { level: 1, name: /care moves better/i })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Choose your role' })).toHaveAttribute('href', '/#demo-roles')
    expect(screen.getByText(/another visitor may change/i)).toBeInTheDocument()
    expect(screen.getByText(/never enter personal/i)).toBeInTheDocument()
    expect(screen.getByText(/password is not published here yet/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Sign in as patient' })).toBeDisabled()
  })

  it.each(['patient', 'doctor', 'pharmacist'])('sends only a login hint and fresh login request for %s', async (role) => {
    const user = userEvent.setup()
    renderLanding()
    await waitFor(() => expect(screen.getByRole('button', { name: `Sign in as ${role}` })).toBeEnabled())
    await user.click(screen.getByRole('button', { name: `Sign in as ${role}` }))
    expect(auth.loginWithRedirect).toHaveBeenCalledExactlyOnceWith({
      appState: { returnTo: '/app' },
      authorizationParams: { login_hint: `care.relay.demo+${role}@gmail.com`, prompt: 'login' },
    })
  })

  it('lets an existing user reopen their workspace or sign into another account', async () => {
    auth.isAuthenticated = true
    const user = userEvent.setup()
    renderLanding()
    expect(screen.getByRole('link', { name: 'Open your current workspace' })).toHaveAttribute('href', '/app')
    await waitFor(() => expect(screen.getByRole('button', { name: 'Sign in as doctor' })).toBeEnabled())
    await user.click(screen.getByRole('button', { name: 'Sign in as doctor' }))
    expect(auth.loginWithRedirect).toHaveBeenCalledWith(expect.objectContaining({
      authorizationParams: { login_hint: 'care.relay.demo+doctor@gmail.com', prompt: 'login' },
    }))
  })

  it('shows a safe sign-in error and allows another attempt', async () => {
    auth.loginWithRedirect.mockRejectedValueOnce(new Error('Private provider diagnostic'))
    const user = userEvent.setup()
    renderLanding()
    const button = screen.getByRole('button', { name: 'Sign in as patient' })
    await waitFor(() => expect(button).toBeEnabled())
    await user.click(button)
    expect(screen.getByRole('alert')).toHaveTextContent('Sign-in could not start.')
    expect(screen.queryByText('Private provider diagnostic')).not.toBeInTheDocument()
    await user.click(button)
    expect(auth.loginWithRedirect).toHaveBeenCalledTimes(2)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('prevents duplicate login redirects while the first is pending', async () => {
    let finish!: () => void
    auth.loginWithRedirect.mockImplementation(() => new Promise((resolve) => { finish = resolve }))
    const user = userEvent.setup()
    renderLanding()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Sign in as patient' })).toBeEnabled())
    await user.click(screen.getByRole('button', { name: 'Sign in as patient' }))
    expect(screen.getByRole('button', { name: 'Sign in as doctor' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Opening secure sign-in…' })).toBeDisabled()
    expect(auth.loginWithRedirect).toHaveBeenCalledTimes(1)
    await act(async () => finish())
  })

  it('supports keyboard reveal and copying of an explicitly configured public test value', async () => {
    const publicTestValue = 'test-only-demo-value'
    vi.stubEnv('VITE_DEMO_PASSWORD', publicTestValue)
    const user = userEvent.setup()
    renderLanding()
    const reveal = screen.getByRole('button', { name: 'Show demo password' })
    expect(reveal).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByText(publicTestValue)).not.toBeInTheDocument()
    reveal.focus()
    await user.keyboard('{Enter}')
    expect(screen.getByText(publicTestValue)).toBeVisible()
    expect(reveal).toHaveAttribute('aria-expanded', 'true')
    await user.click(screen.getByRole('button', { name: 'Copy demo password' }))
    expect(await navigator.clipboard.readText()).toBe(publicTestValue)
    expect(screen.getByText('Demo password copied.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Hide demo password' }))
    expect(screen.queryByText(publicTestValue)).not.toBeInTheDocument()
  })

  it('provides a manual copy fallback when clipboard access fails', async () => {
    vi.stubEnv('VITE_DEMO_PASSWORD', 'test-only-demo-value')
    const user = userEvent.setup()
    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValueOnce(new Error('Clipboard blocked'))
    renderLanding()
    await user.click(screen.getByRole('button', { name: 'Copy demo password' }))
    expect(screen.getByText(/copy is unavailable/i)).toBeInTheDocument()
    expect(screen.getByText('test-only-demo-value')).toBeVisible()
  })

  it('stops automatic checks, retains the page, and recovers after an explicit retry', async () => {
    vi.useFakeTimers()
    fetchMock.mockImplementation(async () => new Response('Unhealthy', { status: 503 }))
    renderLanding()
    await act(async () => { await vi.advanceTimersByTimeAsync(14_000) })
    expect(screen.getByText(/automatic checks have stopped/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Sign in as patient' })).toBeDisabled()
    expect(fetchMock).toHaveBeenCalledTimes(4)
    fetchMock.mockImplementation(async () => new Response('Healthy'))
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }))
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    expect(screen.getByText('The demo is ready.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Sign in as patient' })).toBeEnabled()
    expect(fetchMock).toHaveBeenCalledTimes(5)
  })

  it('keeps retry disabled for the server-requested cooldown', async () => {
    vi.useFakeTimers()
    fetchMock.mockImplementation(async () => new Response('Unavailable', { status: 429, headers: { 'Retry-After': '30' } }))
    renderLanding()
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    expect(screen.getByRole('button', { name: 'Try again' })).toBeDisabled()
    await act(async () => { await vi.advanceTimersByTimeAsync(29_999) })
    expect(screen.getByRole('button', { name: 'Try again' })).toBeDisabled()
    await act(async () => { await vi.advanceTimersByTimeAsync(1) })
    expect(screen.getByRole('button', { name: 'Try again' })).toBeEnabled()
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('aborts the active readiness request when the landing page unmounts', () => {
    fetchMock.mockImplementation((_url, init) => new Promise((_resolve, reject) => {
      init?.signal?.addEventListener('abort', () => reject(init.signal?.reason))
    }))
    const { unmount } = renderLanding()
    const signal = fetchMock.mock.calls[0][1]?.signal
    unmount()
    expect(signal?.aborted).toBe(true)
  })
})
