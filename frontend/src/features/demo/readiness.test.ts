import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { ApiReadinessError, waitForApiReadiness } from './readiness'

function response(status = 200, body = 'Healthy', retryAfter?: string): Response {
  return new Response(body, { status, headers: retryAfter ? { 'Retry-After': retryAfter } : {} })
}

beforeEach(() => vi.useFakeTimers())
afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('demo readiness', () => {
  it('requires database readiness and sends no credentials', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(response())
    vi.stubGlobal('fetch', fetchMock)
    await waitForApiReadiness(new AbortController().signal, vi.fn())
    expect(fetchMock).toHaveBeenCalledExactlyOnceWith(
      expect.stringMatching(/\/health\/ready$/),
      expect.objectContaining({ credentials: 'omit', cache: 'no-store', method: 'GET' }),
    )
    expect(fetchMock.mock.calls[0][1]?.headers).toEqual({ Accept: 'text/plain' })
  })

  it('recovers after a cold start without overlapping requests', async () => {
    const fetchMock = vi.fn<typeof fetch>()
      .mockResolvedValueOnce(response(503, 'Unhealthy'))
      .mockResolvedValueOnce(response())
    vi.stubGlobal('fetch', fetchMock)
    const onAttempt = vi.fn<(attempt: number) => void>()
    const pending = waitForApiReadiness(new AbortController().signal, onAttempt)
    await vi.advanceTimersByTimeAsync(1_999)
    expect(fetchMock).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(1)
    await pending
    expect(onAttempt.mock.calls).toEqual([[1], [2]])
  })

  it('stops after four timed-out requests and a bounded total wait', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockImplementation((_url, init) =>
      new Promise((_resolve, reject) => {
        init?.signal?.addEventListener('abort', () => reject(new DOMException('Timeout', 'AbortError')))
      }),
    )
    vi.stubGlobal('fetch', fetchMock)
    const pending = waitForApiReadiness(new AbortController().signal, vi.fn()).catch((error: unknown) => error)
    await vi.advanceTimersByTimeAsync(46_000)
    expect(await pending).toBeInstanceOf(ApiReadinessError)
    expect(fetchMock).toHaveBeenCalledTimes(4)
    expect(vi.getTimerCount()).toBe(0)
  })

  it('does not mistake a successful HTML fallback for readiness', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockImplementation(async () => response(200, '<html>SPA</html>'))
    vi.stubGlobal('fetch', fetchMock)
    const pending = waitForApiReadiness(new AbortController().signal, vi.fn()).catch((error: unknown) => error)
    await vi.advanceTimersByTimeAsync(14_000)
    expect(await pending).toBeInstanceOf(ApiReadinessError)
    expect(fetchMock).toHaveBeenCalledTimes(4)
  })

  it.each([429, 503])('honors Retry-After on HTTP %s without another automatic request', async (status) => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(response(status, 'Unavailable', '30'))
    vi.stubGlobal('fetch', fetchMock)
    await expect(waitForApiReadiness(new AbortController().signal, vi.fn()))
      .rejects.toMatchObject({ retryAt: Date.now() + 30_000 })
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('honors date-based Retry-After and supplies a fallback for malformed rate-limit headers', async () => {
    const retryAt = Math.ceil(Date.now() / 1_000) * 1_000 + 30_000
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(response(429, '', new Date(retryAt).toUTCString())))
    await expect(waitForApiReadiness(new AbortController().signal, vi.fn())).rejects.toMatchObject({ retryAt })
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(response(429, '', 'invalid')))
    await expect(waitForApiReadiness(new AbortController().signal, vi.fn()))
      .rejects.toMatchObject({ retryAt: Date.now() + 60_000 })
  })

  it('cancels pending backoff on unmount', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(response(503))
    vi.stubGlobal('fetch', fetchMock)
    const controller = new AbortController()
    const pending = waitForApiReadiness(controller.signal, vi.fn()).catch((error: unknown) => error)
    await vi.advanceTimersByTimeAsync(100)
    controller.abort()
    expect(await pending).toBe(controller.signal.reason)
    await vi.advanceTimersByTimeAsync(20_000)
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(vi.getTimerCount()).toBe(0)
  })
})
