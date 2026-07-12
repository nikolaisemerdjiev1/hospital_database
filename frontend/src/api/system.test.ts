import { afterEach, describe, expect, it, vi } from 'vitest'

import { getSystemStatus } from './system'

function createJsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: vi.fn<() => Promise<unknown>>().mockResolvedValue(body),
  } as unknown as Response
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('getSystemStatus', () => {
  it('returns a validated API status response', async () => {
    const body = {
      service: 'Hospital Coordination API',
      status: 'ready',
      environment: 'Testing',
      timestamp: '2026-07-11T18:00:00Z',
    }
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(createJsonResponse(body)))

    await expect(getSystemStatus(new AbortController().signal)).resolves.toEqual(body)
  })

  it('rejects an invalid API response instead of trusting its shape', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>().mockResolvedValue(createJsonResponse({ status: 'ready' })),
    )

    await expect(getSystemStatus(new AbortController().signal)).rejects.toThrow(
      'invalid system status response',
    )
  })

  it('rejects a timestamp that cannot be rendered', async () => {
    const body = {
      service: 'Hospital Coordination API',
      status: 'ready',
      environment: 'Testing',
      timestamp: 'not-a-timestamp',
    }
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(createJsonResponse(body)))

    await expect(getSystemStatus(new AbortController().signal)).rejects.toThrow(
      'invalid system status response',
    )
  })

  it('rejects unsuccessful HTTP responses', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(createJsonResponse({}, 503)))

    await expect(getSystemStatus(new AbortController().signal)).rejects.toThrow('HTTP 503')
  })
})
