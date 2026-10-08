// Lote 13 — panel derecho de Recibo (maqueta `recibo()`): "Detalle del recibo · REC-…" con su estatus y, a la derecha,
// "Origen · Orden de compra: PO-…" / "Origen · Cliente: …" / "Origen · Ciego"; lápiz (abre el modal del encabezado, el
// mismo del doble clic en la lista) e ícono "Historial" (historial de estatus en un modal). Debajo, los datos del
// encabezado, la rejilla de líneas (`ReceiptLinesEditor`), las notas de la maqueta ("Escanea o teclea…" y la de diferencia,
// con otro texto en ciegos: decisión 3) y el botón ancho "Confirmar recibo" (warehouse.receive; con confirmación;
// deshabilitado sin líneas, con filas sin guardar o con error, o si ya está confirmado, con el motivo debajo). Confirmado,
// el panel "Tareas de acomodo" (`ReceiptPutawayTasks`). Lectura: inventory.view + WMS_LOTSERIAL (por la ruta).
// Lote 16, recibo DIRECTO A POSICIÓN (modo del encabezado): chip "Directo a posición" junto al estatus, columna "Posición
// destino" en la rejilla, botón "Usar posiciones sugeridas" (warehouse.receive; abierto y con líneas sin destino: `POST
// /receipts/{id}/targets/suggest`, D3 — nada se llena solo) y "Confirmar recibo" bloqueado con "Falta la posición destino en
// {n} línea(s).". Al confirmar entra cada línea a su posición, sin tareas (Completado → Acomodado en el mismo momento).
import { useQueryClient } from '@tanstack/react-query'
import { useId, useState } from 'react'
import { Can, useCan } from '../../kernel/access'
import { ApiError } from '../../kernel/api/problem'
import { StatusChip, StatusHistory } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, EmptyState, IconCheck, IconClock, IconDoc, IconEdit, Modal, Panel, Spinner, toast } from '../../kernel/ui'
import { IconAlert } from '../../kernel/ui/icons'
import { useApplyReceiptTargetSuggestions, useConfirmReceipt, useReceipt, warehouseKeys, type ReceiptDetailDto } from './api'
import { formatDate, formatDateTime, lineErrorsByIndex } from './lineRules'
import { problemText } from './problemText'
import { hasDocument, RECEIPT_STATUS_DOMAIN, receiptOriginText } from './receiptFilters'
import { confirmBlockers, missingTargets, rowVariance } from './receiptLineEdit'
import { isDirectMode } from './receivingMode'
import { DamageReportModal } from './DamageReportModal'
import { ReceiptLinesEditor } from './ReceiptLinesEditor'
import { ReceiptPutawayTasks } from './ReceiptPutawayTasks'
import { useReceiptLineRows } from './useReceiptLineRows'

const ENTITY_TYPE = 'RECEIPT'

export interface ReceiptDetailPanelProps {
  publicId: string
  /** Abre el modal del encabezado (edición, o solo lectura si está confirmado o sin permiso). */
  onEditHeader: () => void
}

export function ReceiptDetailPanel({ publicId, onEditHeader }: ReceiptDetailPanelProps) {
  const t = useT()
  const { data: receipt, isLoading, error } = useReceipt(publicId)
  if (isLoading) {
    return (
      <Panel flush>
        <Spinner block />
      </Panel>
    )
  }
  if (error || !receipt?.header) {
    const notFound = error instanceof ApiError && error.code === 'not_found'
    return (
      <Panel flush>
        <EmptyState title={notFound ? t('warehouse.receipts.notFound') : (error?.message ?? t('errors.generic'))} />
      </Panel>
    )
  }
  return <ReceiptDetailBody key={publicId} receipt={receipt} onEditHeader={onEditHeader} />
}

