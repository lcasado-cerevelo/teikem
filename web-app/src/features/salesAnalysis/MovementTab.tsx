// Lote 241 — pestaña «Movimiento de productos» (el libro Movimiento_de_productos.xlsx del dueño): rango de meses, Resumen por
// producto con TODAS las columnas del contrato (grupos de columnas o selección propia para que quepa; columnas ordenables; ABC,
// estado y semanas de cobertura con color), matrices Unidades / Venta $ / Compras por mes (se desplazan dentro de su
// contenedor), Clientes por producto, los gráficos de cobertura y de unidades de los principales, y «Qué significa cada columna».
import { useMemo, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useLang, useT } from '../../kernel/i18n/useT'
import { DataTable, type DataColumn } from '../../kernel/ui/DataTable'
import { Filters, SelectFilter } from '../../kernel/ui/Filters'
import { Panel } from '../../kernel/ui/Panel'
import { QBox } from '../../kernel/ui/QBox'
import { SearchSelect } from '../../kernel/ui/SearchSelect'
import { Spinner } from '../../kernel/ui/Spinner'
import { toast } from '../../kernel/ui/toast'
import { matchesQ } from '../../kernel/ui/matchesQ'
import { useTenantSettings } from '../../kernel/catalogs'
import { fetchProductMovement, useProductMovement } from './salesAnalysisApi'
import {
  EMPTY_MOVEMENT_FILTER,
  MOVEMENT_COLUMNS,
  MOVEMENT_COLUMN_GROUPS,
  MOVEMENT_STATUSES,
  columnsOfGroup,
  coveragePoints,
  distinct,
  filterMovement,
  heatLevel,
  heatMix,
  matrixColumnTotals,
  matrixMax,
  monthOptions,
  monthShort,
  productKey,
  topUnitsSeries,
  type MovementColumn,
  type MovementColumnGroup,
  type MovementFilter,
  type ProductClient,
  type ProductMonthSeries,
  type ProductMovement,
  type ProductMovementRow,
} from './salesAnalysisRules'
import { NOTE_KEYS, buildMovementWorkbook, downloadWorkbook, monthHeaders, workbookFileName } from './salesAnalysisExcel'
import { AbcChip, CoverageChip, KpiTiles, MonthSelect, MovementStatusChip, QueryError, Warnings } from './SaParts'
import { CoverageChart, TopUnitsChart } from './SalesCharts'
import { useSaFormat, type SaFormat } from './saFormat'

const NO_ROWS: never[] = []
const GROUPS: MovementColumnGroup[] = ['sales', 'stock', 'reorder', 'prices', 'monthly', 'clients', 'all', 'custom']
const COVERAGE_TOP = 15

export function MovementTab() {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const q = useProductMovement(from, to)
  const data = q.data
  const options = useMemo(() => monthOptions(f.today(), 36), [f])
  const { data: tenant } = useTenantSettings()
  const [exporting, setExporting] = useState(false)

  const exportXlsx = async () => {
    if (!data) return
    setExporting(true)
    try {
      const movement = data ?? (await fetchProductMovement(from, to))
      await downloadWorkbook(
        (XLSX) => buildMovementWorkbook(XLSX, { movement, company: tenant?.name ?? '', t, lang }),
        workbookFileName(t('salesAnalysis.xlsx.movementFile', { from: movement.fromMonth ?? '', to: movement.toMonth ?? '' })),
      )
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    } finally {
      setExporting(false)
    }
  }

  return (
    <div className="sa-tab">
      <div className="sa-toolbar">
        <Filters label={t('salesAnalysis.movement.filters')}>
          <MonthSelect id="sa-from" label={t('salesAnalysis.movement.from')} value={from || (data?.fromMonth ?? '')} options={options} onChange={setFrom} />
          <MonthSelect id="sa-to" label={t('salesAnalysis.movement.to')} value={to || (data?.toMonth ?? '')} options={options} onChange={setTo} />
        </Filters>
        <div className="sa-toolbar-act">
          <button type="button" className="btn" onClick={() => void exportXlsx()} disabled={!data || exporting}>
            {exporting ? t('salesAnalysis.exporting') : t('salesAnalysis.exportXlsx')}
          </button>
        </div>
      </div>
      {q.isError ? (
        <QueryError error={q.error} />
      ) : !data ? (
        <Spinner block label={t('common.loading')} />
      ) : (
        <MovementBody data={data} />
      )}
    </div>
  )
}

