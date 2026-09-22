import type { Auth0ProviderOptions } from '@auth0/auth0-react'
import { act, render } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'

import { AuthProvider } from './AuthProvider'

const sdk = vi.hoisted(() => ({ options: null as Auth0ProviderOptions | null }))

vi.mock('@auth0/auth0-react', () => ({
  useAuth0: () => ({ isLoading: true, isAuthenticated: false }),
  Auth0Provider: (options: Auth0ProviderOptions) => {
    sdk.options = options
    return options.children
  },
}))

describe('AuthProvider redirect boundary', () => {
  it.each([
    { appState: { returnTo: '/app/pharmacy' }, destination: '/app/pharmacy' },
    { appState: undefined, destination: '/app' },
  ])('restores $destination after SDK authentication', async ({ appState, destination }) => {
    const router = createMemoryRouter(
      [{ path: '*', element: <AuthProvider><p>Requested workspace</p></AuthProvider> }],
      { initialEntries: ['/auth/callback'] },
    )
    render(<RouterProvider router={router} />)

    // Recovery must not introduce persistent browser token storage.
    expect(sdk.options).toMatchObject({
      useRefreshTokens: true,
      useRefreshTokensFallback: true,
      cacheLocation: 'memory',
    })
    const onRedirect = sdk.options?.onRedirectCallback
    expect(onRedirect).toBeTypeOf('function')
    await act(async () => { onRedirect?.(appState) })
    expect(router.state.location.pathname).toBe(destination)
    expect(router.state.historyAction).toBe('REPLACE')
  })
})
