import { afterEach, describe, expect, it, vi } from 'vitest'

import { bookAppointment, getAvailability } from './scheduling'

function createJsonResponse(body: unknown, status = 200, contentType = 'application/json'): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: {
      get: vi.fn<(name: string) => string>().mockReturnValue(contentType),
    },
    json: vi.fn<() => Promise<unknown>>().mockResolvedValue(body),
  } as unknown as Response
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('scheduling API client', () => {
  it('sends bounded UTC availability parameters with the access token', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(createJsonResponse([]))
    vi.stubGlobal('fetch', fetchMock)
    const from = new Date('2030-09-01T12:00:00Z')
    const to = new Date('2030-09-30T12:00:00Z')

    await getAvailability('access-token', 42, from, to)

    const [url, init] = fetchMock.mock.calls[0]
    expect(String(url)).toContain('/api/v1/clinicians/42/availability?')
    expect(String(url)).toContain('from=2030-09-01T12%3A00%3A00.000Z')
    expect(String(url)).toContain('to=2030-09-30T12%3A00%3A00.000Z')
    expect(init?.headers).toMatchObject({ Authorization: 'Bearer access-token' })
  })

  it('sends the observed slot version when booking', async () => {
    const response = {
      id: 9,
      clinicianId: 2,
      clinicianDisplayName: 'Dr. Maya Chen',
      clinicianSpecialty: 'Family Medicine',
      startsAtUtc: '2030-09-08T16:00:00Z',
      endsAtUtc: '2030-09-08T16:45:00Z',
      reason: 'Annual wellness visit',
      status: 'Scheduled',
      cancelledAtUtc: null,
      cancellationReason: null,
      version: 800,
    }
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(createJsonResponse(response, 201))
    vi.stubGlobal('fetch', fetchMock)

    await bookAppointment(
      'access-token',
      {
        id: 7,
        startsAtUtc: response.startsAtUtc,
        endsAtUtc: response.endsAtUtc,
        version: 713,
      },
      'Annual wellness visit',
    )

    const [, init] = fetchMock.mock.calls[0]
    expect(init?.method).toBe('POST')
    expect(JSON.parse(String(init?.body))).toEqual({
      availabilitySlotId: 7,
      expectedAvailabilityVersion: 713,
      reason: 'Annual wellness visit',
    })
  })

  it('preserves conflict codes and trace IDs for recovery UI', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>().mockResolvedValue(
        createJsonResponse(
          {
            status: 409,
            title: 'The scheduling request conflicts with current state.',
            detail: 'This appointment time is no longer available.',
            errorCode: 'availability_taken',
            traceId: 'trace-123',
          },
          409,
          'application/problem+json',
        ),
      ),
    )

    const promise = bookAppointment(
      'access-token',
      {
        id: 7,
        startsAtUtc: '2030-09-08T16:00:00Z',
        endsAtUtc: '2030-09-08T16:45:00Z',
        version: 713,
      },
      'Annual wellness visit',
    )

    await expect(promise).rejects.toMatchObject({
      status: 409,
      errorCode: 'availability_taken',
      traceId: 'trace-123',
    })
  })
})
