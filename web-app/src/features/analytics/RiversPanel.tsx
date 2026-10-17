// Lote 88 (P12) — los dos ríos del Pulso: «Paquetes en la calle» (ORDERS_RIVER, pulse.orders + orders.view + LTL_GROUND) y «Dinero COD» (COD_RIVER, pulse.cod + cod.view + COD). Estilo de la maqueta: una etiqueta
// de sección y tarjetas unidas por una tubería (azul para el flujo, naranja para el dinero). Cada tarjeta lleva su número grande, un texto pequeño y, en Entrada y Entregado, las barritas de 7 días; un clic en
// la tarjeta abre la pantalla donde se atiende. Son fotos de ahora: se vuelven a pedir cada minuto.
import { Fragment, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { useFormat } from '../../kernel/format'
import { useLang, useT } from '../../kernel/i18n/useT'
import { IconBox, IconCash } from '../../kernel/ui/screenIcons'
import { ChartVisual } from './ChartVisual'
import { longDay } from './format'
import { useRiver, type PulseRiverNodeDto } from './riverApi'
import { nodeHref, sparkPoints, type RiverKind } from './riverRules'
import { StreamLabel } from './PulseSections'
import './pulse.css'

const TONE: Record<RiverKind, 'flow' | 'money'> = { orders: 'flow', cod: 'money' }

function River({ kind, title, icon }: { kind: RiverKind; title: string; icon: ReactNode }) {
  const t = useT()
  const f = useFormat()
  const lang = useLang()
  const river = useRiver(kind)
  const tone = TONE[kind]
  const nodes = river.data?.nodes ?? []
  const valueOf = (n: PulseRiverNodeDto) => (n.kind === 'money' ? f.money(n.value ?? 0) : f.number(n.value ?? 0))

  /** El texto pequeño de cada tarjeta (con sus datos secundarios). */
  const subOf = (n: PulseRiverNodeDto): string => {
    const d = (key: string) => (n.details ?? []).find((x) => x.key === key)?.value ?? 0
    switch (`${kind}.${n.key}`) {
      case 'orders.ENTRY':
        return t('analytics.pulse.rivers.orders.sub.ENTRY', { waiting: f.number(d('waiting')) })
      case 'orders.ON_ROUTE':
        return t('analytics.pulse.rivers.orders.sub.ON_ROUTE', { drivers: f.number(d('drivers')) })
      case 'cod.TO_COLLECT':
        return t('analytics.pulse.rivers.cod.sub.TO_COLLECT', { amount: f.money(d('amount')) })
      case 'cod.COLLECTED': {
        const other = d('other')
        const base = t('analytics.pulse.rivers.cod.sub.COLLECTED', { cash: f.money(d('cash')), checks: f.money(d('checks')) })
        return other > 0 ? `${base} · ${t('analytics.pulse.rivers.cod.sub.other', { other: f.money(other) })}` : base
      }
      case 'cod.RECONCILED':
        return t('analytics.pulse.rivers.cod.sub.RECONCILED', { clients: f.number(d('clients')) })
      case 'cod.REMITTANCE':
        return t('analytics.pulse.rivers.cod.sub.REMITTANCE', { remittances: f.number(d('remittances')), clients: f.number(d('clients')) })
      default:
        return t(`analytics.pulse.rivers.${kind}.sub.${n.key}`)
    }
  }

  return (
    <section className="river-band" aria-label={title} data-river={kind}>
      <StreamLabel icon={icon} tone={tone}>
        {title}
      </StreamLabel>
      <div className={`river ${tone}-river`} role="group" aria-label={title}>
        {river.isError ? (
          <p className="help" role="alert">{t('errors.generic')}</p>
        ) : nodes.length === 0 ? (
          <p className="help" aria-busy="true">{t('common.loading')}</p>
        ) : (
          nodes.map((n, i) => {
            const spark = sparkPoints(n.spark, river.data?.day ?? '')
            const label = t(`analytics.pulse.rivers.${kind}.${n.key}`)
            return (
              <Fragment key={n.key}>
                {i > 0 && <div className={tone === 'money' ? 'pipe money' : 'pipe'} aria-hidden="true" />}
                <div className={`node ${tone}`} data-node={n.key}>
                  <Link className="node-link" to={nodeHref(kind, n.key ?? '')}>
                    <div className="ph">
                      <span>{label}</span>
                    </div>
                    <div className="big">{valueOf(n)}</div>
                    <div className="sub">{subOf(n)}</div>
                  </Link>
                  {spark && (
                    <div className="wh-mini">
                      <ChartVisual
                        chartType="BAR"
                        size="mini"
                        isMoney={false}
                        name={label}
                        points={spark.map((p) => ({ label: p.date, value: p.value, color: 'var(--flow)' }))}
                        unit={t('analytics.pulse.rivers.orders.unit')}
                        tooltipLabel={(date, k) => (k === spark.length - 1 ? t('analytics.pulse.day.tooltipToday', { date: longDay(date, lang) }) : longDay(date, lang))}
                      />
                    </div>
                  )}
                </div>
              </Fragment>
            )
          })
        )}
      </div>
    </section>
  )
}

/** Panel ORDERS_RIVER. */
export function OrdersRiver() {
  const t = useT()
  return <River kind="orders" title={t('analytics.pulse.rivers.orders.title')} icon={<IconBox />} />
}

/** Panel COD_RIVER. */
export function CodRiver() {
  const t = useT()
  return <River kind="cod" title={t('analytics.pulse.rivers.cod.title')} icon={<IconCash />} />
}

