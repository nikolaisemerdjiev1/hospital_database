import { useAuth0 } from '@auth0/auth0-react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'

import { ApiProblemError } from '../../api/client'
import { getIdentity, type Identity } from '../../api/identity'
import {
  getPharmacyFulfillment,
  transitionPharmacyFulfillment,
  type FulfillmentStatus,
  type FulfillmentTransitionTarget,
  type PharmacyFulfillment,
} from '../../api/pharmacy'
import { PharmacyErrorNotice } from './PharmacyFeedback'
import './pharmacy.css'

const statusLabels: Record<FulfillmentStatus, string> = {
  Pending: 'Unclaimed',
  InReview: 'In review',
  Ready: 'Ready for pickup',
  Dispensed: 'Dispensed',
  Cancelled: 'Cancelled',
}

const workflowStages = [
  { status: 'Pending', label: 'Received' },
  { status: 'InReview', label: 'Review' },
  { status: 'Ready', label: 'Ready' },
  { status: 'Dispensed', label: 'Dispensed' },
] as const

function formatTimestamp(value: string | null) {
  if (!value) return 'Not reached'
  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
    timeZoneName: 'short',
  }).format(new Date(value))
}

function currentStageIndex(fulfillment: PharmacyFulfillment) {
  if (fulfillment.status !== 'Cancelled') {
    return workflowStages.findIndex((stage) => stage.status === fulfillment.status)
  }
  if (fulfillment.dispensedAtUtc) return 3
  if (fulfillment.readyAtUtc) return 2
  if (fulfillment.reviewStartedAtUtc) return 1
  return 0
}

function FulfillmentRail({ fulfillment }: { fulfillment: PharmacyFulfillment }) {
  const activeIndex = currentStageIndex(fulfillment)

  return (
    <ol className="fulfillment-rail" aria-label="Fulfillment progress">
      {workflowStages.map((stage, index) => {
        const isComplete = index < activeIndex || fulfillment.status === 'Dispensed'
        const isCurrent = index === activeIndex && fulfillment.status !== 'Cancelled'
        return (
          <li
            key={stage.status}
            className={isComplete ? 'is-complete' : isCurrent ? 'is-current' : undefined}
            aria-current={isCurrent ? 'step' : undefined}
          >
            <span aria-hidden="true">{isComplete ? '✓' : index + 1}</span>
            <div>
              <small>Step {index + 1}</small>
              <strong>{stage.label}</strong>
            </div>
          </li>
        )
      })}
    </ol>
  )
}

function DispenseConfirmation({
  medicationName,
  patientName,
  isWorking,
  onConfirm,
  onClose,
}: {
  medicationName: string
  patientName: string
  isWorking: boolean
  onConfirm: () => void
  onClose: () => void
}) {
  const dialog = useRef<HTMLDialogElement>(null)
  const cancelButton = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    const element = dialog.current
    if (!element) return

    if (typeof element.showModal === 'function') {
      element.showModal()
    } else {
      element.setAttribute('open', '')
    }
    cancelButton.current?.focus()

    return () => {
      if (typeof element.close === 'function' && element.open) {
        element.close()
      } else {
        element.removeAttribute('open')
      }
    }
  }, [])

  return (
    <dialog
      ref={dialog}
      className="action-panel dispense-confirmation"
      aria-labelledby="dispense-confirmation-title"
      onCancel={(event) => {
        event.preventDefault()
        if (!isWorking) onClose()
      }}
    >
      <div>
        <p className="eyebrow">Final pharmacy handoff</p>
        <h2 id="dispense-confirmation-title">Confirm medication was dispensed?</h2>
        <p>
          Record {medicationName} as dispensed to {patientName}. This permanently prevents
          prescription cancellation in the demo workflow.
        </p>
      </div>
      <div className="button-row">
        <button
          className="primary-button"
          type="button"
          disabled={isWorking}
          onClick={onConfirm}
        >
          {isWorking ? 'Recording dispense…' : 'Confirm dispensed'}
        </button>
        <button
          ref={cancelButton}
          className="text-button"
          type="button"
          disabled={isWorking}
          onClick={onClose}
        >
          Go back
        </button>
      </div>
    </dialog>
  )
}

function nextAction(status: FulfillmentStatus): {
  target: FulfillmentTransitionTarget
  label: string
  workingLabel: string
} | null {
  switch (status) {
    case 'Pending':
      return { target: 'InReview', label: 'Start review and claim', workingLabel: 'Claiming order…' }
    case 'InReview':
      return { target: 'Ready', label: 'Mark ready for pickup', workingLabel: 'Marking ready…' }
    case 'Ready':
      return { target: 'Dispensed', label: 'Confirm dispensing', workingLabel: 'Opening confirmation…' }
    case 'Dispensed':
    case 'Cancelled':
      return null
  }
}

