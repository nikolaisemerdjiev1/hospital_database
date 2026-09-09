import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  getPharmacyFulfillment,
  getPharmacyQueue,
  transitionPharmacyFulfillment,
} from './pharmacy'

function okJson(body: unknown = {}): Response {
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

describe('pharmacy API', () => {
  it('builds bounded queue queries with optional status filtering', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(okJson({ items: [] }))
    vi.stubGlobal('fetch', fetchMock)

    await getPharmacyQueue('token', { page: 2, pageSize: 25, status: 'Ready' })

    const [url] = fetchMock.mock.calls[0]
    const parsed = new URL(String(url))
    expect(parsed.pathname).toBe('/api/v1/fulfillments')
    expect(parsed.searchParams.get('page')).toBe('2')
    expect(parsed.searchParams.get('pageSize')).toBe('25')
    expect(parsed.searchParams.get('status')).toBe('Ready')
  })

  it('loads details and sends the concurrency version for transitions', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(okJson())
    vi.stubGlobal('fetch', fetchMock)

    await getPharmacyFulfillment('token', 51)
    await transitionPharmacyFulfillment('token', 51, 'Dispensed', 901)

    expect(fetchMock.mock.calls[0][0]).toMatch('/api/v1/fulfillments/51')
    expect(fetchMock.mock.calls[1][0]).toMatch('/api/v1/fulfillments/51/transitions')
    expect(fetchMock.mock.calls[1][1]?.method).toBe('POST')
    expect(JSON.parse(String(fetchMock.mock.calls[1][1]?.body))).toEqual({
      targetStatus: 'Dispensed',
      expectedVersion: 901,
    })
  })
})
