import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import App from './App'

const auth = vi.hoisted(() => ({
  isAuthenticated: false,
  isLoading: false,
  loginWithRedirect: vi.fn<(options?: unknown) => Promise<void>>(),
  logout: vi.fn<(options?: unknown) => void>(),
  getAccessTokenSilently: vi.fn<() => Promise<string>>().mockResolvedValue('test-access-token'),
}))

vi.mock('@auth0/auth0-react', () => ({
  useAuth0: () => auth,
}))

function createJsonResponse(
  body: unknown,
  status = 200,
  contentType = 'application/json',
): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: {
      get: vi.fn<(name: string) => string>().mockReturnValue(contentType),
    },
    json: vi.fn<() => Promise<unknown>>().mockResolvedValue(body),
  } as unknown as Response
}

function createDeferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((complete) => {
    resolve = complete
  })
  return { promise, resolve }
}

function renderRoute(path = '/') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <App />
    </MemoryRouter>,
  )
}

beforeEach(() => {
  auth.isAuthenticated = false
  auth.isLoading = false
  auth.loginWithRedirect.mockReset()
  auth.logout.mockReset()
  auth.getAccessTokenSilently.mockReset().mockResolvedValue('test-access-token')
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('App', () => {
  it('explains the synthetic care journey and starts Auth0 login', async () => {
    const user = userEvent.setup()

    renderRoute()

    expect(
      screen.getByRole('heading', { level: 1, name: /care moves better/i }),
    ).toBeInTheDocument()
    expect(screen.getByText(/every person and health detail/i)).toBeInTheDocument()
    expect(screen.getAllByRole('listitem')).toHaveLength(4)

    await user.click(screen.getByRole('button', { name: 'Enter the patient demo' }))

    expect(auth.loginWithRedirect).toHaveBeenCalledWith({ appState: { returnTo: '/app' } })
  })

  it('protects the patient workspace when no Auth0 session exists', () => {
    renderRoute('/app')

    expect(
      screen.getByRole('heading', { level: 1, name: /sign in to continue/i }),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Sign in with Auth0' })).toBeInTheDocument()
  })

  it('moves an authenticated callback into the patient workspace', async () => {
    auth.isAuthenticated = true
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>((input) => {
        if (String(input).endsWith('/api/v1/identity/me')) {
          return Promise.resolve(
            createJsonResponse({ userProfileId: 1, displayName: 'Avery Brooks', role: 'patient' }),
          )
        }

        return Promise.resolve(
          createJsonResponse({ items: [], page: 1, pageSize: 50, totalItems: 0, totalPages: 0 }),
        )
      }),
    )

    renderRoute('/auth/callback')

    expect(
      await screen.findByRole('heading', { level: 1, name: /good day, avery/i }),
    ).toBeInTheDocument()
    expect(screen.queryByText('Completing sign in')).not.toBeInTheDocument()
  })

  it('shows the authenticated patient care itinerary', async () => {
    auth.isAuthenticated = true
    const futureAppointment = {
      id: 11,
      clinicianId: 3,
      clinicianDisplayName: 'Dr. Maya Chen',
      clinicianSpecialty: 'Family Medicine',
      startsAtUtc: '2035-09-08T16:00:00Z',
      endsAtUtc: '2035-09-08T16:45:00Z',
      reason: 'Annual wellness visit',
      status: 'Scheduled',
      cancelledAtUtc: null,
      cancellationReason: null,
      version: 821,
    }
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>((input) => {
        const url = String(input)
        if (url.endsWith('/api/v1/identity/me')) {
          return Promise.resolve(
            createJsonResponse({ userProfileId: 1, displayName: 'Avery Brooks', role: 'patient' }),
          )
        }

        return Promise.resolve(
          createJsonResponse({
            items: [futureAppointment],
            page: 1,
            pageSize: 50,
            totalItems: 1,
            totalPages: 1,
          }),
        )
      }),
    )

    renderRoute('/app')

    expect(await screen.findByRole('heading', { level: 1, name: /good day, avery/i })).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 3, name: 'Dr. Maya Chen' })).toBeInTheDocument()
    expect(screen.getByText('Annual wellness visit')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cancel visit' })).toBeInTheDocument()
    expect(screen.getByText(/local timezone/i)).toBeInTheDocument()
  })

  it('guides an authenticated patient through clinician and time selection', async () => {
    auth.isAuthenticated = true
    const user = userEvent.setup()
    const fetchMock = vi.fn<typeof fetch>((input, init) => {
      const url = String(input)
      if (url.endsWith('/api/v1/clinicians')) {
        return Promise.resolve(
          createJsonResponse([
            { id: 3, displayName: 'Dr. Maya Chen', specialty: 'Family Medicine' },
          ]),
        )
      }
      if (url.includes('/availability?')) {
        return Promise.resolve(
          createJsonResponse([
            {
              id: 17,
              startsAtUtc: '2035-09-08T16:00:00Z',
              endsAtUtc: '2035-09-08T16:45:00Z',
              version: 900,
            },
          ]),
        )
      }

      if (url.endsWith('/api/v1/appointments') && init?.method === 'POST') {
        return Promise.resolve(
          createJsonResponse(
            {
              id: 24,
              clinicianId: 3,
              clinicianDisplayName: 'Dr. Maya Chen',
              clinicianSpecialty: 'Family Medicine',
              startsAtUtc: '2035-09-08T16:00:00Z',
              endsAtUtc: '2035-09-08T16:45:00Z',
              reason: 'Wellness visit',
              status: 'Scheduled',
              cancelledAtUtc: null,
              cancellationReason: null,
              version: 901,
            },
            201,
          ),
        )
      }

      if (url.endsWith('/api/v1/identity/me')) {
        return Promise.resolve(
          createJsonResponse({ userProfileId: 1, displayName: 'Avery Brooks', role: 'patient' }),
        )
      }

      return Promise.resolve(createJsonResponse({ items: [], page: 1, pageSize: 50, totalItems: 0, totalPages: 0 }))
    })
    vi.stubGlobal('fetch', fetchMock)

    renderRoute('/app/appointments/new')

    await user.click(await screen.findByLabelText(/dr\. maya chen/i))
    await user.click(await screen.findByLabelText(/sep 8/i))
    await user.type(screen.getByLabelText(/what would you like to discuss/i), 'Wellness visit')

    expect(screen.getByRole('button', { name: 'Book this visit' })).toBeEnabled()
    expect(screen.getByText(/dr\. maya chen/i, { selector: '.booking-summary strong' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Book this visit' }))

    expect(await screen.findByText(/visit booked/i)).toBeInTheDocument()
  })

  it('ignores an older availability response after the patient changes clinicians', async () => {
    auth.isAuthenticated = true
    const user = userEvent.setup()
    const mayaAvailability = createDeferred<Response>()
    const theoAvailability = createDeferred<Response>()
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>((input) => {
        const url = String(input)
        if (url.endsWith('/api/v1/clinicians')) {
          return Promise.resolve(
            createJsonResponse([
              { id: 3, displayName: 'Dr. Maya Chen', specialty: 'Family Medicine' },
              { id: 4, displayName: 'Dr. Theo Grant', specialty: 'Internal Medicine' },
            ]),
          )
        }
        if (url.includes('/clinicians/3/availability?')) {
          return mayaAvailability.promise
        }
        if (url.includes('/clinicians/4/availability?')) {
          return theoAvailability.promise
        }

        return Promise.resolve(createJsonResponse([]))
      }),
    )

    renderRoute('/app/appointments/new')

    await user.click(await screen.findByLabelText(/dr\. maya chen/i))
    await user.click(screen.getByLabelText(/dr\. theo grant/i))
    await act(async () => {
      theoAvailability.resolve(
        createJsonResponse([
          {
            id: 18,
            startsAtUtc: '2035-09-09T16:00:00Z',
            endsAtUtc: '2035-09-09T16:45:00Z',
            version: 902,
          },
        ]),
      )
      await theoAvailability.promise
    })

    expect(await screen.findByLabelText(/sep 9/i)).toBeInTheDocument()

    await act(async () => {
      mayaAvailability.resolve(
        createJsonResponse([
          {
            id: 17,
            startsAtUtc: '2035-09-08T16:00:00Z',
            endsAtUtc: '2035-09-08T16:45:00Z',
            version: 900,
          },
        ]),
      )
      await mayaAvailability.promise
    })

    expect(screen.queryByLabelText(/sep 8/i)).not.toBeInTheDocument()
    expect(screen.getByText(/dr\. theo grant/i, { selector: '.booking-summary strong' })).toBeInTheDocument()
  })

  it('closes a stale cancellation panel and refreshes after a conflict', async () => {
    auth.isAuthenticated = true
    const user = userEvent.setup()
    const scheduledAppointment = {
      id: 31,
      clinicianId: 3,
      clinicianDisplayName: 'Dr. Maya Chen',
      clinicianSpecialty: 'Family Medicine',
      startsAtUtc: '2035-09-08T16:00:00Z',
      endsAtUtc: '2035-09-08T16:45:00Z',
      reason: 'Wellness visit',
      status: 'Scheduled',
      cancelledAtUtc: null,
      cancellationReason: null,
      version: 910,
    }
    let appointmentReads = 0
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>((input, init) => {
        const url = String(input)
        if (url.endsWith('/api/v1/identity/me')) {
          return Promise.resolve(
            createJsonResponse({ userProfileId: 1, displayName: 'Avery Brooks', role: 'patient' }),
          )
        }
        if (url.includes('/transitions') && init?.method === 'POST') {
          return Promise.resolve(
            createJsonResponse(
              {
                status: 409,
                title: 'The scheduling request conflicts with current state.',
                detail: 'This appointment changed. Refresh it before trying again.',
                errorCode: 'appointment_changed',
                traceId: 'trace-stale-cancellation',
              },
              409,
              'application/problem+json',
            ),
          )
        }
        if (url.includes('/api/v1/appointments?')) {
          appointmentReads += 1
          return Promise.resolve(
            createJsonResponse({
              items:
                appointmentReads === 1
                  ? [scheduledAppointment]
                  : [
                      {
                        ...scheduledAppointment,
                        status: 'Cancelled',
                        cancelledAtUtc: '2035-09-01T16:00:00Z',
                        version: 911,
                      },
                    ],
              page: 1,
              pageSize: 50,
              totalItems: 1,
              totalPages: 1,
            }),
          )
        }

        return Promise.resolve(createJsonResponse({}))
      }),
    )

    renderRoute('/app')

    await user.click(await screen.findByRole('button', { name: 'Cancel visit' }))
    const cancelButtons = screen.getAllByRole('button', { name: 'Cancel visit' })
    await user.click(cancelButtons.at(-1)!)

    expect(await screen.findByText('Cancelled')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Cancel this visit?' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Cancel visit' })).not.toBeInTheDocument()
  })
})
