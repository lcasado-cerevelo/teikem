// Lote 242 — despachar una orden de venta DESDE «Recolección y empaque» (la ficha del documento): es el mismo despacho de Ventas → la orden → «Despachar» (consume la reserva, baja el
// inventario y crea la recolección EMP-##### enlazada a la orden), con la misma pregunta de después («¿Qué sigue con lo despachado?»: retiro, entrega o después). Reutiliza las ventanas de
// `OrderModal` para que haya UN solo despacho de órdenes de venta en la web. Permiso: warehouse.pick (lo exige el botón y el servidor).
import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Modal, Spinner, toast } from '../../kernel/ui'
import { invalidateStock, warehouseKeys } from '../warehouse/api'
import { PackModal } from '../warehouse/PackModal'
import { DispatchModal, NextDialog } from './OrderModal'
import { useSalesOrder, type SalesDispatchResultDto } from './salesApi'

export function SalesDispatchFlow({ orderPublicId, onClose, onDone }: { orderPublicId: string; onClose: () => void; onDone?: () => void }) {
  const t = useT()
  const qc = useQueryClient()
  const order = useSalesOrder(orderPublicId)
  const [next, setNext] = useState<SalesDispatchResultDto | null>(null)
  const [delivering, setDelivering] = useState<{ publicId: string; number: string } | null>(null)

  // al terminar (retiro, entrega o después) se refrescan los documentos, las recolecciones y el inventario de «Recolección y empaque»
  const finish = () => {
    void qc.invalidateQueries({ queryKey: warehouseKeys.pickBatches })
    void invalidateStock(qc)
    ;(onDone ?? onClose)()
  }

  if (order.isPending || order.isError) {
    return (
      <Modal open size="md" title={t('sales.loading')} onClose={onClose} footer={<button type="button" className="btn" onClick={onClose}>{t('sales.close')}</button>}>
        <div className="pb">{order.isError ? <p className="ferr" role="alert">{applyProblemDetails(order.error).title}</p> : <Spinner block />}</div>
      </Modal>
    )
  }
  const data = order.data
  if (delivering)
    return (
      <PackModal
        batch={{ publicId: delivering.publicId, number: delivering.number, clientName: null, rowVersion: null }}
        forSalesOrder={{ orderPublicId: data.publicId!, clientPublicId: data.clientPublicId!, consigneePublicId: data.consigneePublicId }}
        onClose={finish}
      />
    )
  if (next)
    return (
      <NextDialog
        result={next}
        onPickup={finish}
        onLater={finish}
        onDelivery={() => setDelivering({ publicId: next.pickBatchPublicId!, number: next.pickBatchNumber ?? '' })}
      />
    )
  return (
    <DispatchModal
      order={data}
      onClose={onClose}
      onDone={(r) => {
        toast.success(t('sales.dispatch.done', { number: r.pickBatchNumber ?? '' }))
        setNext(r)
      }}
    />
  )
}