function MovementBody({ data }: { data: ProductMovement }) {
  const t = useT()
  const fm = useSaFormat()
  const products = data.products ?? NO_ROWS
  const withSales = products.filter((p) => (p.units ?? 0) !== 0).length
  const low = products.filter((p) => p.coverageWeeks !== null && p.coverageWeeks !== undefined && p.coverageWeeks < 2).length
  return (
    <>
      <Warnings items={data.warnings} />
      <KpiTiles
        label={t('salesAnalysis.movement.kpis')}
        items={[
          { key: 'sales', label: t('salesAnalysis.movement.kpiSales'), value: fm.money(data.totalSales), sub: t('salesAnalysis.movement.kpiRange', { from: data.fromMonth ?? '', to: data.toMonth ?? '' }) },
          { key: 'units', label: t('salesAnalysis.movement.kpiUnits'), value: fm.units(data.totalUnits), sub: t('salesAnalysis.movement.kpiProducts', { n: withSales, total: products.length }) },
          { key: 'low', label: t('salesAnalysis.movement.kpiLow'), value: String(low), sub: t('salesAnalysis.movement.kpiAsOf', { date: fm.date(data.asOf) }), tone: low > 0 ? 'below' : 'met' },
        ]}
      />
      <ResumenPanel data={data} />
      <div className="sa-split">
        <Panel>
          <CoverageChart points={coveragePoints(products, COVERAGE_TOP)} total={products.filter((p) => p.coverageWeeks !== null && p.coverageWeeks !== undefined).length} />
        </Panel>
        <Panel>
          <TopUnitsChart months={data.months ?? NO_ROWS} series={topUnitsSeries(data, 6)} />
        </Panel>
      </div>
      <MatrixPanel data={data} />
      <ClientsPanel rows={data.topClients ?? NO_ROWS} />
      <NotesPanel data={data} />
    </>
  )
}

