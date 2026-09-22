import { apiBaseUrl } from '../../api/system'

/* eslint-disable no-await-in-loop -- Serial probes and backoff avoid amplifying a cold start. */

export const readinessAttempts = 4
const requestTimeoutMs = 8_000

export class ApiReadinessError extends Error {
  readonly retryAt: number

  constructor(retryAt = 0) {
    super('The demo is not ready yet. Please try again shortly.')
    this.name = 'ApiReadinessError'
    this.retryAt = retryAt
  }
}

function retryTime(header: string | null): number {
  if (!header?.trim()) return 0
  const seconds = Number(header)
  const timestamp = Number.isFinite(seconds) && seconds >= 0
    ? Date.now() + seconds * 1_000
    : Date.parse(header)
  return Number.isFinite(timestamp) && timestamp > Date.now() ? timestamp : 0
}

function pause(milliseconds: number, signal: AbortSignal): Promise<void> {
  signal.throwIfAborted()
  return new Promise((resolve, reject) => {
    const finish = () => {
      signal.removeEventListener('abort', cancel)
      resolve()
    }
    const timer = window.setTimeout(finish, milliseconds)
    const cancel = () => {
      window.clearTimeout(timer)
      signal.removeEventListener('abort', cancel)
      reject(signal.reason)
    }
    signal.addEventListener('abort', cancel, { once: true })
  })
}

/** Passive, anonymous probes only. Never replay a workflow mutation to wake the API. */
export async function waitForApiReadiness(
  signal: AbortSignal,
  onAttempt: (attempt: number) => void,
): Promise<void> {
  for (let attempt = 1; attempt <= readinessAttempts; attempt += 1) {
    signal.throwIfAborted()
    onAttempt(attempt)
    const timeout = new AbortController()
    const timer = window.setTimeout(() => timeout.abort(), requestTimeoutMs)

    try {
      const response = await fetch(`${apiBaseUrl}/health/ready`, {
        method: 'GET',
        headers: { Accept: 'text/plain' },
        credentials: 'omit',
        cache: 'no-store',
        signal: AbortSignal.any([signal, timeout.signal]),
      })
      if (response.status === 200 && (await response.text()).trim() === 'Healthy') return

      const retryAt = retryTime(response.headers.get('Retry-After'))
      if (response.status === 429 || retryAt) {
        throw new ApiReadinessError(retryAt || Date.now() + 60_000)
      }
    } catch (error) {
      signal.throwIfAborted()
      if (error instanceof ApiReadinessError) throw error
      // A timeout or network failure uses the same bounded retry schedule as a cold start.
    } finally {
      window.clearTimeout(timer)
    }

    if (attempt < readinessAttempts) await pause(2_000 * 2 ** (attempt - 1), signal)
  }

  throw new ApiReadinessError()
}
