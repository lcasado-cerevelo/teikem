// Lote F-E2a — «Rutas por despachar» (manual 05 §6.1; maqueta `renderDispatchModal`): las rutas en borrador o planificadas del día con sus
// bloqueantes (rojo: la fila no se puede marcar) y avisos (ámbar: se despacha confirmando). «Despachar seleccionadas» usa el lote del servidor
// (siempre 200) y muestra el resultado de cada ruta con su error. Despachar no se deshace.
import { useMemo, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { EmptyState, IconRoute, IconTruck, Modal, Spinner, toast } from '../../kernel/ui'
import { useDispatchable, useDispatchBatch } from './dispatchApi'
import {
  batchSummary,
  COMMENT_MAX,
  isSelectable,
  MAX_BATCH_TRIPS,
  selectableIds,
  selectedWithWarnings,
  stopsLabel,
  type DispatchableTrip,
  type DispatchResultItem,
} from './dispatchRules'
import { TripIssues } from './TripIssues'

export function DispatchModal({ date, preselect, onClose }: { date: string; /** La ruta activa, si se puede despachar: viene marcada. */ preselect: string | null; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const list = useDispatchable(date, true)
  const batch = useDispatchBatch()
  const items = useMemo<DispatchableTrip[]>(() => list.data ?? [], [list.data])
  const [chosen, setChosen] = useState<string[] | null>(null)
  const [comment, setComment] = useState('')
  const [confirming, setConfirming] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [results, setResults] = useState<DispatchResultItem[] | null>(null)

  // la ruta activa llega marcada si se puede despachar; en cuanto el usuario toca una casilla manda su elección
  const mine = items.find((i) => i.trip?.publicId === preselect)
  const selected = chosen ?? (mine && isSelectable(mine) && mine.trip?.publicId ? [mine.trip.publicId] : [])

  // una ruta que dejó de estar en el selector (otro usuario la despachó) se desmarca sola
  const live = selected.filter((id) => items.some((i) => i.trip?.publicId === id && isSelectable(i)))
  const selectable = selectableIds(items)
  const allChecked = selectable.length > 0 && live.length === selectable.length
  const tooMany = live.length > MAX_BATCH_TRIPS
  const tooLong = comment.length > COMMENT_MAX
  const withWarnings = selectedWithWarnings(items, live)

  const toggle = (id: string, on: boolean) => {
    setConfirming(false)
    setChosen(on ? [...live, id] : live.filter((x) => x !== id))
  }

  const run = async () => {
    // las rutas con avisos se despachan confirmando: el primer clic pide la confirmación
    if (withWarnings.length > 0 && !confirming) {
      setConfirming(true)
      return
    }
    setError(null)
    try {
      const res = await batch.mutateAsync({ tripPublicIds: live, comment: comment.trim() || undefined })
      const rows = res.items ?? []
      setResults(rows)
      const sum = batchSummary(rows)
      if (sum.dispatched > 0) toast.success(t('dispatch.dispatch.done', { count: sum.dispatched }))
    } catch (err) {
      setError(applyProblemDetails(err).title)
    }
  }

  const sum = results ? batchSummary(results) : null

  return (
    <Modal
      open
      size="md"
      title={results ? t('dispatch.dispatch.resultTitle') : t('dispatch.dispatch.title')}
      onClose={onClose}
      dismissible={!batch.isPending}
      footer={
        results ? (
          <button type="button" className="btn flow" onClick={onClose}>
            {t('dispatch.closeButton')}
          </button>
        ) : (
          <>
            <button type="button" className="btn" onClick={onClose} disabled={batch.isPending}>
              {t('common.cancel')}
            </button>
            {items.length > 0 && (
              <button type="button" className="btn flow" onClick={() => void run()} disabled={live.length === 0 || tooMany || tooLong || batch.isPending}>
                <IconTruck />{' '}
                {batch.isPending
                  ? t('common.loading')
                  : confirming
                    ? t('dispatch.dispatch.confirmWithWarnings', { count: live.length })
                    : t('dispatch.dispatch.confirm', { count: live.length })}
              </button>
            )}
          </>
        )
      }
    >
      {error && (
        <div className="form-alert" role="alert">
          {error}
        </div>
      )}
      {results && sum ? (
        <>
          <p role="status">{t('dispatch.dispatch.summary', { dispatched: sum.dispatched, requested: sum.requested })}</p>
          <ul className="disp-results" aria-label={t('dispatch.dispatch.resultTitle')}>
            {results.map((r) => (
              <li key={r.tripPublicId} className={r.dispatched ? 'ok-line' : 'err-line'}>
                <b className="mono">{r.code}</b> — {r.dispatched ? t('dispatch.dispatch.ok') : (r.error ?? t('dispatch.dispatch.failed'))}
                {!r.dispatched && <TripIssues issues={r.issues} />}
              </li>
            ))}
          </ul>
        </>
      ) : list.isPending ? (
        <Spinner block label={t('common.loading')} />
      ) : list.isError ? (
        <div className="form-alert" role="alert">
          {applyProblemDetails(list.error).title}
        </div>
      ) : items.length === 0 ? (
        <EmptyState icon={<IconRoute />} title={t('dispatch.dispatch.none')} />
      ) : (
        <>
          <p className="disp-note">{t('dispatch.dispatch.hint', { date: f.date(date) })}</p>
          <div className="disp-modal-list">
            <label className="disp-pick">
              <input
                type="checkbox"
                checked={allChecked}
                disabled={selectable.length === 0}
                onChange={(e) => {
                  setConfirming(false)
                  setChosen(e.target.checked ? selectable : [])
                }}
              />
              <b>{t('dispatch.dispatch.selectAll')}</b>
            </label>
            {items.map((i) => {
              const id = i.trip?.publicId ?? ''
              const ok = isSelectable(i)
              const detail = [i.trip?.driverName ?? i.trip?.driverCode, i.trip?.zoneCode, stopsLabel(t, i.trip?.stopCount ?? 0)].filter(Boolean).join(' · ')
              return (
                <label key={id} className={ok ? 'disp-pick' : 'disp-pick off'}>
                  <input type="checkbox" checked={live.includes(id)} disabled={!ok} aria-label={i.trip?.code ?? ''} onChange={(e) => toggle(id, e.target.checked)} />
                  <span className="body">
                    <span className="ref">{i.trip?.code}</span>
                    <span className="meta">{detail}</span>
                    <TripIssues issues={i.issues} />
                  </span>
                </label>
              )
            })}
          </div>
          {tooMany && <p className="ferr" role="alert">{t('dispatch.errors.batchMax')}</p>}
          {confirming && withWarnings.length > 0 && (
            <p className="disp-note" role="status">
              {t('dispatch.dispatch.warningsConfirm', { count: withWarnings.length })}
            </p>
          )}
          <div className="f" style={{ marginTop: 12 }}>
            <label htmlFor="dispatch-comment">{t('dispatch.dispatch.comment')}</label>
            <textarea id="dispatch-comment" rows={2} value={comment} onChange={(e) => setComment(e.target.value)} aria-invalid={tooLong || undefined} />
            {tooLong && <p className="ferr" role="alert">{t('status.commentTooLong', { max: COMMENT_MAX })}</p>}
          </div>
        </>
      )}
    </Modal>
  )
}
