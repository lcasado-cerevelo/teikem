// Pieza "Recolección y empaque" (Lote F6) — ficha de la recolección. `/warehouse/pick-batches/:publicId` (la ruta se conserva:
// la enlaza la Actividad reciente; desde la lista la ficha se abre en un modal, `PickBatchDetailModal`).
// Lectura: inventory.view + WMS_LOTSERIAL (por la ruta). Empacar: warehouse.pick + orders.create (`PackModal`: crea la orden
// real con el número de la recolección como número de empaque; predeterminados de la compañía en Tipo de servicio y de
// paquete). Eliminar: warehouse.pick, y si ya está PACKED además orders.cancel (`DeletePickBatchDialog`: revierte el
// inventario a su posición original; bloqueado si la orden ya avanzó — 422). Sin transición manual: COLLECTED → PACKED →
// CANCELLED se disparan desde las acciones. Cuerpo compartido con el modal: `PickBatchDetailBody`. Manual 06 §7.
import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { ApiError } from '../../kernel/api/problem'
import { StatusChip } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { Chip, EmptyState, Spinner } from '../../kernel/ui'
import { usePickBatch, type PickBatchDto } from './api'
import { DeletePickBatchDialog } from './DeletePickBatchDialog'
import { PackModal } from './PackModal'
import { PickBatchDetailActions, PickBatchDetailBody } from './PickBatchDetailBody'
import { PICK_BATCH_STATUS_DOMAIN } from './pickBatchView'

export default function PickBatchDetailScreen() {
  const t = useT()
  const navigate = useNavigate()
  const { publicId = '' } = useParams()
  const { data: batch, isLoading, error } = usePickBatch(publicId)
  const [packing, setPacking] = useState(false)
  const [deleting, setDeleting] = useState<PickBatchDto | null>(null)

  if (isLoading) return <Spinner block />
  if (error || !batch) {
    const notFound = error instanceof ApiError && error.code === 'not_found'
    return (
      <EmptyState
        title={notFound ? t('warehouse.pickBatches.notFound') : (error?.message ?? t('errors.generic'))}
        action={
          <Link className="btn" to="/warehouse/pick-batches">
            {t('warehouse.pickBatches.back')}
          </Link>
        }
      />
    )
  }

  return (
    <div className="wrap">
      <div className="head">
        <div style={{ minWidth: 0 }}>
          <h1>
            <span className="ref">{batch.number}</span> · {batch.warehouseCode}
          </h1>
          <p>
            <StatusChip domain={PICK_BATCH_STATUS_DOMAIN} code={batch.statusCode} label={batch.status} />
            {batch.isActive === false && (
              <>
                {' '}
                <Chip tone="fail">{t('warehouse.pickBatches.deletedChip')}</Chip>
              </>
            )}{' '}
            {batch.clientName ?? t('warehouse.pickBatches.own')}
          </p>
        </div>
        <div className="act">
          <PickBatchDetailActions batch={batch} onPack={() => setPacking(true)} onDelete={() => setDeleting(batch)} />
        </div>
      </div>

      <PickBatchDetailBody batch={batch} />

      {packing && <PackModal batch={batch} onClose={() => setPacking(false)} />}
      <DeletePickBatchDialog batch={deleting} onClose={() => setDeleting(null)} onDeleted={() => navigate('/warehouse/pick-batches')} />
    </div>
  )
}