function ReceiptDetailBody({ receipt, onEditHeader }: { receipt: ReceiptDetailDto; onEditHeader: () => void }) {
  const t = useT()
  const lang = useLang()
  const qc = useQueryClient()
  const hintId = useId()
  const canReceive = useCan('warehouse.receive')
  const confirm = useConfirmReceipt()
  const applySuggested = useApplyReceiptTargetSuggestions()
  const [confirming, setConfirming] = useState(false)
  const [history, setHistory] = useState(false)
  const [damaging, setDamaging] = useState(false)   // 2026-10-08: reportar lo que llegó dañado en este recibo
  const header = receipt.header ?? {}
  const publicId = header.publicId ?? ''
  const isOpen = header.isOpen === true
  const manual = !hasDocument(header.origin)
  const direct = isDirectMode(header.receivingModeCode)
  const lines = useReceiptLineRows(receipt, { manual, editable: isOpen && canReceive, direct })
  const blocker = confirmBlockers(lines.rows, isOpen, manual, direct)
  const missing = direct ? missingTargets(lines.rows) : 0
  const anySaving = lines.rows.some((r) => r.saving)
  const anyVariance = lines.rows.some((r) => (rowVariance(r, manual) ?? 0) !== 0)
  const tasks = receipt.putawayTasks ?? []

  const meta = [
    { k: 'warehouse', v: header.warehouseCode },
    { k: 'staging', v: header.defaultStagingBinCode },
    { k: 'dock', v: header.dockCode },
    { k: 'carrier', v: header.carrier },
    { k: 'reference', v: header.reference },
    { k: 'expectedDate', v: formatDate(header.expectedDate, lang) },
    { k: 'createdAt', v: formatDateTime(header.createdAtUtc, lang) },
    { k: 'receivedAt', v: formatDateTime(header.receivedAtUtc, lang) },
  ].filter((m) => m.v)

  const editLabel = isOpen && canReceive ? t('warehouse.receipts.detail.editHeader') : t('warehouse.receipts.detail.viewHeader')

  return (
    <div className="rcp-detail">
      <Panel
        flush
        icon={<IconDoc />}
        title={
          <>
            {t('warehouse.receipts.detail.title')} · <span className="ref">{header.number}</span>
            <span className="rcp-title-chips">
              <StatusChip domain={RECEIPT_STATUS_DOMAIN} code={header.statusCode} label={header.status} />
              {direct && <Chip tone="route">{t('warehouse.receipts.chip.direct')}</Chip>}
            </span>
          </>
        }
        actions={
          <>
            <span className="rcp-origin">{receiptOriginText(header, t)}</span>
            <Can perm="warehouse.damage">
              <button type="button" className="btn sm" onClick={() => setDamaging(true)}>
                {t('warehouse.damage.reportFromReceipt')}
              </button>
            </Can>
            <button type="button" className="rowbtn" aria-label={editLabel} title={editLabel} onClick={onEditHeader}>
              <IconEdit />
            </button>
            <button
              type="button"
              className="rowbtn"
              aria-label={t('warehouse.receipts.detail.history')}
              title={t('warehouse.receipts.detail.history')}
              onClick={() => setHistory(true)}
            >
              <IconClock />
            </button>
          </>
        }
      >
        {meta.length > 0 && (
          <dl className="rcp-meta">
            {meta.map((m) => (
              <div key={m.k}>
                <dt>{t(`warehouse.receipts.detail.meta.${m.k}`)}</dt>
                <dd>{m.v}</dd>
              </div>
            ))}
          </dl>
        )}

        <ReceiptLinesEditor receipt={receipt} state={lines} />

        {direct && isOpen && missing > 0 && (
          <Can perm="warehouse.receive">
            <div className="rcp-direct-acts">
              <button
                type="button"
                className="btn sm"
                disabled={applySuggested.isPending || anySaving}
                onClick={async () => {
                  // rowVersion de la caché al enviar (cambia con cada línea guardada)
                  const cached = qc.getQueryData<ReceiptDetailDto>([warehouseKeys.receipt[0], publicId])
                  try {
                    const res = await applySuggested.mutateAsync({ publicId, body: { rowVersion: cached?.rowVersion ?? receipt.rowVersion ?? null } })
                    toast.success(t('warehouse.receipts.detail.suggestedApplied', { n: res.assigned ?? 0, m: res.withoutSuggestion ?? 0 }))
                  } catch (err) {
                    if (err instanceof ApiError && err.code === 'conflict') void qc.invalidateQueries({ queryKey: [warehouseKeys.receipt[0], publicId] })
                    toast.error(problemText(err))
                  }
                }}
              >
                {applySuggested.isPending ? t('common.loading') : t('warehouse.receipts.detail.useSuggested')}
              </button>
            </div>
          </Can>
        )}

        {isOpen && canReceive && <div className="note rcp-note">{t('warehouse.receipts.detail.scanHint')}</div>}
        {isOpen && anyVariance && (
          <div className="note rcp-note rcp-note-money" role="status">
            <IconAlert />
            <span>{t(manual ? 'warehouse.receipts.detail.varianceNoteBlind' : 'warehouse.receipts.detail.varianceNote')}</span>
          </div>
        )}
        <Can perm="warehouse.receive">
          <div className="rcp-confirm">
            <button
              type="button"
              className="btn flow block"
              disabled={blocker !== null}
              aria-describedby={blocker ? hintId : undefined}
              onClick={() => setConfirming(true)}
            >
              <IconCheck /> {t('warehouse.receipts.detail.confirm')}
            </button>
            {blocker && (
              <p className="help" id={hintId}>
                {t(`warehouse.receipts.detail.blockers.${blocker}`, { n: missing })}
              </p>
            )}
          </div>
        </Can>
      </Panel>

      {!isOpen && tasks.length > 0 && <ReceiptPutawayTasks tasks={tasks} queueLink />}

      <DamageReportModal
        open={damaging}
        onClose={() => setDamaging(false)}
        receipt={header.warehousePublicId ? { publicId, number: header.number ?? '', warehousePublicId: header.warehousePublicId } : undefined}
      />

      <Modal open={history} size="md" title={t('warehouse.receipts.detail.historyTitle', { number: header.number ?? '' })} onClose={() => setHistory(false)}>
        {history && header.id != null && <StatusHistory entityType={ENTITY_TYPE} entityId={header.id} domain={RECEIPT_STATUS_DOMAIN} />}
      </Modal>

      <ConfirmDialog
        open={confirming}
        title={t('warehouse.receipts.detail.confirmTitle')}
        message={t(direct ? 'warehouse.receipts.detail.confirmBodyDirect' : 'warehouse.receipts.detail.confirmBody', { number: header.number ?? '' })}
        confirmLabel={t('warehouse.receipts.detail.confirm')}
        onConfirm={async () => {
          // rowVersion de la caché al enviar: cambia con cada línea guardada
          const cached = qc.getQueryData<ReceiptDetailDto>([warehouseKeys.receipt[0], publicId])
          try {
            await confirm.mutateAsync({ publicId, body: { rowVersion: cached?.rowVersion ?? receipt.rowVersion ?? null } })
          } catch (err) {
            // 400 con errores por línea (`lines[i]`, en el orden de las líneas por id): se muestran en su fila
            const byIndex = lineErrorsByIndex(err)
            const ids = (receipt.lines ?? []).map((l) => l.id ?? 0).sort((a, b) => a - b)
            const byId = new Map<number, string[]>()
            for (const [i, msgs] of Object.entries(byIndex)) {
              const id = ids[Number(i)]
              if (id) byId.set(id, msgs)
            }
            if (byId.size > 0) lines.setLineErrors(byId)
            if (err instanceof ApiError && err.code === 'conflict') void qc.invalidateQueries({ queryKey: [warehouseKeys.receipt[0], publicId] })
            throw err
          }
          toast.success(t('warehouse.receipts.detail.confirmed', { number: header.number ?? '' }))
        }}
        onClose={() => setConfirming(false)}
      />
    </div>
  )
}
