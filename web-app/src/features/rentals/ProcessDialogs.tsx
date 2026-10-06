// Lote F18 (Rentas F-R2) — diálogos de la cola de proceso de los equipos devueltos (manual 11 §6), `rental.maintenance`:
// - Avanzar: a un estatus HABILITADO de la compañía (no terminal: "Lista" y "Dada de baja" tienen su acción). El motor de estatus
//   del servidor decide; un salto fuera de orden responde 422 y su mensaje sale tal cual (p. ej. "Salto ilegal: …").
// - Completar ("Lista"): traslado opcional a otra posición del MISMO almacén (no la zona En renta) y la serie vuelve a estar
//   disponible (se libera la reserva).
// - Dar de baja: exige además `inventory.adjust`; confirmación fuerte (escribir la serie) porque es un ajuste de salida con motivo
//   Daño (DAMAGE) y la serie queda dada de baja.
// - Historial: los pasos del proceso (`/status/history/RENTAL_PROCESS/{id}`).
// Todos muestran el error del servidor tal cual y no se cierran.
import { useMemo, useState } from 'react'
import { StatusChip, StatusHistory, useStatuses } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { Modal, toast } from '../../kernel/ui'
import { BinPicker } from '../warehouse/pickers'
import { problemText } from '../warehouse/problemText'
import { useRentalProcessAction } from './api'
import {
  advanceOptions,
  PROCESS_ENTITY_TYPE,
  PROCESS_STATUS_DOMAIN,
  RENTAL_ZONE_TYPES,
  RETURN_LIMITS,
  scrapConfirmed,
  suggestedAdvance,
  type RentalProcessDto,
} from './returnRules'

export type ProcessDialogKind = 'advance' | 'complete' | 'scrap' | 'history'

/** Cabecera común: serie, producto, almacén · posición y estatus actual. */
function ProcessSummary({ process }: { process: RentalProcessDto }) {
  const t = useT()
  return (
    <p className="ren-confirm ren-links">
      <span className="mono">{process.serialNumber}</span>
      <span>
        {process.sku} · {process.productName}
      </span>
      <span>{t('rentalProcesses.dialog.where', { warehouse: process.warehouseCode ?? '', bin: process.binCode ?? '' })}</span>
      <StatusChip domain={PROCESS_STATUS_DOMAIN} code={process.statusCode} label={process.status} />
    </p>
  )
}

function useSubmit() {
  const action = useRentalProcessAction()
  const [alert, setAlert] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  async function run(fn: () => Promise<unknown>, after: () => void) {
    setSaving(true)
    setAlert(null)
    try {
      await fn()
      after()
    } catch (err) {
      setAlert(problemText(err))
    } finally {
      setSaving(false)
    }
  }
  return { action, alert, saving, run }
}

function CommentField({ id, value, onChange, error }: { id: string; value: string; onChange: (v: string) => void; error?: string | null }) {
  const t = useT()
  return (
    <div className="f">
      <label htmlFor={id}>{t('rentalProcesses.dialog.comment')}</label>
      <textarea
        id={id}
        rows={2}
        maxLength={RETURN_LIMITS.comment}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? `${id}-err` : undefined}
      />
      {error && (
        <p className="ferr" id={`${id}-err`}>
          {error}
        </p>
      )}
    </div>
  )
}

function Alert({ text }: { text: string | null }) {
  if (!text) return null
  return (
    <div className="form-alert" role="alert">
      {text}
    </div>
  )
}

// =====================================================================================================================
// Avanzar
// =====================================================================================================================

