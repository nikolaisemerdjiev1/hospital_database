import { useAuth0 } from '@auth0/auth0-react'
import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type FormEvent,
  type KeyboardEvent,
} from 'react'
import { Link, useLocation, useParams } from 'react-router-dom'

import { ApiProblemError } from '../../api/client'
import {
  cancelPrescription,
  completeConsultation,
  getConsultation,
  issuePrescription,
  saveConsultationDraft,
  searchMedications,
  type ConsultationFields,
  type DoctorConsultation,
  type DoctorPrescription,
  type MedicationCatalogItem,
  type MedicationCatalogSearch,
} from '../../api/clinical'
import { useNavigationGuard } from '../../navigation/NavigationGuard'
import { ClinicalErrorNotice } from './ClinicalFeedback'
import './doctor.css'

const emptyFields: ConsultationFields = {
  outcome: '',
  clinicalNotes: '',
  patientSummary: '',
  careInstructions: '',
}

function fieldsFromConsultation(consultation: DoctorConsultation): ConsultationFields {
  return {
    outcome: consultation.outcome ?? '',
    clinicalNotes: consultation.clinicalNotes ?? '',
    patientSummary: consultation.patientSummary ?? '',
    careInstructions: consultation.careInstructions ?? '',
  }
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat('en-US', {
    weekday: 'short',
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
    timeZoneName: 'short',
  }).format(new Date(value))
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  }).format(new Date(`${value}T00:00:00`))
}

