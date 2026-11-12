// Lote 241 — pestaña «Por representante» (el libro Analisis_Ventas_<Mes>.xlsx del dueño): mes y mes de comparación, tarjetas,
// Comparativo por ruta y representante con la conciliación, Líneas (con su líder), Línea por representante, Producto por
// representante, Producto por ruta (incluye lo que no vendió) y las Transacciones del mes (paginadas por el servidor con
// `exportRows`). Cada vista lleva su gráfico al lado y la pestaña entera se exporta a UN libro de Excel con las hojas del original.
import { useEffect, useMemo, useState } from 'react'
import { useLang, useT } from '../../kernel/i18n/useT'
import { DataTable, type DataColumn } from '../../kernel/ui/DataTable'
import { Filters, SelectFilter } from '../../kernel/ui/Filters'
import { Panel } from '../../kernel/ui/Panel'
import { QBox } from '../../kernel/ui/QBox'
import { Spinner } from '../../kernel/ui/Spinner'
import { toast } from '../../kernel/ui/toast'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { fetchProductMovement, fetchTransactions, useSalesByRep, useSalesTransactions, type TransactionQuery } from './salesAnalysisApi'
import {
  EMPTY_PRODUCT_FILTER,
  changeTone,
  complianceLevel,
  distinct,
  filterRepProducts,
  filterRouteProducts,
  lineShareFromLines,
  monthLong,
  monthOptions,
  prevMonth,
  repKey,
  repOptions,
  repQuotaFromRows,
  type ProductFilter,
  type SalesByRep,
  type SalesLineShare,
  type SalesRepLine,
  type SalesRepProduct,
  type SalesRepRow,
  type SalesRouteProduct,
  type SalesTransaction,
} from './salesAnalysisRules'
import { buildByRepWorkbook, downloadWorkbook, workbookFileName } from './salesAnalysisExcel'
import { ComplianceBar, KpiTiles, MonthSelect, QueryError, Warnings } from './SaParts'
import { LineShareChart, RepQuotaChart } from './SalesCharts'
import { useSaFormat } from './saFormat'

const NO_ROWS: never[] = []
type TxRow = SalesTransaction & { _k: number }

export function ByRepTab() {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const [month, setMonth] = useState('')
  const [compare, setCompare] = useState('')
  const q = useSalesByRep(month, compare)
  const data = q.data
  const shownMonth = data?.month ?? month
  const options = useMemo(() => monthOptions(f.today(), 36), [f])
  const [exporting, setExporting] = useState(false)

  const exportXlsx = async () => {
    if (!data?.month) return
    setExporting(true)
    try {
      const [tx, inventory] = await Promise.all([
        fetchTransactions({ month: data.month, repId: null, q: '' }, 0, 500, true),
        fetchProductMovement(data.month, data.month).catch(() => null),
      ])
      await downloadWorkbook(
        (XLSX) => buildByRepWorkbook(XLSX, { byRep: data, transactions: tx.items ?? [], inventory, t, lang }),
        workbookFileName(t('salesAnalysis.xlsx.byRepFile', { month: data.month })),
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
        <Filters label={t('salesAnalysis.byRep.filters')}>
          <MonthSelect id="sa-month" label={t('salesAnalysis.byRep.month')} value={shownMonth} options={options} onChange={(v) => (setMonth(v), setCompare(''))} />
          <MonthSelect
            id="sa-compare"
            label={t('salesAnalysis.byRep.compare')}
            value={compare}
            options={options}
            onChange={setCompare}
            emptyLabel={shownMonth ? t('salesAnalysis.byRep.previous', { month: monthLong(prevMonth(shownMonth), lang) }) : t('salesAnalysis.byRep.previousShort')}
          />
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
        <>
          <Warnings items={data.warnings} />
          <ByRepKpis data={data} />
          <ComparativoPanel data={data} />
          <LinesPanel data={data} />
          <RepLinesPanel data={data} />
          <RepProductsPanel data={data} />
          <RouteProductsPanel data={data} />
          <TransactionsPanel month={data.month ?? ''} reps={data.reps ?? NO_ROWS} />
        </>
      )}
      {q.isFetching && data && <span className="sr-only" role="status">{t('common.loading')}</span>}
    </div>
  )
}

