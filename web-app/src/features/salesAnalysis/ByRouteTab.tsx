// Lote 244 — pestaña «Por ruta» (la vista por omisión del Análisis de ventas): mes y mes de comparación, tarjetas, el comparativo
// ruta de venta → personas (plegable, con «Sin ruta» y «Sin vendedor»), el cumplimiento según el ajuste «la cuota se mide por»
// (en la ruta con ROUTE, en la persona con REP), la tabla «Por persona», los totales con la conciliación, las Líneas, el Producto
// por ruta y las Transacciones del mes (paginadas por el servidor, con filtro de ruta y `exportRows`). «Por representante» sigue
// existiendo como la otra vista.
import { Fragment, useEffect, useMemo, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useLang, useT } from '../../kernel/i18n/useT'
import { Chip } from '../../kernel/ui/Chip'
import { DataTable, type DataColumn } from '../../kernel/ui/DataTable'
import { Filters, SelectFilter } from '../../kernel/ui/Filters'
import { Panel } from '../../kernel/ui/Panel'
import { QBox } from '../../kernel/ui/QBox'
import { Spinner } from '../../kernel/ui/Spinner'
import { toast } from '../../kernel/ui/toast'
import { buildByRouteWorkbook, downloadWorkbook, workbookFileName } from './salesAnalysisExcel'
import { fetchTransactions, useSalesByRoute, useSalesTransactions, type TransactionQuery } from './salesAnalysisApi'
import {
  changeTone,
  complianceLevel,
  distinct,
  lineShareFromLines,
  monthLong,
  monthOptions,
  parseQuotaBy,
  prevMonth,
  routeKey,
  routeQuotaFromRows,
  type QuotaBy,
  type SalesByRoute,
  type SalesLineShare,
  type SalesPersonTotal,
  type SalesRouteProduct,
  type SalesRouteRow,
  type SalesTransaction,
} from './salesAnalysisRules'
import { matchesQ } from '../../kernel/ui/matchesQ'
import { ComplianceBar, KpiTiles, MonthSelect, QueryError, Warnings } from './SaParts'
import { LineShareChart, RepQuotaChart } from './SalesCharts'
import { useSaFormat } from './saFormat'

const NO_ROWS: never[] = []
type TxRow = SalesTransaction & { _k: number }

export function ByRouteTab() {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const [month, setMonth] = useState('')
  const [compare, setCompare] = useState('')
  const q = useSalesByRoute(month, compare)
  const data = q.data
  const shownMonth = data?.month ?? month
  const options = useMemo(() => monthOptions(f.today(), 36), [f])
  const [exporting, setExporting] = useState(false)

  const exportXlsx = async () => {
    if (!data?.month) return
    setExporting(true)
    try {
      const tx = await fetchTransactions({ month: data.month, repId: null, routeId: null, q: '' }, 0, 500, true)
      await downloadWorkbook(
        (XLSX) => buildByRouteWorkbook(XLSX, { byRoute: data, transactions: tx.items ?? [], t, lang }),
        workbookFileName(t('salesAnalysis.byRoute.xlsxFile', { month: data.month })),
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
        <Filters label={t('salesAnalysis.byRoute.filters')}>
          <MonthSelect id="sa-rt-month" label={t('salesAnalysis.byRoute.month')} value={shownMonth} options={options} onChange={(v) => (setMonth(v), setCompare(''))} />
          <MonthSelect
            id="sa-rt-compare"
            label={t('salesAnalysis.byRoute.compare')}
            value={compare}
            options={options}
            onChange={setCompare}
            emptyLabel={shownMonth ? t('salesAnalysis.byRoute.previous', { month: monthLong(prevMonth(shownMonth), lang) }) : t('salesAnalysis.byRep.previousShort')}
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
          <RouteKpis data={data} />
          <p className="sa-muted sa-basis" role="note">
            {parseQuotaBy(data.quotaBy) === 'ROUTE' ? t('salesAnalysis.byRoute.quotaBasisRoute') : t('salesAnalysis.byRoute.quotaBasisRep')}
          </p>
          <RoutesPanel data={data} />
          {parseQuotaBy(data.quotaBy) === 'REP' && <PeoplePanel data={data} />}
          <LinesPanel data={data} />
          <RouteProductsPanel data={data} />
          <TransactionsPanel month={data.month ?? ''} routes={data.routes ?? NO_ROWS} />
        </>
      )}
      {q.isFetching && data && <span className="sr-only" role="status">{t('common.loading')}</span>}
    </div>
  )
}

