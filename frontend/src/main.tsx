import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { createBrowserRouter, RouterProvider } from 'react-router-dom'

import '@fontsource-variable/manrope/wght.css'
import '@fontsource/atkinson-hyperlegible/latin-400.css'
import '@fontsource/atkinson-hyperlegible/latin-700.css'
import '@fontsource/ibm-plex-mono/latin-400.css'
import '@fontsource/ibm-plex-mono/latin-700.css'

import App from './App.tsx'
import { AuthProvider } from './auth/AuthProvider.tsx'
import './index.css'

const rootElement = document.getElementById('root')

if (!rootElement) {
  throw new Error('The application root element was not found.')
}

const router = createBrowserRouter([
  {
    path: '*',
    element: (
      <AuthProvider>
        <App />
      </AuthProvider>
    ),
  },
])

createRoot(rootElement).render(
  <StrictMode>
    <RouterProvider router={router} />
  </StrictMode>,
)