function ConfirmationPanel({
  title,
  detail,
  confirmLabel,
  isWorking,
  isDestructive = false,
  onConfirm,
  onClose,
}: {
  title: string
  detail: string
  confirmLabel: string
  isWorking: boolean
  isDestructive?: boolean
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
      className="action-panel clinical-confirmation"
      aria-labelledby="confirm-title"
      onCancel={(event) => {
        event.preventDefault()
        if (!isWorking) onClose()
      }}
    >
      <div>
        <p className="eyebrow">Confirm workflow change</p>
        <h2 id="confirm-title">{title}</h2>
        <p>{detail}</p>
      </div>
      <div className="button-row">
        <button
          className={isDestructive ? 'danger-button' : 'primary-button'}
          type="button"
          disabled={isWorking}
          onClick={onConfirm}
        >
          {isWorking ? 'Saving…' : confirmLabel}
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

function VisitRibbon({ consultation }: { consultation: DoctorConsultation }) {
  const activePrescriptions = consultation.prescriptions.filter(
    (prescription) => prescription.status === 'Issued',
  )
  const fulfillment = activePrescriptions[0]?.fulfillmentStatus

  return (
    <ol className="visit-ribbon" aria-label="Visit workflow">
      <li className="is-complete">
        <span>1</span>
        <div>
          <small>Appointment</small>
          <strong>{consultation.appointment.status === 'InProgress' ? 'In progress' : consultation.appointment.status}</strong>
        </div>
      </li>
      <li className={consultation.status === 'Completed' ? 'is-complete' : 'is-current'}>
        <span>2</span>
        <div>
          <small>Consultation</small>
          <strong>{consultation.status}</strong>
        </div>
      </li>
      <li className={activePrescriptions.length ? 'is-complete' : ''}>
        <span>3</span>
        <div>
          <small>Prescription</small>
          <strong>{activePrescriptions.length ? `${activePrescriptions.length} issued` : 'Not issued'}</strong>
        </div>
      </li>
      <li className={fulfillment ? 'is-current' : ''}>
        <span>4</span>
        <div>
          <small>Pharmacy</small>
          <strong>{fulfillment ?? 'Waiting'}</strong>
        </div>
      </li>
    </ol>
  )
}

function PrescriptionWorkspace({
  consultationId,
  prescriptions,
  onPrescriptionUpdated,
  onReload,
}: {
  consultationId: number
  prescriptions: DoctorPrescription[]
  onPrescriptionUpdated: (prescription: DoctorPrescription) => void
  onReload: () => void
}) {
  const { getAccessTokenSilently } = useAuth0()
  const [query, setQuery] = useState('')
  const [searchResult, setSearchResult] = useState<MedicationCatalogSearch | null>(null)
  const [selectedMedication, setSelectedMedication] = useState<MedicationCatalogItem | null>(null)
  const [activeOption, setActiveOption] = useState(-1)
  const [dose, setDose] = useState('')
  const [instructions, setInstructions] = useState('')
  const [quantity, setQuantity] = useState('30')
  const [isSearching, setIsSearching] = useState(false)
  const [isSaving, setIsSaving] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [cancelling, setCancelling] = useState<DoctorPrescription | null>(null)
  const searchSequence = useRef(0)

  useEffect(() => {
    const normalized = query.trim()
    setActiveOption(-1)

    if (selectedMedication || normalized.length < 2) {
      setSearchResult(null)
      setIsSearching(false)
      return
    }

    const controller = new AbortController()
    const sequence = ++searchSequence.current
    const timer = window.setTimeout(() => {
      setIsSearching(true)
      setError(null)
      getAccessTokenSilently()
        .then((token) => searchMedications(token, normalized, controller.signal))
        .then((result) => {
          if (searchSequence.current === sequence) setSearchResult(result)
        })
        .catch((requestError: unknown) => {
          if (!controller.signal.aborted && searchSequence.current === sequence) {
            setError(requestError)
          }
        })
        .finally(() => {
          if (!controller.signal.aborted && searchSequence.current === sequence) {
            setIsSearching(false)
          }
        })
    }, 350)

    return () => {
      window.clearTimeout(timer)
      controller.abort()
    }
  }, [getAccessTokenSilently, query, selectedMedication])

  function chooseMedication(medication: MedicationCatalogItem) {
    setSelectedMedication(medication)
    setDose('')
    setInstructions('')
    setQuantity('30')
    setQuery(medication.displayName)
    setSearchResult(null)
    setActiveOption(-1)
    setNotice(null)
  }

  function handleSearchKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    const items = searchResult?.items ?? []
    if (!items.length) return

    if (event.key === 'ArrowDown') {
      event.preventDefault()
      setActiveOption((current) => Math.min(current + 1, items.length - 1))
    } else if (event.key === 'ArrowUp') {
      event.preventDefault()
      setActiveOption((current) => Math.max(current - 1, 0))
    } else if (event.key === 'Enter' && activeOption >= 0) {
      event.preventDefault()
      chooseMedication(items[activeOption])
    } else if (event.key === 'Escape') {
      setSearchResult(null)
      setActiveOption(-1)
    }
  }

  async function handleIssue(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const parsedQuantity = Number(quantity)
    if (
      !selectedMedication ||
      !dose.trim() ||
      !instructions.trim() ||
      !Number.isInteger(parsedQuantity) ||
      parsedQuantity < 1 ||
      parsedQuantity > 999
    ) return

    setIsSaving(true)
    setError(null)
    setNotice(null)
    try {
      const token = await getAccessTokenSilently()
      const prescription = await issuePrescription(token, consultationId, {
        medicationId: selectedMedication.medicationId,
        dose: dose.trim(),
        instructions: instructions.trim(),
        quantity: parsedQuantity,
      })
      onPrescriptionUpdated(prescription)
      setSelectedMedication(null)
      setQuery('')
      setDose('')
      setInstructions('')
      setQuantity('30')
      setNotice(`${prescription.medicationDisplayName} was issued to the pharmacy queue.`)
    } catch (requestError) {
      if (requestError instanceof ApiProblemError && requestError.status === 409) {
        setError(null)
        setNotice('The prescription state changed elsewhere. Loading the latest visit record.')
        onReload()
      } else {
        setError(requestError)
      }
    } finally {
      setIsSaving(false)
    }
  }

  async function handleCancellation() {
    if (!cancelling) return

    setIsSaving(true)
    setError(null)
    setNotice(null)
    try {
      const token = await getAccessTokenSilently()
      const prescription = await cancelPrescription(
        token,
        cancelling.id,
        cancelling.version,
      )
      onPrescriptionUpdated(prescription)
      setNotice(`${prescription.medicationDisplayName} was cancelled before dispensing.`)
      setCancelling(null)
    } catch (requestError) {
      setCancelling(null)
      if (requestError instanceof ApiProblemError && requestError.status === 409) {
        setError(null)
        setNotice('The prescription state changed elsewhere. Loading the latest visit record.')
        onReload()
      } else {
        setError(requestError)
      }
    } finally {
      setIsSaving(false)
    }
  }

  const catalogMessage = searchResult?.catalogStatus === 'Fallback'
    ? 'RxNorm is temporarily unavailable. Showing matching local reference records.'
    : searchResult?.catalogStatus === 'Cached'
      ? 'Results loaded from the recently verified local catalog.'
      : searchResult
        ? 'Results verified through RxNorm.'
        : null

  return (
    <section className="prescription-workspace" aria-labelledby="prescription-title">
      <div className="clinical-section-heading">
        <div>
          <p className="eyebrow">Medication handoff</p>
          <h2 id="prescription-title">Prescriptions</h2>
          <p>Search standardized products, then record the order sent to the mock pharmacy queue.</p>
        </div>
        <span className="rxnorm-mark">Powered by RxNorm</span>
      </div>

      {notice && <output className="success-notice">{notice}</output>}
      {error !== null && (
        <ClinicalErrorNotice
          error={error}
          title="The prescription workflow could not be updated."
          actionLabel="Reload consultation"
          onAction={onReload}
        />
      )}

      <div className="prescription-layout">
        <form className="medication-order" onSubmit={handleIssue}>
          <div className="medication-search">
            <label htmlFor="medication-query">Search RxNorm medications</label>
            <p id="medication-search-help">Enter at least two characters. Search by product or medication name.</p>
            <input
              id="medication-query"
              type="search"
              value={query}
              autoComplete="off"
              aria-controls="medication-results"
              aria-describedby="medication-search-help medication-catalog-status"
              aria-activedescendant={activeOption >= 0 ? `medication-option-${activeOption}` : undefined}
              onChange={(event) => {
                setQuery(event.target.value)
                setSelectedMedication(null)
              }}
              onKeyDown={handleSearchKeyDown}
              placeholder="Example: amoxicillin"
            />
            <div id="medication-catalog-status" className="catalog-status" aria-live="polite">
              {isSearching ? 'Searching the medication catalog…' : catalogMessage}
            </div>

            {searchResult && !isSearching && (
              <section id="medication-results" className="medication-results" aria-label="Medication results">
                {searchResult.items.length ? (
                  <ul>
                    {searchResult.items.map((medication, index) => (
                      <li key={medication.medicationId}>
                        <button
                          id={`medication-option-${index}`}
                          type="button"
                          aria-current={activeOption === index ? 'true' : undefined}
                          onMouseEnter={() => setActiveOption(index)}
                          onClick={() => chooseMedication(medication)}
                        >
                          <strong>{medication.displayName}</strong>
                          <span>
                            {[medication.strength, medication.doseForm].filter(Boolean).join(' · ') || 'Product details from RxNorm'}
                          </span>
                          <code>RxCUI {medication.rxCui}</code>
                        </button>
                      </li>
                    ))}
                  </ul>
                ) : (
                  <p>No matching clinical products were found. Try a broader medication name.</p>
                )}
              </section>
            )}
          </div>

          {selectedMedication ? (
            <fieldset className="selected-medication">
              <legend>Order details</legend>
              <div className="selected-medication__name">
                <div>
                  <strong>{selectedMedication.displayName}</strong>
                  <code>RxCUI {selectedMedication.rxCui}</code>
                </div>
                <button className="text-button" type="button" onClick={() => {
                  setSelectedMedication(null)
                  setQuery('')
                }}>
                  Change medication
                </button>
              </div>
              <label>
                Prescribed dose
                <input
                  required
                  value={dose}
                  maxLength={100}
                  onChange={(event) => setDose(event.target.value)}
                  placeholder="Example: 10 mg"
                />
                <small>Enter the ordered dose; catalog strength is reference information only.</small>
              </label>
              <label>
                Directions for the patient
                <textarea
                  required
                  value={instructions}
                  maxLength={1000}
                  onChange={(event) => setInstructions(event.target.value)}
                  placeholder="Example: Take one tablet by mouth once daily."
                />
                <small>{instructions.length}/1,000 characters</small>
              </label>
              <label>
                Quantity
                <input
                  required
                  type="number"
                  min={1}
                  max={999}
                  value={quantity}
                  onChange={(event) => setQuantity(event.currentTarget.value)}
                />
              </label>
              <button
                className="primary-button"
                type="submit"
                disabled={
                  isSaving ||
                  !Number.isInteger(Number(quantity)) ||
                  Number(quantity) < 1 ||
                  Number(quantity) > 999
                }
              >
                {isSaving ? 'Issuing prescription…' : 'Issue prescription'}
              </button>
            </fieldset>
          ) : (
            <p className="medication-prompt">Choose a catalog result to enter dose, directions, and quantity.</p>
          )}
        </form>

        <div className="prescription-history">
          <h3>Orders from this visit</h3>
          {prescriptions.length ? prescriptions.map((prescription) => (
            <article className="prescription-card" key={prescription.id}>
              <div>
                <span className={`status-pill status-pill--${prescription.status.toLowerCase()}`}>
                  {prescription.status}
                </span>
                <span className="fulfillment-label">Pharmacy: {prescription.fulfillmentStatus ?? 'Not queued'}</span>
              </div>
              <h4>{prescription.medicationDisplayName}</h4>
              <code>RxCUI {prescription.rxCui}</code>
              <dl>
                <div><dt>Dose</dt><dd>{prescription.dose}</dd></div>
                <div><dt>Quantity</dt><dd>{prescription.quantity}</dd></div>
                <div><dt>Directions</dt><dd>{prescription.instructions}</dd></div>
              </dl>
              {prescription.status === 'Issued' && prescription.fulfillmentStatus !== 'Dispensed' && (
                <button className="text-button danger-link" type="button" onClick={() => setCancelling(prescription)}>
                  Cancel prescription
                </button>
              )}
              {prescription.fulfillmentStatus === 'Dispensed' && (
                <p className="muted-copy">Dispensed prescriptions can no longer be cancelled.</p>
              )}
            </article>
          )) : (
            <div className="empty-state compact">
              <h4>No prescriptions from this visit</h4>
              <p>Issued medications and pharmacy state will appear here.</p>
            </div>
          )}
        </div>
      </div>

      {cancelling && (
        <ConfirmationPanel
          title={`Cancel ${cancelling.medicationDisplayName}?`}
          detail="This also cancels its mock pharmacy fulfillment. A dispensed prescription cannot be cancelled."
          confirmLabel="Cancel prescription"
          isWorking={isSaving}
          isDestructive
          onConfirm={handleCancellation}
          onClose={() => setCancelling(null)}
        />
      )}
    </section>
  )
}

export function ConsultationPage() {
  const { consultationId: consultationIdValue } = useParams()
  const location = useLocation()
  const { getAccessTokenSilently } = useAuth0()
  const consultationId = Number(consultationIdValue)
  const [consultation, setConsultation] = useState<DoctorConsultation | null>(null)
  const [fields, setFields] = useState<ConsultationFields>(emptyFields)
  const [savedFields, setSavedFields] = useState<ConsultationFields>(emptyFields)
  const [isLoading, setIsLoading] = useState(true)
  const [isSaving, setIsSaving] = useState(false)
  const [isConfirmingCompletion, setIsConfirmingCompletion] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [reloadKey, setReloadKey] = useState(0)

  useEffect(() => {
    if (!Number.isSafeInteger(consultationId) || consultationId <= 0) {
      setIsLoading(false)
      return
    }

    const controller = new AbortController()
    setIsLoading(true)
    setError(null)
    getAccessTokenSilently()
      .then((token) => getConsultation(token, consultationId, controller.signal))
      .then((loaded) => {
        const loadedFields = fieldsFromConsultation(loaded)
        setConsultation(loaded)
        setFields(loadedFields)
        setSavedFields(loadedFields)
      })
      .catch((requestError: unknown) => {
        if (!controller.signal.aborted) setError(requestError)
      })
      .finally(() => {
        if (!controller.signal.aborted) setIsLoading(false)
      })

    return () => controller.abort()
  }, [consultationId, getAccessTokenSilently, reloadKey])

  const isDirty = useMemo(
    () => JSON.stringify(fields) !== JSON.stringify(savedFields),
    [fields, savedFields],
  )
  const isComplete = Object.values(fields).every((value) => value.trim().length > 0)
  useNavigationGuard(isDirty, 'Leave without saving your consultation changes?')

  async function saveDraft(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!consultation || consultation.status !== 'Draft') return

    setIsSaving(true)
    setError(null)
    setNotice(null)
    try {
      const token = await getAccessTokenSilently()
      const saved = await saveConsultationDraft(
        token,
        consultation.id,
        fields,
        consultation.version,
      )
      const normalizedFields = fieldsFromConsultation(saved)
      setConsultation(saved)
      setFields(normalizedFields)
      setSavedFields(normalizedFields)
      setNotice('Draft saved. The latest version is ready for your next change.')
    } catch (requestError) {
      setError(requestError)
    } finally {
      setIsSaving(false)
    }
  }

  async function finishConsultation() {
    if (!consultation || !isComplete) return

    setIsSaving(true)
    setError(null)
    setNotice(null)
    try {
      const token = await getAccessTokenSilently()
      const completed = await completeConsultation(
        token,
        consultation.id,
        fields,
        consultation.version,
      )
      const normalizedFields = fieldsFromConsultation(completed)
      setConsultation(completed)
      setFields(normalizedFields)
      setSavedFields(normalizedFields)
      setIsConfirmingCompletion(false)
      setNotice('Consultation completed. Medication orders can now be issued.')
    } catch (requestError) {
      setError(requestError)
      setIsConfirmingCompletion(false)
    } finally {
      setIsSaving(false)
    }
  }

  function updateField(field: keyof ConsultationFields, value: string) {
    setFields((current) => ({ ...current, [field]: value }))
    setNotice(null)
  }

  function updatePrescription(updated: DoctorPrescription) {
    setConsultation((current) => {
      if (!current) return current
      const exists = current.prescriptions.some((item) => item.id === updated.id)
      return {
        ...current,
        prescriptions: exists
          ? current.prescriptions.map((item) => item.id === updated.id ? updated : item)
          : [updated, ...current.prescriptions],
      }
    })
  }

  if (!Number.isSafeInteger(consultationId) || consultationId <= 0) {
    return (
      <main id="main-content" className="centered-state">
        <p className="eyebrow">Doctor workspace</p>
        <h1>This consultation link is not valid.</h1>
        <Link className="primary-button" to="/app/doctor">Return to care queue</Link>
      </main>
    )
  }

  if (isLoading) {
    return (
      <main id="main-content" className="centered-state" aria-busy="true" aria-live="polite">
        <span className="loading-orbit" aria-hidden="true" />
        <p className="eyebrow">Doctor workspace</p>
        <h1>Opening the consultation</h1>
      </main>
    )
  }

  if (!consultation) {
    return (
      <main id="main-content" className="centered-state">
        <ClinicalErrorNotice
          error={error}
          title="This consultation could not be opened."
          actionLabel="Try again"
          onAction={() => setReloadKey((current) => current + 1)}
        />
        <Link className="back-link" to="/app/doctor">← Return to care queue</Link>
      </main>
    )
  }

  const routeNotice = typeof location.state === 'object' && location.state && 'notice' in location.state
    ? String(location.state.notice)
    : null

  return (
    <main id="main-content" className="consultation-workspace">
      <div className="consultation-topline">
        <Link className="back-link" to="/app/doctor">
          ← Care queue
        </Link>
        <span className={`status-pill status-pill--${consultation.status.toLowerCase()}`}>
          {consultation.status}
        </span>
      </div>

      <header className="consultation-heading">
        <div>
          <p className="eyebrow">Consultation workspace</p>
          <h1>{consultation.patient.displayName}</h1>
          <p>{consultation.appointment.reason}</p>
        </div>
        <div className="save-state" aria-live="polite">
          <span className={isDirty ? 'is-unsaved' : 'is-saved'} aria-hidden="true" />
          {consultation.status === 'Completed'
            ? 'Completed record'
            : isDirty
              ? 'Unsaved changes'
              : 'All changes saved'}
        </div>
      </header>

      <VisitRibbon consultation={consultation} />

      {(routeNotice || notice) && <output className="success-notice">{notice ?? routeNotice}</output>}
      {error !== null && (
        <ClinicalErrorNotice
          error={error}
          title="The consultation could not be updated."
          actionLabel="Reload current version"
          onAction={() => setReloadKey((current) => current + 1)}
        />
      )}

      <div className="consultation-layout">
        <form className="consultation-form" onSubmit={saveDraft}>
          <div className="clinical-section-heading">
            <div>
              <p className="eyebrow">Visit record</p>
              <h2>{consultation.status === 'Draft' ? 'Document the handoff' : 'Completed consultation'}</h2>
            </div>
            <span>Version {consultation.version}</span>
          </div>

          <label>
            Outcome
            <input
              value={fields.outcome}
              readOnly={consultation.status === 'Completed'}
              maxLength={500}
              onChange={(event) => updateField('outcome', event.target.value)}
              placeholder="Example: Symptoms improving with current plan"
            />
            <small>{fields.outcome.length}/500 characters</small>
          </label>

          <label>
            Clinical notes <span className="field-visibility">Care team only</span>
            <textarea
              value={fields.clinicalNotes}
              readOnly={consultation.status === 'Completed'}
              maxLength={4000}
              onChange={(event) => updateField('clinicalNotes', event.target.value)}
              placeholder="Record the synthetic clinical context for this visit."
            />
            <small>{fields.clinicalNotes.length}/4,000 characters</small>
          </label>

          <div className="patient-facing-fields">
            <p><strong>Patient-facing handoff</strong> · These two sections are written for the patient workspace.</p>
            <label>
              Visit summary
              <textarea
                value={fields.patientSummary}
                readOnly={consultation.status === 'Completed'}
                maxLength={2000}
                onChange={(event) => updateField('patientSummary', event.target.value)}
                placeholder="Summarize what was discussed in plain language."
              />
              <small>{fields.patientSummary.length}/2,000 characters</small>
            </label>
            <label>
              Care instructions
              <textarea
                value={fields.careInstructions}
                readOnly={consultation.status === 'Completed'}
                maxLength={2000}
                onChange={(event) => updateField('careInstructions', event.target.value)}
                placeholder="List the next steps the patient should follow."
              />
              <small>{fields.careInstructions.length}/2,000 characters</small>
            </label>
          </div>

          {consultation.status === 'Draft' && (
            <div className="consultation-actions">
              <button className="secondary-button" type="submit" disabled={!isDirty || isSaving}>
                {isSaving ? 'Saving draft…' : 'Save draft'}
              </button>
              <button
                className="primary-button"
                type="button"
                disabled={!isComplete || isSaving}
                aria-describedby={!isComplete ? 'completion-requirements' : undefined}
                onClick={() => setIsConfirmingCompletion(true)}
              >
                Complete consultation
              </button>
              <p id="completion-requirements">
                Complete all four sections before finishing the consultation.
              </p>
            </div>
          )}
        </form>

        <aside className="patient-context" aria-labelledby="patient-context-title">
          <p className="eyebrow">Visit context</p>
          <h2 id="patient-context-title">Keep the person in view</h2>
          <dl>
            <div><dt>Patient</dt><dd>{consultation.patient.displayName}</dd></div>
            <div><dt>Date of birth</dt><dd>{formatDate(consultation.patient.dateOfBirth)}</dd></div>
            <div><dt>Medical record</dt><dd>{consultation.patient.medicalRecordNumber}</dd></div>
            <div><dt>Visit time</dt><dd>{formatDateTime(consultation.appointment.startsAtUtc)}</dd></div>
            <div className="allergy-context">
              <dt>Allergy summary</dt>
              <dd>{consultation.patient.allergySummary ?? 'No allergy summary on file'}</dd>
            </div>
          </dl>
          <p className="synthetic-context-note">Synthetic portfolio data · Not clinical guidance</p>
        </aside>
      </div>

      {consultation.status === 'Completed' ? (
        <PrescriptionWorkspace
          consultationId={consultation.id}
          prescriptions={consultation.prescriptions}
          onPrescriptionUpdated={updatePrescription}
          onReload={() => setReloadKey((current) => current + 1)}
        />
      ) : (
        <section className="prescription-locked" aria-labelledby="prescription-locked-title">
          <span aria-hidden="true">03</span>
          <div>
            <p className="eyebrow">Next handoff</p>
            <h2 id="prescription-locked-title">Complete the consultation before prescribing</h2>
            <p>This keeps the visit record stable before a medication order enters the pharmacy queue.</p>
          </div>
        </section>
      )}

      {isConfirmingCompletion && (
        <ConfirmationPanel
          title="Complete this consultation?"
          detail="The visit note becomes read-only and the appointment moves to Completed. You can then issue medication orders."
          confirmLabel="Complete consultation"
          isWorking={isSaving}
          onConfirm={finishConsultation}
          onClose={() => setIsConfirmingCompletion(false)}
        />
      )}
    </main>
  )
}
