import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
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
  const router = createMemoryRouter(
    [{ path: '*', element: <App /> }],
    { initialEntries: [path] },
  )

  return { router, ...render(<RouterProvider router={router} />) }
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

    await user.click(screen.getByRole('button', { name: 'Enter the care demo' }))

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

  it('routes a doctor to an assigned clinical worklist', async () => {
    auth.isAuthenticated = true
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>((input) => {
        const url = String(input)
        if (url.endsWith('/api/v1/identity/me')) {
          return Promise.resolve(
            createJsonResponse({
              userProfileId: 7,
              displayName: 'Maya Chen',
              role: 'doctor',
            }),
          )
        }

        return Promise.resolve(
          createJsonResponse({
            items: [
              {
                appointmentId: 81,
                patientDisplayName: 'Avery Brooks',
                startsAtUtc: '2035-09-08T16:00:00Z',
                endsAtUtc: '2035-09-08T16:45:00Z',
                reason: 'Medication follow-up',
                appointmentStatus: 'Scheduled',
                appointmentVersion: 920,
                consultation: null,
              },
              {
                appointmentId: 82,
                patientDisplayName: 'Jordan Lee',
                startsAtUtc: '2035-09-08T17:00:00Z',
                endsAtUtc: '2035-09-08T17:45:00Z',
                reason: 'Blood pressure review',
                appointmentStatus: 'InProgress',
                appointmentVersion: 921,
                consultation: { id: 91, status: 'Draft', version: 922 },
              },
            ],
            page: 1,
            pageSize: 50,
            totalItems: 2,
            totalPages: 1,
          }),
        )
      }),
    )

    renderRoute('/app')

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Care queue' }),
    ).toBeInTheDocument()
    expect(await screen.findByRole('heading', { name: 'Avery Brooks' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Start consultation' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Continue consultation' })).toHaveAttribute(
      'href',
      '/app/doctor/consultations/91',
    )
    expect(screen.getByRole('button', { name: 'All visits' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
  })

  it('does not present an unsupported signed role as a patient', async () => {
    auth.isAuthenticated = true
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>(() => Promise.resolve(createJsonResponse({
        userProfileId: 9,
        displayName: 'Alex Morgan',
        role: 'administrator',
      }))),
    )

    renderRoute('/app')

    expect(
      await screen.findByRole('heading', { name: /administrator experience is not part/i }),
    ).toBeInTheDocument()
    expect(screen.queryByText(/good day, alex/i)).not.toBeInTheDocument()
  })

  it('helps a doctor save and deliberately complete a consultation', async () => {
    auth.isAuthenticated = true
    const user = userEvent.setup()
    const consultation = {
      id: 91,
      status: 'Draft',
      startedAtUtc: '2035-09-08T16:00:00Z',
      completedAtUtc: null,
      version: 100,
      appointment: {
        id: 81,
        startsAtUtc: '2035-09-08T16:00:00Z',
        endsAtUtc: '2035-09-08T16:45:00Z',
        reason: 'Medication follow-up',
        status: 'InProgress',
        version: 920,
      },
      patient: {
        displayName: 'Avery Brooks',
        medicalRecordNumber: 'MRN-001',
        dateOfBirth: '1990-01-01',
        allergySummary: 'Penicillin',
      },
      outcome: null,
      clinicalNotes: null,
      patientSummary: null,
      careInstructions: null,
      prescriptions: [],
    }
    const fetchMock = vi.fn<typeof fetch>((input, init) => {
      const url = String(input)
      if (url.endsWith('/api/v1/consultations/91') && init?.method === 'PUT') {
        const body = JSON.parse(String(init.body)) as Record<string, unknown>
        return Promise.resolve(createJsonResponse({ ...consultation, ...body, version: 101 }))
      }
      if (url.endsWith('/api/v1/consultations/91/completion')) {
        const body = JSON.parse(String(init?.body)) as Record<string, unknown>
        return Promise.resolve(createJsonResponse({
          ...consultation,
          ...body,
          status: 'Completed',
          completedAtUtc: '2035-09-08T16:40:00Z',
          version: 102,
          appointment: { ...consultation.appointment, status: 'Completed', version: 921 },
        }))
      }

      return Promise.resolve(createJsonResponse(consultation))
    })
    vi.stubGlobal('fetch', fetchMock)

    renderRoute('/app/doctor/consultations/91')

    expect(await screen.findByRole('heading', { level: 1, name: 'Avery Brooks' })).toBeInTheDocument()
    expect(screen.getByText('Penicillin')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Complete consultation' })).toBeDisabled()

    await user.type(screen.getByLabelText(/^Outcome/), 'Symptoms are improving')
    await user.type(screen.getByLabelText(/^Clinical notes/), 'Reviewed the synthetic treatment response.')
    await user.type(screen.getByLabelText(/^Visit summary/), 'Your symptoms are improving.')
    await user.type(screen.getByLabelText(/^Care instructions/), 'Continue the current plan and follow up as scheduled.')

    expect(screen.getByText('Unsaved changes')).toBeInTheDocument()
    const confirmNavigation = vi.spyOn(window, 'confirm').mockReturnValue(false)
    await user.click(screen.getByRole('link', { name: 'Harbor Care home' }))
    expect(confirmNavigation).toHaveBeenCalledWith(
      'Leave without saving your consultation changes?',
    )
    expect(screen.getByRole('heading', { level: 1, name: 'Avery Brooks' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Save draft' }))
    expect(await screen.findByText(/draft saved/i)).toBeInTheDocument()
    expect(screen.getByText('All changes saved')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Complete consultation' }))
    const completionDialog = screen.getByRole('dialog', { name: 'Complete this consultation?' })
    expect(within(completionDialog).getByRole('button', { name: 'Go back' })).toHaveFocus()
    await user.click(within(completionDialog).getByRole('button', { name: 'Complete consultation' }))

    expect(await screen.findByText(/consultation completed/i)).toBeInTheDocument()
    expect(screen.getByText('Completed record')).toBeInTheDocument()
    expect(screen.getByLabelText('Search RxNorm medications')).toBeInTheDocument()
  })

  it('explains fallback medication data and confirms prescription changes', async () => {
    auth.isAuthenticated = true
    const user = userEvent.setup()
    const consultation = {
      id: 91,
      status: 'Completed',
      startedAtUtc: '2035-09-08T16:00:00Z',
      completedAtUtc: '2035-09-08T16:40:00Z',
      version: 102,
      appointment: {
        id: 81,
        startsAtUtc: '2035-09-08T16:00:00Z',
        endsAtUtc: '2035-09-08T16:45:00Z',
        reason: 'Medication follow-up',
        status: 'Completed',
        version: 921,
      },
      patient: {
        displayName: 'Avery Brooks',
        medicalRecordNumber: 'MRN-001',
        dateOfBirth: '1990-01-01',
        allergySummary: 'Penicillin',
      },
      outcome: 'Symptoms are improving',
      clinicalNotes: 'Reviewed the synthetic treatment response.',
      patientSummary: 'Your symptoms are improving.',
      careInstructions: 'Continue the current plan.',
      prescriptions: [],
    }
    const issuedPrescription = {
      id: 44,
      consultationId: 91,
      medicationId: 12,
      rxCui: '308192',
      medicationDisplayName: 'Amoxicillin 500 MG Oral Capsule',
      dose: '500 mg',
      instructions: 'Take one capsule by mouth twice daily.',
      quantity: 14,
      status: 'Issued',
      issuedAtUtc: '2035-09-08T16:42:00Z',
      cancelledAtUtc: null,
      fulfillmentStatus: 'Pending',
      version: 300,
    }
    const fetchMock = vi.fn<typeof fetch>((input, init) => {
      const url = String(input)
      if (url.includes('/api/v1/medications?')) {
        return Promise.resolve(createJsonResponse({
          items: [{
            medicationId: 12,
            rxCui: '308192',
            displayName: 'Amoxicillin 500 MG Oral Capsule',
            conceptType: 'Semantic clinical drug',
            strength: '500 mg',
            doseForm: 'Oral Capsule',
            source: 'SeededFallback',
          }],
          catalogStatus: 'Fallback',
        }))
      }
      if (url.endsWith('/api/v1/consultations/91/prescriptions') && init?.method === 'POST') {
        return Promise.resolve(createJsonResponse(issuedPrescription, 201))
      }
      if (url.endsWith('/api/v1/prescriptions/44/cancellation')) {
        return Promise.resolve(createJsonResponse({
          ...issuedPrescription,
          status: 'Cancelled',
          cancelledAtUtc: '2035-09-08T16:43:00Z',
          fulfillmentStatus: 'Cancelled',
          version: 301,
        }))
      }

      return Promise.resolve(createJsonResponse(consultation))
    })
    vi.stubGlobal('fetch', fetchMock)

    renderRoute('/app/doctor/consultations/91')

    const search = await screen.findByLabelText('Search RxNorm medications')
    await user.type(search, 'amoxicillin')
    expect(
      await screen.findByText(/RxNorm is temporarily unavailable/i),
    ).toBeInTheDocument()

    await user.keyboard('{ArrowDown}')
    expect(search).toHaveAttribute('aria-activedescendant', 'medication-option-0')
    await user.keyboard('{Enter}')
    expect(screen.getByLabelText(/^Prescribed dose/)).toHaveValue('')
    await user.type(screen.getByLabelText(/^Prescribed dose/), '500 mg')
    await user.type(
      screen.getByLabelText(/^Directions for the patient/),
      'Take one capsule by mouth twice daily.',
    )
    await user.clear(screen.getByLabelText(/^Quantity/))
    await user.type(screen.getByLabelText(/^Quantity/), '14')
    await user.click(screen.getByRole('button', { name: 'Issue prescription' }))

    expect(await screen.findByText(/was issued to the pharmacy queue/i)).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Amoxicillin 500 MG Oral Capsule' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Cancel prescription' }))
    const cancellationDialog = screen.getByRole('dialog', {
      name: 'Cancel Amoxicillin 500 MG Oral Capsule?',
    })
    await user.click(within(cancellationDialog).getByRole('button', { name: 'Cancel prescription' }))

    const cancellationNotice = await screen.findByText(/was cancelled before dispensing/i)
    expect(cancellationNotice).toBeInTheDocument()
    expect(cancellationNotice).toHaveClass('success-notice')
    expect(cancellationNotice.tagName).toBe('OUTPUT')
    expect(screen.queryByRole('button', { name: 'Cancel prescription' })).not.toBeInTheDocument()
  })

  it('reloads current prescription state without leaving a stale conflict alert', async () => {
    auth.isAuthenticated = true
    const user = userEvent.setup()
    const prescription = {
      id: 44,
      rxCui: '308192',
      medicationDisplayName: 'Amoxicillin 500 MG Oral Capsule',
      dose: '500 mg',
      instructions: 'Take one capsule by mouth twice daily.',
      quantity: 14,
      status: 'Issued',
      issuedAtUtc: '2035-09-08T16:42:00Z',
      cancelledAtUtc: null,
      fulfillmentStatus: 'Pending',
      version: 300,
    }
    const consultation = {
      id: 91,
      status: 'Completed',
      startedAtUtc: '2035-09-08T16:00:00Z',
      completedAtUtc: '2035-09-08T16:40:00Z',
      version: 102,
      appointment: {
        id: 81,
        startsAtUtc: '2035-09-08T16:00:00Z',
        endsAtUtc: '2035-09-08T16:45:00Z',
        reason: 'Medication follow-up',
        status: 'Completed',
        version: 921,
      },
      patient: {
        displayName: 'Avery Brooks',
        medicalRecordNumber: 'MRN-001',
        dateOfBirth: '1990-01-01',
        allergySummary: 'Penicillin',
      },
      outcome: 'Symptoms are improving',
      clinicalNotes: 'Reviewed the synthetic treatment response.',
      patientSummary: 'Your symptoms are improving.',
      careInstructions: 'Continue the current plan.',
      prescriptions: [prescription],
    }
    let consultationReads = 0
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>((input) => {
        const url = String(input)
        if (url.endsWith('/api/v1/prescriptions/44/cancellation')) {
          return Promise.resolve(createJsonResponse({
            status: 409,
            title: 'The prescription changed.',
            detail: 'Reload the current prescription before trying again.',
            errorCode: 'prescription_changed',
            traceId: 'trace-prescription-conflict',
          }, 409, 'application/problem+json'))
        }

        consultationReads += 1
        return Promise.resolve(createJsonResponse(consultation))
      }),
    )

    renderRoute('/app/doctor/consultations/91')

    await user.click(await screen.findByRole('button', { name: 'Cancel prescription' }))
    const dialog = screen.getByRole('dialog', {
      name: 'Cancel Amoxicillin 500 MG Oral Capsule?',
    })
    await user.click(within(dialog).getByRole('button', { name: 'Cancel prescription' }))

    expect(
      await screen.findByText(/prescription state changed elsewhere/i),
    ).toBeInTheDocument()
    expect(consultationReads).toBe(2)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
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