function RouteKpis({ data }: { data: SalesByRoute }) {
  const t = useT()
  const lang = useLang()
  const fm = useSaFormat()
  const tot = data.totals ?? {}
  const rec = data.reconciliation ?? {}
  const ct = changeTone(tot.change)
  return (
    <KpiTiles
      label={t('salesAnalysis.byRoute.kpis')}
      items={[
        { key: 'sales', label: t('salesAnalysis.byRoute.kpiSales', { month: monthLong(data.month ?? '', lang) }), value: fm.money(tot.sales), sub: t('salesAnalysis.byRoute.kpiMonthTotal', { amount: fm.money(rec.monthTotal) }) },
        {
          key: 'change',
          label: t('salesAnalysis.byRoute.kpiChange', { month: monthLong(data.compareMonth ?? '', lang) }),
          value: `${(tot.change ?? 0) > 0 ? '+' : ''}${fm.money(tot.change)}`,
          sub: `${fm.signedPct(tot.changePct)} · ${t('salesAnalysis.byRoute.kpiCompareWas', { amount: fm.money(tot.compareSales) })}`,
          tone: ct,
        },
        {
          key: 'quota',
          label: t('salesAnalysis.byRoute.kpiQuota'),
          value: fm.pct(tot.compliance),
          sub: t('salesAnalysis.byRoute.kpiQuotaOf', { quota: fm.money(tot.quota) }),
          tone: complianceLevel(tot.compliance),
          extra: <ComplianceBar value={tot.compliance} />,
        },
      ]}
    />
  )
}