function ByRepKpis({ data }: { data: SalesByRep }) {
  const t = useT()
  const lang = useLang()
  const fm = useSaFormat()
  const tot = data.totals ?? {}
  const rec = data.reconciliation ?? {}
  const ct = changeTone(tot.change)
  return (
    <KpiTiles
      label={t('salesAnalysis.byRep.kpis')}
      items={[
        { key: 'sales', label: t('salesAnalysis.byRep.kpiSales', { month: monthLong(data.month ?? '', lang) }), value: fm.money(tot.sales), sub: t('salesAnalysis.byRep.kpiMonthTotal', { amount: fm.money(rec.monthTotal) }) },
        {
          key: 'change',
          label: t('salesAnalysis.byRep.kpiChange', { month: monthLong(data.compareMonth ?? '', lang) }),
          value: `${(tot.change ?? 0) > 0 ? '+' : ''}${fm.money(tot.change)}`,
          sub: `${fm.signedPct(tot.changePct)} · ${t('salesAnalysis.byRep.kpiCompareWas', { amount: fm.money(tot.compareSales) })}`,
          tone: ct,
        },
        {
          key: 'quota',
          label: t('salesAnalysis.byRep.kpiQuota'),
          value: fm.pct(tot.compliance),
          sub: t('salesAnalysis.byRep.kpiQuotaOf', { quota: fm.money(tot.quota) }),
          tone: complianceLevel(tot.compliance),
          extra: <ComplianceBar value={tot.compliance} />,
        },
      ]}
    />
  )
}

