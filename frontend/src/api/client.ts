import { apiBaseUrl } from './system'

interface ProblemDetails {
  status?: number
  title?: string
  detail?: string
  errorCode?: string
  traceId?: string
}

export class ApiProblemError extends Error {
  readonly status: number
  readonly errorCode: string | undefined
  readonly traceId: string | undefined

  constructor(
    status: number,
    errorCode: string | undefined,
    traceId: string | undefined,
    message: string,
  ) {
    super(message)
    this.name = 'ApiProblemError'
    this.status = status
    this.errorCode = errorCode
    this.traceId = traceId
  }
}

export async function apiRequest<T>(
  path: string,
  accessToken: string,
  init: RequestInit = {},
): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: {
      Accept: 'application/json',
      Authorization: `Bearer ${accessToken}`,
      ...(init.body ? { 'Content-Type': 'application/json' } : {}),
      ...init.headers,
    },
    cache: 'no-store',
  })

  if (!response.ok) {
    let problem: ProblemDetails = {}

    if (response.headers.get('content-type')?.includes('application/problem+json')) {
      problem = (await response.json()) as ProblemDetails
    }

    throw new ApiProblemError(
      response.status,
      problem.errorCode,
      problem.traceId,
      problem.detail ?? problem.title ?? `The request failed with HTTP ${response.status}.`,
    )
  }

  return (await response.json()) as T
}
