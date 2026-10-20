// Lote 148 — pestaña «Valor del inventario» de Productos e inventario (`?tab=valuation`, permiso inventory.value): por producto con existencia, lo que hay en mano, su costo unitario y su valor
// según el método de valuación de la compañía (costo promedio o por capas; se cambia en Ajustes → Operación). Los totales son de todo el filtro. Ordenar y exportar usan todo el filtro (`exportRows`).
import { useEffect, useMemo, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { api, unwrap } from '../../kernel/api/client'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import type { components } from '../../kernel/api/schema'
import { formatQuantity, useLang, useT } from '../../kernel/i18n'
import { DataTable, Panel, QBox, Spinner, SummaryBar, type DataColumn } from '../../kernel/ui'
import { useQuery, keepPreviousData } from '@tanstack/react-query'
import { useWarehouses } from './api'
import { formatMoneyValue } from './lineRules'

type Row = components['schemas']['ValuationRowDto']

function useDebounced(value: string, ms = 300) {
  const [v, setV] = useState(value)
  useEffect(() => {
    const id = window.setTimeout(() => setV(value), ms)
    return () => window.clearTimeout(id)
  }, [value, ms])
  return v
}

interface Filter {
  warehousePublicId: string
  q: string
}

const read = (f: Filter) => (skip: number, take: number) =>
  unwrap(api.GET('/api/v1/inventory/valuation', { params: { query: { warehousePublicId: f.warehousePublicId || undefined, q: f.q || undefined, skip, take } } }))

export function ValuationPanel() {
  const t = useT()
  const lang = useLang()
  const [warehouse, setWarehouse] = useState('')
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const search = useDebounced(q.trim())
  const filter = useMemo<Filter>(() => ({ warehousePublicId: warehouse, q: search }), [warehouse, search])
  const warehouses = useWarehouses()
  const report = useQuery({
    queryKey: ['valuation', filter, page, pageSize],
    queryFn: () => read(filter)((page - 1) * pageSize, pageSize),
    placeholderData: keepPreviousData,
  })

  const columns = useMemo<DataColumn<Row>[]>(
    () => [
      { id: 'sku', header: t('warehouse.valuation.col.sku'), cell: (r) => <span className="ref">{r.sku}</span>, sortValue: (r) => r.sku ?? '', exportValue: (r) => r.sku ?? '', card: 'title' },
      { id: 'product', header: t('warehouse.valuation.col.product'), cell: (r) => r.productName, sortValue: (r) => r.productName ?? '' },
      { id: 'owner', header: t('warehouse.valuation.col.owner'), cell: (r) => r.clientName ?? t('warehouse.valuation.own'), sortValue: (r) => r.clientName ?? '' },
      { id: 'onHand', header: t('warehouse.valuation.col.onHand'), align: 'end', cell: (r) => formatQuantity(r.onHand ?? 0, lang), sortValue: (r) => r.onHand ?? 0, exportValue: (r) => r.onHand ?? 0 },
      {
        id: 'unitCost',
        header: t('warehouse.valuation.col.unitCost'),
        align: 'end',
        cell: (r) => (r.unitCost == null ? <span className="rcp-faint">—</span> : formatMoneyValue(r.unitCost, lang, { unitPrice: true })),
        sortValue: (r) => r.unitCost ?? 0,
        exportValue: (r) => r.unitCost ?? '',
      },
      { id: 'value', header: t('warehouse.valuation.col.value'), align: 'end', cell: (r) => <b>{formatMoneyValue(r.value ?? 0, lang)}</b>, sortValue: (r) => r.value ?? 0, exportValue: (r) => r.value ?? 0 },
      {
        id: 'unvalued',
        header: t('warehouse.valuation.col.unvalued'),
        align: 'end',
        cell: (r) => ((r.unvaluedQty ?? 0) > 0 ? <span className="rcp-diff">{formatQuantity(r.unvaluedQty ?? 0, lang)}</span> : <span className="rcp-faint">—</span>),
        sortValue: (r) => r.unvaluedQty ?? 0,
        exportValue: (r) => r.unvaluedQty ?? 0,
      },
    ],
    [t, lang],
  )

  const data = report.data
  return (
    <Panel title={t('warehouse.valuation.title')} badge={report.isPending ? undefined : data?.total}>
      <div className="pb">
        <div className="qrow tax-filters">
          <QBox value={q} onChange={(v) => (setQ(v), setPage(1))} placeholder={t('warehouse.valuation.search')} />
          <select aria-label={t('warehouse.valuation.warehouse')} value={warehouse} onChange={(e) => (setWarehouse(e.target.value), setPage(1))}>
            <option value="">{t('warehouse.valuation.allWarehouses')}</option>
            {(warehouses.data ?? []).map((w) => (
              <option key={w.publicId} value={w.publicId ?? ''}>
                {w.code} · {w.name}
              </option>
            ))}
          </select>
        </div>
        {data && (
          <SummaryBar
            items={[
              { label: t('warehouse.valuation.totalValue'), value: formatMoneyValue(data.totalValue ?? 0, lang) },
              { label: t('warehouse.valuation.totalQty'), value: formatQuantity(data.totalQty ?? 0, lang) },
              { label: t('warehouse.valuation.unvalued'), value: formatQuantity(data.unvaluedQty ?? 0, lang) },
              { label: t('warehouse.valuation.method'), value: t(`warehouse.valuation.methods.${data.method ?? 'AVERAGE'}`) },
            ]}
          />
        )}
        {report.isError ? (
          <p className="ferr" role="alert">
            {applyProblemDetails(report.error).title}
          </p>
        ) : report.isPending ? (
          <Spinner block />
        ) : (
          <DataTable
            label={t('warehouse.valuation.title')}
            columns={columns}
            rows={data?.items ?? []}
            rowKey={(r) => r.productPublicId ?? ''}
            page={page}
            pageSize={pageSize}
            total={data?.total ?? 0}
            onPage={setPage}
            onPageSize={(s) => (setPageSize(s), setPage(1))}
            empty={t('warehouse.valuation.empty')}
            exportRows={() => fetchAllPages<Row>(read(filter))}
          />
        )}
        <p className="help">{t('warehouse.valuation.help')}</p>
      </div>
    </Panel>
  )
}
