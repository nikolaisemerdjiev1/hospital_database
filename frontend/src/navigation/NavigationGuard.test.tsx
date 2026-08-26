import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import {
  createMemoryRouter,
  Link,
  RouterProvider,
  useLocation,
} from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { NavigationGuardProvider, useNavigationGuard } from './NavigationGuard'

const warning = 'Leave without saving your changes?'

function GuardedEditor() {
  const [isDirty, setIsDirty] = useState(false)
  const location = useLocation()
  useNavigationGuard(isDirty, warning)

  return (
    <main>
      <h1>{location.pathname}</h1>
      <button type="button" onClick={() => setIsDirty(true)}>Make changes</button>
      <button type="button" onClick={() => setIsDirty(false)}>Save changes</button>
      <Link to="/destination">Open destination</Link>
    </main>
  )
}

function renderGuard(initialEntries = ['/editor'], initialIndex = initialEntries.length - 1) {
  const router = createMemoryRouter(
    [
      {
        path: '*',
        element: (
          <NavigationGuardProvider>
            <GuardedEditor />
          </NavigationGuardProvider>
        ),
      },
    ],
    { initialEntries, initialIndex },
  )

  return { router, ...render(<RouterProvider router={router} />) }
}

afterEach(() => {
  vi.restoreAllMocks()
})

describe('NavigationGuard', () => {
  it('protects every internal link and releases navigation after saving', async () => {
    const user = userEvent.setup()
    const confirmNavigation = vi.spyOn(window, 'confirm').mockReturnValue(false)
    const { router } = renderGuard()

    await user.click(screen.getByRole('button', { name: 'Make changes' }))
    await user.click(screen.getByRole('link', { name: 'Open destination' }))

    expect(confirmNavigation).toHaveBeenCalledWith(warning)
    expect(router.state.location.pathname).toBe('/editor')

    await user.click(screen.getByRole('button', { name: 'Save changes' }))
    await user.click(screen.getByRole('link', { name: 'Open destination' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/destination'))
    expect(confirmNavigation).toHaveBeenCalledTimes(1)
  })

  it('guards browser history navigation and can continue the blocked transition', async () => {
    const user = userEvent.setup()
    const confirmNavigation = vi.spyOn(window, 'confirm').mockReturnValue(false)
    const { router } = renderGuard(['/previous', '/editor'], 1)

    await user.click(screen.getByRole('button', { name: 'Make changes' }))
    await act(async () => {
      await router.navigate(-1)
    })

    expect(confirmNavigation).toHaveBeenCalledWith(warning)
    expect(router.state.location.pathname).toBe('/editor')

    confirmNavigation.mockReturnValue(true)
    await act(async () => {
      await router.navigate(-1)
    })

    await waitFor(() => expect(router.state.location.pathname).toBe('/previous'))
  })

  it('warns on full-page exits only while changes are unsaved', async () => {
    const user = userEvent.setup()
    renderGuard()

    await user.click(screen.getByRole('button', { name: 'Make changes' }))
    const dirtyExit = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(dirtyExit)
    expect(dirtyExit.defaultPrevented).toBe(true)

    await user.click(screen.getByRole('button', { name: 'Save changes' }))
    const savedExit = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(savedExit)
    expect(savedExit.defaultPrevented).toBe(false)
  })
})