/** Comparativo ruta → personas. Las personas de cada ruta están plegadas; el botón de la fila (o «Desplegar todas») las abre. */
function RoutesPanel({ data }: { data: SalesByRoute }) {
  const t = useT()
  const lang = useLang()
  const fm = useSaFormat()
  const mode: QuotaBy = parseQuotaBy(data.quotaBy)
  const rows = data.routes ?? NO_ROWS
  const tot = data.totals ?? {}
  const rec = data.reconciliation ?? {}
  const [open, setOpen] = useState<ReadonlySet<string>>(new Set())
  const keyOf = (r: SalesRouteRow) => routeKey(r.routeId)
  const expandable = rows.filter((r) => (r.people?.length ?? 0) > 0)
  const allOpen = expandable.length > 0 && expandable.every((r) => open.has(keyOf(r)))
  const toggle = (k: string) =>
    setOpen((prev) => {
      const next = new Set(prev)
      if (next.has(k)) next.delete(k)
      else next.add(k)
      return next
    })
  const cName = monthLong(data.compareMonth ?? '', lang)
  const mName = monthLong(data.month ?? '', lang)
  const empty = (
    <td className="sa-muted" aria-hidden="true">
      —
    </td>
  )
  return (
    <div className="sa-split sa-rt-split">
      <Panel title={t('salesAnalysis.byRoute.comparativo')} subtitle={t('salesAnalysis.byRoute.comparativoSub')} badge={rows.length} flush>
        <div className="pb sa-rt-bar">
          <button type="button" className="btn sm" onClick={() => setOpen(allOpen ? new Set() : new Set(expandable.map(keyOf)))} disabled={expandable.length === 0}>
            {allOpen ? t('salesAnalysis.byRoute.collapseAll') : t('salesAnalysis.byRoute.expandAll')}
          </button>
        </div>
        <div className="sa-scroll" tabIndex={0} role="region" aria-label={t('salesAnalysis.byRoute.comparativo')}>
          <table className="sa-mini sa-rt" aria-label={t('salesAnalysis.byRoute.comparativoTable')}>
            <thead>
              <tr>
                <th scope="col">{t('salesAnalysis.byRoute.route')}</th>
                <th scope="col">{cName}</th>
                <th scope="col">{mName}</th>
                <th scope="col">{t('salesAnalysis.col.change')}</th>
                <th scope="col">{t('salesAnalysis.col.changePct')}</th>
                <th scope="col">{t('salesAnalysis.col.quota')}</th>
                <th scope="col">{t('salesAnalysis.col.compliance')}</th>
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 && (
                <tr>
                  <td colSpan={7} className="sa-muted">
                    {t('salesAnalysis.byRoute.noRoutes')}
                  </td>
                </tr>
              )}
              {rows.map((r) => {
                const k = keyOf(r)
                const isOpen = open.has(k)
                const people = r.people ?? NO_ROWS
                const name = r.routeId == null ? t('salesAnalysis.byRoute.noRoute') : (r.routeName ?? '')
                return (
                  <Fragment key={k}>
                    <tr className="sa-rt-route">
                      <th scope="row">
                        <button
                          type="button"
                          className="sa-rt-toggle"
                          aria-expanded={isOpen}
                          aria-label={t(isOpen ? 'salesAnalysis.byRoute.collapse' : 'salesAnalysis.byRoute.expand', { route: name })}
                          onClick={() => toggle(k)}
                          disabled={people.length === 0}
                        >
                          <span className="sa-rt-caret" aria-hidden="true">{people.length === 0 ? '·' : isOpen ? '▾' : '▸'}</span>
                          <span className="sa-rt-name">{name}</span>
                        </button>
                        {r.routeId != null && (
                          <span className="sa-rt-sub">
                            {r.primaryName ? `${t('salesAnalysis.byRoute.primary')}: ${r.primaryName}` : <Chip tone="warn">{t('salesAnalysis.byRoute.noPrimary')}</Chip>}
                          </span>
                        )}
                      </th>
                      <td>{fm.money(r.compareSales)}</td>
                      <td>{fm.money(r.sales)}</td>
                      <td><span className={`sa-delta d-${changeTone(r.change)}`}>{fm.money(r.change)}</span></td>
                      <td><span className={`sa-delta d-${changeTone(r.change)}`}>{fm.signedPct(r.changePct)}</span></td>
                      {mode === 'ROUTE' ? <td>{fm.money(r.quota)}</td> : empty}
                      <td className="sa-rt-comp">{mode === 'ROUTE' ? <ComplianceBar value={r.compliance} /> : <span className="sa-muted">—</span>}</td>
                    </tr>
                    {isOpen &&
                      people.map((p) => (
                        <tr key={p.personKey} className="sa-rt-person">
                          <th scope="row">
                            <span className="sa-rt-pname">{p.name ?? t('salesAnalysis.byRoute.noSeller')}</span>
                            {p.name != null && p.personKey !== 'NONE' && <Chip tone={p.isPrimary ? 'deliv' : 'cap'}>{p.isPrimary ? t('salesAnalysis.byRoute.rolePrimary') : t('salesAnalysis.byRoute.roleSupport')}</Chip>}
                          </th>
                          <td>{fm.money(p.compareSales)}</td>
                          <td>{fm.money(p.sales)}</td>
                          <td><span className={`sa-delta d-${changeTone(p.change)}`}>{fm.money(p.change)}</span></td>
                          <td><span className={`sa-delta d-${changeTone(p.change)}`}>{fm.signedPct(p.changePct)}</span></td>
                          {mode === 'REP' && p.quota != null ? <td>{fm.money(p.quota)}</td> : empty}
                          <td className="sa-rt-comp">{mode === 'REP' && p.compliance != null ? <ComplianceBar value={p.compliance} /> : <span className="sa-muted">—</span>}</td>
                        </tr>
                      ))}
                  </Fragment>
                )
              })}
            </tbody>
          </table>
        </div>
        <div className="sa-scroll pb">
          <table className="sa-mini sa-rec" aria-label={t('salesAnalysis.byRoute.reconciliation')}>
            <thead>
              <tr>
                <th scope="col">{t('salesAnalysis.byRoute.reconciliation')}</th>
                <th scope="col">{cName}</th>
                <th scope="col">{mName}</th>
                <th scope="col">{t('salesAnalysis.col.change')}</th>
              </tr>
            </thead>
            <tbody>
              <tr className="tot">
                <th scope="row">{t('salesAnalysis.byRoute.totalRoutes')}</th>
                <td>{fm.money(tot.compareSales)}</td>
                <td>{fm.money(tot.sales)}</td>
                <td>{`${fm.money(tot.change)} · ${fm.signedPct(tot.changePct)}`}</td>
              </tr>
              <tr>
                <th scope="row" title={t('salesAnalysis.byRoute.unassignedHelp')}>{t('salesAnalysis.byRoute.unassigned')}</th>
                <td>{fm.money(rec.unassignedCompare)}</td>
                <td>{fm.money(rec.unassigned)}</td>
                <td />
              </tr>
              <tr>
                <th scope="row">{t('salesAnalysis.byRoute.shippingOther')}</th>
                <td />
                <td>{fm.money(rec.shippingAndOther)}</td>
                <td />
              </tr>
              <tr className="tot">
                <th scope="row">{t('salesAnalysis.byRoute.monthTotal')}</th>
                <td />
                <td>{fm.money(rec.monthTotal)}</td>
                <td />
              </tr>
              {Math.abs(rec.difference ?? 0) >= 0.005 && (
                <tr>
                  <th scope="row">{t('salesAnalysis.byRoute.difference')}</th>
                  <td />
                  <td className="sa-delta d-down">{fm.money(rec.difference)}</td>
                  <td />
                </tr>
              )}
              <tr>
                <td colSpan={4} className="detail sa-muted">
                  {t('salesAnalysis.byRoute.recDetail', { product: fm.money(rec.productSales), discounts: fm.money(rec.discounts), shipping: fm.money(rec.shipping), other: fm.money(rec.other) })}
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </Panel>
      <Panel>
        <RepQuotaChart points={routeQuotaFromRows(rows)} month={data.month} mode={mode} />
      </Panel>
    </div>
  )
}

