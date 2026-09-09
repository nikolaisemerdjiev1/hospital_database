import { useAuth0 } from '@auth0/auth0-react'
import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'

import {
  getPharmacyQueue,
  type FulfillmentStatus,
  type PharmacyWorkQueueItem,
  type PharmacyWorkQueuePage,
} from '../../api/pharmacy'
import { getIdentity, type Identity } from '../../api/identity'
import { PharmacyErrorNotice } from './PharmacyFeedback'
import './pharmacy.css'

const queueFilters: ReadonlyArray<{
  label: string
  value?: FulfillmentStatus
}> = [
  { label: 'Active work' },
  { label: 'Unclaimed', value: 'Pending' },
  { label: 'My review', value: 'InReview' },
  { label: 'Ready', value: 'Ready' },
  { label: 'Dispensed', value: 'Dispensed' },
  { label: 'Cancelled', value: 'Cancelled' },
]

const statusLabels: Record<FulfillmentStatus, string> = {
  Pending: 'Unclaimed',
  InReview: 'In review',
  Ready: 'Ready for pickup',
  Dispensed: 'Dispensed',
  Cancelled: 'Cancelled',
}

function formatOrderTime(value: string) {
  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
    timeZoneName: 'short',
  }).format(new Date(value))
}

function actionLabel(status: FulfillmentStatus) {
  switch (status) {
    case 'Pending':
      return 'Review and claim'
    case 'InReview':
      return 'Continue review'
    case 'Ready':
      return 'Confirm dispensing'
    case 'Dispensed':
    case 'Cancelled':
      return 'View record'
  }
}

function QueueOrder({ item }: { item: PharmacyWorkQueueItem }) {
  return (
    <article className={`pharmacy-order pharmacy-order--${item.fulfillmentStatus.toLowerCase()}`}>
      <div className="pharmacy-order__state">
        <span className={`pharmacy-status pharmacy-status--${item.fulfillmentStatus.toLowerCase()}`}>
          {statusLabels[item.fulfillmentStatus]}
        </span>
        <time dateTime={item.issuedAtUtc}>{formatOrderTime(item.issuedAtUtc)}</time>
      </div>
      <div className="pharmacy-order__medication">
        <p>{item.patientDisplayName}</p>
        <h3>{item.medicationDisplayName}</h3>
        <span>{item.dose} · Quantity {item.quantity}</span>
      </div>
      <div className="pharmacy-order__reference" aria-label={`Prescription ${item.prescriptionId}`}>
        <span>Rx order</span>
        <strong>#{item.prescriptionId}</strong>
      </div>
      <Link
        className="secondary-button pharmacy-order__action"
        to={`/app/pharmacy/fulfillments/${item.fulfillmentId}`}
      >
        {actionLabel(item.fulfillmentStatus)}
      </Link>
    </article>
  )
}

