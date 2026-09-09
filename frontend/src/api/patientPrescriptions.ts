import { apiRequest } from './client'

export type PatientPharmacyStatus =
  | 'Received by pharmacy'
  | 'Under pharmacist review'
  | 'Ready for pickup'
  | 'Dispensed'
  | 'Cancelled'

export interface PatientPrescription {
  id: number
  medicationDisplayName: string
  dose: string
  instructions: string
  quantity: number
  issuedAtUtc: string
  cancelledAtUtc: string | null
  pharmacyStatus: PatientPharmacyStatus
}

export interface PatientPrescriptionPage {
  items: PatientPrescription[]
  page: number
  pageSize: number
  totalItems: number
  totalPages: number
}

export function getPatientPrescriptions(
  accessToken: string,
  page = 1,
  pageSize = 4,
  signal?: AbortSignal,
): Promise<PatientPrescriptionPage> {
  const query = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  })

  return apiRequest<PatientPrescriptionPage>(
    `/api/v1/prescriptions?${query.toString()}`,
    accessToken,
    { signal },
  )
}
