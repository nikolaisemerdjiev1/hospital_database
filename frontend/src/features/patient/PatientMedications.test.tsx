import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { PatientMedications } from './PatientMedications'

const auth = vi.hoisted(() => ({
  getAccessTokenSilently: vi.fn<() => Promise<string>>().mockResolvedValue('patient-token'),
}))

vi.mock('@auth0/auth0-react', () => ({
  useAuth0: () => auth,
}))

function jsonResponse(
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

function prescription(
  id: number,
  pharmacyStatus:
    | 'Received by pharmacy'
    | 'Under pharmacist review'
    | 'Ready for pickup'
    | 'Dispensed'
    | 'Cancelled',
) {
  return {
    id,
    medicationDisplayName: `Medication ${id}`,
    dose: '10 mg',
    instructions: 'Take one tablet by mouth daily.',
    quantity: 30,
    issuedAtUtc: '2035-09-08T16:42:00Z',
    cancelledAtUtc: pharmacyStatus === 'Cancelled' ? '2035-09-09T16:42:00Z' : null,
    pharmacyStatus,
  }
}

beforeEach(() => {
  auth.getAccessTokenSilently.mockReset().mockResolvedValue('patient-token')
})

afterEach(() => vi.unstubAllGlobals())

describe('PatientMedications', () => {
  it('shows patient-safe status language without pharmacy operational metadata', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>().mockResolvedValue(
        jsonResponse({
          items: [
            {
              ...prescription(1, 'Ready for pickup'),
              assignedPharmacistDisplayName: 'Alex Rivera',
              version: 918,
            },
          ],
          page: 1,
          pageSize: 4,
          totalItems: 1,
          totalPages: 1,
        }),
      ),
    )

    render(<PatientMedications />)

    expect(await screen.findByRole('heading', { name: 'Medication 1' })).toBeInTheDocument()
    expect(screen.getByText('Ready for pickup')).toBeInTheDocument()
    expect(screen.getByText(/ready for pickup in this fictional care journey/i)).toBeInTheDocument()
    expect(screen.getByText('Take one tablet by mouth daily.')).toBeInTheDocument()
    expect(screen.queryByText('Alex Rivera')).not.toBeInTheDocument()
    expect(screen.queryByText(/918|version/i)).not.toBeInTheDocument()
  })

  it('renders every approved patient-safe pharmacy status', async () => {
    const statuses = [
      'Received by pharmacy',
      'Under pharmacist review',
      'Ready for pickup',
      'Dispensed',
      'Cancelled',
    ] as const
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>().mockResolvedValue(
        jsonResponse({
          items: statuses.map((status, index) => prescription(index + 1, status)),
          page: 1,
          pageSize: 4,
          totalItems: 5,
          totalPages: 1,
        }),
      ),
    )

    render(<PatientMedications />)

    await screen.findByRole('heading', { name: 'Medication 1' })
    for (const status of statuses) {
      expect(
        screen.getByText(status, { selector: '.patient-prescription__status' }),
      ).toBeInTheDocument()
    }
  })

  it('shows loading and empty states with clear patient guidance', async () => {
    let resolveRequest!: (value: Response) => void
    const request = new Promise<Response>((resolve) => {
      resolveRequest = resolve
    })
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockReturnValue(request))

    render(<PatientMedications />)

    expect(screen.getByText('Loading medication updates…')).toBeInTheDocument()
    resolveRequest(
      jsonResponse({ items: [], page: 1, pageSize: 4, totalItems: 0, totalPages: 0 }),
    )

    expect(await screen.findByRole('heading', { name: 'No prescriptions yet' })).toBeInTheDocument()
    expect(screen.getByText(/completed visits will appear here/i)).toBeInTheDocument()
  })

  it('recovers from a localized request failure', async () => {
    const user = userEvent.setup()
    const fetchMock = vi
      .fn<typeof fetch>()
      .mockResolvedValueOnce(
        jsonResponse(
          { title: 'Service unavailable', detail: 'Database unavailable' },
          503,
          'application/problem+json',
        ),
      )
      .mockResolvedValueOnce(
        jsonResponse({ items: [], page: 1, pageSize: 4, totalItems: 0, totalPages: 0 }),
      )
    vi.stubGlobal('fetch', fetchMock)

    render(<PatientMedications />)

    expect(
      await screen.findByRole('heading', { name: 'Medication updates could not be loaded.' }),
    ).toBeInTheDocument()
    expect(screen.queryByText('Database unavailable')).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByRole('heading', { name: 'No prescriptions yet' })).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  it('loads the next and previous medication history pages', async () => {
    const user = userEvent.setup()
    const fetchMock = vi.fn<typeof fetch>((input) => {
      const url = new URL(String(input))
      const requestedPage = Number(url.searchParams.get('page'))
      return Promise.resolve(
        jsonResponse({
          items: [prescription(requestedPage, 'Dispensed')],
          page: requestedPage,
          pageSize: 4,
          totalItems: 8,
          totalPages: 2,
        }),
      )
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<PatientMedications />)

    expect(await screen.findByRole('heading', { name: 'Medication 1' })).toBeInTheDocument()
    expect(screen.getByText('Page 1 of 2')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled()

    await user.click(screen.getByRole('button', { name: 'Next' }))

    expect(await screen.findByRole('heading', { name: 'Medication 2' })).toBeInTheDocument()
    expect(screen.getByText('Page 2 of 2')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled()

    await user.click(screen.getByRole('button', { name: 'Previous' }))
    expect(await screen.findByRole('heading', { name: 'Medication 1' })).toBeInTheDocument()
  })
})
