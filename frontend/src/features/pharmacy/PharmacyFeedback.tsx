import { ApiProblemError } from '../../api/client'

export function PharmacyErrorNotice({
  error,
  title,
  actionLabel,
  onAction,
}: {
  error: unknown
  title: string
  actionLabel?: string
  onAction?: () => void
}) {
  const traceId = error instanceof ApiProblemError ? error.traceId : undefined
  const message = error instanceof Error ? error.message : 'The request could not be completed.'

  return (
    <div className="pharmacy-error" role="alert">
      <strong>{title}</strong>
      <p>{message}</p>
      {traceId && <small>Support reference: {traceId}</small>}
      {actionLabel && onAction && (
        <button className="secondary-button" type="button" onClick={onAction}>
          {actionLabel}
        </button>
      )}
    </div>
  )
}