/** «Por persona» (cuota por persona): la venta TOTAL de cada persona en todas sus rutas contra su cuota. */
function PeoplePanel({ data }: { data: SalesByRoute }) {
  const t = useT()
  const lang = useLang()
  const fm = useSaFormat()
  const rows = data.people ?? NO_ROWS
  const cName = monthLong(data.compareMonth ?? '', lang)
  const mName = monthLong(data.month ?? '', lang)
  const columns = useMemo<DataColumn<SalesPersonTotal>[]>(
    () => [
      { id: 'person', header: t('salesAnalysis.byRoute.person'), cell: (r) => r.name ?? t('salesAnalysis.byRoute.noSeller'), sortValue: (r) => r.name ?? '', card: 'title' },
      { id: 'routes', header: t('salesAnalysis.col.route'), cell: (r) => r.routes || '—', sortValue: (r) => r.routes ?? '' },
      { id: 'compare', header: cName, cell: (r) => fm.money(r.compareSales), sortValue: (r) => r.compareSales ?? 0, align: 'end', exportValue: (r) => r.compareSales ?? 0 },
      { id: 'sales', header: mName, cell: (r) => fm.money(r.sales), sortValue: (r) => r.sales ?? 0, align: 'end', exportValue: (r) => r.sales ?? 0 },
      { id: 'change', header: t('salesAnalysis.col.change'), cell: (r) => <span className={`sa-delta d-${changeTone(r.change)}`}>{fm.money(r.change)}</span>, sortValue: (r) => r.change ?? 0, align: 'end', exportValue: (r) => r.change ?? 0 },
      { id: 'quota', header: t('salesAnalysis.col.quota'), cell: (r) => fm.money(r.quota), sortValue: (r) => r.quota ?? null, align: 'end', exportValue: (r) => r.quota ?? null },
      { id: 'compliance', header: t('salesAnalysis.col.compliance'), cell: (r) => <ComplianceBar value={r.compliance} />, sortValue: (r) => r.compliance ?? null, exportValue: (r) => r.compliance ?? null },
    ],
    [t, fm, cName, mName],
  )
  return (
    <Panel title={t('salesAnalysis.byRoute.byPerson')} subtitle={t('salesAnalysis.byRoute.byPersonSub')} badge={rows.length} flush>
      <DataTable label={t('salesAnalysis.byRoute.byPerson')} columns={columns} rows={rows} rowKey={(r) => r.personKey ?? ''} pagination={false} empty={t('salesAnalysis.byRoute.noPeople')} />
    </Panel>
  )
}

function LinesPanel({ data }: { data: SalesByRoute }) {
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
      <Panel title={t('salesAnalysis.byRoute.lines')} subtitle={t('salesAnalysis.byRoute.linesSub')} badge={rows.length} flush>
        <DataTable label={t('salesAnalysis.byRoute.lines')} columns={columns} rows={rows} rowKey={(r) => `${r.line}|${r.isDiscounts}`} pagination={false} rowClassName={(r) => (r.isDiscounts ? 'dim' : undefined)} />
      </Panel>
      <Panel>
        <LineShareChart points={lineShareFromLines(rows)} />
      </Panel>
    </div>
  )
}

