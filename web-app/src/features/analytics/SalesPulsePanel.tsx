// Lote 241: primera cifra «Venta del mes vs cuota» (monthSales / monthQuota) que abre Análisis → Análisis de ventas.
// Lote 222 (plan §1.7) — panel «Ventas» del Pulso (SALES; pulse.sales + sales.view + módulo SALES_ORDERS, orden 13 del dominio): pedidos de hoy, urgentes abiertos, clientes por aprobar,
// pedidos esperando al cliente nuevo y, cuando existan (P5/P6), facturas por pasar a QuickBooks y consignación sin pagar (null = no se pinta). Cada cifra abre su lista (`routes` del servidor).
import { useQuery } from '@tanstack/react-query'
import type { CSSProperties } from 'react'
import { Link } from 'react-router-dom'
import { api, unwrap } from '../../kernel/api/client'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n/useT'
import { Panel } from '../../kernel/ui/Panel'
import { IconCart } from '../../kernel/ui/screenIcons'

// la pantalla vive en Análisis (el `routes.monthSales` del servidor apunta todavía a /sales-orders?tab=analysis)
const SALES_ANALYSIS_PATH = '/analytics/sales'
const GRID: CSSProperties = { display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 170px), 1fr))', gap: 12 }
const TILE: CSSProperties = { border: '1px solid var(--line)', borderRadius: 'var(--radius-sm)', padding: '12px 14px', minWidth: 0, display: 'block', color: 'inherit', textDecoration: 'none' }
const LABEL: CSSProperties = { fontSize: 12.5, color: 'var(--muted)' }
const VALUE: CSSProperties = { fontSize: 26, fontWeight: 800, lineHeight: 1.2 }

export function useSalesPulse() {
  return useQuery({ queryKey: ['sales', '/api/v1/sales-orders/pulse'], queryFn: () => unwrap(api.GET('/api/v1/sales-orders/pulse')), meta: { handleAccessDenied: false } })
}

export function SalesPulsePanel() {
  const t = useT()
  const f = useFormat()
  const pulse = useSalesPulse()
  const d = pulse.data
  const routes = d?.routes ?? {}
  const tiles: { key: string; label: string; value: number | null | undefined; sub?: string }[] = [
    { key: 'ordersToday', label: t('analytics.pulse.sales.ordersToday'), value: d?.ordersToday, sub: d ? f.money(d.ordersTodayAmount ?? 0) : undefined },
    { key: 'urgentOpen', label: t('analytics.pulse.sales.urgentOpen'), value: d?.urgentOpen },
    { key: 'clientsToApprove', label: t('analytics.pulse.sales.clientsToApprove'), value: d?.clientsToApprove },
    { key: 'awaitingApproval', label: t('analytics.pulse.sales.awaitingApproval'), value: d?.awaitingApproval },
    { key: 'invoicesToQuickBooks', label: t('analytics.pulse.sales.invoicesToQuickBooks'), value: d?.invoicesToQuickBooks },
    { key: 'consignmentUnpaid', label: t('analytics.pulse.sales.consignmentUnpaid'), value: d?.consignmentUnpaid },
  ].filter((x) => d === undefined || x.value !== null)
  return (
    <Panel icon={<IconCart />} title={t('analytics.pulse.panels.SALES')}>
      {pulse.isError ? (
        <p className="ferr" role="alert">{applyProblemDetails(pulse.error).title}</p>
      ) : (
        <div style={GRID}>
          {/* Lote 241: venta del mes (en curso, hasta hoy) contra la suma de cuotas; solo viene con sales.analysis.view|manage */}
          {d?.monthSales !== null && d?.monthSales !== undefined && (
            <Link to={SALES_ANALYSIS_PATH} role="group" aria-label={t('analytics.pulse.sales.monthVsQuota')} style={TILE}>
              <div style={LABEL}>{t('analytics.pulse.sales.monthVsQuota')}</div>
              <div style={VALUE}>{f.money(d.monthSales, { decimals: 0 })}</div>
              <div style={LABEL}>
                {d.monthQuota
                  ? t('analytics.pulse.sales.ofQuota', { quota: f.money(d.monthQuota, { decimals: 0 }), pct: f.number((d.monthSales / d.monthQuota) * 100, { maximumFractionDigits: 0 }) })
                  : t('analytics.pulse.sales.noQuota')}
              </div>
              {!!d.monthQuota && (
                <div aria-hidden="true" style={{ height: 6, borderRadius: 4, background: 'var(--neutral-bg)', marginTop: 6, overflow: 'hidden' }}>
                  <div style={{ height: '100%', width: `${Math.min(100, (d.monthSales / d.monthQuota) * 100)}%`, background: d.monthSales >= d.monthQuota ? 'var(--ok)' : d.monthSales >= d.monthQuota * 0.9 ? 'var(--warn)' : 'var(--flow)' }} />
                </div>
              )}
            </Link>
          )}
          {tiles.map((x) => {
            const body = (
              <>
                <div style={LABEL}>{x.label}</div>
                <div style={VALUE}>{pulse.isPending ? '…' : f.number(x.value ?? 0)}</div>
                {x.sub && <div style={LABEL}>{x.sub}</div>}
              </>
            )
            const to = routes[x.key]
            return to ? (
              <Link key={x.key} to={to} role="group" aria-label={x.label} style={TILE}>{body}</Link>
            ) : (
              <div key={x.key} role="group" aria-label={x.label} style={TILE}>{body}</div>
            )
          })}
        </div>
      )}
    </Panel>
  )
}
