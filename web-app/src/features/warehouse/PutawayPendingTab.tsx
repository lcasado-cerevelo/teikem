// Lote 13 — pestaña 'Acomodo pendiente' de Recibo (`?tab=putaway`, decisión 5): LISTA DE RECIBOS Completados o Completados
// con diferencia que aún tienen tareas de acomodo por cerrar (`GET /receipts?phase=PENDING_PUTAWAY`), con los mismos filtros
// que Recibos (Estatus limitado a esos dos) y el mismo maestro-detalle; a la derecha, las tareas de acomodo del recibo
// elegido (`?receipt=`) con asignar, iniciar, completar y cancelar (`ReceiptPutawayTasks`). Al cerrar la última tarea el
// recibo pasa a Acomodado y sale de la lista. Doble clic: el encabezado (solo lectura: ya está confirmado).
import { useMemo } from 'react'
import { useT } from '../../kernel/i18n'
import { EmptyState, IconCheckin, Panel, Spinner } from '../../kernel/ui'
import { useReceipt, type ReceiptListItemDto } from './api'
import { ReceiptFilterBar } from './ReceiptFilterBar'
import { ReceiptMasterList } from './ReceiptMasterList'
import { PUTAWAY_PENDING_STATUSES, selectedReceiptId } from './receiptFilters'
import { ReceiptPutawayTasks } from './ReceiptPutawayTasks'
import { useReceiptList } from './useReceiptList'

const NO_ITEMS: ReceiptListItemDto[] = []

export interface PutawayPendingTabProps {
  /** `?receipt=` de la URL. */
  selectedParam: string | null
  onSelect: (publicId: string) => void
  onOpenHeader: (publicId: string) => void
}

export function PutawayPendingTab({ selectedParam, onSelect, onOpenHeader }: PutawayPendingTabProps) {
  const t = useT()
  const l = useReceiptList('PENDING_PUTAWAY')
  const items = l.list.data?.items ?? NO_ITEMS
  const selected = selectedReceiptId(items, selectedParam)
  const detail = useReceipt(selected)
  const statusCodes = useMemo(() => [...PUTAWAY_PENDING_STATUSES], [])

  let right
  if (!selected)
    right = (
      <Panel flush>
        <div className="empty rcp-empty lg">
          <div>
            <IconCheckin />
            <p>{t('warehouse.receipts.putawayTab.select')}</p>
          </div>
        </div>
      </Panel>
    )
  else if (detail.isLoading)
    right = (
      <Panel flush>
        <Spinner block />
      </Panel>
    )
  else if (detail.error || !detail.data)
    right = (
      <Panel flush>
        <EmptyState title={detail.error?.message ?? t('warehouse.receipts.notFound')} />
      </Panel>
    )
  else
    right = (
      <ReceiptPutawayTasks
        tasks={detail.data.putawayTasks}
        title={
          <>
            {t('warehouse.receipts.putawayTab.tasksTitle')} · <span className="ref">{detail.data.header?.number}</span>
          </>
        }
      />
    )

  return (
    <>
      <ReceiptFilterBar value={l.filters} onChange={l.setFilters} statusCodes={statusCodes} />
      <div className="rcp-cols">
        <ReceiptMasterList
          title={t('warehouse.receipts.putawayTab.listTitle')}
          items={items}
          total={l.list.data?.total}
          loading={l.list.isLoading}
          error={l.list.error}
          selectedId={selected}
          onSelect={onSelect}
          onOpen={onOpenHeader}
          q={l.q}
          onQ={l.setQ}
          page={l.page}
          pageSize={l.pageSize}
          onPage={l.setPage}
          onPageSize={l.setPageSize}
          exportRows={l.exportRows}
        />
        <div className="rcp-side">{right}</div>
      </div>
    </>
  )
}
