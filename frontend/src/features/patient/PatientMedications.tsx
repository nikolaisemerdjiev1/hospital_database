import { useAuth0 } from '@auth0/auth0-react'
import { useEffect, useState } from 'react'

import {
  getPatientPrescriptions,
  type PatientPharmacyStatus,
  type PatientPrescription,
  type PatientPrescriptionPage,
} from '../../api/patientPrescriptions'
import './patient.css'

const pageSize = 4

const statusPresentation: Record<
  PatientPharmacyStatus,
  { className: string; explanation: string }
> = {
  'Received by pharmacy': {
    className: 'received',
    explanation: 'Your pharmacy has received the prescription and will review it next.',
  },
  'Under pharmacist review': {
    className: 'review',
    explanation: 'A pharmacist is reviewing the prescription before preparing it.',
  },
  'Ready for pickup': {
    className: 'ready',
    explanation: 'This medication is ready for pickup in this fictional care journey.',
  },
  Dispensed: {
    className: 'dispensed',
    explanation: 'The pharmacy recorded this medication as picked up.',
  },
  Cancelled: {
    className: 'cancelled',
    explanation: 'This prescription was cancelled before it was dispensed.',
  },
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  }).format(new Date(value))
}

function PrescriptionCard({ prescription }: { prescription: PatientPrescription }) {
  const presentation = statusPresentation[prescription.pharmacyStatus]

  return (
    <li>
      <article className="patient-prescription">
        <div className="patient-prescription__topline">
          <span
            className={`patient-prescription__status patient-prescription__status--${presentation.className}`}
          >
            {prescription.pharmacyStatus}
          </span>
          <span>Issued {formatDate(prescription.issuedAtUtc)}</span>
        </div>

        <div className="patient-prescription__body">
          <div>
            <p className="eyebrow">Medication</p>
            <h3>{prescription.medicationDisplayName}</h3>
            <p className="patient-prescription__explanation">{presentation.explanation}</p>
          </div>

          <dl className="patient-prescription__facts">
            <div>
              <dt>Dose</dt>
              <dd>{prescription.dose}</dd>
            </div>
            <div>
              <dt>Quantity</dt>
              <dd>{prescription.quantity}</dd>
            </div>
            {prescription.cancelledAtUtc && (
              <div>
                <dt>Cancelled</dt>
                <dd>{formatDate(prescription.cancelledAtUtc)}</dd>
              </div>
            )}
          </dl>

          <div className="patient-prescription__directions">
            <span>Directions</span>
            <p>{prescription.instructions}</p>
          </div>
        </div>
      </article>
    </li>
  )
}

export function PatientMedications() {
  const { getAccessTokenSilently } = useAuth0()
  const [prescriptions, setPrescriptions] = useState<PatientPrescriptionPage | null>(null)
  const [page, setPage] = useState(1)
  const [refreshKey, setRefreshKey] = useState(0)
  const [isLoading, setIsLoading] = useState(true)
  const [hasError, setHasError] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    setIsLoading(true)
    setHasError(false)

    getAccessTokenSilently()
      .then((token) => getPatientPrescriptions(token, page, pageSize, controller.signal))
      .then(setPrescriptions)
      .catch(() => {
        if (!controller.signal.aborted) setHasError(true)
      })
      .finally(() => {
        if (!controller.signal.aborted) setIsLoading(false)
      })

    return () => controller.abort()
  }, [getAccessTokenSilently, page, refreshKey])

  return (
    <section
      className="patient-medications"
      aria-labelledby="patient-medications-title"
      aria-busy={isLoading}
    >
      <div className="section-heading patient-medications__heading">
        <div>
          <p className="eyebrow">Medications and pharmacy</p>
          <h2 id="patient-medications-title">Your prescription handoffs</h2>
        </div>
        {prescriptions && <span>{prescriptions.totalItems} total</span>}
      </div>
      <p className="patient-medications__summary">
        Follow the latest pharmacy status for each prescription. Newest updates appear first.
      </p>

      {isLoading ? (
        <output className="patient-medications__state">
          <span className="loading-orbit" aria-hidden="true" />
          <p>Loading medication updates…</p>
        </output>
      ) : hasError ? (
        <div className="patient-medications__state patient-medications__state--error" role="alert">
          <h3>Medication updates could not be loaded.</h3>
          <p>Check your connection, then try again.</p>
          <button
            className="secondary-button"
            type="button"
            onClick={() => setRefreshKey((key) => key + 1)}
          >
            Try again
          </button>
        </div>
      ) : prescriptions?.items.length === 0 ? (
        <div className="patient-medications__state">
          <span className="patient-medications__empty-mark" aria-hidden="true">Rx</span>
          <h3>No prescriptions yet</h3>
          <p>Prescriptions from completed visits will appear here.</p>
        </div>
      ) : (
        <>
          <ol className="patient-medications__list">
            {prescriptions?.items.map((prescription) => (
              <PrescriptionCard key={prescription.id} prescription={prescription} />
            ))}
          </ol>

          {prescriptions && prescriptions.totalPages > 1 && (
            <nav className="patient-medications__pagination" aria-label="Medication history pages">
              <button
                className="secondary-button"
                type="button"
                disabled={page === 1 || isLoading}
                onClick={() => setPage((current) => current - 1)}
              >
                Previous
              </button>
              <p aria-live="polite">
                Page {prescriptions.page} of {prescriptions.totalPages}
              </p>
              <button
                className="secondary-button"
                type="button"
                disabled={page === prescriptions.totalPages || isLoading}
                onClick={() => setPage((current) => current + 1)}
              >
                Next
              </button>
            </nav>
          )}
        </>
      )}
    </section>
  )
}
