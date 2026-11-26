// Lote 249 — «Unir al envío de OV-…»: una recolección recolectada de una orden de venta sale en el MISMO envío que otra recolección de su familia (la orden, su raíz y las
// adiciones de la raíz), p. ej. la de una orden interna de adición. Si el destino ya está empacado hace falta reabrir su empaque (warehouse.reopen-pack, con motivo).
import { useQueries } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useCan } from '../../kernel/access'
import { api, unwrap } from '../../kernel/api/client'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Modal, Spinner, toast } from '../../kernel/ui'
import { salesKeys, useSalesOrder, type SalesOrderDto } from '../sales/salesApi'
import { useJoinOrderShipment, type PickBatchDto } from './api'

interface Target {
  batchPublicId: string
  number: string
  orderNumber: string
  status: string
}

/** Las recolecciones vivas de la familia de la orden (sin la propia): el destino posible del envío. */
function useFamilyTargets(batch: PickBatchDto) {
  const own = useSalesOrder(batch.salesOrderPublicId ?? null)
  const rootId = own.data ? own.data.parentPublicId ?? own.data.publicId ?? null : null
  const root = useSalesOrder(rootId && rootId !== own.data?.publicId ? rootId : null)
  const rootOrder: SalesOrderDto | undefined = rootId && rootId === own.data?.publicId ? own.data : root.data
  const others = useQueries({
    queries: (rootOrder?.additions ?? [])
      .filter((a) => a.publicId && a.publicId !== batch.salesOrderPublicId)
      .map((a) => ({ queryKey: salesKeys.one(a.publicId ?? ''), queryFn: () => unwrap(api.GET('/api/v1/sales-orders/{publicId}', { params: { path: { publicId: a.publicId ?? '' } } })) })),
  })
  const loading = own.isPending || (rootId !== null && rootId !== own.data?.publicId && root.isPending) || others.some((q) => q.isPending)
  const targets = useMemo(() => {
    const orders = [own.data, rootOrder, ...others.map((q) => q.data)].filter((o): o is SalesOrderDto => Boolean(o))
    const seen = new Set<string>()
    const list: Target[] = []
    for (const o of orders) {
      for (const d of o.dispatches ?? []) {
        if (!d.pickBatchPublicId || d.pickBatchPublicId === batch.publicId || d.status === 'CANCELLED' || seen.has(d.pickBatchPublicId)) continue
        seen.add(d.pickBatchPublicId)
        list.push({ batchPublicId: d.pickBatchPublicId, number: d.number ?? '', orderNumber: o.number ?? '', status: d.status ?? '' })
      }
    }
    return list
  }, [own.data, rootOrder, others, batch.publicId])
  return { loading, targets }
}

export function JoinOrderShipmentDialog({ batch, onClose }: { batch: PickBatchDto; onClose: () => void }) {
  const t = useT()
  const canReopen = useCan('warehouse.reopen-pack')
  const join = useJoinOrderShipment()
  const { loading, targets } = useFamilyTargets(batch)
  const [target, setTarget] = useState('')
  const [reopen, setReopen] = useState(false)
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  const chosen = targets.find((x) => x.batchPublicId === (target || (targets.length === 1 ? targets[0].batchPublicId : '')))
  const needsReopen = chosen?.status === 'PACKED'
  const submit = async () => {
    setError(null)
    if (!chosen) return setError(t('warehouse.joinShipment.pick'))
    if (needsReopen && !reopen) return setError(t('warehouse.joinShipment.needsReopen', { number: chosen.number }))
    if (needsReopen && reason.trim() === '') return setError(t('warehouse.reopenPack.reasonRequired'))
    try {
      await join.mutateAsync({ publicId: batch.publicId ?? '', body: { targetBatchPublicId: chosen.batchPublicId, reopenPack: needsReopen && reopen ? true : undefined, reason: needsReopen && reopen ? reason.trim() : undefined } })
      toast.success(t('warehouse.joinShipment.done', { number: batch.number ?? '' }))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }
  return (
    <Modal
      open
      size="md"
      title={t('warehouse.joinShipment.title', { order: batch.salesOrderNumber ?? '' })}
      onClose={onClose}
      dismissible={!join.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={join.isPending}>{t('common.cancel')}</button>
          <button type="button" className="btn flow" onClick={() => void submit()} disabled={join.isPending || loading || targets.length === 0}>{join.isPending ? t('common.loading') : t('warehouse.joinShipment.confirm')}</button>
        </>
      }
    >
      <div className="pb">
        <p>{t('warehouse.joinShipment.body', { number: batch.number ?? '' })}</p>
        {loading ? (
          <Spinner block />
        ) : targets.length === 0 ? (
          <p className="note" role="status">{t('warehouse.joinShipment.none')}</p>
        ) : (
          <>
            <div className="f">
              <label htmlFor="js-target">{t('warehouse.joinShipment.target')}</label>
              <select id="js-target" value={chosen?.batchPublicId ?? ''} onChange={(e) => setTarget(e.target.value)}>
                {targets.length > 1 && <option value="">{t('warehouse.joinShipment.pick')}</option>}
                {targets.map((x) => (
                  <option key={x.batchPublicId} value={x.batchPublicId}>
                    {x.orderNumber} · {x.number} ({t(`sales.dispatches.statuses.${x.status}`)})
                  </option>
                ))}
              </select>
            </div>
            {needsReopen &&
              (canReopen ? (
                <>
                  <label className="sw">
                    <input type="checkbox" role="switch" checked={reopen} onChange={(e) => setReopen(e.target.checked)} />
                    <span className="tk" aria-hidden="true" />
                    <span>{t('warehouse.joinShipment.reopen', { number: chosen?.number ?? '' })}</span>
                  </label>
                  {reopen && (
                    <div className="f">
                      <label htmlFor="js-reason">{t('warehouse.reopenPack.reason')}</label>
                      <textarea id="js-reason" rows={2} maxLength={500} value={reason} aria-required="true" onChange={(e) => setReason(e.target.value)} />
                    </div>
                  )}
                </>
              ) : (
                <p className="note" role="note">{t('warehouse.joinShipment.noPermission', { number: chosen?.number ?? '' })}</p>
              ))}
          </>
        )}
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}
