// Lote 14 (P7) — pestaña Conciliación del Kárdex: lista paginada de descuadres Kárdex ↔ saldo (`GET /inventory/discrepancies`,
// con los filtros compartidos que aplican: almacén, posición, producto, categoría, fechas de detección y estatus —por defecto
// Pendiente—), "Ejecutar conciliación" (`POST /inventory/reconciliation/run`, inventory.adjust: todo el tenant o los
// productos del filtro, hasta 200) con su resultado, y el estado de la revisión automática en segundo plano (D14,
// `GET /inventory/reconciliation/status`, inventory.adjust: "al día" o "N en cola"). Clic en una fila (o sus íconos Corregir /
// Descartar) abre el detalle `DiscrepancyModal` vía `?discrepancy=<publicId>`.
import { useMemo, useState } from 'react'
import { Can, useCan } from '../../kernel/access'
import { StatusChip } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import { DataTable, EmptyState, IconCheck, IconDoc, Panel, SummaryBar, toast, type DataColumn, type RowAction } from '../../kernel/ui'
import { IconRefreshCw, IconXCircle } from '../../kernel/ui/actionIcons'
import {
  exportInventoryDiscrepancies,
  useInventoryDiscrepancies,
  useReconciliationStatus,
  useRunReconciliation,
  type InventoryDiscrepancyDto,
  type ReconciliationRunDto,
} from './api'
import { problemText } from './problemText'
import { discrepancyQuery, formatSignedQty, qtyClass, rangeInverted, sideText, type InventoryFilterState } from './kardexView'
import { formatDateTime, formatNumber } from './lineRules'

/** Tope de productos de la conciliación manual (el API responde 400 con más). */
export const RECONCILIATION_MAX_PRODUCTS = 200

export interface DiscrepanciesPanelProps {
  filters: InventoryFilterState
  page: number
  pageSize: number
  onPage: (page: number) => void
  onPageSize: (size: number) => void
  /** Abre el detalle del descuadre. */
  onOpen: (publicId: string) => void
}