function useMovementColumns(fm: SaFormat): Record<MovementColumn, DataColumn<ProductMovementRow>> {
  const t = useT()
  const lang = useLang()
  return useMemo(() => {
    const h = (c: MovementColumn) => t(`salesAnalysis.col.${c}`)
    const n = (c: MovementColumn, fmt: (v: number | null | undefined) => string, get: (r: ProductMovementRow) => number | null | undefined): DataColumn<ProductMovementRow> => ({
      id: c,
      header: h(c),
      cell: (r) => fmt(get(r)),
      sortValue: (r) => get(r) ?? null,
      exportValue: (r) => get(r) ?? null,
      align: 'end',
    })
    const s = (c: MovementColumn, get: (r: ProductMovementRow) => string | null | undefined): DataColumn<ProductMovementRow> => ({ id: c, header: h(c), cell: (r) => get(r) || '—', sortValue: (r) => get(r) ?? '' })
    return {
      sku: { id: 'sku', header: h('sku'), cell: (r) => <span className="mono">{r.sku ?? ''}</span>, sortValue: (r) => r.sku ?? '', exportValue: (r) => r.sku ?? '' },
      name: { id: 'name', header: h('name'), cell: (r) => (r.matched === false ? <span title={t('salesAnalysis.movement.unmatched')}>{r.name} <span className="chip s-warn">{t('salesAnalysis.movement.unmatchedShort')}</span></span> : (r.name ?? '')), sortValue: (r) => r.name ?? '', exportValue: (r) => r.name ?? '', card: 'title' },
      category: s('category', (r) => r.category),
      brand: s('brand', (r) => r.brand),
      line: s('line', (r) => r.line),
      abc: { id: 'abc', header: h('abc'), cell: (r) => <AbcChip abc={r.abc} />, sortValue: (r) => (r.abc && r.abc !== '-' ? r.abc : 'Z'), exportValue: (r) => r.abc ?? '' },
      status: { id: 'status', header: h('status'), cell: (r) => <MovementStatusChip status={r.status} />, sortValue: (r) => MOVEMENT_STATUSES.indexOf(r.status as never), exportValue: (r) => (r.status ? t(`salesAnalysis.status.${r.status}`) : '') },
      price: n('price', (v) => (v === null || v === undefined ? '—' : fm.money(v)), (r) => r.price),
      cost: n('cost', (v) => (v === null || v === undefined ? '—' : fm.money(v)), (r) => r.cost),
      marginPct: n('marginPct', fm.pct, (r) => r.marginPct),
      onHand: n('onHand', fm.units, (r) => r.onHand),
      reserved: n('reserved', fm.units, (r) => r.reserved),
      available: n('available', fm.units, (r) => r.available),
      units: n('units', fm.units, (r) => r.units),
      sales: n('sales', fm.money0, (r) => r.sales),
      shareOfSales: n('shareOfSales', fm.pct, (r) => r.shareOfSales),
      monthlyAverage: n('monthlyAverage', fm.units, (r) => r.monthlyAverage),
      monthlyMin: n('monthlyMin', fm.units, (r) => r.monthlyMin),
      monthlyMax: n('monthlyMax', fm.units, (r) => r.monthlyMax),
      bestMonth: { id: 'bestMonth', header: h('bestMonth'), cell: (r) => (r.bestMonth ? monthShort(r.bestMonth, lang) : '—'), sortValue: (r) => r.bestMonth ?? '', exportValue: (r) => (r.bestMonth ? monthShort(r.bestMonth, lang) : '') },
      weeklyAverage: {
        ...n('weeklyAverage', fm.dec1, (r) => r.weeklyAverage),
        cell: (r) => (
          <span title={r.weeklyConfidence ? t(`salesAnalysis.confidence.${r.weeklyConfidence}`) : undefined}>
            {fm.dec1(r.weeklyAverage)}
            {r.weeklyConfidence && r.weeklyConfidence !== 'HIGH' && <span className="sa-conf"> · {t(`salesAnalysis.confidenceShort.${r.weeklyConfidence}`)}</span>}
          </span>
        ),
      },
      coverageWeeks: { id: 'coverageWeeks', header: h('coverageWeeks'), cell: (r) => <CoverageChip weeks={r.coverageWeeks} months={r.coverageMonths} />, sortValue: (r) => r.coverageWeeks ?? null, exportValue: (r) => r.coverageWeeks ?? null, align: 'end' },
      projected1: n('projected1', fm.units, (r) => r.projected1),
      projected2: n('projected2', fm.units, (r) => r.projected2),
      projected3: n('projected3', fm.units, (r) => r.projected3),
      reorderPoint: n('reorderPoint', fm.units, (r) => r.reorderPoint),
      suggestedReorderPoint: {
        ...n('suggestedReorderPoint', fm.units, (r) => r.suggestedReorderPoint),
        cell: (r) => <strong className={r.suggestedReorderPoint !== null && r.suggestedReorderPoint !== undefined && (r.available ?? 0) <= r.suggestedReorderPoint ? 'sa-delta d-down' : undefined}>{fm.units(r.suggestedReorderPoint)}</strong>,
      },
      trend: { ...n('trend', fm.signedPct, (r) => r.trend), cell: (r) => <span className={`sa-delta d-${r.trend === null || r.trend === undefined || Math.abs(r.trend) < 0.25 ? 'flat' : r.trend > 0 ? 'up' : 'down'}`}>{fm.signedPct(r.trend)}</span> },
      clients: n('clients', fm.units, (r) => r.clients),
      invoices: n('invoices', fm.units, (r) => r.invoices),
      topClient: s('topClient', (r) => r.topClient),
      topClientUnits: n('topClientUnits', fm.units, (r) => r.topClientUnits),
      lastSale: { id: 'lastSale', header: h('lastSale'), cell: (r) => fm.date(r.lastSale), sortValue: (r) => r.lastSale ?? '', exportValue: (r) => r.lastSale ?? '' },
      pendingPurchase: n('pendingPurchase', fm.units, (r) => r.pendingPurchase),
    }
  }, [t, fm, lang])
}

