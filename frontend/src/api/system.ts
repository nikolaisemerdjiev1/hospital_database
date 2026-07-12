export interface SystemStatus {
  service: string
  status: string
  environment: string
  timestamp: string
}

const defaultApiBaseUrl = 'http://localhost:5050'

function resolveApiBaseUrl(): string {
  const configuredUrl = import.meta.env.VITE_API_BASE_URL?.trim()
  const parsedUrl = new URL(configuredUrl || defaultApiBaseUrl)

  if (!['http:', 'https:'].includes(parsedUrl.protocol)) {
    throw new Error('VITE_API_BASE_URL must use HTTP or HTTPS.')
  }

  if (
    parsedUrl.username ||
    parsedUrl.password ||
    parsedUrl.pathname !== '/' ||
    parsedUrl.search ||
    parsedUrl.hash
  ) {
    throw new Error('VITE_API_BASE_URL must be an origin without credentials, a path, or parameters.')
  }

  return parsedUrl.origin
}

function isSystemStatus(value: unknown): value is SystemStatus {
  if (!value || typeof value !== 'object') {
    return false
  }

  const candidate = value as Record<string, unknown>

  return (
    typeof candidate.service === 'string' &&
    typeof candidate.status === 'string' &&
    typeof candidate.environment === 'string' &&
    typeof candidate.timestamp === 'string' &&
    !Number.isNaN(Date.parse(candidate.timestamp))
  )
}

export const apiBaseUrl = resolveApiBaseUrl()

export async function getSystemStatus(signal: AbortSignal): Promise<SystemStatus> {
  const response = await fetch(`${apiBaseUrl}/api/v1/system/status`, {
    headers: {
      Accept: 'application/json',
    },
    cache: 'no-store',
    signal,
  })

  if (!response.ok) {
    throw new Error(`The API status request failed with HTTP ${response.status}.`)
  }

  const body: unknown = await response.json()

  if (!isSystemStatus(body)) {
    throw new Error('The API returned an invalid system status response.')
  }

  return body
}