function RouteProductsPanel({ data }: { data: SalesByRoute }) {
  const t = useT()
  const fm = useSaFormat()
  const all = data.routeProducts ?? NO_ROWS
  const [route, setRoute] = useState('')
  const [line, setLine] = useState('')
  const [text, setText] = useState('')
  const rows = all.filter((r) => (!route || (r.routeName ?? '') === route) && (!line || (r.line ?? '') === line) && matchesQ(text, r.sku, r.product, r.repNames, r.routeName, r.line))
  const columns = useMemo<DataColumn<SalesRouteProduct>[]>(
    () => [
      { id: 'route', header: t('salesAnalysis.col.route'), cell: (r) => r.routeName || t('salesAnalysis.byRoute.noRoute'), sortValue: (r) => r.routeName ?? '' },
      { id: 'reps', header: t('salesAnalysis.byRoute.people'), cell: (r) => r.repNames || '—', sortValue: (r) => r.repNames ?? '' },
      { id: 'line', header: t('salesAnalysis.col.line'), cell: (r) => r.line ?? '', sortValue: (r) => r.line ?? '' },
      { id: 'sku', header: t('salesAnalysis.col.sku'), cell: (r) => <span className="mono">{r.sku ?? ''}</span>, sortValue: (r) => r.sku ?? '', exportValue: (r) => r.sku ?? '' },
      { id: 'product', header: t('salesAnalysis.col.variety'), cell: (r) => r.product ?? '', sortValue: (r) => r.product ?? '', card: 'title' },
      { id: 'units', header: t('salesAnalysis.col.netUnits'), cell: (r) => <span className={(r.units ?? 0) === 0 ? 'sa-muted' : undefined}>{fm.units(r.units)}</span>, sortValue: (r) => r.units ?? 0, align: 'end', exportValue: (r) => r.units ?? 0 },
      { id: 'sales', header: t('salesAnalysis.col.netSales'), cell: (r) => <span className={(r.sales ?? 0) === 0 ? 'sa-muted' : undefined}>{fm.money(r.sales)}</span>, sortValue: (r) => r.sales ?? 0, align: 'end', exportValue: (r) => r.sales ?? 0 },
    ],
    [t, fm],
  )
  return (
    <Panel title={t('salesAnalysis.byRoute.routeProducts')} subtitle={t('salesAnalysis.byRoute.routeProductsSub')} badge={rows.length} flush collapsible storageKey="sa.byRoute.products">
      <div className="pb">
        <Filters onClear={route || line || text ? () => (setRoute(''), setLine(''), setText('')) : undefined}>
          <SelectFilter label={t('salesAnalysis.col.route')} value={route} onChange={setRoute} options={distinct(all.map((r) => r.routeName)).map((r) => ({ value: r, label: r }))} />
          <SelectFilter label={t('salesAnalysis.col.line')} value={line} onChange={setLine} options={distinct(all.map((r) => r.line)).map((l) => ({ value: l, label: l }))} />
        </Filters>
        <div className="qrow">
          <QBox value={text} onChange={setText} placeholder={t('salesAnalysis.byRep.searchProduct')} />
        </div>
      </div>
      <DataTable label={t('salesAnalysis.byRoute.routeProducts')} columns={columns} rows={rows} rowKey={(r) => `${r.routeId ?? 'none'}|${r.productId ?? r.sku}`} rowClassName={(r) => ((r.units ?? 0) === 0 && (r.sales ?? 0) === 0 ? 'dim' : undefined)} />
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

function TransactionsPanel({ month, routes }: { month: string; routes: readonly SalesRouteRow[] }) {
  const t = useT()
  const fm = useSaFormat()
  const [route, setRoute] = useState('')
  const [text, setText] = useState('')
  const q = useDebounced(text, 300)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const query: TransactionQuery = { month, repId: null, routeId: route === '' ? null : route === 'none' ? 0 : Number(route), q }
  const list = useSalesTransactions(query, page, pageSize)
  useEffect(() => setPage(1), [month, route, q])
  const columns = useMemo<DataColumn<TxRow>[]>(
    () => [
      { id: 'date', header: t('salesAnalysis.col.date'), cell: (r) => fm.date(r.date), sortValue: (r) => r.date ?? '', exportValue: (r) => r.date ?? '' },
      { id: 'route', header: t('salesAnalysis.col.route'), cell: (r) => r.routeName || t('salesAnalysis.byRoute.noRoute'), sortValue: (r) => r.routeName ?? '' },
      { id: 'rep', header: t('salesAnalysis.byRoute.person'), cell: (r) => r.repName ?? t('salesAnalysis.byRoute.noSeller'), sortValue: (r) => r.repName ?? '' },
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
  const routeOpts = [
    ...routes.filter((r) => r.routeId != null).map((r) => ({ value: routeKey(r.routeId), label: r.routeName ?? '' })),
    { value: 'none', label: t('salesAnalysis.byRoute.noRoute') },
  ]
  return (
    <Panel title={t('salesAnalysis.byRoute.transactions')} subtitle={t('salesAnalysis.byRep.transactionsSub')} badge={list.data?.total} flush collapsible storageKey="sa.byRoute.transactions">
      <div className="pb">
        <Filters onClear={route || text ? () => (setRoute(''), setText('')) : undefined}>
          <SelectFilter label={t('salesAnalysis.col.route')} value={route} onChange={setRoute} options={routeOpts} />
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
          label={t('salesAnalysis.byRoute.transactions')}
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
