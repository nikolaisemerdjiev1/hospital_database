import { apiRequest } from './client'

export interface Identity {
  userProfileId: number
  displayName: string
  role: string
}

export function getIdentity(accessToken: string, signal?: AbortSignal): Promise<Identity> {
  return apiRequest<Identity>('/api/v1/identity/me', accessToken, { signal })
}
