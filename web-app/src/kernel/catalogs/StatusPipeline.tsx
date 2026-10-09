import { useQueryClient } from '@tanstack/react-query'
import { Fragment, useEffect, useId, useMemo, useState } from 'react'
import { applyProblemDetails } from '../api/problem'
import { useT } from '../i18n'
import { catalogKeys, useLateralEntries, usePipelineValidation, useStatuses, useStatusHistory } from './api'
import { statusChipStyle } from './chipStyle'
import { allowedTransitions, stepStates } from './transitions'
import { StageKinds, sameCode, type StatusOption } from './types'
import './catalogs.css'

/** Máximo del comentario de una transición (EntityStatusHistory.Comment, NVARCHAR(500)). */
export const TRANSITION_COMMENT_MAX = 500

export interface StatusPipelineProps {
  /** Dominio de estatus (ej. `OrderStatus`). */
  domain: string
  /** Código EntityType del registro (ej. `TRANSPORT_ORDER`): reglas laterales e historial. */
  entityType: string
  /** Id del registro: con él se lee el historial para saber a qué etapa se puede volver desde un lateral. */
  entityId?: number | null
  /** Estatus actual (InternalCode); vacío = registro nuevo. */
  currentCode: string | null | undefined
  /**
   * Ejecuta la transición (el endpoint propio del módulo). Si devuelve una promesa, el diálogo espera y muestra el error
   * del servidor si falla. Sin `onTransition` el pipeline es solo lectura.
   */
  onTransition?: (toCode: string, comment: string | undefined) => Promise<unknown> | void
  /** Oculta las acciones (p. ej. el usuario no tiene el permiso de la acción). */
  disabled?: boolean
  /**
   * Códigos a los que el usuario puede mover el registro desde la pantalla (los demás los dispara el sistema, p. ej. la
   * recepción de una orden de compra). Sin la prop se ofrecen todas las transiciones válidas.
   */
  manualTargets?: readonly string[]
}

/**
 * Pipeline de estatus: etapas del pipeline en orden (hechas / actual / pendientes), laterales y terminales como píldoras,
 * y botones con las transiciones que el servidor aceptaría desde el estatus actual (con comentario opcional).
 */
export function StatusPipeline({ domain, entityType, entityId, currentCode, onTransition, disabled, manualTargets }: StatusPipelineProps) {
  const t = useT()
  const queryClient = useQueryClient()
  const actionable = !!onTransition && !disabled
  const statusesQuery = useStatuses(domain, { includeDisabled: true })
  const validation = usePipelineValidation(domain, actionable)
  const lateral = useLateralEntries(entityType, actionable)
  const history = useStatusHistory(entityType, entityId, true)
  const [target, setTarget] = useState<StatusOption | null>(null)

  const statuses = useMemo(() => statusesQuery.data ?? [], [statusesQuery.data])
  const historyCodes = useMemo(() => (history.data ?? []).map((h) => h.toCode), [history.data])
  const current = statuses.find((s) => sameCode(s.code, currentCode))
  const visible = statuses.filter((s) => s.isEnabled || s === current)
  const pipeline = visible.filter((s) => s.stageKind === StageKinds.Pipeline)
  const laterals = visible.filter((s) => s.stageKind === StageKinds.Lateral)
  const terminals = visible.filter((s) => s.stageKind === StageKinds.Terminal)
  const states = stepStates(pipeline, current, historyCodes)

  // Las reglas y el historial deben estar resueltos (o haber fallado) antes de ofrecer transiciones.
  const rulesReady = !lateral.isPending && (!entityId || !history.isPending)
  const targets = (actionable && rulesReady
    ? allowedTransitions({ statuses, currentCode, lateralEntries: lateral.data ?? [], historyToCodes: historyCodes })
    : []
  ).filter((s) => !manualTargets || manualTargets.some((c) => sameCode(c, s.code)))
  const next = current?.stageKind === StageKinds.Pipeline ? targets.find((s) => s.stageKind !== StageKinds.Lateral && s.sortOrder > current.sortOrder) : undefined

  if (statusesQuery.isPending) return <div className="spin" role="status" aria-label={t('common.loading')} />
  if (statusesQuery.isError) return <p className="note">{applyProblemDetails(statusesQuery.error).title}</p>

  async function confirm(toCode: string, comment: string | undefined) {
    await onTransition?.(toCode, comment)
    if (entityId) await queryClient.invalidateQueries({ queryKey: catalogKeys.history(entityType, entityId) })
    setTarget(null)
  }

  return (
    <div className="stpipe">
      <ol className="stpipe-steps" aria-label={t('status.pipeline')}>
        {pipeline.map((s, i) => {
          const state = states.get(s.code) ?? 'upcoming'
          return (
            <Fragment key={s.code}>
              {i > 0 && <li className="stpipe-sep" aria-hidden="true" />}
              <li className={`stpipe-step ${state}`} aria-current={state === 'current' ? 'step' : undefined}>
                <span className="dot">{state === 'done' ? '✓' : i + 1}</span>
                <span className="lbl">{s.label}</span>
              </li>
            </Fragment>
          )
        })}
      </ol>

      {laterals.length > 0 && <SideRow title={t('status.lateral')} items={laterals} current={current} />}
      {terminals.length > 0 && <SideRow title={t('status.terminal')} items={terminals} current={current} />}

      {actionable && validation.data && validation.data.isValid === false && (
        <p className="note" role="alert">
          {t('status.pipelineInvalid')} {(validation.data.errors ?? []).join(' ')}
        </p>
      )}

      {targets.length > 0 && (
        <div className="stpipe-act">
          {targets.map((s) => (
            <button
              key={s.code}
              type="button"
              className={s === next ? 'btn flow sm' : 'btn sm'}
              onClick={() => setTarget(s)}
            >
              {s === next ? t('status.advanceTo', { status: s.label }) : t('status.moveTo', { status: s.label })}
            </button>
          ))}
        </div>
      )}

      {target && <TransitionDialog target={target} onCancel={() => setTarget(null)} onConfirm={confirm} />}
    </div>
  )
}