function ComparativoPanel({ data }: { data: SalesByRep }) {
  const t = useT()
  const lang = useLang()
  const fm = useSaFormat()
  const rows = data.reps ?? NO_ROWS
  const tot = data.totals ?? {}
  const rec = data.reconciliation ?? {}
  const cName = monthLong(data.compareMonth ?? '', lang)
  const mName = monthLong(data.month ?? '', lang)
  const columns = useMemo<DataColumn<SalesRepRow>[]>(
    () => [
      { id: 'route', header: t('salesAnalysis.col.route'), cell: (r) => r.routeName || '—', sortValue: (r) => r.routeName ?? '' },
      { id: 'rep', header: t('salesAnalysis.col.rep'), cell: (r) => r.repName ?? t('salesAnalysis.noRep'), sortValue: (r) => r.repName ?? '', card: 'title' },
      { id: 'compare', header: cName, cell: (r) => fm.money(r.compareSales), sortValue: (r) => r.compareSales ?? 0, align: 'end', exportValue: (r) => r.compareSales ?? 0 },
      { id: 'sales', header: mName, cell: (r) => fm.money(r.sales), sortValue: (r) => r.sales ?? 0, align: 'end', exportValue: (r) => r.sales ?? 0 },
      { id: 'change', header: t('salesAnalysis.col.change'), cell: (r) => <span className={`sa-delta d-${changeTone(r.change)}`}>{fm.money(r.change)}</span>, sortValue: (r) => r.change ?? 0, align: 'end', exportValue: (r) => r.change ?? 0 },
      { id: 'changePct', header: t('salesAnalysis.col.changePct'), cell: (r) => <span className={`sa-delta d-${changeTone(r.change)}`}>{fm.signedPct(r.changePct)}</span>, sortValue: (r) => r.changePct ?? null, align: 'end', exportValue: (r) => r.changePct ?? null },
      { id: 'quota', header: t('salesAnalysis.col.quota'), cell: (r) => fm.money(r.quota), sortValue: (r) => r.quota ?? null, align: 'end', exportValue: (r) => r.quota ?? null },
      { id: 'compliance', header: t('salesAnalysis.col.compliance'), cell: (r) => <ComplianceBar value={r.compliance} />, sortValue: (r) => r.compliance ?? null, exportValue: (r) => r.compliance ?? null },
    ],
    [t, fm, cName, mName],
  )
  return (
    <div className="sa-split">
      <Panel title={t('salesAnalysis.byRep.comparativo')} subtitle={t('salesAnalysis.byRep.comparativoSub')} badge={rows.length} flush>
        <DataTable label={t('salesAnalysis.byRep.comparativo')} columns={columns} rows={rows} rowKey={(r) => repKey(r.repId)} pagination={false} empty={t('salesAnalysis.byRep.noReps')} />
        <div className="sa-scroll pb">
          <table className="sa-mini sa-rec" aria-label={t('salesAnalysis.byRep.reconciliation')}>
            <thead>
              <tr>
                <th scope="col">{t('salesAnalysis.byRep.reconciliation')}</th>
                <th scope="col">{cName}</th>
                <th scope="col">{mName}</th>
                <th scope="col">{t('salesAnalysis.col.change')}</th>
              </tr>
            </thead>
            <tbody>
              <tr className="tot">
                <th scope="row">{t('salesAnalysis.byRep.totalRoutes')}</th>
                <td>{fm.money(tot.compareSales)}</td>
                <td>{fm.money(tot.sales)}</td>
                <td>{`${fm.money(tot.change)} · ${fm.signedPct(tot.changePct)}`}</td>
              </tr>
              <tr>
                <th scope="row" title={t('salesAnalysis.byRep.unassignedHelp')}>{t('salesAnalysis.byRep.unassigned')}</th>
                <td>{fm.money(rec.unassignedCompare)}</td>
                <td>{fm.money(rec.unassigned)}</td>
                <td />
              </tr>
              <tr>
                <th scope="row">{t('salesAnalysis.byRep.shippingOther')}</th>
                <td />
                <td>{fm.money(rec.shippingAndOther)}</td>
                <td />
              </tr>
              <tr className="tot">
                <th scope="row">{t('salesAnalysis.byRep.monthTotal')}</th>
                <td />
                <td>{fm.money(rec.monthTotal)}</td>
                <td />
              </tr>
              {Math.abs(rec.difference ?? 0) >= 0.005 && (
                <tr>
                  <th scope="row">{t('salesAnalysis.byRep.difference')}</th>
                  <td />
                  <td className="sa-delta d-down">{fm.money(rec.difference)}</td>
                  <td />
                </tr>
              )}
              <tr>
                <td colSpan={4} className="detail sa-muted">
                  {t('salesAnalysis.byRep.recDetail', { product: fm.money(rec.productSales), discounts: fm.money(rec.discounts), shipping: fm.money(rec.shipping), other: fm.money(rec.other) })}
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </Panel>
      <Panel>
        <RepQuotaChart points={repQuotaFromRows(rows)} month={data.month} />
      </Panel>
    </div>
  )
}

