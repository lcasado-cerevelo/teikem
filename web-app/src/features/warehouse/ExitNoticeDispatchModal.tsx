// Lote 145 — despachar contra un aviso de salida: por línea con algo reservado se escribe cuánto sale (por omisión todo lo reservado; 0 = no sale esa línea).
// El servidor consume la reserva, saca el inventario y crea la recolección enlazada; la posición/lote son los reservados (que ya siguen la rotación). Permiso warehouse.pick.
import { useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Modal, toast } from '../../kernel/ui'
import { useExitNoticeMutations, type ExitNoticeDto } from './exitNoticeApi'
import { dispatchLines, dispatchQuantityIssue } from './exitNoticeRules'

export function ExitNoticeDispatchModal({ notice, onClose }: { notice: ExitNoticeDto; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const { dispatch } = useExitNoticeMutations()
  const lines = (notice.lines ?? []).filter((l) => (l.qtyReserved ?? 0) > 0)
  const [qtys, setQtys] = useState<Record<number, number | null>>(() => Object.fromEntries(lines.map((l) => [l.id!, l.qtyReserved ?? 0])))
  const [note, setNote] = useState('')
  const issues = Object.fromEntries(lines.map((l) => [l.id!, dispatchQuantityIssue(qtys[l.id!], l.qtyReserved ?? 0)]))
  const body = dispatchLines(qtys)
  const invalid = Object.values(issues).some(Boolean) || body.length === 0

  const submit = async () => {
    try {
      const r = await dispatch.mutateAsync({ id: notice.publicId!, body: { lines: body, note: note.trim() || null } })
      toast.success(t('warehouse.exitNotices.dispatched', { number: r.pickBatchNumber ?? '' }))
      onClose()
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="md"
      title={t('warehouse.exitNotices.dispatchTitle', { number: notice.number ?? '' })}
      onClose={onClose}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="button" className="btn flow" disabled={invalid || dispatch.isPending} onClick={() => void submit()}>
            {dispatch.isPending ? t('common.loading') : t('warehouse.exitNotices.dispatchConfirm')}
          </button>
        </>
      }
    >
      <p className="help">{t('warehouse.exitNotices.dispatchHelp')}</p>
      {lines.map((l) => {
        const issue = issues[l.id!]
        return (
          <div key={l.id} className="field">
            <label htmlFor={`dq-${l.id}`}>
              <b>{l.sku}</b> · {l.productName}
              {l.lotNumber ? ` · ${l.lotNumber}` : ''} — {t('warehouse.exitNotices.dispatchReserved', { qty: f.number(l.qtyReserved ?? 0) })}
            </label>
            <input
              id={`dq-${l.id}`}
              type="number"
              min={0}
              step="0.001"
              inputMode="decimal"
              value={qtys[l.id!] ?? ''}
              aria-invalid={issue ? true : undefined}
              onChange={(e) => setQtys((s) => ({ ...s, [l.id!]: e.target.value === '' ? null : Number(e.target.value) }))}
            />
            {issue && (
              <p className="ferr" role="alert">
                {t(issue)}
              </p>
            )}
          </div>
        )
      })}
      <div className="field">
        <label htmlFor="dispatch-note">{t('warehouse.exitNotices.dispatchNote')}</label>
        <input id="dispatch-note" type="text" maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} />
      </div>
    </Modal>
  )
}