function SideRow({ title, items, current }: { title: string; items: StatusOption[]; current: StatusOption | undefined }) {
  return (
    <div className="stpipe-side">
      <span className="stpipe-k">{title}</span>
      {items.map((s) => (
        <span
          key={s.code}
          className={s === current ? 'chip on' : 'chip'}
          style={statusChipStyle(s.color)}
          aria-current={s === current ? 'step' : undefined}
        >
          {s.label}
        </span>
      ))}
    </div>
  )
}

interface TransitionDialogProps {
  target: StatusOption
  onCancel: () => void
  onConfirm: (toCode: string, comment: string | undefined) => Promise<void>
}

/** Confirmación de la transición con comentario opcional; muestra el error del servidor sin cerrarse. */
function TransitionDialog({ target, onCancel, onConfirm }: TransitionDialogProps) {
  const t = useT()
  const titleId = useId()
  const commentId = useId()
  const [comment, setComment] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const tooLong = comment.length > TRANSITION_COMMENT_MAX

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !busy) onCancel()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [busy, onCancel])

  async function submit() {
    if (tooLong || busy) return
    setBusy(true)
    setError(null)
    try {
      await onConfirm(target.code, comment.trim() || undefined)
    } catch (e) {
      const applied = applyProblemDetails(e)
      setError(applied.errors.comment?.join(' ') ?? applied.title)
      setBusy(false)
    }
  }

  return (
    <div className="scrim on">
      <div className="pal" role="dialog" aria-modal="true" aria-labelledby={titleId}>
        <div className="pi">
          <strong id={titleId}>{t('status.confirmTitle', { status: target.label })}</strong>
        </div>
        <div className="pb">
          <div className="f">
            <label htmlFor={commentId}>{t('status.comment')}</label>
            <textarea
              id={commentId}
              rows={3}
              autoFocus
              value={comment}
              aria-invalid={tooLong || undefined}
              onChange={(e) => setComment(e.target.value)}
            />
            <div className="stpipe-count">
              {comment.length}/{TRANSITION_COMMENT_MAX}
            </div>
            {tooLong && <p className="ferr">{t('status.commentTooLong', { max: TRANSITION_COMMENT_MAX })}</p>}
            {error && (
              <p className="ferr" role="alert">
                {error}
              </p>
            )}
          </div>
        </div>
        <div className="ft">
          <button type="button" className="btn" onClick={onCancel} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="button" className="btn flow" onClick={() => void submit()} disabled={busy || tooLong}>
            {busy ? t('common.loading') : t('status.confirm')}
          </button>
        </div>
      </div>
    </div>
  )
}