function ResumenPanel({ data }: { data: ProductMovement }) {
  const t = useT()
  const fm = useSaFormat()
  const all = data.products ?? NO_ROWS
  const [group, setGroup] = useState<MovementColumnGroup>('sales')
  const [custom, setCustom] = useState<string[]>([...MOVEMENT_COLUMN_GROUPS.sales])
  const [filter, setFilter] = useState<MovementFilter>(EMPTY_MOVEMENT_FILTER)
  const defs = useMovementColumns(fm)
  const visible = columnsOfGroup(group, custom)
  const columns = visible.map((c) => defs[c])
  const rows = filterMovement(all, filter)
  const dirty = filter.line || filter.abc || filter.status || filter.q
  return (
    <Panel title={t('salesAnalysis.movement.resumen')} subtitle={t('salesAnalysis.movement.resumenSub', { asOf: fm.date(data.asOf), weeksFrom: fm.date(data.weeksFrom), weeksTo: fm.date(data.weeksTo) })} badge={rows.length} flush>
      <div className="pb">
        <div className="sa-colgroups">
          <span className="sa-colgroups-label" id="sa-colgroups">{t('salesAnalysis.movement.columns')}</span>
          <div className="seg" role="group" aria-labelledby="sa-colgroups">
            {GROUPS.map((g) => (
              <button key={g} type="button" className={group === g ? 'on' : undefined} aria-pressed={group === g} onClick={() => setGroup(g)}>
                {t(`salesAnalysis.movement.group.${g}`)}
              </button>
            ))}
          </div>
        </div>
        {group === 'custom' && (
          <Filters>
            <SearchSelect
              label={t('salesAnalysis.movement.pickColumns')}
              value={custom}
              onChange={setCustom}
              options={MOVEMENT_COLUMNS.filter((c) => c !== 'sku' && c !== 'name').map((c) => ({ value: c, label: t(`salesAnalysis.col.${c}`) }))}
            />
          </Filters>
        )}
        <Filters onClear={dirty ? () => setFilter(EMPTY_MOVEMENT_FILTER) : undefined}>
          <SelectFilter label={t('salesAnalysis.col.line')} value={filter.line} onChange={(line) => setFilter({ ...filter, line })} options={distinct(all.map((r) => r.line)).map((l) => ({ value: l, label: l }))} />
          <SelectFilter label={t('salesAnalysis.col.abc')} value={filter.abc} onChange={(abc) => setFilter({ ...filter, abc })} options={['A', 'B', 'C', '-'].map((v) => ({ value: v, label: v }))} />
          <SelectFilter label={t('salesAnalysis.col.status')} value={filter.status} onChange={(status) => setFilter({ ...filter, status })} options={MOVEMENT_STATUSES.map((v) => ({ value: v, label: t(`salesAnalysis.status.${v}`) }))} />
        </Filters>
        <div className="qrow">
          <QBox value={filter.q} onChange={(q) => setFilter({ ...filter, q })} placeholder={t('salesAnalysis.movement.search')} />
        </div>
      </div>
      <DataTable
        label={t('salesAnalysis.movement.resumen')}
        columns={columns}
        rows={rows}
        rowKey={productKey}
        defaultSort={{ id: visible.includes('sales') ? 'sales' : 'sku', desc: visible.includes('sales') }}
        exportRows={async () => rows}
        empty={t('salesAnalysis.movement.empty')}
      />
    </Panel>
  )
}

type MatrixKind = 'units' | 'sales' | 'purchases'

