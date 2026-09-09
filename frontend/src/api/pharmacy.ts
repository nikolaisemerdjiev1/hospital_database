import { apiRequest } from './client'

export type FulfillmentStatus =
  | 'Pending'
  | 'InReview'
  | 'Ready'
  | 'Dispensed'
  | 'Cancelled'

export type FulfillmentTransitionTarget = 'InReview' | 'Ready' | 'Dispensed'

export interface PharmacyWorkQueueItem {
  fulfillmentId: number
  prescriptionId: number
  patientDisplayName: string
  medicationDisplayName: string
  dose: string
  quantity: number
  issuedAtUtc: string
  fulfillmentStatus: FulfillmentStatus
  createdAtUtc: string
  version: number
}

export interface PharmacyWorkQueuePage {
  items: PharmacyWorkQueueItem[]
  page: number
  pageSize: number
  totalItems: number
  totalPages: number
}

export interface PharmacyPatientContext {
  displayName: string
  allergySummary: string | null
}

export interface PharmacyPrescriptionContext {
  id: number
  rxCui: string
  medicationDisplayName: string
  dose: string
  instructions: string
  quantity: number
  status: 'Issued' | 'Cancelled'
  issuedAtUtc: string
  cancelledAtUtc: string | null
  prescriberDisplayName: string
}

export interface PharmacyFulfillment {
  id: number
  status: FulfillmentStatus
  createdAtUtc: string
  reviewStartedAtUtc: string | null
  readyAtUtc: string | null
  dispensedAtUtc: string | null
  cancelledAtUtc: string | null
  version: number
  assignedToCurrentPharmacist: boolean
  patient: PharmacyPatientContext
  prescription: PharmacyPrescriptionContext
}

export interface PharmacyQueueQuery {
  page?: number
  pageSize?: number
  status?: FulfillmentStatus
}

export function getPharmacyQueue(
  accessToken: string,
  query: PharmacyQueueQuery = {},
  signal?: AbortSignal,
): Promise<PharmacyWorkQueuePage> {
  const search = new URLSearchParams({
    page: String(query.page ?? 1),
    pageSize: String(query.pageSize ?? 20),
  })
  if (query.status) search.set('status', query.status)

  return apiRequest<PharmacyWorkQueuePage>(
    `/api/v1/fulfillments?${search.toString()}`,
    accessToken,
    { signal },
  )
}

export function getPharmacyFulfillment(
  accessToken: string,
  fulfillmentId: number,
  signal?: AbortSignal,
): Promise<PharmacyFulfillment> {
  return apiRequest<PharmacyFulfillment>(
    `/api/v1/fulfillments/${fulfillmentId}`,
    accessToken,
    { signal },
  )
}

export function transitionPharmacyFulfillment(
  accessToken: string,
  fulfillmentId: number,
  targetStatus: FulfillmentTransitionTarget,
  expectedVersion: number,
): Promise<PharmacyFulfillment> {
  return apiRequest<PharmacyFulfillment>(
    `/api/v1/fulfillments/${fulfillmentId}/transitions`,
    accessToken,
    {
      method: 'POST',
      body: JSON.stringify({ targetStatus, expectedVersion }),
    },
  )
}
