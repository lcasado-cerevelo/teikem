// Pieza "Recolección y empaque" — Eliminar una recolección (`DELETE /api/v1/pick-batches/{publicId}`, warehouse.pick y, si
// ya está PACKED, además orders.cancel: se borra también su orden). El inventario vuelve a su posición original con un
// movimiento de reversa; bloqueado (422) si la orden ya avanzó de su etapa inicial. Lo abren la acción de fila "Eliminar"
// de la lista (`PickBatchesPanel`) y la ficha (`PickBatchDetailBody`). Un 409 (rowVersion) recarga la ficha y la lista y el
// mensaje queda en el diálogo. Manual 06 §7.
import { useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { ConfirmDialog, toast } from '../../kernel/ui'
import { useDeletePickBatch, warehouseKeys, type PickBatchDto } from './api'

export type DeletableBatch = Pick<PickBatchDto, 'publicId' | 'number' | 'statusCode' | 'orderNumber' | 'rowVersion'>

export interface DeletePickBatchDialogProps {
  /** Recolección a eliminar (null = cerrado). */
  batch: DeletableBatch | null
  onClose: () => void
  /** Tras eliminar (el diálogo ya avisó con un toast). */
  onDeleted?: () => void
}

export function DeletePickBatchDialog({ batch, onClose, onDeleted }: DeletePickBatchDialogProps) {
  const t = useT()
  const qc = useQueryClient()
  const del = useDeletePickBatch()
  const packed = batch?.statusCode === 'PACKED'
  const number = batch?.number ?? ''
  return (
    <ConfirmDialog
      open={batch !== null}
      tone="danger"
      title={t('warehouse.pickBatches.detail.deleteTitle')}
      message={
        packed
          ? t('warehouse.pickBatches.detail.deletePackedBody', { number, order: batch?.orderNumber ?? '' })
          : t('warehouse.pickBatches.detail.deleteBody', { number })
      }
      confirmLabel={t('warehouse.pickBatches.detail.delete')}
      onConfirm={async () => {
        if (!batch) return
        try {
          await del.mutateAsync({ publicId: batch.publicId ?? '', body: { rowVersion: batch.rowVersion ?? null } })
        } catch (err) {
          if (err instanceof ApiError && err.code === 'conflict') {
            void qc.invalidateQueries({ queryKey: warehouseKeys.pickBatch })
            void qc.invalidateQueries({ queryKey: warehouseKeys.pickBatches })
          }
          throw err
        }
        toast.success(t('warehouse.pickBatches.detail.deleted', { number }))
        // ConfirmDialog cierra solo (onClose) al terminar bien
        onDeleted?.()
      }}
      onClose={onClose}
    />
  )
}
