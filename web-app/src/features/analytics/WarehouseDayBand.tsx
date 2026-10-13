// Lote 15 (P4) — franja "Almacén hoy" del Pulso (panel WAREHOUSE_DAY: pulse.warehouse + inventory.view + WMS_LOTSERIAL, lo
// decide el servidor). Estilo de la maqueta ("Paquetes en la calle"): etiqueta de sección con ícono y 4 tarjetas unidas por
// una tubería punteada, en violeta (D3). Cada tarjeta: número grande = HOY, texto pequeño = total de los 7 días y 7 barritas
// (`ChartVisual size="mini"`; tooltip con la fecha y la cantidad; la última es hoy); "Productos bajo mínimo" va al final, sin
// gráfico. Naranja (borde y número) en "Conteos con diferencia" si hoy hubo alguno y en "Productos bajo mínimo" si hay (D3).
// Clic en la tarjeta = el detalle ya filtrado con los 7 días y el almacén (D4, `warehouseDayHref`); las barritas solo
// muestran. El almacén es el MISMO del panel "Almacén" (D5, `useWarehouseFilter` compartido): cambiarlo aquí lo cambia allá.
// Título "Almacén hoy · últimos 7 días", nunca "Almacén" a secas (hallazgo 13: los recorridos buscan ese h2 del panel).
// En celular la franja se compacta (2×2, número y barritas, sin el texto pequeño) por CSS (`pulse.css`).
import { Fragment, useId, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { useLang, useT } from '../../kernel/i18n/useT'
import { IconAlert } from '../../kernel/ui/icons'
import { IconBasket, IconCheckin, IconClip, IconPencil, IconWarehouse } from '../../kernel/ui/screenIcons'
import { useWarehouseFilter, useWarehousePulseDays, WAREHOUSE_DAY_DAYS } from './api'
import { ChartVisual } from './ChartVisual'
import { formatValue, longDay } from './format'
import { StreamLabel } from './PulseSections'
import { warehouseDayCards, type WarehouseDayCard, type WarehouseDayCardKey, type WarehouseDayPair } from './warehouseDay'
import './pulse.css'

const CARD_ICON: Record<WarehouseDayCardKey, () => ReactNode> = {
  received: IconCheckin,
  outbound: IconBasket,
  countsVariance: IconClip,
  moves: IconPencil,
  belowMin: IconAlert,
}

/** Color de las barritas: violeta; la de hoy en naranja si la tarjeta está en alerta. */
const BAR_COLOR = 'var(--violet)'
const BAR_ALERT = 'var(--money)'
/** «Transferencias y ajustes»: una barra de cada color por día (transferencias violeta, ajustes azul). */
const TRANSFER_COLOR = 'var(--violet)'
const ADJUST_COLOR = 'var(--flow)'

/**
 * Barritas de dos en dos (2026-10-12): por cada día una barra de transferencias y una de ajustes, a la misma escala (la mayor de las dos series = la altura
 * completa). SVG propio con `<title>` en cada barra (fecha, tipo y cantidad); la última pareja es hoy.
 */
function PairBars({ pairs, label }: { pairs: readonly WarehouseDayPair[]; label: string }) {
  const t = useT()
  const lang = useLang()
  const max = Math.max(1, ...pairs.flatMap((p) => [p.transfers, p.adjustments]))
  const H = 28
  const BW = 6
  const GAP = 2
  const GROUP = BW * 2 + GAP + 6
  const W = pairs.length * GROUP
  const bar = (v: number) => (v <= 0 ? 1 : Math.max(2, Math.round((v / max) * H)))
  return (
    <svg className="wh-pairs" viewBox={`0 0 ${W} ${H}`} role="img" aria-label={label} preserveAspectRatio="none">
      {pairs.map((p, i) => {
        const x = i * GROUP + 3
        const day = i === pairs.length - 1 ? t('analytics.pulse.day.tooltipToday', { date: longDay(p.date, lang) }) : longDay(p.date, lang)
        return (
          <g key={p.date || i}>
            <rect x={x} y={H - bar(p.transfers)} width={BW} height={bar(p.transfers)} rx="1.5" fill={TRANSFER_COLOR} opacity={p.transfers > 0 ? 1 : 0.25}>
              <title>{`${day} · ${t('analytics.pulse.day.transfers')}: ${p.transfers}`}</title>
            </rect>
            <rect x={x + BW + GAP} y={H - bar(p.adjustments)} width={BW} height={bar(p.adjustments)} rx="1.5" fill={ADJUST_COLOR} opacity={p.adjustments > 0 ? 1 : 0.25}>
              <title>{`${day} · ${t('analytics.pulse.day.adjustments')}: ${p.adjustments}`}</title>
            </rect>
          </g>
        )
      })}
    </svg>
  )
}

function DayNode({ card, value, loading }: { card: WarehouseDayCard; value: string; loading: boolean }) {
  const t = useT()
  const lang = useLang()
  const Icon = CARD_ICON[card.key]
  const label = t(`analytics.pulse.day.${card.key}`)
  const sub =
    card.key === 'belowMin'
      ? t('analytics.pulse.day.nowSub')
      : card.split
        ? null   // «Transferencias y ajustes» lleva su propia leyenda con los dos colores (abajo)
        : card.total != null
        ? t('analytics.pulse.day.totalSub', { days: WAREHOUSE_DAY_DAYS, total: formatValue(card.total, false) })
        : null
  const last = (card.points?.length ?? 0) - 1
  const alertText = card.alert ? t(card.key === 'belowMin' ? 'analytics.pulse.day.alertBelowMin' : 'analytics.pulse.day.alertCounts') : null
  return (
    <div className={card.alert ? 'node wh alert' : 'node wh'} data-card={card.key}>
      <Link className="node-link" to={card.href}>
        <div className="ph">
          <Icon />
          <span>{label}</span>
        </div>
        <div className="big" aria-busy={loading || undefined}>
          {value}
        </div>
        {sub && <div className="sub">{sub}</div>}
        {card.split && (
          <div className="sub wh-legend">
            <span className="wh-key" style={{ ['--k' as string]: TRANSFER_COLOR }}>
              {t('analytics.pulse.day.transfers')} {formatValue(card.split.transfersTotal, false)}
            </span>
            <span className="wh-key" style={{ ['--k' as string]: ADJUST_COLOR }}>
              {t('analytics.pulse.day.adjustments')} {formatValue(card.split.adjustmentsTotal, false)}
            </span>
          </div>
        )}
        {alertText && <span className="sr-only">{alertText}</span>}
      </Link>
      {card.pairs && (
        <div className="wh-mini">
          <PairBars pairs={card.pairs} label={t('analytics.pulse.day.movesChart', { days: WAREHOUSE_DAY_DAYS })} />
        </div>
      )}
      {card.points && (
        <div className="wh-mini">
          <ChartVisual
            chartType="BAR"
            size="mini"
            isMoney={false}
            name={label}
            points={card.points.map((p, i) => ({ label: p.date, value: p.value, color: card.alert && i === last ? BAR_ALERT : BAR_COLOR }))}
            unit={t(card.key === 'countsVariance' ? 'analytics.pulse.day.unitCounts' : 'analytics.pulse.day.unitUnits')}
            tooltipLabel={(date, i) => (i === last ? t('analytics.pulse.day.tooltipToday', { date: longDay(date, lang) }) : longDay(date, lang))}
          />
        </div>
      )}
    </div>
  )
}

/** Franja "Almacén hoy" (entrada WAREHOUSE_DAY de `pulsePanels.tsx`). */
export function WarehouseDayBand() {
  const t = useT()
  const headingId = useId()
  const selectId = useId()
  const { filter, setFilter, warehouses } = useWarehouseFilter()
  const wh = filter.warehousePublicId
  const days = useWarehousePulseDays(wh)
  const cards = warehouseDayCards(days.data, wh)
  const list = warehouses.data ?? []
  // Mientras llegan los almacenes, el guardado se conserva como opción para que el <select> no lo pierda.
  const keepSaved = Boolean(wh) && !list.some((w) => w.publicId === wh)

  const valueOf = (card: WarehouseDayCard) => (days.isLoading ? '…' : days.isError ? '—' : formatValue(card.today, false))

  return (
    <section className="wh-band" aria-labelledby={headingId}>
      <div className="wh-band-h">
        <StreamLabel icon={<IconWarehouse />} id={headingId} tone="wh">
          {/* Solo "Almacén hoy" (pedido de Luis, 2026-09-30): cada tarjeta ya muestra sus 7 días. */}
          {t('analytics.pulse.panels.WAREHOUSE_DAY')}
        </StreamLabel>
        <label className="wh-band-sel" htmlFor={selectId}>
          <span className="sr-only">{t('analytics.pulse.day.warehouse')}</span>
          <select id={selectId} value={wh ?? ''} onChange={(e) => setFilter({ ...filter, warehousePublicId: e.target.value || null })}>
            <option value="">{t('analytics.pulse.day.allWarehouses')}</option>
            {keepSaved && wh && <option value={wh}>{t('common.loading')}</option>}
            {list.map((w) => (
              <option key={w.publicId} value={w.publicId ?? ''} title={[w.code, w.name].filter(Boolean).join(' · ')}>
                {w.code || w.name}
              </option>
            ))}
          </select>
        </label>
      </div>
      <div className="river wh-river" role="group" aria-label={t('analytics.pulse.day.cards')}>
        {cards.map((card, i) => (
          <Fragment key={card.key}>
            {i > 0 && <div className="pipe wh" aria-hidden="true" />}
            <DayNode card={card} value={valueOf(card)} loading={days.isLoading} />
          </Fragment>
        ))}
      </div>
    </section>
  )
}