function LinesPanel({ data }: { data: SalesByRep }) {
  const t = useT()
  const fm = useSaFormat()
  const rows = data.lines ?? NO_ROWS
  const columns = useMemo<DataColumn<SalesLineShare>[]>(
    () => [
      { id: 'line', header: t('salesAnalysis.col.line'), cell: (r) => r.line ?? '', sortValue: (r) => r.line ?? '', card: 'title' },
      { id: 'sales', header: t('salesAnalysis.col.sales'), cell: (r) => fm.money(r.sales), sortValue: (r) => r.sales ?? 0, align: 'end', exportValue: (r) => r.sales ?? 0 },
      { id: 'shareNet', header: t('salesAnalysis.col.shareNet'), cell: (r) => fm.pct(r.shareOfNet), sortValue: (r) => r.shareOfNet ?? null, align: 'end', exportValue: (r) => r.shareOfNet ?? null },
      { id: 'shareGross', header: t('salesAnalysis.col.shareGross'), cell: (r) => fm.pct(r.shareOfGross), sortValue: (r) => r.shareOfGross ?? null, align: 'end', exportValue: (r) => r.shareOfGross ?? null },
      { id: 'leader', header: t('salesAnalysis.col.leader'), cell: (r) => (r.isDiscounts ? t('salesAnalysis.notApplicable') : (r.leaderName ?? '—')), sortValue: (r) => r.leaderName ?? '' },
      { id: 'leaderRoute', header: t('salesAnalysis.col.leaderRoute'), cell: (r) => (r.isDiscounts ? '—' : (r.leaderRoute ?? '—')), sortValue: (r) => r.leaderRoute ?? '' },
      { id: 'leaderSales', header: t('salesAnalysis.col.leaderSales'), cell: (r) => (r.isDiscounts ? '—' : fm.money(r.leaderSales)), sortValue: (r) => r.leaderSales ?? null, align: 'end', exportValue: (r) => r.leaderSales ?? null },
    ],
    [t, fm],
  )
  return (
    <div className="sa-split">
      <Panel title={t('salesAnalysis.byRep.lines')} subtitle={t('salesAnalysis.byRep.linesSub')} badge={rows.length} flush>
        <DataTable label={t('salesAnalysis.byRep.lines')} columns={columns} rows={rows} rowKey={(r) => `${r.line}|${r.isDiscounts}`} pagination={false} rowClassName={(r) => (r.isDiscounts ? 'dim' : undefined)} />
      </Panel>
      <Panel>
        <LineShareChart points={lineShareFromLines(rows)} />
      </Panel>
    </div>
  )
}

function RepLinesPanel({ data }: { data: SalesByRep }) {
  const t = useT()
  const fm = useSaFormat()
  const all = data.repLines ?? NO_ROWS
  const [rep, setRep] = useState('')
  const [line, setLine] = useState('')
  const [hideZero, setHideZero] = useState(true)
  const rows = all.filter((r) => (!rep || repKey(r.repId) === rep) && (!line || r.line === line) && (!hideZero || Math.abs(r.sales ?? 0) >= 0.005))
  const reps = distinct(all.map((r) => repKey(r.repId))).map((k) => ({ value: k, label: all.find((r) => repKey(r.repId) === k)?.repName ?? t('salesAnalysis.noRep') }))
  const columns = useMemo<DataColumn<SalesRepLine>[]>(
    () => [
      { id: 'route', header: t('salesAnalysis.col.route'), cell: (r) => r.routeName || '—', sortValue: (r) => r.routeName ?? '' },
      { id: 'rep', header: t('salesAnalysis.col.rep'), cell: (r) => r.repName ?? t('salesAnalysis.noRep'), sortValue: (r) => r.repName ?? '', card: 'title' },
      { id: 'line', header: t('salesAnalysis.col.line'), cell: (r) => r.line ?? '', sortValue: (r) => r.line ?? '' },
      { id: 'sales', header: t('salesAnalysis.col.netSales'), cell: (r) => fm.money(r.sales), sortValue: (r) => r.sales ?? 0, align: 'end', exportValue: (r) => r.sales ?? 0 },
      { id: 'share', header: t('salesAnalysis.col.shareOfRep'), cell: (r) => fm.pct(r.shareOfRep), sortValue: (r) => r.shareOfRep ?? null, align: 'end', exportValue: (r) => r.shareOfRep ?? null },
    ],
    [t, fm],
  )
  return (
    <Panel title={t('salesAnalysis.byRep.repLines')} subtitle={t('salesAnalysis.byRep.repLinesSub')} badge={rows.length} flush collapsible storageKey="sa.repLines">
      <div className="pb">
        <Filters onClear={rep || line || !hideZero ? () => (setRep(''), setLine(''), setHideZero(true)) : undefined}>
          <SelectFilter label={t('salesAnalysis.col.rep')} value={rep} onChange={setRep} options={reps} />
          <SelectFilter label={t('salesAnalysis.col.line')} value={line} onChange={setLine} options={distinct(all.map((r) => r.line)).map((l) => ({ value: l, label: l }))} />
          <div className="f sa-check">
            <label>
              <input type="checkbox" checked={hideZero} onChange={(e) => setHideZero(e.target.checked)} /> {t('salesAnalysis.byRep.hideZero')}
            </label>
          </div>
        </Filters>
      </div>
      <DataTable label={t('salesAnalysis.byRep.repLines')} columns={columns} rows={rows} rowKey={(r) => `${repKey(r.repId)}|${r.line}|${r.isDiscounts}`} />
    </Panel>
  )
}

