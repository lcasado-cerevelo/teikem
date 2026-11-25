// Lote 245 — «Venta» en la ficha de la cadena: venta neta por mes (12 meses) y los clientes de la cadena en el mes elegido contra el anterior. Solo con
// sales.analysis.view|manage. Son datos del análisis de ventas (misma regla de venta del Lote 241: kits repartidos, notas de crédito restan).
import { useMemo, useState } from 'react'
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { Can } from '../../kernel/access'
import { useFormat } from '../../kernel/format'
import { useLang, useT } from '../../kernel/i18n'
import { DataTable, Panel, Spinner, type DataColumn } from '../../kernel/ui'
import { IconChart } from '../../kernel/ui/screenIcons'
import { MonthSelect, QueryError } from '../salesAnalysis/SaParts'
import { monthLong, monthOptions, monthShort } from '../salesAnalysis/salesAnalysisRules'
import { useSaFormat } from '../salesAnalysis/saFormat'
import { useChainSales, useChainSeries, type ChainDetail, type ChainSalesClient } from './chainsApi'

const TOOLTIP_STYLE = { background: 'var(--panel)', border: '1px solid var(--line)', borderRadius: 8, color: 'var(--text)', fontSize: 12 }
const TICK = { fontSize: 11, fill: 'var(--muted)' }

export function ChainSalesSection({ chain }: { chain: ChainDetail }) {
  return (
    <Can perm="sales.analysis.view|sales.analysis.manage">
      <ChainSalesBody chain={chain} />
    </Can>
  )
}

function ChainSalesBody({ chain }: { chain: ChainDetail }) {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const fm = useSaFormat()
  const publicId = chain.publicId ?? ''
  const [month, setMonth] = useState('')
  const series = useChainSeries(publicId)
  const sales = useChainSales(month, '', publicId)
  const options = useMemo(() => monthOptions(f.today(), 36), [f])
  const shown = sales.data?.month ?? month
  const row = sales.data?.rows?.[0]
  const bars = (series.data?.months ?? []).map((m) => ({ label: monthShort(m.month ?? '', lang), sales: m.sales ?? 0 }))

  const columns = useMemo<DataColumn<ChainSalesClient>[]>(
    () => [
      { id: 'name', header: t('chains.sales.client'), cell: (c) => <b className="chn-name">{c.clientName}</b>, sortValue: (c) => c.clientName ?? '', exportValue: (c) => c.clientName ?? '', card: 'title' },
      { id: 'compare', header: t('chains.sales.compare'), cell: (c) => fm.money(c.compareSales), sortValue: (c) => c.compareSales ?? 0, exportValue: (c) => c.compareSales ?? 0, align: 'end' },
      { id: 'sales', header: t('chains.sales.sales'), cell: (c) => fm.money(c.sales), sortValue: (c) => c.sales ?? 0, exportValue: (c) => c.sales ?? 0, align: 'end' },
      { id: 'change', header: t('chains.sales.change'), cell: (c) => fm.signedPct(c.changePct), sortValue: (c) => c.changePct ?? 0, exportValue: (c) => c.changePct ?? 0, align: 'end' },
      { id: 'share', header: t('chains.sales.share'), cell: (c) => fm.pct(c.shareOfChain), sortValue: (c) => c.shareOfChain ?? 0, exportValue: (c) => c.shareOfChain ?? 0, align: 'end' },
    ],
    [t, fm],
  )

  return (
    <Panel icon={<IconChart />} title={t('chains.sales.title')} subtitle={t('chains.sales.subtitle')} collapsible defaultCollapsed storageKey="chains.sales">
      {series.isError ? (
        <QueryError error={series.error} />
      ) : series.isPending ? (
        <Spinner block />
      ) : (
        <figure className="chn-chart">
          <figcaption>{t('chains.sales.byMonth')}</figcaption>
          <div className="chn-chartbox" role="img" aria-label={`${t('chains.sales.byMonth')}. ${bars.map((b) => `${b.label}: ${fm.money(b.sales)}`).join('; ')}`}>
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={bars} accessibilityLayer={false} margin={{ top: 4, right: 8, bottom: 0, left: 0 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="var(--line)" vertical={false} />
                <XAxis dataKey="label" tick={TICK} stroke="var(--line)" interval="preserveStartEnd" />
                <YAxis tickFormatter={fm.compactMoney} tick={TICK} stroke="var(--line)" width={56} />
                <Tooltip contentStyle={TOOLTIP_STYLE} cursor={{ fill: 'rgba(127,127,127,.12)' }} formatter={(v) => [fm.money(Number(v)), t('chains.sales.sales')]} />
                <Bar dataKey="sales" fill="var(--money)" radius={[4, 4, 0, 0]} isAnimationActive={false} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </figure>
      )}
      <div className="chn-month">
        <MonthSelect id="chn-sales-month" label={t('chains.sales.month')} value={shown} options={options} onChange={setMonth} />
        {shown && <p className="sa-muted">{t('chains.sales.vs', { month: monthLong(sales.data?.compareMonth ?? '', lang) })}</p>}
      </div>
      {sales.isError ? (
        <QueryError error={sales.error} />
      ) : sales.isPending ? (
        <Spinner block />
      ) : (
        <DataTable
          label={t('chains.sales.clientsInMonth')}
          columns={columns}
          rows={row?.clients ?? []}
          rowKey={(c) => c.clientPublicId ?? c.clientName ?? ''}
          dense
          empty={t('chains.sales.empty')}
          defaultSort={{ id: 'sales', desc: true }}
        />
      )}
    </Panel>
  )
}
