// Lote 14 — detalle de SOLO LECTURA de un movimiento del Kárdex (`GET /api/v1/inventory/transactions/{id}`, inventory.view +
// WMS_LOTSERIAL): fecha y hora, usuario, tipo (chip de color), producto, dueño, categoría, cantidad con signo, de → a, lote
// (vence), serie, motivo y nota; el documento de origen (número, estatus, fecha, parte, referencia; la tarea de almacén con
// su documento padre) con "Abrir" solo si el usuario tiene el permiso y el módulo de esa pantalla; y los movimientos
// relacionados (los del mismo documento o, sin documento, los del mismo asiento; tope 200). Lo abren el Kárdex, la pantalla
// Transferencias y ajustes y los descuadres (clic en una fila). Los errores del API (404 'Movimiento no encontrado.') se
// muestran tal cual.
import { useMemo, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { useAccess, permAllowed } from '../../kernel/access'
import { useLang, useT } from '../../kernel/i18n'
import { Chip, DataTable, Modal, Spinner, type DataColumn } from '../../kernel/ui'
import { useInventoryTransaction, type KardexDocumentDto, type KardexRowDto } from './api'
import { documentLink, fromToText, lotSerialText, movementOrigin, movementQtyView, splitDateTime, txnTypeTone } from './kardexView'
import { formatDate, formatDateTime } from './lineRules'
import './warehouse.css'

export interface KardexTransactionModalProps {
  /** id del movimiento (null = cerrado). */
  txnId: number | null
  onClose: () => void
}

export function KardexTransactionModal({ txnId, onClose }: KardexTransactionModalProps) {
  const t = useT()
  const { data, isLoading, error } = useInventoryTransaction(txnId, { handleAccessDenied: false })
  const row = data?.transaction
  return (
    <Modal open={txnId != null} size="lg" title={t('warehouse.kardexDetail.title', { id: txnId ?? '' })} onClose={onClose}
      footer={
        <button type="button" className="btn" onClick={onClose}>
          {t('warehouse.kardexDetail.close')}
        </button>
      }
    >
      {isLoading && <Spinner block label={t('common.loading')} />}
      {error && (
        <p className="ferr" role="alert">
          {error.message}
        </p>
      )}
      {data && row && (
        <>
          <TransactionFacts row={row} ownerName={data.ownerName ?? row.ownerName} categoryName={data.categoryName ?? row.categoryName} lotExpiry={data.lotExpiryDate} />
          <DocumentBox doc={data.document} onNavigate={onClose} />
          <h3 className="kx-h3">{t('warehouse.kardexDetail.related', { count: data.related?.length ?? 0 })}</h3>
          <RelatedTable rows={data.related ?? []} currentId={row.id ?? 0} />
          {data.relatedTruncated && <p className="help">{t('warehouse.kardexDetail.relatedTruncated')}</p>}
        </>
      )}
    </Modal>
  )
}

function TransactionFacts({
  row,
  ownerName,
  categoryName,
  lotExpiry,
}: {
  row: KardexRowDto
  ownerName?: string | null
  categoryName?: string | null
  lotExpiry?: string | null
}) {
  const t = useT()
  const lang = useLang()
  const when = splitDateTime(row.createdAtUtc, lang)
  const q = movementQtyView(row, lang)
  const dash = '—'
  const facts: { key: string; label: string; value: ReactNode }[] = [
    { key: 'when', label: t('warehouse.kardexDetail.when'), value: `${when.date} ${when.time}`.trim() || dash },
    { key: 'user', label: t('warehouse.kardexDetail.user'), value: row.userName || t('warehouse.kardexDetail.system') },
    { key: 'type', label: t('warehouse.kardexDetail.type'), value: <Chip tone={txnTypeTone(row.typeCode)}>{row.type ?? row.typeCode}</Chip> },
    {
      key: 'product',
      label: t('warehouse.kardexDetail.product'),
      value: (
        <>
          <span className="ref">{row.sku}</span> · {row.productName}
        </>
      ),
    },
    { key: 'owner', label: t('warehouse.kardexDetail.owner'), value: ownerName || dash },
    { key: 'category', label: t('warehouse.kardexDetail.category'), value: categoryName || dash },
    { key: 'qty', label: t('warehouse.kardexDetail.quantity'), value: <span className={`mono ${q.className}`}>{q.text}</span> },
    { key: 'fromTo', label: t('warehouse.kardexDetail.fromTo'), value: <span className="mono">{fromToText(row) || dash}</span> },
    {
      key: 'lot',
      label: t('warehouse.kardexDetail.lot'),
      value: row.lotNumber ? `${row.lotNumber}${lotExpiry ? ` · ${t('warehouse.kardexDetail.expires', { date: formatDate(lotExpiry, lang) })}` : ''}` : dash,
    },
    { key: 'serial', label: t('warehouse.kardexDetail.serial'), value: row.serialNumber || dash },
    { key: 'reason', label: t('warehouse.kardexDetail.reason'), value: row.reason || row.reasonCode || dash },
    { key: 'origin', label: t('warehouse.kardexDetail.origin'), value: t(`warehouse.kardexView.origin.${movementOrigin(row.refEntityCode)}`) },
  ]
  return (
    <>
      <dl className="kx-facts">
        {facts.map((f) => (
          <div key={f.key}>
            <dt>{f.label}</dt>
            <dd>{f.value}</dd>
          </div>
        ))}
      </dl>
      <dl className="kx-facts">
        <div style={{ gridColumn: '1 / -1' }}>
          <dt>{t('warehouse.kardexDetail.notes')}</dt>
          <dd>{row.notes || dash}</dd>
        </div>
      </dl>
    </>
  )
}

/** Documento de origen con su botón "Abrir" (si hay pantalla y el usuario tiene su permiso y módulo). */
function DocumentBox({ doc, onNavigate }: { doc: KardexDocumentDto | null | undefined; onNavigate: () => void }) {
  const t = useT()
  if (!doc) {
    return (
      <div className="kx-doc">
        <div className="kx-doc-main">
          <small className="kx-ref">{t('warehouse.kardexDetail.document')}</small>
          <b>{t('warehouse.kardexDetail.noDocument')}</b>
        </div>
      </div>
    )
  }
  return (
    <>
      <DocumentRow doc={doc} onNavigate={onNavigate} title={t('warehouse.kardexDetail.document')} />
      {doc.parent && <DocumentRow doc={doc.parent} onNavigate={onNavigate} title={t('warehouse.kardexDetail.parentDocument')} />}
    </>
  )
}

function DocumentRow({ doc, onNavigate, title }: { doc: KardexDocumentDto; onNavigate: () => void; title: string }) {
  const t = useT()
  const lang = useLang()
  const { permissions, modules } = useAccess()
  // la tarea de almacén no tiene pantalla propia: su "Abrir" es el del documento padre (se pinta en su propio renglón)
  const link = doc.entityCode?.toUpperCase() === 'WAREHOUSE_TASK' ? null : documentLink(doc)
  const canOpen = link != null && permAllowed(link.perm, permissions) && modules.has(link.module)
  const meta = [doc.dateUtc ? formatDateTime(doc.dateUtc, lang) : '', doc.partyName ?? '', doc.reference ?? ''].filter(Boolean).join(' · ')
  return (
    <div className="kx-doc">
      <div className="kx-doc-main">
        <small className="kx-ref">
          {title} · {doc.entityLabel ?? doc.entityCode}
        </small>
        <b>{doc.number || `#${doc.id}`}</b>
        {meta && <span className="kx-doc-meta">{meta}</span>}
      </div>
      {doc.status && <Chip>{doc.status}</Chip>}
      {canOpen && link && (
        <Link className="btn sm flow" to={link.to} onClick={onNavigate}>
          {t('warehouse.kardexDetail.open')}
        </Link>
      )}
    </div>
  )
}

function RelatedTable({ rows, currentId }: { rows: KardexRowDto[]; currentId: number }) {
  const t = useT()
  const lang = useLang()
  const columns = useMemo<DataColumn<KardexRowDto>[]>(
    () => [
      {
        id: 'when',
        header: t('warehouse.kardexDetail.when'),
        cell: (r) => {
          const d = splitDateTime(r.createdAtUtc, lang)
          return `${d.date} ${d.time}`
        },
        sortValue: (r) => r.createdAtUtc,
        card: 'title',
      },
      { id: 'type', header: t('warehouse.kardexDetail.type'), cell: (r) => <Chip tone={txnTypeTone(r.typeCode)}>{r.type ?? r.typeCode}</Chip>, sortValue: (r) => r.type ?? r.typeCode },
      { id: 'sku', header: t('warehouse.kardexDetail.sku'), cell: (r) => <span className="ref">{r.sku}</span>, sortValue: (r) => r.sku },
      {
        id: 'qty',
        header: t('warehouse.kardexDetail.quantity'),
        cell: (r) => {
          const q = movementQtyView(r, lang)
          return <span className={`mono ${q.className}`}>{q.text}</span>
        },
        sortValue: (r) => movementQtyView(r, lang).value,
        align: 'end',
        exportValue: (r) => movementQtyView(r, lang).value,
      },
      { id: 'fromTo', header: t('warehouse.kardexDetail.fromTo'), cell: (r) => fromToText(r), sortValue: (r) => fromToText(r) },
      { id: 'lot', header: t('warehouse.kardexDetail.lotSerial'), cell: (r) => lotSerialText(r), sortValue: (r) => lotSerialText(r) },
    ],
    [t, lang],
  )
  return (
    <DataTable
      label={t('warehouse.kardexDetail.relatedLabel')}
      columns={columns}
      rows={rows}
      rowKey={(r) => r.id ?? 0}
      rowClassName={(r) => (r.id === currentId ? 'kx-current' : undefined)}
      pageSize={10}
      exportable={false}
      dense
    />
  )
}
