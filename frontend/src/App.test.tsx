import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'

import App from './App'

const statusFixture = {
  service: 'Hospital Coordination API',
  status: 'ready',
  environment: 'Testing',
  timestamp: '2026-07-11T18:00:00Z',
}

function createJsonResponse(body: unknown): Response {
  return {
    ok: true,
    status: 200,
    json: vi.fn<() => Promise<unknown>>().mockResolvedValue(body),
  } as unknown as Response
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('App', () => {
  it('shows the product purpose and workflow while the API is loading', () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(() => new Promise<Response>(() => undefined)))

    render(<App />)

    expect(screen.getByRole('heading', { level: 1, name: /one care plan/i })).toBeInTheDocument()
    expect(screen.getByText('Checking API')).toBeInTheDocument()
    expect(screen.getAllByRole('listitem')).toHaveLength(4)
    expect(screen.getByText(/all people and health information/i)).toBeInTheDocument()
  })

  it('shows API details when the foundation is connected', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>().mockResolvedValue(createJsonResponse(statusFixture)),
    )

    render(<App />)

    expect(await screen.findByText('Foundation connected')).toBeInTheDocument()
    expect(screen.getByText('Hospital Coordination API')).toBeInTheDocument()
    expect(screen.getByText('Testing')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /view the api contract/i })).toHaveAttribute(
      'href',
      'http://localhost:5050/openapi/v1.json',
    )
  })

  it('offers a recovery action when the API is unavailable', async () => {
    const user = userEvent.setup()
    const fetchMock = vi
      .fn<typeof fetch>()
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValueOnce(createJsonResponse(statusFixture))
    vi.stubGlobal('fetch', fetchMock)

    render(<App />)

    expect(await screen.findByText('API not connected')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Check again' }))

    expect(await screen.findByText('Foundation connected')).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })
})