function successMessage(target: FulfillmentTransitionTarget) {
  switch (target) {
    case 'InReview':
      return 'Review started. This order is now assigned to you.'
    case 'Ready':
      return 'Order marked ready for pickup.'
    case 'Dispensed':
      return 'Dispensing recorded. This fulfillment is complete.'
  }
}

export function FulfillmentDetailPage() {
  const { fulfillmentId } = useParams()
  const parsedFulfillmentId = Number(fulfillmentId)
  const { getAccessTokenSilently } = useAuth0()
  const [identity, setIdentity] = useState<Identity | null>(null)
  const [fulfillment, setFulfillment] = useState<PharmacyFulfillment | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [transitioningTo, setTransitioningTo] = useState<FulfillmentTransitionTarget | null>(null)
  const [isConfirmingDispense, setIsConfirmingDispense] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const [conflictNotice, setConflictNotice] = useState<string | null>(null)
  const [error, setError] = useState<unknown>(null)
  const dispenseTrigger = useRef<HTMLButtonElement>(null)
  const shouldRestoreDispenseFocus = useRef(false)

  useEffect(() => {
    if (!isConfirmingDispense && shouldRestoreDispenseFocus.current) {
      shouldRestoreDispenseFocus.current = false
      dispenseTrigger.current?.focus()
    }
  }, [isConfirmingDispense])

  const loadFulfillment = useCallback(() => {
    const controller = new AbortController()
    setIsLoading(true)
    setError(null)

    if (!Number.isSafeInteger(parsedFulfillmentId) || parsedFulfillmentId <= 0) {
      setError(new Error('The fulfillment reference is invalid.'))
      setIsLoading(false)
      return () => controller.abort()
    }

    getAccessTokenSilently()
      .then(async (token) => {
        const currentIdentity = await getIdentity(token, controller.signal)
        setIdentity(currentIdentity)
        if (currentIdentity.role.toLowerCase() !== 'pharmacist') return null
        return getPharmacyFulfillment(token, parsedFulfillmentId, controller.signal)
      })
      .then((result) => {
        if (result) setFulfillment(result)
      })
      .catch((requestError: unknown) => {
        if (!controller.signal.aborted) setError(requestError)
      })
      .finally(() => {
        if (!controller.signal.aborted) setIsLoading(false)
      })

    return () => controller.abort()
  }, [getAccessTokenSilently, parsedFulfillmentId])

  useEffect(loadFulfillment, [loadFulfillment])

  async function handleTransition(target: FulfillmentTransitionTarget) {
    if (!fulfillment) return
    if (target === 'Dispensed' && !isConfirmingDispense) {
      setIsConfirmingDispense(true)
      return
    }

    setTransitioningTo(target)
    setError(null)
    setNotice(null)
    setConflictNotice(null)

    try {
      const token = await getAccessTokenSilently()
      const updated = await transitionPharmacyFulfillment(
        token,
        fulfillment.id,
        target,
        fulfillment.version,
      )
      setFulfillment(updated)
      setNotice(successMessage(target))
      setIsConfirmingDispense(false)
    } catch (requestError) {
      setIsConfirmingDispense(false)
      if (requestError instanceof ApiProblemError && requestError.status === 409) {
        try {
          const token = await getAccessTokenSilently()
          const latest = await getPharmacyFulfillment(token, fulfillment.id)
          setFulfillment(latest)
          setConflictNotice(
            'This order changed in another session. The latest status is shown below.',
          )
        } catch (refreshError) {
          setError(refreshError)
        }
      } else {
        setError(requestError)
      }
    } finally {
      setTransitioningTo(null)
    }
  }

  if (!isLoading && identity && identity.role.toLowerCase() !== 'pharmacist') {
    return (
      <main id="main-content" className="centered-state">
        <p className="eyebrow">Role-specific workspace</p>
        <h1>This fulfillment record is for pharmacist demo accounts.</h1>
        <Link className="primary-button" to="/app">
          Return to your workspace
        </Link>
      </main>
    )
  }

  if (isLoading) {
    return (
      <main id="main-content" className="centered-state" aria-live="polite" aria-busy="true">
        <span className="loading-orbit" aria-hidden="true" />
        <p className="eyebrow">Pharmacist workspace</p>
        <h1>Loading the medication handoff</h1>
      </main>
    )
  }

  if (!fulfillment) {
    return (
      <main id="main-content" className="centered-state">
        {error !== null && (
          <PharmacyErrorNotice error={error} title="This fulfillment could not be opened." />
        )}
        <Link className="primary-button" to="/app/pharmacy">
          Return to dispensing queue
        </Link>
      </main>
    )
  }

  const action = nextAction(fulfillment.status)
  const isWorking = transitioningTo !== null

  return (
    <main id="main-content" className="fulfillment-workspace">
      <div className="fulfillment-topline">
        <Link className="text-button" to="/app/pharmacy">← Back to dispensing queue</Link>
        <span>Rx order #{fulfillment.prescription.id}</span>
      </div>

      <header className="fulfillment-heading">
        <div>
          <p className="eyebrow">Medication handoff</p>
          <h1>{fulfillment.patient.displayName}</h1>
          <p>{fulfillment.prescription.medicationDisplayName}</p>
        </div>
        <span className={`pharmacy-status pharmacy-status--${fulfillment.status.toLowerCase()}`}>
          {statusLabels[fulfillment.status]}
        </span>
      </header>

      <FulfillmentRail fulfillment={fulfillment} />

      {fulfillment.status === 'Cancelled' && (
        <output className="pharmacy-terminal-note">
          This order was cancelled before dispensing. No pharmacy action is available.
        </output>
      )}
      {notice && <output className="pharmacy-success">{notice}</output>}
      {conflictNotice && <output className="pharmacy-update">{conflictNotice}</output>}
      {error !== null && (
        <PharmacyErrorNotice
          error={error}
          title="The fulfillment action could not be completed."
          actionLabel="Refresh order"
          onAction={loadFulfillment}
        />
      )}

      <div className="fulfillment-layout">
        <section className="order-review" aria-labelledby="order-review-title">
          <div className="order-review__heading">
            <div>
              <p className="eyebrow">Order review</p>
              <h2 id="order-review-title">Prescription details</h2>
            </div>
            <code>RxCUI {fulfillment.prescription.rxCui}</code>
          </div>

          <div className="allergy-banner">
            <span aria-hidden="true">!</span>
            <div>
              <strong>Allergy summary</strong>
              <p>
                {fulfillment.patient.allergySummary?.trim() ||
                  'No allergies documented in this synthetic patient profile.'}
              </p>
            </div>
          </div>

          <dl className="order-facts">
            <div><dt>Medication</dt><dd>{fulfillment.prescription.medicationDisplayName}</dd></div>
            <div><dt>Dose</dt><dd>{fulfillment.prescription.dose}</dd></div>
            <div><dt>Quantity</dt><dd>{fulfillment.prescription.quantity}</dd></div>
            <div><dt>Directions</dt><dd>{fulfillment.prescription.instructions}</dd></div>
            <div><dt>Prescriber</dt><dd>{fulfillment.prescription.prescriberDisplayName}</dd></div>
            <div><dt>Issued</dt><dd>{formatTimestamp(fulfillment.prescription.issuedAtUtc)}</dd></div>
          </dl>
        </section>

        <aside className="fulfillment-action-panel" aria-labelledby="next-action-title">
          <div>
            <p className="eyebrow">Next safe action</p>
            <h2 id="next-action-title">
              {action ? action.label : 'No action needed'}
            </h2>
            <p>
              {fulfillment.status === 'Pending' &&
                'Starting review atomically assigns this order to you.'}
              {fulfillment.status === 'InReview' &&
                'Confirm the order is prepared before marking it ready.'}
              {fulfillment.status === 'Ready' &&
                'Dispensing is final and requires explicit confirmation.'}
              {fulfillment.status === 'Dispensed' &&
                'This medication handoff has been completed.'}
              {fulfillment.status === 'Cancelled' &&
                'This medication handoff ended before dispensing.'}
            </p>
          </div>

          {action && (
            <button
              ref={action.target === 'Dispensed' ? dispenseTrigger : undefined}
              className="primary-button fulfillment-primary-action"
              type="button"
              disabled={isWorking}
              onClick={() => handleTransition(action.target)}
            >
              {transitioningTo === action.target ? action.workingLabel : action.label}
            </button>
          )}

          <dl className="fulfillment-timestamps">
            <div><dt>Received</dt><dd>{formatTimestamp(fulfillment.createdAtUtc)}</dd></div>
            <div><dt>Review started</dt><dd>{formatTimestamp(fulfillment.reviewStartedAtUtc)}</dd></div>
            <div><dt>Ready</dt><dd>{formatTimestamp(fulfillment.readyAtUtc)}</dd></div>
            <div><dt>Dispensed</dt><dd>{formatTimestamp(fulfillment.dispensedAtUtc)}</dd></div>
          </dl>
        </aside>
      </div>

      {isConfirmingDispense && (
        <DispenseConfirmation
          medicationName={fulfillment.prescription.medicationDisplayName}
          patientName={fulfillment.patient.displayName}
          isWorking={transitioningTo === 'Dispensed'}
          onConfirm={() => handleTransition('Dispensed')}
          onClose={() => {
            shouldRestoreDispenseFocus.current = true
            setIsConfirmingDispense(false)
          }}
        />
      )}
    </main>
  )
}