function ProductFilters({ value, onChange, routes, reps, lines }: { value: ProductFilter; onChange: (f: ProductFilter) => void; routes: string[]; reps: { value: string; label: string }[]; lines: string[] }) {
  const t = useT()
  const dirty = value.route || value.rep || value.line || value.q
  return (
    <div className="pb">
      <Filters onClear={dirty ? () => onChange(EMPTY_PRODUCT_FILTER) : undefined}>
        <SelectFilter label={t('salesAnalysis.col.route')} value={value.route} onChange={(route) => onChange({ ...value, route })} options={routes.map((r) => ({ value: r, label: r }))} />
        <SelectFilter label={t('salesAnalysis.col.rep')} value={value.rep} onChange={(rep) => onChange({ ...value, rep })} options={reps} />
        <SelectFilter label={t('salesAnalysis.col.line')} value={value.line} onChange={(line) => onChange({ ...value, line })} options={lines.map((l) => ({ value: l, label: l }))} />
      </Filters>
      <div className="qrow">
        <QBox value={value.q} onChange={(q) => onChange({ ...value, q })} placeholder={t('salesAnalysis.byRep.searchProduct')} />
      </div>
    </div>
  )
}

function RepProductsPanel({ data }: { data: SalesByRep }) {
  const t = useT()
  const fm = useSaFormat()
  const all = data.repProducts ?? NO_ROWS
  const [filter, setFilter] = useState<ProductFilter>(EMPTY_PRODUCT_FILTER)
  const rows = filterRepProducts(all, filter)
  const columns = useMemo<DataColumn<SalesRepProduct>[]>(
    () => [
      { id: 'rep', header: t('salesAnalysis.col.rep'), cell: (r) => r.repName ?? t('salesAnalysis.noRep'), sortValue: (r) => r.repName ?? '' },
      { id: 'route', header: t('salesAnalysis.col.route'), cell: (r) => r.routeName || '—', sortValue: (r) => r.routeName ?? '' },
      { id: 'line', header: t('salesAnalysis.col.line'), cell: (r) => r.line ?? '', sortValue: (r) => r.line ?? '' },
      { id: 'sku', header: t('salesAnalysis.col.sku'), cell: (r) => <span className="mono">{r.sku ?? ''}</span>, sortValue: (r) => r.sku ?? '', exportValue: (r) => r.sku ?? '' },
      { id: 'product', header: t('salesAnalysis.col.product'), cell: (r) => r.product ?? '', sortValue: (r) => r.product ?? '', card: 'title' },
      { id: 'units', header: t('salesAnalysis.col.netUnits'), cell: (r) => fm.units(r.units), sortValue: (r) => r.units ?? 0, align: 'end', exportValue: (r) => r.units ?? 0 },
      { id: 'sales', header: t('salesAnalysis.col.netSales'), cell: (r) => fm.money(r.sales), sortValue: (r) => r.sales ?? 0, align: 'end', exportValue: (r) => r.sales ?? 0 },
    ],
    [t, fm],
  )
  return (
    <Panel title={t('salesAnalysis.byRep.repProducts')} subtitle={t('salesAnalysis.byRep.repProductsSub')} badge={rows.length} flush collapsible storageKey="sa.repProducts">
      <ProductFilters value={filter} onChange={setFilter} routes={distinct(all.map((r) => r.routeName))} reps={repOptions(data.reps ?? NO_ROWS)} lines={distinct(all.map((r) => r.line))} />
      <DataTable label={t('salesAnalysis.byRep.repProducts')} columns={columns} rows={rows} rowKey={(r) => `${repKey(r.repId)}|${r.productId ?? r.sku}|${r.lineClass}`} />
    </Panel>
  )
}

