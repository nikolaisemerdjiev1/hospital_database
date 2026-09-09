import { afterEach, describe, expect, it, vi } from 'vitest'

import { getPatientPrescriptions } from './patientPrescriptions'

function okJson(body: unknown): Response {
  return {
    ok: true,
    status: 200,
    headers: {
      get: vi.fn<(name: string) => string>().mockReturnValue('application/json'),
    },
    json: vi.fn<() => Promise<unknown>>().mockResolvedValue(body),
  } as unknown as Response
}

afterEach(() => vi.unstubAllGlobals())

describe('patient prescription API', () => {
  it('requests one bounded patient-owned page with the access token', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      okJson({ items: [], page: 2, pageSize: 4, totalItems: 5, totalPages: 2 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await getPatientPrescriptions('patient-token', 2, 4)

    const [url, init] = fetchMock.mock.calls[0]
    const parsed = new URL(String(url))
    expect(parsed.pathname).toBe('/api/v1/prescriptions')
    expect(parsed.searchParams.get('page')).toBe('2')
    expect(parsed.searchParams.get('pageSize')).toBe('4')
    expect(init?.headers).toMatchObject({ Authorization: 'Bearer patient-token' })
  })
})
