// Despacho manual (2026-10-11) — estatus de una recolección en las vistas de Recolección y empaque: la de empaque usa el chip del
// dominio PickBatchStatus; un despacho manual (DMA) se muestra «Despachado» (COLLECTED) o «Cancelado» (eliminado con reversa).
import { StatusChip } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { Chip } from '../../kernel/ui'
import type { PickBatchDto } from './api'
import { isManualCancelled, manualStatusLabel } from './manualIssueView'
import { PICK_BATCH_STATUS_DOMAIN } from './pickBatchView'

export function PickBatchStatusChip({ batch }: { batch: Pick<PickBatchDto, 'statusCode' | 'status' | 'isActive' | 'isManual'> }) {
  const t = useT()
  if (!batch.isManual) return <StatusChip domain={PICK_BATCH_STATUS_DOMAIN} code={batch.statusCode} label={batch.status} />
  return (
    <Chip tone={isManualCancelled(batch) ? 'fail' : 'deliv'}>
      {manualStatusLabel(batch, t('warehouse.manualIssues.status.dispatched'), t('warehouse.manualIssues.status.cancelled'))}
    </Chip>
  )
}
