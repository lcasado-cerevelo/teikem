// Pieza "Recolección y empaque" (Lote 13) — ficha de una recolección en un modal, al hacer clic en una fila de la lista
// (así no se pierde el borrador del panel "Recolección"). Pide `GET /api/v1/pick-batches/{publicId}` y pinta
// `PickBatchDetailBody`; el pie lleva Cerrar, Eliminar y Empacar (`PickBatchDetailActions`). Empacar y Eliminar abren su
// diálogo encima: mientras están abiertos este modal no se cierra con Escape ni con clic fuera. Eliminar cierra el modal.
import { useState } from 'react'
import { ApiError } from '../../kernel/api/problem'
import { StatusChip } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { Chip, EmptyState, Modal, Spinner } from '../../kernel/ui'
import { usePickBatch, type PickBatchDto } from './api'
import { DeletePickBatchDialog } from './DeletePickBatchDialog'
import { PackModal } from './PackModal'
import { PickBatchDetailActions, PickBatchDetailBody } from './PickBatchDetailBody'
import { PICK_BATCH_STATUS_DOMAIN } from './pickBatchView'

export interface PickBatchDetailModalProps {
  /** publicId de la recolección (null = cerrado). */
  publicId: string | null
  onClose: () => void
}

export function PickBatchDetailModal({ publicId, onClose }: PickBatchDetailModalProps) {
  const t = useT()
  const { data: batch, isLoading, error } = usePickBatch(publicId)
  const [packing, setPacking] = useState<PickBatchDto | null>(null)
  const [deleting, setDeleting] = useState<PickBatchDto | null>(null)
  const busy = packing !== null || deleting !== null

  const notFound = error instanceof ApiError && error.code === 'not_found'
  const title = batch ? (
    <span className="collect-dtitle">
      <span className="ref">{batch.number}</span> · {batch.warehouseCode}{' '}
      <StatusChip domain={PICK_BATCH_STATUS_DOMAIN} code={batch.statusCode} label={batch.status} />
      {batch.isActive === false && <Chip tone="fail">{t('warehouse.pickBatches.deletedChip')}</Chip>}
    </span>
  ) : (
    t('warehouse.pickBatches.detailTitle')
  )

  return (
    <>
      <Modal
        open={publicId !== null}
        size="lg"
        title={title}
        onClose={onClose}
        dismissible={!busy}
        footer={
          <>
            <button type="button" className="btn" onClick={onClose} disabled={busy}>
              {t('warehouse.pickBatches.detail.close')}
            </button>
            {batch && batch.publicId === publicId && (
              <PickBatchDetailActions batch={batch} onPack={() => setPacking(batch)} onDelete={() => setDeleting(batch)} />
            )}
          </>
        }
      >
        {isLoading ? (
          <Spinner block />
        ) : error || !batch ? (
          <EmptyState title={notFound ? t('warehouse.pickBatches.notFound') : (error?.message ?? t('errors.generic'))} />
        ) : (
          <>
            <p className="collect-dsub">{batch.clientName ?? t('warehouse.pickBatches.own')}</p>
            <PickBatchDetailBody batch={batch} variant="modal" />
          </>
        )}
      </Modal>
      {packing && <PackModal batch={packing} onClose={() => setPacking(null)} />}
      <DeletePickBatchDialog batch={deleting} onClose={() => setDeleting(null)} onDeleted={onClose} />
    </>
  )
}