export function DiscrepanciesPanel({ filters, page, pageSize, onPage, onPageSize, onOpen }: DiscrepanciesPanelProps) {
  const t = useT()
  const lang = useLang()
  const canAdjust = useCan('inventory.adjust')
  const inverted = rangeInverted(filters.range)
  const base = useMemo(() => discrepancyQuery(filters), [filters])
  const query = useMemo(() => ({ ...base, skip: (page - 1) * pageSize, take: pageSize }), [base, page, pageSize])
  const { data, isLoading, error } = useInventoryDiscrepancies(query, { enabled: !inverted })
  const status = useReconciliationStatus({ enabled: canAdjust, handleAccessDenied: false, refetchInterval: 15_000 })
  const run = useRunReconciliation()
  const [lastRun, setLastRun] = useState<ReconciliationRunDto | null>(null)

  const columns = useMemo<DataColumn<InventoryDiscrepancyDto>[]>(
    () => [
      {
        id: 'detected',
        header: t('warehouse.discrepancies.columns.detected'),
        cell: (d) => <span className="mono">{formatDateTime(d.detectedAtUtc, lang)}</span>,
        exportValue: (d) => d.detectedAtUtc,
        sortValue: (d) => d.detectedAtUtc,
      },
      { id: 'sku', header: t('warehouse.discrepancies.columns.sku'), cell: (d) => <span className="ref">{d.sku}</span>, exportValue: (d) => d.sku ?? '', sortValue: (d) => d.sku, card: 'title' },
      { id: 'product', header: t('warehouse.discrepancies.columns.product'), cell: (d) => d.productName ?? '', sortValue: (d) => d.productName },
      {
        id: 'where',
        header: t('warehouse.discrepancies.columns.where'),
        cell: (d) => <span className="mono">{sideText(d.warehouseCode, d.binCode) || t('warehouse.discrepancies.productTotal')}</span>,
        exportValue: (d) => sideText(d.warehouseCode, d.binCode) || t('warehouse.discrepancies.productTotal'),
        sortValue: (d) => sideText(d.warehouseCode, d.binCode),
      },
      { id: 'lot', header: t('warehouse.discrepancies.columns.lot'), cell: (d) => d.lotNumber ?? '', sortValue: (d) => d.lotNumber },
      { id: 'ledger', header: t('warehouse.discrepancies.columns.ledgerQty'), cell: (d) => formatNumber(d.ledgerQty, lang), exportValue: (d) => d.ledgerQty ?? 0, sortValue: (d) => d.ledgerQty, align: 'end' },
      { id: 'balance', header: t('warehouse.discrepancies.columns.balanceQty'), cell: (d) => formatNumber(d.balanceQty, lang), exportValue: (d) => d.balanceQty ?? 0, sortValue: (d) => d.balanceQty, align: 'end' },
      {
        id: 'difference',
        header: t('warehouse.discrepancies.columns.difference'),
        cell: (d) => <span className={`mono ${qtyClass(d.difference)}`}>{formatSignedQty(d.difference, lang)}</span>,
        exportValue: (d) => d.difference ?? 0,
        sortValue: (d) => d.difference,
        align: 'end',
      },
      { id: 'kind', header: t('warehouse.discrepancies.columns.kind'), cell: (d) => d.kind ?? d.kindCode ?? '', sortValue: (d) => d.kind ?? d.kindCode, card: 'hidden' },
      {
        id: 'status',
        header: t('warehouse.discrepancies.columns.status'),
        cell: (d) => <StatusChip domain="InventoryDiscrepancyStatus" code={d.statusCode} label={d.status} />,
        exportValue: (d) => d.status ?? d.statusCode ?? '',
        sortValue: (d) => d.status ?? d.statusCode,
      },
    ],
    [t, lang],
  )

  const actions = useMemo<RowAction<InventoryDiscrepancyDto>[]>(
    () => [
      {
        key: 'rebuild',
        label: t('warehouse.discrepancies.rebuild'),
        icon: <IconRefreshCw />,
        tone: 'flow',
        perm: 'inventory.adjust',
        visible: (d) => d.statusCode === 'OPEN' && d.kindCode === 'BALANCE',
        onClick: (d) => d.publicId && onOpen(d.publicId),
      },
      {
        key: 'dismiss',
        label: t('warehouse.discrepancies.dismiss'),
        icon: <IconXCircle />,
        perm: 'inventory.adjust',
        visible: (d) => d.statusCode === 'OPEN',
        onClick: (d) => d.publicId && onOpen(d.publicId),
      },
    ],
    [t, onOpen],
  )

  const runNow = async () => {
    const ids = filters.products.map((p) => p.publicId)
    try {
      const result = await run.mutateAsync({ productPublicIds: ids.length > 0 && ids.length <= RECONCILIATION_MAX_PRODUCTS ? ids : null })
      setLastRun(result)
      toast.success(t('warehouse.discrepancies.runDone'))
    } catch (err) {
      toast.error(problemText(err))
    }
  }

  const st = status.data
  let statusText: string | null = null
  if (canAdjust && st) {
    if (st.enabled === false) statusText = t('warehouse.discrepancies.auto.off')
    else if ((st.pending ?? 0) > 0) statusText = t('warehouse.discrepancies.auto.pending', { count: st.pending ?? 0 })
    else statusText = t('warehouse.discrepancies.auto.upToDate')
  }

  return (
    <>
      <SummaryBar
        label={t('warehouse.discrepancies.summaryLabel')}
        loading={isLoading}
        items={[
          { key: 'open', label: t('warehouse.discrepancies.summary.open'), value: data ? formatNumber(data.openCount ?? 0, lang) : '—', tone: (data?.openCount ?? 0) > 0 ? 'out' : undefined },
          { key: 'shown', label: t('warehouse.discrepancies.summary.shown'), value: data ? formatNumber(data.total ?? 0, lang) : '—' },
        ]}
        aside={statusText ? <span title={st?.lastError ?? undefined}>{statusText}</span> : undefined}
      />
      {lastRun && (
        <p className="note" role="status">
          {t('warehouse.discrepancies.runResult', {
            date: formatDateTime(lastRun.checkedAtUtc, lang),
            products: formatNumber(lastRun.productsChecked ?? 0, lang),
            balances: formatNumber(lastRun.balancesChecked ?? 0, lang),
            opened: lastRun.opened ?? 0,
            stillOpen: lastRun.stillOpen ?? 0,
            selfCorrected: lastRun.selfCorrected ?? 0,
          })}
        </p>
      )}
      <Panel
        flush
        icon={<IconDoc />}
        title={t('warehouse.discrepancies.title')}
        badge={data ? (data.total ?? 0) : undefined}
        subtitle={t('warehouse.discrepancies.subtitle')}
        actions={
          <Can perm="inventory.adjust">
            <button type="button" className="btn sm flow" onClick={runNow} disabled={run.isPending} title={t('warehouse.discrepancies.runHint')}>
              {run.isPending ? t('common.loading') : t('warehouse.discrepancies.run')}
            </button>
          </Can>
        }
      >
        {inverted ? (
          <p className="pb ferr" role="alert">
            {t('warehouse.inventory.kardex.errors.fromAfterTo')}
          </p>
        ) : error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('warehouse.discrepancies.title')}
            columns={columns}
            rows={data?.items ?? []}
            rowKey={(d) => d.publicId ?? ''}
            loading={isLoading}
            page={page}
            pageSize={pageSize}
            total={data?.total ?? 0}
            onPage={onPage}
            onPageSize={onPageSize}
            rowActions={actions}
            onRowClick={(d) => d.publicId && onOpen(d.publicId)}
            exportRows={() => exportInventoryDiscrepancies(base)}
            empty={<EmptyState icon={<IconCheck />} title={t('warehouse.discrepancies.empty')} body={t('warehouse.discrepancies.emptyBody')} />}
          />
        )}
      </Panel>
    </>
  )
}