function RouteProductsPanel({ data }: { data: SalesByRep }) {
  const t = useT()
  const fm = useSaFormat()
  const all = data.routeProducts ?? NO_ROWS
  const [filter, setFilter] = useState<ProductFilter>(EMPTY_PRODUCT_FILTER)
  const repName = (data.reps ?? NO_ROWS).find((r) => repKey(r.repId) === filter.rep)?.repName ?? null
  const rows = filterRouteProducts(all, filter, repName)
  const columns = useMemo<DataColumn<SalesRouteProduct>[]>(
    () => [
      { id: 'route', header: t('salesAnalysis.col.route'), cell: (r) => r.routeName || t('salesAnalysis.noRep'), sortValue: (r) => r.routeName ?? '' },
      { id: 'reps', header: t('salesAnalysis.col.reps'), cell: (r) => r.repNames || '—', sortValue: (r) => r.repNames ?? '' },
      { id: 'line', header: t('salesAnalysis.col.line'), cell: (r) => r.line ?? '', sortValue: (r) => r.line ?? '' },
      { id: 'sku', header: t('salesAnalysis.col.sku'), cell: (r) => <span className="mono">{r.sku ?? ''}</span>, sortValue: (r) => r.sku ?? '', exportValue: (r) => r.sku ?? '' },
      { id: 'product', header: t('salesAnalysis.col.variety'), cell: (r) => r.product ?? '', sortValue: (r) => r.product ?? '', card: 'title' },
      { id: 'units', header: t('salesAnalysis.col.netUnits'), cell: (r) => <span className={(r.units ?? 0) === 0 ? 'sa-muted' : undefined}>{fm.units(r.units)}</span>, sortValue: (r) => r.units ?? 0, align: 'end', exportValue: (r) => r.units ?? 0 },
      { id: 'sales', header: t('salesAnalysis.col.netSales'), cell: (r) => <span className={(r.sales ?? 0) === 0 ? 'sa-muted' : undefined}>{fm.money(r.sales)}</span>, sortValue: (r) => r.sales ?? 0, align: 'end', exportValue: (r) => r.sales ?? 0 },
    ],
    [t, fm],
  )
  return (
    <Panel title={t('salesAnalysis.byRep.routeProducts')} subtitle={t('salesAnalysis.byRep.routeProductsSub')} badge={rows.length} flush collapsible storageKey="sa.routeProducts">
      <ProductFilters value={filter} onChange={setFilter} routes={distinct(all.map((r) => r.routeName))} reps={repOptions(data.reps ?? NO_ROWS)} lines={distinct(all.map((r) => r.line))} />
      <DataTable label={t('salesAnalysis.byRep.routeProducts')} columns={columns} rows={rows} rowKey={(r) => `${r.routeId ?? 'none'}|${r.productId ?? r.sku}`} rowClassName={(r) => ((r.units ?? 0) === 0 && (r.sales ?? 0) === 0 ? 'dim' : undefined)} />
    </Panel>
  )
}

function useDebounced<T>(value: T, ms: number): T {
  const [v, setV] = useState(value)
  useEffect(() => {
    const id = setTimeout(() => setV(value), ms)
    return () => clearTimeout(id)
  }, [value, ms])
  return v
}

