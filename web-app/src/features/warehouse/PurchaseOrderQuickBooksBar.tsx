// Lote 246 (Lote F) — franja «QuickBooks» de la ficha de una orden de compra: de dónde nació (origen), su estado frente a QuickBooks con quién y cuándo, las acciones de ESA orden
// (purchasing.quickbooks) y el aviso de que editarla o cancelarla no pasa solo a QuickBooks. Ninguna acción cambia el estatus de la orden.
import { useState } from 'react'
import { Can } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, toast } from '../../kernel/ui'
import type { PurchaseOrderDto } from './api'
import { usePurchaseQbMutations } from './purchaseQuickBooksApi'
import { originOf, ORIGIN_TONE, qbStateOf, QB_STATE_TONE, touchedQuickBooks } from './purchaseQuickBooksRules'
import { PurchaseOrderQuickBooksModal } from './PurchaseOrderQuickBooksModal'
import './purchaseQuickBooks.css'

export function PurchaseOrderQuickBooksBar({ po }: { po: PurchaseOrderDto }) {
  const t = useT()
  const f = useFormat()
  const { confirm, notPassed, mark, unmark } = usePurchaseQbMutations()
  const [opening, setOpening] = useState(false)
  const [asking, setAsking] = useState<'mark' | 'unmark' | null>(null)
  const state = qbStateOf(po.quickBooksState)
  const origin = originOf(po.originCode)
  const id = po.publicId ?? ''
  const busy = confirm.isPending || notPassed.isPending || mark.isPending || unmark.isPending
  const run = (p: Promise<unknown>, okKey: string) =>
    p.then(
      () => toast.success(t(okKey, { number: po.number ?? '' })),
      (e) => toast.error(applyProblemDetails(e).title),
    )

  const who =
    state === 'IN_QUICKBOOKS' && po.quickBooksAtUtc
      ? t('purchaseQb.detail.markedBy', { date: f.dateTime(po.quickBooksAtUtc), name: po.quickBooksByName ?? '—' })
      : state === 'EXPORTED' && po.quickBooksExportedAtUtc
        ? t('purchaseQb.detail.exportedBy', { date: f.dateTime(po.quickBooksExportedAtUtc), name: po.quickBooksExportedByName ?? '—' })
        : null

  return (
    <>
      <div className="pqb-detail" data-testid="pqb-detail">
        <span className="pqb-chips">
          <Chip tone={ORIGIN_TONE[origin]}>{po.origin || t(`purchaseQb.origin.${origin}`)}</Chip>
          <Chip tone={QB_STATE_TONE[state]}>{t(`purchaseQb.state.${state}`)}</Chip>
        </span>
        {who && <span className="pqb-line">{who}</span>}
        <Can perm="purchasing.quickbooks">
          {(state === 'PENDING' || state === 'EXPORTED') && (
            <button type="button" className="btn sm money" disabled={busy} onClick={() => setOpening(true)}>{t('purchaseQb.detail.pass')}</button>
          )}
          {state === 'EXPORTED' && (
            <>
              <button type="button" className="btn sm flow" disabled={busy} onClick={() => void run(confirm.mutateAsync([id]), 'purchaseQb.detail.confirmed')}>{t('purchaseQb.detail.confirm')}</button>
              <button type="button" className="btn sm" disabled={busy} onClick={() => void run(notPassed.mutateAsync([id]), 'purchaseQb.detail.notPassedDone')}>{t('purchaseQb.detail.notPassed')}</button>
            </>
          )}
          {state === 'PENDING' && <button type="button" className="btn sm" disabled={busy} onClick={() => setAsking('mark')}>{t('purchaseQb.detail.mark')}</button>}
          {state === 'IN_QUICKBOOKS' && <button type="button" className="btn sm" disabled={busy} onClick={() => setAsking('unmark')}>{t('purchaseQb.detail.unmark')}</button>}
        </Can>
      </div>
      {touchedQuickBooks(state) && <p className="note pqb-edit-warn" role="status">{t('purchaseQb.detail.editWarning')}</p>}
      {opening && <PurchaseOrderQuickBooksModal onlyId={id} onClose={() => setOpening(false)} />}
      <ConfirmDialog
        open={asking !== null}
        tone={asking === 'unmark' ? 'danger' : 'flow'}
        title={t(asking === 'unmark' ? 'purchaseQb.detail.unmarkTitle' : 'purchaseQb.detail.markTitle', { number: po.number ?? '' })}
        message={t(asking === 'unmark' ? 'purchaseQb.detail.unmarkBody' : 'purchaseQb.detail.markBody')}
        confirmLabel={t(asking === 'unmark' ? 'purchaseQb.detail.unmark' : 'purchaseQb.detail.mark')}
        onConfirm={async () => {
          if (asking === 'unmark') await run(unmark.mutateAsync([id]), 'purchaseQb.detail.unmarked')
          else await run(mark.mutateAsync([id]), 'purchaseQb.detail.markedOne')
          setAsking(null)
        }}
        onClose={() => setAsking(null)}
      />
    </>
  )
}