export function AdvanceProcessModal({ process, onClose }: { process: RentalProcessDto; onClose: () => void }) {
  const t = useT()
  const { data: statuses = [] } = useStatuses(PROCESS_STATUS_DOMAIN)
  const options = useMemo(() => advanceOptions(statuses, process.statusCode), [statuses, process.statusCode])
  const suggested = useMemo(() => suggestedAdvance(statuses, process.statusCode), [statuses, process.statusCode])
  const [status, setStatus] = useState<string | null>(null)
  const [comment, setComment] = useState('')
  const [statusError, setStatusError] = useState<string | null>(null)
  const { action, alert, saving, run } = useSubmit()
  const value = status ?? suggested

  return (
    <Modal
      open
      title={t('rentalProcesses.advance.title', { serial: process.serialNumber ?? '' })}
      onClose={onClose}
      dismissible={!saving}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={saving}>
            {t('common.cancel')}
          </button>
          <button
            type="button"
            className="btn flow"
            disabled={saving}
            onClick={() => {
              if (!value) {
                setStatusError(t('rentalProcesses.errors.statusRequired'))
                return
              }
              const label = statuses.find((s) => s.code === value)?.label ?? value
              void run(
                () => action.mutateAsync({ action: 'advance', id: process.id ?? 0, body: { status: value, comment: comment.trim() || null, rowVersion: process.rowVersion ?? null } }),
                () => {
                  toast.success(t('rentalProcesses.toast.advanced', { serial: process.serialNumber ?? '', status: label }))
                  onClose()
                },
              )
            }}
          >
            {saving ? t('common.loading') : t('rentalProcesses.actions.advance')}
          </button>
        </>
      }
    >
      <Alert text={alert} />
      <ProcessSummary process={process} />
      <p className="help">{t('rentalProcesses.advance.help')}</p>
      <div className="f">
        <label htmlFor="proc-advance-status">{t('rentalProcesses.advance.to')}</label>
        <select
          id="proc-advance-status"
          value={value}
          aria-invalid={statusError ? true : undefined}
          aria-describedby={statusError ? 'proc-advance-status-err' : undefined}
          onChange={(e) => {
            setStatus(e.target.value)
            setStatusError(null)
          }}
        >
          <option value="">{t('rentalProcesses.advance.pick')}</option>
          {options.map((s) => (
            <option key={s.code} value={s.code}>
              {s.code === suggested ? t('rentalProcesses.advance.suggested', { label: s.label }) : s.label}
            </option>
          ))}
        </select>
        {statusError && (
          <p className="ferr" id="proc-advance-status-err">
            {statusError}
          </p>
        )}
      </div>
      <CommentField id="proc-advance-comment" value={comment} onChange={setComment} />
    </Modal>
  )
}

// =====================================================================================================================
// Completar (Lista)
// =====================================================================================================================

export function CompleteProcessModal({ process, onClose }: { process: RentalProcessDto; onClose: () => void }) {
  const t = useT()
  const [binId, setBinId] = useState<number | null>(null)
  const [comment, setComment] = useState('')
  const { action, alert, saving, run } = useSubmit()

  return (
    <Modal
      open
      title={t('rentalProcesses.complete.title', { serial: process.serialNumber ?? '' })}
      onClose={onClose}
      dismissible={!saving}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={saving}>
            {t('common.cancel')}
          </button>
          <button
            type="button"
            className="btn flow"
            disabled={saving}
            onClick={() =>
              void run(
                () => action.mutateAsync({ action: 'complete', id: process.id ?? 0, body: { binId, comment: comment.trim() || null, rowVersion: process.rowVersion ?? null } }),
                () => {
                  toast.success(t('rentalProcesses.toast.completed', { serial: process.serialNumber ?? '' }))
                  onClose()
                },
              )
            }
          >
            {saving ? t('common.loading') : t('rentalProcesses.actions.complete')}
          </button>
        </>
      }
    >
      <Alert text={alert} />
      <ProcessSummary process={process} />
      <p className="ren-confirm">{t('rentalProcesses.complete.body')}</p>
      {/* el comentario va primero: el modal enfoca el primer control y el selector de posición abriría su lista encima del pie */}
      <CommentField id="proc-complete-comment" value={comment} onChange={setComment} />
      <div className="f">
        <label htmlFor="proc-complete-bin">{t('rentalProcesses.complete.bin', { warehouse: process.warehouseCode ?? '' })}</label>
        <BinPicker
          id="proc-complete-bin"
          warehousePublicId={process.warehousePublicId}
          value={binId}
          onChange={(id) => setBinId(id)}
          excludeZoneTypeCodes={RENTAL_ZONE_TYPES}
          placeholder={t('rentalProcesses.complete.stay', { bin: process.binCode ?? '' })}
        />
        <p className="help">{t('rentalProcesses.complete.binHelp')}</p>
      </div>
    </Modal>
  )
}