function TransactionsPanel({ month, reps }: { month: string; reps: readonly SalesRepRow[] }) {
  const t = useT()
  const fm = useSaFormat()
  const [rep, setRep] = useState('')
  const [text, setText] = useState('')
  const q = useDebounced(text, 300)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const query: TransactionQuery = { month, repId: rep === '' ? null : rep === 'none' ? 0 : Number(rep), q }
  const list = useSalesTransactions(query, page, pageSize)
  useEffect(() => setPage(1), [month, rep, q])
  const columns = useMemo<DataColumn<TxRow>[]>(
    () => [
      { id: 'date', header: t('salesAnalysis.col.date'), cell: (r) => fm.date(r.date), sortValue: (r) => r.date ?? '', exportValue: (r) => r.date ?? '' },
      { id: 'rep', header: t('salesAnalysis.col.rep'), cell: (r) => r.repName ?? t('salesAnalysis.noRep'), sortValue: (r) => r.repName ?? '' },
      { id: 'route', header: t('salesAnalysis.col.route'), cell: (r) => r.routeName || '—', sortValue: (r) => r.routeName ?? '' },
      { id: 'type', header: t('salesAnalysis.col.type'), cell: (r) => r.type ?? '', sortValue: (r) => r.type ?? '' },
      { id: 'number', header: t('salesAnalysis.col.number'), cell: (r) => <span className="mono">{r.number ?? ''}</span>, sortValue: (r) => r.number ?? '', exportValue: (r) => r.number ?? '' },
      { id: 'client', header: t('salesAnalysis.col.client'), cell: (r) => r.clientName ?? '', sortValue: (r) => r.clientName ?? '', card: 'title' },
      { id: 'sku', header: t('salesAnalysis.col.sku'), cell: (r) => <span className="mono">{r.sku ?? ''}</span>, sortValue: (r) => r.sku ?? '', exportValue: (r) => r.sku ?? '' },
      { id: 'product', header: t('salesAnalysis.col.product'), cell: (r) => r.product ?? '', sortValue: (r) => r.product ?? '' },
      { id: 'line', header: t('salesAnalysis.col.line'), cell: (r) => r.line || '—', sortValue: (r) => r.line ?? '' },
      { id: 'units', header: t('salesAnalysis.col.units'), cell: (r) => fm.units(r.quantity), sortValue: (r) => r.quantity ?? 0, align: 'end', exportValue: (r) => r.quantity ?? 0 },
      { id: 'amount', header: t('salesAnalysis.col.amount'), cell: (r) => fm.money(r.amount), sortValue: (r) => r.amount ?? 0, align: 'end', exportValue: (r) => r.amount ?? 0 },
      { id: 'source', header: t('salesAnalysis.col.source'), cell: (r) => (r.source ? t(`salesAnalysis.source.${r.source}`) : ''), sortValue: (r) => r.source ?? '' },
    ],
    [t, fm],
  )
  const rows = useMemo<TxRow[]>(() => (list.data?.items ?? []).map((x, i) => ({ ...x, _k: (page - 1) * pageSize + i })), [list.data, page, pageSize])
  const repOpts = [...repOptions(reps).filter((o) => o.value !== 'none'), { value: 'none', label: t('salesAnalysis.noRep') }]
  return (
    <Panel title={t('salesAnalysis.byRep.transactions')} subtitle={t('salesAnalysis.byRep.transactionsSub')} badge={list.data?.total} flush collapsible storageKey="sa.transactions">
      <div className="pb">
        <Filters onClear={rep || text ? () => (setRep(''), setText('')) : undefined}>
          <SelectFilter label={t('salesAnalysis.col.rep')} value={rep} onChange={setRep} options={repOpts} />
        </Filters>
        <div className="qrow">
          <QBox value={text} onChange={setText} placeholder={t('salesAnalysis.byRep.searchTx')} />
        </div>
      </div>
      {list.isError ? (
        <div className="pb">
          <QueryError error={list.error} />
        </div>
      ) : (
        <DataTable
          label={t('salesAnalysis.byRep.transactions')}
          columns={columns}
          rows={rows}
          rowKey={(r) => String(r._k)}
          loading={list.isPending}
          page={page}
          pageSize={pageSize}
          total={list.data?.total ?? 0}
          onPage={setPage}
          onPageSize={(n) => (setPageSize(n), setPage(1))}
          exportRows={() => fetchTransactions(query, 0, 500, true).then((r) => (r.items ?? []).map((x, i) => ({ ...x, _k: i })))}
          empty={t('salesAnalysis.byRep.noTx')}
        />
      )}
    </Panel>
  )
}
