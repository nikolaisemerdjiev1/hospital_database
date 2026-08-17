import { Auth0Provider, type AppState } from '@auth0/auth0-react'
import type { PropsWithChildren } from 'react'
import { useNavigate } from 'react-router-dom'

function requirePublicSetting(name: string, value: string | undefined): string {
  const normalized = value?.trim()

  if (!normalized) {
    throw new Error(`${name} must be configured before the application starts.`)
  }

  return normalized
}

const domain = requirePublicSetting('VITE_AUTH0_DOMAIN', import.meta.env.VITE_AUTH0_DOMAIN)
const clientId = requirePublicSetting('VITE_AUTH0_CLIENT_ID', import.meta.env.VITE_AUTH0_CLIENT_ID)
const audience = requirePublicSetting('VITE_AUTH0_AUDIENCE', import.meta.env.VITE_AUTH0_AUDIENCE)

export function AuthProvider({ children }: PropsWithChildren) {
  const navigate = useNavigate()

  function handleRedirect(appState?: AppState) {
    navigate(appState?.returnTo ?? '/app', { replace: true })
  }

  return (
    <Auth0Provider
      domain={domain}
      clientId={clientId}
      authorizationParams={{
        audience,
        redirect_uri: `${window.location.origin}/auth/callback`,
        scope: 'openid profile email',
      }}
      onRedirectCallback={handleRedirect}
      useRefreshTokens
      cacheLocation="memory"
    >
      {children}
    </Auth0Provider>
  )
}
