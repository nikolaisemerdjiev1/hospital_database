import { apiRequest } from './client'

export type AppointmentStatus =
  | 'Scheduled'
  | 'InProgress'
  | 'Completed'
  | 'Cancelled'
  | 'NoShow'

export interface ConsultationReference {
  id: number
  status: 'Draft' | 'Completed'
  version: number
}

export interface DoctorWorklistItem {
  appointmentId: number
  patientDisplayName: string
  startsAtUtc: string
  endsAtUtc: string
  reason: string
  appointmentStatus: AppointmentStatus
  appointmentVersion: number
  consultation: ConsultationReference | null
}

export interface DoctorWorklistPage {
  items: DoctorWorklistItem[]
  page: number
  pageSize: number
  totalItems: number
  totalPages: number
}

export interface DoctorAppointmentContext {
  id: number
  startsAtUtc: string
  endsAtUtc: string
  reason: string
  status: AppointmentStatus
  version: number
}

export interface DoctorPatientContext {
  displayName: string
  medicalRecordNumber: string
  dateOfBirth: string
  allergySummary: string | null
}

export interface DoctorPrescription {
  id: number
  consultationId?: number
  medicationId?: number
  rxCui: string
  medicationDisplayName: string
  dose: string
  instructions: string
  quantity: number
  status: 'Issued' | 'Cancelled'
  issuedAtUtc: string
  cancelledAtUtc: string | null
  fulfillmentStatus: 'Pending' | 'InReview' | 'Ready' | 'Dispensed' | 'Cancelled' | null
  version: number
}

export interface DoctorConsultation {
  id: number
  status: 'Draft' | 'Completed'
  startedAtUtc: string
  completedAtUtc: string | null
  version: number
  appointment: DoctorAppointmentContext
  patient: DoctorPatientContext
  outcome: string | null
  clinicalNotes: string | null
  patientSummary: string | null
  careInstructions: string | null
  prescriptions: DoctorPrescription[]
}

export interface ConsultationFields {
  outcome: string
  clinicalNotes: string
  patientSummary: string
  careInstructions: string
}

export interface MedicationCatalogItem {
  medicationId: number
  rxCui: string
  displayName: string
  conceptType: string | null
  strength: string | null
  doseForm: string | null
  source: 'RxNorm' | 'SeededFallback'
}

export interface MedicationCatalogSearch {
  items: MedicationCatalogItem[]
  catalogStatus: 'Live' | 'Cached' | 'Fallback'
}

export interface IssuePrescriptionInput {
  medicationId: number
  dose: string
  instructions: string
  quantity: number
}

export interface WorklistQuery {
  from: Date
  to: Date
  page?: number
  pageSize?: number
  status?: AppointmentStatus
}

export function getClinicalWorklist(
  accessToken: string,
  query: WorklistQuery,
  signal?: AbortSignal,
): Promise<DoctorWorklistPage> {
  const search = new URLSearchParams({
    from: query.from.toISOString(),
    to: query.to.toISOString(),
    page: String(query.page ?? 1),
    pageSize: String(query.pageSize ?? 50),
  })
  if (query.status) search.set('status', query.status)

  return apiRequest<DoctorWorklistPage>(
    `/api/v1/clinical-worklist?${search.toString()}`,
    accessToken,
    { signal },
  )
}

export function startConsultation(
  accessToken: string,
  appointmentId: number,
  expectedAppointmentVersion: number,
): Promise<DoctorConsultation> {
  return apiRequest<DoctorConsultation>(
    `/api/v1/appointments/${appointmentId}/consultations`,
    accessToken,
    {
      method: 'POST',
      body: JSON.stringify({ expectedAppointmentVersion }),
    },
  )
}

export function getConsultation(
  accessToken: string,
  consultationId: number,
  signal?: AbortSignal,
): Promise<DoctorConsultation> {
  return apiRequest<DoctorConsultation>(
    `/api/v1/consultations/${consultationId}`,
    accessToken,
    { signal },
  )
}

function consultationBody(fields: ConsultationFields, expectedVersion: number) {
  return JSON.stringify({
    outcome: fields.outcome.trim() || null,
    clinicalNotes: fields.clinicalNotes.trim() || null,
    patientSummary: fields.patientSummary.trim() || null,
    careInstructions: fields.careInstructions.trim() || null,
    expectedVersion,
  })
}

export function saveConsultationDraft(
  accessToken: string,
  consultationId: number,
  fields: ConsultationFields,
  expectedVersion: number,
): Promise<DoctorConsultation> {
  return apiRequest<DoctorConsultation>(
    `/api/v1/consultations/${consultationId}`,
    accessToken,
    {
      method: 'PUT',
      body: consultationBody(fields, expectedVersion),
    },
  )
}

export function completeConsultation(
  accessToken: string,
  consultationId: number,
  fields: ConsultationFields,
  expectedVersion: number,
): Promise<DoctorConsultation> {
  return apiRequest<DoctorConsultation>(
    `/api/v1/consultations/${consultationId}/completion`,
    accessToken,
    {
      method: 'POST',
      body: consultationBody(fields, expectedVersion),
    },
  )
}

export function searchMedications(
  accessToken: string,
  query: string,
  signal?: AbortSignal,
): Promise<MedicationCatalogSearch> {
  const search = new URLSearchParams({ query: query.trim(), limit: '12' })
  return apiRequest<MedicationCatalogSearch>(
    `/api/v1/medications?${search.toString()}`,
    accessToken,
    { signal },
  )
}

export function issuePrescription(
  accessToken: string,
  consultationId: number,
  input: IssuePrescriptionInput,
): Promise<DoctorPrescription> {
  return apiRequest<DoctorPrescription>(
    `/api/v1/consultations/${consultationId}/prescriptions`,
    accessToken,
    {
      method: 'POST',
      body: JSON.stringify(input),
    },
  )
}

export function cancelPrescription(
  accessToken: string,
  prescriptionId: number,
  expectedVersion: number,
): Promise<DoctorPrescription> {
  return apiRequest<DoctorPrescription>(
    `/api/v1/prescriptions/${prescriptionId}/cancellation`,
    accessToken,
    {
      method: 'POST',
      body: JSON.stringify({ expectedVersion }),
    },
  )
}
