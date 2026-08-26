import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  cancelPrescription,
  completeConsultation,
  getClinicalWorklist,
  issuePrescription,
  saveConsultationDraft,
  searchMedications,
  startConsultation,
} from './clinical'

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

describe('clinical API', () => {
  it('builds a bounded worklist query with an optional status', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(okJson({ items: [] }))
    vi.stubGlobal('fetch', fetchMock)

    await getClinicalWorklist('token', {
      from: new Date('2030-01-01T00:00:00Z'),
      to: new Date('2030-01-08T00:00:00Z'),
      status: 'Scheduled',
    })

    const [url] = fetchMock.mock.calls[0]
    const parsed = new URL(String(url))
    expect(parsed.pathname).toBe('/api/v1/clinical-worklist')
    expect(parsed.searchParams.get('from')).toBe('2030-01-01T00:00:00.000Z')
    expect(parsed.searchParams.get('to')).toBe('2030-01-08T00:00:00.000Z')
    expect(parsed.searchParams.get('pageSize')).toBe('50')
    expect(parsed.searchParams.get('status')).toBe('Scheduled')
  })

  it('sends concurrency versions for consultation transitions', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(okJson())
    vi.stubGlobal('fetch', fetchMock)
    const fields = {
      outcome: ' Stable ',
      clinicalNotes: ' Internal note ',
      patientSummary: ' Summary ',
      careInstructions: ' Hydrate ',
    }

    await startConsultation('token', 31, 700)
    await saveConsultationDraft('token', 41, fields, 701)
    await completeConsultation('token', 41, fields, 702)

    expect(fetchMock.mock.calls[0][0]).toMatch('/api/v1/appointments/31/consultations')
    expect(JSON.parse(String(fetchMock.mock.calls[0][1]?.body))).toEqual({
      expectedAppointmentVersion: 700,
    })
    expect(fetchMock.mock.calls[1][1]?.method).toBe('PUT')
    expect(JSON.parse(String(fetchMock.mock.calls[1][1]?.body))).toEqual({
      outcome: 'Stable',
      clinicalNotes: 'Internal note',
      patientSummary: 'Summary',
      careInstructions: 'Hydrate',
      expectedVersion: 701,
    })
    expect(fetchMock.mock.calls[2][0]).toMatch('/api/v1/consultations/41/completion')
  })

  it('encodes medication searches and prescription commands', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(okJson())
    vi.stubGlobal('fetch', fetchMock)

    await searchMedications('token', ' iron citrate ')
    await issuePrescription('token', 41, {
      medicationId: 91,
      dose: '10 mg',
      instructions: 'Take daily.',
      quantity: 30,
    })
    await cancelPrescription('token', 51, 812)

    expect(fetchMock.mock.calls[0][0]).toMatch('query=iron+citrate&limit=12')
    expect(fetchMock.mock.calls[1][0]).toMatch('/api/v1/consultations/41/prescriptions')
    expect(JSON.parse(String(fetchMock.mock.calls[1][1]?.body))).toEqual({
      medicationId: 91,
      dose: '10 mg',
      instructions: 'Take daily.',
      quantity: 30,
    })
    expect(fetchMock.mock.calls[2][0]).toMatch('/api/v1/prescriptions/51/cancellation')
    expect(JSON.parse(String(fetchMock.mock.calls[2][1]?.body))).toEqual({
      expectedVersion: 812,
    })
  })
})
