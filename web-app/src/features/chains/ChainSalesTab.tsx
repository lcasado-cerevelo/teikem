// Lote 245 — pestaña «Por cadena» del Análisis de ventas (una vista MÁS: «Por ruta» sigue siendo la de por omisión). Mes y mes de comparación, tarjetas, el comparativo
// cadena → clientes (renglones expandibles por cliente, con «Sin cadena» y las cadenas inactivas que vendieron), la conciliación (cadenas + sin cadena = venta neta del
// mes) y un gráfico de barras por cadena. La venta se atribuye a la cadena ACTUAL del cliente. Una cadena no es una ruta.
import { Fragment, useMemo, useState } from 'react'
import { Bar, BarChart, CartesianGrid, Cell, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { useFormat } from '../../kernel/format'
import { useLang, useT } from '../../kernel/i18n/useT'
import { Chip } from '../../kernel/ui/Chip'
import { Filters } from '../../kernel/ui/Filters'
import { Panel } from '../../kernel/ui/Panel'
import { Spinner } from '../../kernel/ui/Spinner'
import { KpiTiles, MonthSelect, QueryError } from '../salesAnalysis/SaParts'
import { changeTone, monthLong, monthOptions, prevMonth } from '../salesAnalysis/salesAnalysisRules'
import { useSaFormat } from '../salesAnalysis/saFormat'
import { useChainSales, type ChainSalesRow } from './chainsApi'
import './chains.css'

const NO_ROWS: never[] = []
const TOOLTIP_STYLE = { background: 'var(--panel)', border: '1px solid var(--line)', borderRadius: 8, color: 'var(--text)', fontSize: 12 }
const TICK = { fontSize: 11, fill: 'var(--muted)' }
const rowKey = (r: ChainSalesRow) => r.chainPublicId ?? 'NONE'

export function ChainSalesTab() {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const fm = useSaFormat()
  const [month, setMonth] = useState('')
  const [compare, setCompare] = useState('')
  const q = useChainSales(month, compare)
  const data = q.data
  const shownMonth = data?.month ?? month
  const options = useMemo(() => monthOptions(f.today(), 36), [f])
  const [open, setOpen] = useState<ReadonlySet<string>>(new Set())

  const rows = useMemo<ChainSalesRow[]>(() => [...(data?.rows ?? NO_ROWS), ...(data?.noChain ? [data.noChain] : [])], [data])
  const expandable = rows.filter((r) => (r.clients?.length ?? 0) > 0)
  const allOpen = expandable.length > 0 && expandable.every((r) => open.has(rowKey(r)))
  const toggle = (k: string) =>
    setOpen((prev) => {
      const next = new Set(prev)
      if (next.has(k)) next.delete(k)
      else next.add(k)
      return next
    })
  const nameOf = (r: ChainSalesRow) => (r.chainPublicId == null ? t('chains.noChain') : (r.chainName ?? ''))
  const bars = rows.filter((r) => (r.sales ?? 0) !== 0).map((r) => ({ key: rowKey(r), name: nameOf(r), sales: r.sales ?? 0, none: r.chainPublicId == null }))

  const tot = data?.totals
  const rec = data?.reconciliation
  const cName = monthLong(data?.compareMonth ?? '', lang)
  const mName = monthLong(data?.month ?? '', lang)

  return (
    <div className="sa-tab chn-tab">
      <div className="sa-toolbar">
        <Filters label={t('chains.tab.filters')}>
          <MonthSelect id="sa-ch-month" label={t('salesAnalysis.byRoute.month')} value={shownMonth} options={options} onChange={(v) => (setMonth(v), setCompare(''))} />
          <MonthSelect
            id="sa-ch-compare"
            label={t('salesAnalysis.byRoute.compare')}
            value={compare}
            options={options}
            onChange={setCompare}
            emptyLabel={shownMonth ? t('salesAnalysis.byRoute.previous', { month: monthLong(prevMonth(shownMonth), lang) }) : t('salesAnalysis.byRep.previousShort')}
          />
        </Filters>
      </div>
      {q.isError ? (
        <QueryError error={q.error} />
      ) : !data ? (
        <Spinner block label={t('common.loading')} />
      ) : (
        <>
          <KpiTiles
            label={t('chains.tab.kpis')}
            items={[
              { key: 'sales', label: t('salesAnalysis.byRoute.kpiSales', { month: mName }), value: fm.money(tot?.sales), sub: t('salesAnalysis.byRoute.kpiMonthTotal', { amount: fm.money(rec?.monthNet) }) },
              {
                key: 'change',
                label: t('salesAnalysis.byRoute.kpiChange', { month: cName }),
                value: `${(tot?.change ?? 0) > 0 ? '+' : ''}${fm.money(tot?.change)}`,
                sub: `${fm.signedPct(tot?.changePct)} · ${t('salesAnalysis.byRoute.kpiCompareWas', { amount: fm.money(tot?.compareSales) })}`,
                tone: changeTone(tot?.change),
              },
              { key: 'chains', label: t('chains.tab.kpiChains'), value: rows.filter((r) => r.chainPublicId != null).length, sub: t('chains.tab.kpiNoChain', { amount: fm.money(rec?.noChain) }) },
            ]}
          />
          <p className="sa-muted sa-basis" role="note">
            {t('chains.tab.basis')}
          </p>
          <Panel title={t('chains.tab.comparative')} subtitle={t('chains.tab.comparativeSub')} badge={rows.length} flush>
            <div className="pb sa-rt-bar">
              <button type="button" className="btn sm" onClick={() => setOpen(allOpen ? new Set() : new Set(expandable.map(rowKey)))} disabled={expandable.length === 0}>
                {allOpen ? t('chains.tab.collapseAll') : t('chains.tab.expandAll')}
              </button>
            </div>
            <div className="sa-scroll" tabIndex={0} role="region" aria-label={t('chains.tab.comparative')}>
              <table className="sa-mini sa-rt chn-sales-table" aria-label={t('chains.tab.comparative')}>
                <thead>
                  <tr>
                    <th scope="col">{t('chains.tab.chain')}</th>
                    <th scope="col">{cName}</th>
                    <th scope="col">{mName}</th>
                    <th scope="col">{t('salesAnalysis.col.change')}</th>
                    <th scope="col">{t('salesAnalysis.col.changePct')}</th>
                    <th scope="col">{t('chains.tab.share')}</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.length === 0 && (
                    <tr>
                      <td colSpan={6} className="sa-muted">
                        {t('chains.tab.empty')}
                      </td>
                    </tr>
                  )}
                  {rows.map((r) => {
                    const k = rowKey(r)
                    const isOpen = open.has(k)
                    const clients = r.clients ?? NO_ROWS
                    return (
                      <Fragment key={k}>
                        <tr className="sa-rt-route">
                          <th scope="row">
                            <button
                              type="button"
                              className="sa-rt-toggle"
                              aria-expanded={isOpen}
                              aria-label={t(isOpen ? 'chains.tab.collapse' : 'chains.tab.expand', { chain: nameOf(r) })}
                              onClick={() => toggle(k)}
                              disabled={clients.length === 0}
                            >
                              <span className="sa-rt-caret" aria-hidden="true">{clients.length === 0 ? '·' : isOpen ? '▾' : '▸'}</span>
                              <span className="sa-rt-name">{nameOf(r)}</span>
                            </button>
                            <span className="sa-rt-sub">
                              {t('chains.tab.clientsWithSales', { n: r.clientsWithSales ?? 0, total: r.clientCount ?? 0 })}
                              {r.chainPublicId != null && r.isActive === false && <Chip tone="warn">{t('chains.inactive')}</Chip>}
                            </span>
                          </th>
                          <td>{fm.money(r.compareSales)}</td>
                          <td>{fm.money(r.sales)}</td>
                          <td><span className={`sa-delta d-${changeTone(r.change)}`}>{fm.money(r.change)}</span></td>
                          <td><span className={`sa-delta d-${changeTone(r.change)}`}>{fm.signedPct(r.changePct)}</span></td>
                          <td>{fm.pct(r.shareOfNet)}</td>
                        </tr>
                        {isOpen &&
                          clients.map((c, i) => (
                            <tr key={`${c.clientPublicId ?? c.clientName}-${i}`} className="sa-rt-person">
                              <th scope="row">
                                <span className="sa-rt-pname">{c.clientName}</span>
                                {c.clientPublicId == null && <Chip tone="neutral">{t('chains.tab.unmatched')}</Chip>}
                              </th>
                              <td>{fm.money(c.compareSales)}</td>
                              <td>{fm.money(c.sales)}</td>
                              <td><span className={`sa-delta d-${changeTone(c.change)}`}>{fm.money(c.change)}</span></td>
                              <td><span className={`sa-delta d-${changeTone(c.change)}`}>{fm.signedPct(c.changePct)}</span></td>
                              <td>{fm.pct(c.shareOfChain)}</td>
                            </tr>
                          ))}
                      </Fragment>
                    )
                  })}
                </tbody>
              </table>
            </div>
            <div className="sa-scroll pb">
              <table className="sa-mini sa-rec" aria-label={t('chains.tab.reconciliation')}>
                <tbody>
                  <tr>
                    <th scope="row">{t('chains.tab.recChains')}</th>
                    <td>{fm.money(rec?.chainsTotal)}</td>
                  </tr>
                  <tr>
                    <th scope="row">{t('chains.noChain')}</th>
                    <td>{fm.money(rec?.noChain)}</td>
                  </tr>
                  <tr>
                    <th scope="row">{t('chains.tab.recMonth', { month: mName })}</th>
                    <td>{fm.money(rec?.monthNet)}</td>
                  </tr>
                  <tr>
                    <th scope="row">{t('chains.tab.recDifference')}</th>
                    <td className={(rec?.difference ?? 0) === 0 ? '' : 'sa-delta d-down'}>{fm.money(rec?.difference)}</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </Panel>
          {bars.length > 0 && (
            <Panel title={t('chains.tab.chart')} subtitle={mName}>
              <div className="chn-chartbox chn-chartbox-tall" style={{ height: Math.max(180, bars.length * 38 + 40) }} role="img" aria-label={`${t('chains.tab.chart')}. ${bars.map((b) => `${b.name}: ${fm.money(b.sales)}`).join('; ')}`}>
                <ResponsiveContainer width="100%" height="100%">
                  <BarChart data={bars} layout="vertical" accessibilityLayer={false} margin={{ top: 4, right: 16, bottom: 0, left: 0 }}>
                    <CartesianGrid strokeDasharray="3 3" stroke="var(--line)" horizontal={false} />
                    <XAxis type="number" tickFormatter={fm.compactMoney} tick={TICK} stroke="var(--line)" />
                    <YAxis type="category" dataKey="name" width={110} tick={TICK} tickFormatter={(v) => (String(v).length > 16 ? `${String(v).slice(0, 15)}…` : String(v))} stroke="var(--line)" />
                    <Tooltip contentStyle={TOOLTIP_STYLE} cursor={{ fill: 'rgba(127,127,127,.12)' }} formatter={(v) => [fm.money(Number(v)), t('chains.sales.sales')]} />
                    <Bar dataKey="sales" barSize={18} radius={[0, 4, 4, 0]} isAnimationActive={false}>
                      {bars.map((b) => (
                        <Cell key={b.key} fill={b.none ? 'var(--muted)' : 'var(--money)'} />
                      ))}
                    </Bar>
                  </BarChart>
                </ResponsiveContainer>
              </div>
            </Panel>
          )}
        </>
      )}
      {q.isFetching && data && <span className="sr-only" role="status">{t('common.loading')}</span>}
    </div>
  )
}
