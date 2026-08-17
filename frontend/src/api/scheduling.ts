import { apiRequest } from './client'

export interface Clinician {
  id: number
  displayName: string
  specialty: string
}

export interface Availability {
  id: number
  startsAtUtc: string
  endsAtUtc: string
  version: number
}

export interface Appointment {
  id: number
  clinicianId: number
  clinicianDisplayName: string
  clinicianSpecialty: string
  startsAtUtc: string
  endsAtUtc: string
  reason: string
  status: 'Scheduled' | 'InProgress' | 'Completed' | 'Cancelled' | 'NoShow'
  cancelledAtUtc: string | null
  cancellationReason: string | null
  version: number
}

export interface AppointmentPage {
  items: Appointment[]
  page: number
  pageSize: number
  totalItems: number
  totalPages: number
}

export function getClinicians(accessToken: string, signal?: AbortSignal): Promise<Clinician[]> {
  return apiRequest<Clinician[]>('/api/v1/clinicians', accessToken, { signal })
}

export function getAvailability(
  accessToken: string,
  clinicianId: number,
  from: Date,
  to: Date,
  signal?: AbortSignal,
): Promise<Availability[]> {
  const query = new URLSearchParams({
    from: from.toISOString(),
    to: to.toISOString(),
  })

  return apiRequest<Availability[]>(
    `/api/v1/clinicians/${clinicianId}/availability?${query.toString()}`,
    accessToken,
    { signal },
  )
}

export function getAppointments(
  accessToken: string,
  signal?: AbortSignal,
): Promise<AppointmentPage> {
  return apiRequest<AppointmentPage>('/api/v1/appointments?page=1&pageSize=50', accessToken, {
    signal,
  })
}

export function bookAppointment(
  accessToken: string,
  availability: Availability,
  reason: string,
): Promise<Appointment> {
  return apiRequest<Appointment>('/api/v1/appointments', accessToken, {
    method: 'POST',
    body: JSON.stringify({
      availabilitySlotId: availability.id,
      expectedAvailabilityVersion: availability.version,
      reason,
    }),
  })
}

export function cancelAppointment(
  accessToken: string,
  appointment: Appointment,
  reason: string,
): Promise<Appointment> {
  return apiRequest<Appointment>(`/api/v1/appointments/${appointment.id}/transitions`, accessToken, {
    method: 'POST',
    body: JSON.stringify({
      targetStatus: 'Cancelled',
      expectedVersion: appointment.version,
      reason: reason.trim() || null,
    }),
  })
}