// =====================================================================================================================
// Dar de baja (confirmación fuerte)
// =====================================================================================================================

export function ScrapProcessModal({ process, onClose }: { process: RentalProcessDto; onClose: () => void }) {
  const t = useT()
  const [typed, setTyped] = useState('')
  const [comment, setComment] = useState('')
  const [typedError, setTypedError] = useState<string | null>(null)
  const { action, alert, saving, run } = useSubmit()

  return (
    <Modal
      open
      title={t('rentalProcesses.scrap.title', { serial: process.serialNumber ?? '' })}
      onClose={onClose}
      dismissible={!saving}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={saving}>
            {t('common.cancel')}
          </button>
          <button
            type="button"
            className="btn danger"
            disabled={saving}
            onClick={() => {
              if (!scrapConfirmed(typed, process.serialNumber)) {
                setTypedError(t('rentalProcesses.errors.scrapConfirm', { serial: process.serialNumber ?? '' }))
                return
              }
              void run(
                () => action.mutateAsync({ action: 'scrap', id: process.id ?? 0, body: { comment: comment.trim() || null, rowVersion: process.rowVersion ?? null } }),
                () => {
                  toast.success(t('rentalProcesses.toast.scrapped', { serial: process.serialNumber ?? '' }))
                  onClose()
                },
              )
            }}
          >
            {saving ? t('common.loading') : t('rentalProcesses.actions.scrap')}
          </button>
        </>
      }
    >
      <Alert text={alert} />
      <ProcessSummary process={process} />
      <p className="ren-scrap-warn" role="note">
        {t('rentalProcesses.scrap.body', { serial: process.serialNumber ?? '', sku: process.sku ?? '', bin: process.binCode ?? '' })}
      </p>
      <div className="f">
        <label htmlFor="proc-scrap-confirm">{t('rentalProcesses.scrap.confirmLabel', { serial: process.serialNumber ?? '' })}</label>
        <input
          id="proc-scrap-confirm"
          value={typed}
          autoComplete="off"
          aria-invalid={typedError ? true : undefined}
          aria-describedby={typedError ? 'proc-scrap-confirm-err' : undefined}
          onChange={(e) => {
            setTyped(e.target.value)
            setTypedError(null)
          }}
        />
        {typedError && (
          <p className="ferr" id="proc-scrap-confirm-err">
            {typedError}
          </p>
        )}
      </div>
      <CommentField id="proc-scrap-comment" value={comment} onChange={setComment} />
    </Modal>
  )
}

// =====================================================================================================================
// Historial del proceso
// =====================================================================================================================

export function ProcessHistoryModal({ process, onClose }: { process: RentalProcessDto; onClose: () => void }) {
  const t = useT()
  return (
    <Modal
      open
      title={t('rentalProcesses.history.title', { serial: process.serialNumber ?? '' })}
      onClose={onClose}
      footer={
        <button type="button" className="btn" onClick={onClose}>
          {t('common.done')}
        </button>
      }
    >
      <ProcessSummary process={process} />
      <StatusHistory entityType={PROCESS_ENTITY_TYPE} entityId={process.id ?? 0} domain={PROCESS_STATUS_DOMAIN} />
    </Modal>
  )
}