function MatrixPanel({ data }: { data: ProductMovement }) {
  const t = useT()
  const lang = useLang()
  const fm = useSaFormat()
  const [kind, setKind] = useState<MatrixKind>('units')
  const [q, setQ] = useState('')
  const series = (kind === 'units' ? data.unitsByMonth : kind === 'sales' ? data.salesByMonth : data.purchasesByMonth) ?? NO_ROWS
  const rows = series.filter((s) => matchesQ(q, s.sku, s.name))
  const headers = monthHeaders(data, lang, t)
  const max = matrixMax(series)
  const totals = matrixColumnTotals(rows, headers.length)
  const fmt = kind === 'sales' ? fm.money0 : fm.units
  const withStats = kind === 'units'
  const title = t(`salesAnalysis.movement.matrix.${kind}`)
  return (
    <Panel title={t('salesAnalysis.movement.byMonth')} subtitle={t('salesAnalysis.movement.byMonthSub')} badge={rows.length} flush collapsible storageKey="sa.matrix">
      <div className="pb">
        <div className="sa-colgroups">
          <div className="seg" role="group" aria-label={t('salesAnalysis.movement.byMonth')}>
            {(['units', 'sales', 'purchases'] as const).map((k) => (
              <button key={k} type="button" className={kind === k ? 'on' : undefined} aria-pressed={kind === k} onClick={() => setKind(k)}>
                {t(`salesAnalysis.movement.matrix.${k}`)}
              </button>
            ))}
          </div>
        </div>
        <div className="qrow">
          <QBox value={q} onChange={setQ} placeholder={t('salesAnalysis.movement.searchMatrix')} />
        </div>
      </div>
      <div className="sa-scroll sa-matrix-wrap" tabIndex={0} role="region" aria-label={title}>
        <table className="sa-matrix">
          <caption className="sr-only">{title}</caption>
          <thead>
            <tr>
              <th scope="col" className="stick">{t('salesAnalysis.col.product')}</th>
              {headers.map((h) => (
                <th key={h} scope="col">{h}</th>
              ))}
              <th scope="col">{kind === 'purchases' ? t('salesAnalysis.movement.totalPurchased') : t('salesAnalysis.charts.total')}</th>
              {withStats && (
                <>
                  <th scope="col">{t('salesAnalysis.col.monthlyMin')}</th>
                  <th scope="col">{t('salesAnalysis.col.monthlyMax')}</th>
                  <th scope="col">{t('salesAnalysis.col.monthlyAverage')}</th>
                </>
              )}
            </tr>
          </thead>
          <tbody>
            {rows.map((s: ProductMonthSeries) => (
              <tr key={productKey(s)}>
                <th scope="row" className="stick">
                  <span className="mono">{s.sku}</span>
                  <span className="sa-matrix-name">{s.name}</span>
                </th>
                {headers.map((h, i) => {
                  const v = s.values?.[i] ?? 0
                  const mix = heatMix(heatLevel(v, max))
                  return (
                    <td key={h} style={mix > 0 ? { background: `color-mix(in srgb, var(--info) ${mix}%, transparent)` } : undefined}>
                      {v ? fmt(v) : <span className="sa-muted">·</span>}
                    </td>
                  )
                })}
                <td className="tot">{fmt(s.total)}</td>
                {withStats && (
                  <>
                    <td>{fm.units(s.min)}</td>
                    <td>{fm.units(s.max)}</td>
                    <td>{fm.dec1(s.average)}</td>
                  </>
                )}
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr>
              <th scope="row" className="stick">{t('salesAnalysis.charts.total')}</th>
              {totals.map((v, i) => (
                <td key={i}>{fmt(v)}</td>
              ))}
              <td className="tot">{fmt(totals.reduce((a, b) => a + b, 0))}</td>
              {withStats && <td colSpan={3} />}
            </tr>
          </tfoot>
        </table>
      </div>
    </Panel>
  )
}

function ClientsPanel({ rows: all }: { rows: readonly ProductClient[] }) {
  const t = useT()
  const fm = useSaFormat()
  const [q, setQ] = useState('')
  const rows = all.filter((r) => matchesQ(q, r.sku, r.name, r.client))
  const columns = useMemo<DataColumn<ProductClient>[]>(
    () => [
      { id: 'sku', header: t('salesAnalysis.col.sku'), cell: (r) => <span className="mono">{r.sku ?? ''}</span>, sortValue: (r) => r.sku ?? '', exportValue: (r) => r.sku ?? '' },
      { id: 'name', header: t('salesAnalysis.col.name'), cell: (r) => r.name ?? '', sortValue: (r) => r.name ?? '' },
      { id: 'rank', header: t('salesAnalysis.col.rank'), cell: (r) => String(r.rank ?? ''), sortValue: (r) => r.rank ?? 0, align: 'end', exportValue: (r) => r.rank ?? null },
      { id: 'client', header: t('salesAnalysis.col.client'), cell: (r) => r.client ?? '', sortValue: (r) => r.client ?? '', card: 'title' },
      { id: 'units', header: t('salesAnalysis.col.units'), cell: (r) => fm.units(r.units), sortValue: (r) => r.units ?? 0, align: 'end', exportValue: (r) => r.units ?? 0 },
      { id: 'share', header: t('salesAnalysis.col.shareOfProduct'), cell: (r) => fm.pct(r.shareOfProduct), sortValue: (r) => r.shareOfProduct ?? null, align: 'end', exportValue: (r) => r.shareOfProduct ?? null },
    ],
    [t, fm],
  )
  return (
    <Panel title={t('salesAnalysis.movement.clients')} subtitle={t('salesAnalysis.movement.clientsSub')} badge={rows.length} flush collapsible storageKey="sa.clientsByProduct">
      <div className="pb qrow">
        <QBox value={q} onChange={setQ} placeholder={t('salesAnalysis.movement.searchClients')} />
      </div>
      <DataTable label={t('salesAnalysis.movement.clients')} columns={columns} rows={rows} rowKey={(r) => `${r.productId ?? r.sku}|${r.rank}`} />
    </Panel>
  )
}

function NotesPanel({ data }: { data: ProductMovement }) {
  const t = useT()
  const params = { lead: data.leadTimeDays ?? 0, z: data.serviceLevelZ ?? 0, weeksFrom: data.weeksFrom ?? '', weeksTo: data.weeksTo ?? '' }
  return (
    <Panel title={t('salesAnalysis.movement.notes')} collapsible defaultCollapsed storageKey="sa.notes">
      <dl className="sa-notes">
        {NOTE_KEYS.map((k) => (
          <div key={k}>
            <dt>{t(`salesAnalysis.notesTerm.${k}`)}</dt>
            <dd>{t(`salesAnalysis.notes.${k}`, params)}</dd>
          </div>
        ))}
      </dl>
    </Panel>
  )
}