export function PharmacyQueuePage() {
  const { getAccessTokenSilently } = useAuth0()
  const [identity, setIdentity] = useState<Identity | null>(null)
  const [queue, setQueue] = useState<PharmacyWorkQueuePage | null>(null)
  const [filter, setFilter] = useState<FulfillmentStatus | undefined>()
  const [page, setPage] = useState(1)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<unknown>(null)

  const loadQueue = useCallback(() => {
    const controller = new AbortController()
    setIsLoading(true)
    setError(null)

    getAccessTokenSilently()
      .then(async (token) => {
        const currentIdentity = await getIdentity(token, controller.signal)
        setIdentity(currentIdentity)
        if (currentIdentity.role.toLowerCase() !== 'pharmacist') return null

        return getPharmacyQueue(
          token,
          { page, pageSize: 20, status: filter },
          controller.signal,
        )
      })
      .then((result) => {
        if (result) setQueue(result)
      })
      .catch((requestError: unknown) => {
        if (!controller.signal.aborted) setError(requestError)
      })
      .finally(() => {
        if (!controller.signal.aborted) setIsLoading(false)
      })

    return () => controller.abort()
  }, [filter, getAccessTokenSilently, page])

  useEffect(loadQueue, [loadQueue])

  if (!isLoading && identity && identity.role.toLowerCase() !== 'pharmacist') {
    return (
      <main id="main-content" className="centered-state">
        <p className="eyebrow">Role-specific workspace</p>
        <h1>This dispensing queue is for pharmacist demo accounts.</h1>
        <p>Your signed role determines which fictional records and actions are available.</p>
        <Link className="primary-button" to="/app">
          Return to your workspace
        </Link>
      </main>
    )
  }

  const pendingCount = queue?.items.filter(
    (item) => item.fulfillmentStatus === 'Pending',
  ).length ?? 0
  const reviewCount = queue?.items.filter(
    (item) => item.fulfillmentStatus === 'InReview',
  ).length ?? 0
  const readyCount = queue?.items.filter(
    (item) => item.fulfillmentStatus === 'Ready',
  ).length ?? 0

  return (
    <main id="main-content" className="pharmacy-workspace">
      <header className="pharmacy-intro">
        <div>
          <p className="eyebrow">Pharmacist workspace</p>
          <h1>Dispensing queue</h1>
          <p>
            {identity ? (
              <>
                Good day, {identity.displayName.split(' ')[0]}.
                <br />
              </>
            ) : null}
            Move each synthetic order through one safe handoff at a time.
          </p>
        </div>
        <span className="timezone-note">Times shown in your local timezone</span>
      </header>

      <section className="pharmacy-shift-strip" aria-label="Queue summary">
        <div>
          <span>Matching orders</span>
          <strong>{queue?.totalItems ?? '—'}</strong>
        </div>
        <div>
          <span>Unclaimed on page</span>
          <strong>{pendingCount}</strong>
        </div>
        <div>
          <span>In review on page</span>
          <strong>{reviewCount}</strong>
        </div>
        <div>
          <span>Ready on page</span>
          <strong>{readyCount}</strong>
        </div>
        <p>Open an order to review patient safety context before changing its status.</p>
      </section>

      <section className="pharmacy-queue-board" aria-labelledby="pharmacy-queue-title" aria-busy={isLoading}>
        <div className="pharmacy-queue-board__heading">
          <div>
            <p className="eyebrow">Medication handoffs</p>
            <h2 id="pharmacy-queue-title">Orders requiring attention</h2>
          </div>
          <div className="pharmacy-filters" aria-label="Filter fulfillment orders">
            {queueFilters.map((option) => (
              <button
                key={option.label}
                type="button"
                aria-pressed={filter === option.value}
                onClick={() => {
                  setFilter(option.value)
                  setPage(1)
                }}
              >
                {option.label}
              </button>
            ))}
          </div>
        </div>

        {error !== null && (
          <PharmacyErrorNotice
            error={error}
            title="The dispensing queue needs a refresh."
            actionLabel="Refresh queue"
            onAction={loadQueue}
          />
        )}

        {isLoading ? (
          <div className="pharmacy-loading" aria-live="polite">
            <span className="loading-orbit" aria-hidden="true" />
            <p>Organizing medication handoffs…</p>
          </div>
        ) : queue?.items.length ? (
          <div className="pharmacy-orders">
            {queue.items.map((item) => (
              <QueueOrder key={item.fulfillmentId} item={item} />
            ))}
          </div>
        ) : (
          <div className="empty-state compact">
            <h3>No orders match this view</h3>
            <p>Choose another status or refresh when a new prescription reaches pharmacy.</p>
          </div>
        )}

        {!isLoading && queue && queue.totalPages > 1 && (
          <nav className="pharmacy-pagination" aria-label="Fulfillment pages">
            <button
              className="text-button"
              type="button"
              disabled={page === 1}
              onClick={() => setPage((current) => Math.max(1, current - 1))}
            >
              Previous
            </button>
            <span aria-live="polite">Page {queue.page} of {queue.totalPages}</span>
            <button
              className="text-button"
              type="button"
              disabled={page >= queue.totalPages}
              onClick={() => setPage((current) => current + 1)}
            >
              Next
            </button>
          </nav>
        )}
      </section>
    </main>
  )
}
