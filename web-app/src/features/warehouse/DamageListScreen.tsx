// Daños (2026-10-08): pestaña «Daños» de Productos e inventario (`/warehouse/products?tab=damage`; `/warehouse/damage` redirige). Lectura y todo lo demás: `warehouse.damage` (+ módulo WMS por la ruta). Lista de los reportes con filtro por
// estatus y origen y búsqueda libre (SKU, producto o DAN-#####); «Reportar daño» abre el modal; lo que está EN CUARENTENA se desecha o se recupera con
// los íconos de la fila. Clic en la fila no abre nada: la lista ya muestra todo (posición, causa, notas, quién y cuándo).
import { useMemo, useState } from 'react'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Can } from '../../kernel/access'
import { Chip, DataTable, Filters, Panel, QBox, SelectFilter, type DataColumn, type RowAction } from '../../kernel/ui'
import { IconRotateCcw, IconTrash } from '../../kernel/ui/actionIcons'
import { IconLayers } from '../../kernel/ui/screenIcons'
import { useDamageReports, type DamageReportDto } from './api'
import { DamageReportModal } from './DamageReportModal'
import { DamageResolveModal } from './DamageResolveModals'
import { formatNumber } from './lineRules'

const STATUS_COLORS: Record<string, string> = { REPORTED: '#9CA3AF', QUARANTINED: '#F59E0B', DISCARDED: '#EF4444', RECOVERED: '#059669' }
const STATUSES = ['QUARANTINED', 'DISCARDED', 'RECOVERED'] as const

export function DamagePanel() {
  const t = useT()
  const f = useFormat()
  const [status, setStatus] = useState('')
  const [origin, setOrigin] = useState('')
  const [q, setQ] = useState('')
  const [reporting, setReporting] = useState(false)
  const [resolving, setResolving] = useState<{ damage: DamageReportDto; action: 'discard' | 'recover' } | null>(null)
  const { data, isLoading, error } = useDamageReports({ status: status || undefined, origin: origin || undefined, q: q.trim() || undefined, take: 200 })
  const rows = data?.items ?? []

  const columns = useMemo<DataColumn<DamageReportDto>[]>(
    () => [
      { id: 'code', header: t('warehouse.damage.cols.code'), cell: (d) => d.code, sortValue: (d) => d.id, card: 'title' },
      { id: 'product', header: t('warehouse.damage.cols.product'), cell: (d) => `${d.sku ?? ''} · ${d.productName ?? ''}${d.lotNumber ? ` (${d.lotNumber})` : ''}`, sortValue: (d) => d.sku },
      { id: 'qty', header: t('warehouse.damage.cols.quantity'), cell: (d) => formatNumber(d.quantity ?? 0, f.lang), sortValue: (d) => d.quantity, align: 'end' },
      { id: 'origin', header: t('warehouse.damage.cols.origin'), cell: (d) => (d.receiptNumber ? `${d.origin} · ${d.receiptNumber}` : d.origin), sortValue: (d) => d.origin, card: 'hidden' },
      { id: 'cause', header: t('warehouse.damage.cols.cause'), cell: (d) => d.cause, sortValue: (d) => d.cause },
      { id: 'where', header: t('warehouse.damage.cols.where'), cell: (d) => [[d.fromBinCode, d.quarantineBinCode].filter(Boolean).join(' → '), d.isReserved && d.statusCode === 'QUARANTINED' ? t('warehouse.damage.reserved') : null].filter(Boolean).join(' · '), sortValue: (d) => d.quarantineBinCode ?? d.fromBinCode, card: 'hidden' },
      {
        id: 'status',
        header: t('warehouse.damage.cols.status'),
        cell: (d) => <Chip color={STATUS_COLORS[d.statusCode ?? ''] ?? '#9CA3AF'}>{d.status}</Chip>,
        sortValue: (d) => d.status,
      },
      { id: 'reported', header: t('warehouse.damage.cols.reported'), cell: (d) => f.dateTime(d.reportedAtUtc), sortValue: (d) => d.reportedAtUtc, card: 'hidden' },
      { id: 'by', header: t('warehouse.damage.cols.by'), cell: (d) => d.reportedByName ?? '', sortValue: (d) => d.reportedByName, card: 'hidden' },
      { id: 'final', header: t('warehouse.damage.cols.finalDestination'), cell: (d) => d.finalDestination ?? '', sortValue: (d) => d.finalDestination, card: 'hidden' },
      { id: 'notes', header: t('warehouse.damage.cols.notes'), cell: (d) => [d.notes, d.resolutionNotes].filter(Boolean).join(' · '), card: 'hidden' },
    ],
    [t, f],
  )

  const actions = useMemo<RowAction<DamageReportDto>[]>(
    () => [
      {
        key: 'discard',
        label: t('warehouse.damage.discard'),
        perm: 'warehouse.damage',
        visible: (d) => d.statusCode === 'QUARANTINED',
        tone: 'danger',
        icon: <IconTrash />,
        onClick: (d) => setResolving({ damage: d, action: 'discard' }),
      },
      {
        key: 'recover',
        label: t('warehouse.damage.recover'),
        perm: 'warehouse.damage',
        visible: (d) => d.statusCode === 'QUARANTINED',
        icon: <IconRotateCcw />,
        onClick: (d) => setResolving({ damage: d, action: 'recover' }),
      },
    ],
    [t],
  )

  return (
    <>
      <div className="head">
        <div>
          <h2>{t('warehouse.damage.title')}</h2>
          <p>{t('warehouse.damage.subtitle')}</p>
        </div>
        <div className="act">
          <Can perm="warehouse.damage">
            <button type="button" className="btn flow" onClick={() => setReporting(true)}>
              {t('warehouse.damage.report')}
            </button>
          </Can>
        </div>
      </div>

      <Filters
        onClear={() => {
          setStatus('')
          setOrigin('')
          setQ('')
        }}
      >
        <QBox value={q} onChange={setQ} placeholder={t('warehouse.damage.searchPlaceholder')} />
        <SelectFilter
          label={t('warehouse.damage.cols.status')}
          value={status}
          onChange={setStatus}
          allLabel={t('warehouse.damage.all')}
          options={STATUSES.map((s) => ({ value: s, label: t(`warehouse.damage.status.${s}`) }))}
        />
        <SelectFilter
          label={t('warehouse.damage.cols.origin')}
          value={origin}
          onChange={setOrigin}
          allLabel={t('warehouse.damage.all')}
          options={[
            { value: 'WAREHOUSE', label: t('warehouse.damage.originWarehouse') },
            { value: 'RECEIPT', label: t('warehouse.damage.originReceipt') },
          ]}
        />
      </Filters>

      <Panel flush icon={<IconLayers />} title={t('warehouse.damage.title')} badge={data?.total ?? rows.length}>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('warehouse.damage.title')}
            columns={columns}
            rows={rows}
            rowKey={(d) => d.id ?? 0}
            defaultSort={{ id: 'code', desc: true }}
            pageSize={25}
            loading={isLoading}
            rowActions={actions}
            empty={t('warehouse.damage.empty')}
          />
        )}
      </Panel>

      <DamageReportModal open={reporting} onClose={() => setReporting(false)} />
      <DamageResolveModal damage={resolving?.damage ?? null} action={resolving?.action ?? 'discard'} onClose={() => setResolving(null)} />
    </>
  )
}

export default DamagePanel
