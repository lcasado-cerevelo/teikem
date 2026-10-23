// Lote F-E2b — Plano de ruta ESQUEMÁTICO en SVG propio (sin librerías, sin teselas, sin llamadas a servidores externos). Pinta paradas con punto
// (pin numerado según la secuencia; exacto = círculo sólido, aproximado = círculo punteado con halo, manual = rombo), la línea que las une en orden,
// un punto vivo opcional (monitoreo) y, si el servidor lo da, el origen. No conoce el despacho: recibe paradas ya traducidas (ver KIT.md «RouteMap»).
import { useId, useMemo, useRef, type KeyboardEvent, type MouseEvent } from 'react'
import { useFormat } from '../format'
import { useT } from '../i18n/useT'
import { fitProjection, isGeoPoint, niceScale, scaleParts, spreadOverlaps, type GeoPoint, type PinAccuracy, type PinState } from './routeMapRules'
import { useElementWidth } from './useElementWidth'
import './ui.css'

export interface RouteMapStop {
  id: string | number
  /** Número que lleva el pin (la secuencia mostrada). */
  seq: number
  /** Nombre accesible del pin, p. ej. «Parada 3 · Consignatario · pueblo». */
  label: string
  /** Texto corto de la parada sin ubicación (p. ej. el número de la orden); por omisión «#seq». */
  short?: string
  /** Sin punto (o con coordenadas inválidas) la parada no se dibuja y se lista aparte como «Sin ubicación». */
  point?: { lat?: number | null; lng?: number | null } | null
  accuracy?: PinAccuracy
  state?: PinState
}

export interface RouteMapProps {
  /** Paradas EN ORDEN de secuencia: la línea las une en ese orden. */
  stops: readonly RouteMapStop[]
  selectedId?: string | number | null
  /** Con `onSelect` los pines son botones (clic, Enter o espacio) y las paradas sin ubicación también. */
  onSelect?: (id: string | number) => void
  /** Punto vivo opcional (el chofer en monitoreo). */
  live?: (GeoPoint & { label: string }) | null
  /** Recorrido real del chofer (puntos de GPS en orden de tiempo): se dibuja como una línea fina bajo los pines y cuenta para el ajuste del plano. */
  track?: readonly GeoPoint[]
  /** Origen opcional (almacén/salida): solo si el servidor lo da; el plano nunca lo inventa. */
  origin?: (GeoPoint & { label: string }) | null
  /** Pin en prueba (selector de coordenadas); no mueve el ajuste del plano. */
  draft?: GeoPoint | null
  /** Con `onPick`, un clic en el plano entrega la posición bajo el cursor (aproximada: el plano no tiene calles). */
  onPick?: (p: GeoPoint) => void
  /** Alto en px del plano (300 por omisión); el ancho es el del contenedor. */
  height?: number
  /** Nombre accesible del plano. */
  label: string
  /** Texto del plano vacío (sin ninguna parada con punto); por omisión el de `routeMap.*`. */
  emptyHint?: string
  showLegend?: boolean
  className?: string
}

const COLORS: Record<PinState, string> = {
  pending: 'var(--flow-2)',
  current: 'var(--flow)',
  done: 'var(--ok)',
  failed: 'var(--danger)',
}

function pinColor(accuracy: PinAccuracy, state: PinState): string {
  if (state === 'pending' && accuracy === 'approximate') return 'var(--money-2)'
  return COLORS[state]
}

export function RouteMap({ stops, selectedId = null, onSelect, live = null, track, origin = null, draft = null, onPick, height = 300, label, emptyHint, showLegend = true, className }: RouteMapProps) {
  const t = useT()
  const f = useFormat()
  const wrapRef = useRef<HTMLDivElement>(null)
  const svgRef = useRef<SVGSVGElement>(null)
  const measured = useElementWidth(wrapRef)
  const width = measured > 0 ? measured : 600
  const gridId = useId()

  const located = useMemo(() => stops.filter((s) => isGeoPoint(s.point)), [stops])
  const unlocated = useMemo(() => stops.filter((s) => !isGeoPoint(s.point)), [stops])
  const hasLive = isGeoPoint(live)
  const hasOrigin = isGeoPoint(origin)

  const projection = useMemo(() => {
    const pts: GeoPoint[] = located.map((s) => ({ lat: s.point!.lat as number, lng: s.point!.lng as number }))
    if (hasLive && live) pts.push({ lat: live.lat, lng: live.lng })
    if (hasOrigin && origin) pts.push({ lat: origin.lat, lng: origin.lng })
    for (const p of track ?? []) if (isGeoPoint(p)) pts.push({ lat: p.lat, lng: p.lng })
    return fitProjection(pts, width, height)
  }, [located, live, origin, track, hasLive, hasOrigin, width, height])

  const placed = useMemo(() => {
    if (!projection) return new Map<string, { x: number; y: number }>()
    const raw = located.map((s) => ({ id: String(s.id), ...projection.project({ lat: s.point!.lat as number, lng: s.point!.lng as number }) }))
    return spreadOverlaps(raw)
  }, [located, projection])

  const empty = !projection
  const interactive = !!onSelect

  const pick = (e: MouseEvent<SVGRectElement>) => {
    if (!onPick || !projection || !svgRef.current) return
    const r = svgRef.current.getBoundingClientRect()
    onPick(projection.unproject(e.clientX - r.left, e.clientY - r.top))
  }

  const key = (e: KeyboardEvent<SVGGElement>, id: string | number) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault()
      onSelect?.(id)
    }
  }

  const accuracyText = (a: PinAccuracy) => t(`routeMap.accuracy.${a}`)
  const stateText = (s: PinState) => t(`routeMap.state.${s}`)

  // la línea une las paradas con punto en orden; punteada hasta que las dos puntas están atendidas
  const segments = located.slice(1).map((s, i) => {
    const a = placed.get(String(located[i].id))
    const b = placed.get(String(s.id))
    return a && b ? { key: `${located[i].id}-${s.id}`, a, b, done: located[i].state === 'done' && s.state === 'done' } : null
  })

  const trackPath = projection && track && track.length > 1 ? track.filter((p) => isGeoPoint(p)).map((p, i) => { const q = projection.project(p); return `${i === 0 ? 'M' : 'L'}${q.x.toFixed(1)} ${q.y.toFixed(1)}` }).join(' ') : null

  const scale = projection ? niceScale(projection.metersPerPx, Math.min(width / 4, 120)) : null
  const parts = scale ? scaleParts(scale.meters) : null
  const draftPos = draft && projection && isGeoPoint(draft) ? projection.project(draft) : null
  const legendStates = new Set<PinState>(located.map((s) => s.state ?? 'pending'))

  return (
    <div className={['rmap', className].filter(Boolean).join(' ')}>
      <div className="rmap-plane" ref={wrapRef} style={{ height }}>
        <svg ref={svgRef} className={onPick && projection ? 'rmap-svg picking' : 'rmap-svg'} width={width} height={height} viewBox={`0 0 ${width} ${height}`} role="group" aria-label={label}>
          <defs>
            <pattern id={gridId} width="50" height="50" patternUnits="userSpaceOnUse">
              <path d="M50 0H0V50" fill="none" stroke="var(--line-2)" strokeWidth="1" />
            </pattern>
          </defs>
          {/* fondo sólido (recibe el clic: un relleno de patrón solo recibe el puntero sobre las líneas) y encima la cuadrícula, que no lo intercepta */}
          <rect className="rmap-bg" x="0" y="0" width={width} height={height} fill="var(--bg-2)" onClick={pick} />
          <rect x="0" y="0" width={width} height={height} fill={`url(#${gridId})`} pointerEvents="none" />
          {/* norte (arriba) y escala: el plano conserva la proporción real entre latitud y longitud */}
          <g className="rmap-north" transform={`translate(${width - 18} 24)`} aria-hidden="true">
            <path d="M0 -12 L5 2 L0 -1 L-5 2 Z" fill="var(--faint)" />
            <text y="14" textAnchor="middle">N</text>
          </g>
          {scale && parts && (
            <g className="rmap-scale" transform={`translate(12 ${height - 12})`} aria-hidden="true">
              <path d={`M0 -4 V0 H${scale.px} V-4`} fill="none" stroke="var(--muted)" strokeWidth="1.5" />
              <text x={scale.px + 6} y="0">
                {f.number(parts.value)} {parts.unit}
              </text>
            </g>
          )}
          {trackPath && <path className="rmap-track" d={trackPath} fill="none" aria-hidden="true" />}
          {segments.map((s) =>
            s ? <line key={s.key} className={s.done ? 'rmap-line done' : 'rmap-line'} x1={s.a.x} y1={s.a.y} x2={s.b.x} y2={s.b.y} /> : null,
          )}
          {hasOrigin && origin && projection && (
            <g transform={`translate(${projection.project(origin).x} ${projection.project(origin).y})`} role="img" aria-label={origin.label}>
              <title>{origin.label}</title>
              <rect x="-8" y="-8" width="16" height="16" rx="3" fill="var(--text)" stroke="var(--panel)" strokeWidth="2" />
              <text className="rmap-num" fill="var(--panel)" y="4">
                S
              </text>
            </g>
          )}
          {located.map((s) => {
            const pos = placed.get(String(s.id))
            if (!pos) return null
            const accuracy = s.accuracy ?? 'exact'
            const state = s.state ?? 'pending'
            const color = pinColor(accuracy, state)
            const selected = String(s.id) === String(selectedId)
            const tip = `${s.label} · ${accuracyText(accuracy)}${state === 'pending' ? '' : ` · ${stateText(state)}`}`
            return (
              <g
                key={s.id}
                className={`rmap-pin ${accuracy}${selected ? ' on' : ''}`}
                data-state={state}
                transform={`translate(${pos.x} ${pos.y})`}
                {...(interactive
                  ? { role: 'button', tabIndex: 0, 'aria-label': s.label, 'aria-pressed': selected, onClick: (e: MouseEvent) => { e.stopPropagation(); onSelect?.(s.id) }, onKeyDown: (e: KeyboardEvent<SVGGElement>) => key(e, s.id) }
                  : { role: 'img', 'aria-label': s.label })}
              >
                <title>{tip}</title>
                <circle className="hit" r="16" fill="transparent" />
                {selected && <circle className="sel" r="18" fill="none" stroke="var(--text)" strokeWidth="2" />}
                {state === 'current' && <circle r="16" fill={color} opacity="0.2" />}
                {accuracy === 'approximate' && <circle r="16" fill={color} opacity="0.16" />}
                {accuracy === 'approximate' ? (
                  <circle r="10" fill="var(--panel)" stroke={color} strokeWidth="2" strokeDasharray="3 2" />
                ) : accuracy === 'manual' ? (
                  <rect x="-9" y="-9" width="18" height="18" rx="2" transform="rotate(45)" fill={color} stroke="var(--panel)" strokeWidth="2" />
                ) : (
                  <circle r="10" fill={color} stroke="var(--panel)" strokeWidth="2" />
                )}
                <text className="rmap-num" y="3.5" fill={accuracy === 'approximate' ? color : '#fff'} fontSize={s.seq > 99 ? 7 : s.seq > 9 ? 9 : 10}>
                  {s.seq}
                </text>
              </g>
            )
          })}
          {draftPos && (
            <g className="rmap-draft" transform={`translate(${draftPos.x} ${draftPos.y})`} aria-hidden="true">
              <circle r="9" fill="none" stroke="var(--danger)" strokeWidth="2" />
              <path d="M-14 0H-5M5 0H14M0 -14V-5M0 5V14" stroke="var(--danger)" strokeWidth="2" />
            </g>
          )}
          {hasLive && live && projection && (
            <g transform={`translate(${projection.project(live).x} ${projection.project(live).y})`} role="img" aria-label={live.label}>
              <title>{live.label}</title>
              <circle r="12" fill="var(--money-bg)" />
              <circle r="7" fill="var(--money)" stroke="var(--panel)" strokeWidth="2" />
            </g>
          )}
        </svg>
        {empty && (
          <div className="rmap-empty" role="status">
            <b>{stops.length === 0 ? t('routeMap.noStops') : t('routeMap.empty')}</b>
            {stops.length > 0 && <span>{emptyHint ?? t('routeMap.emptyHint')}</span>}
          </div>
        )}
      </div>
      {unlocated.length > 0 && (
        <div className="rmap-unloc">
          <span className="rmap-unloc-t">{t('routeMap.unlocated', { count: unlocated.length })}</span>
          {unlocated.map((s) => {
            const on = String(s.id) === String(selectedId)
            return interactive ? (
              <button key={s.id} type="button" className={`rmap-chip${on ? ' on' : ''}`} aria-label={s.label} aria-pressed={on} onClick={() => onSelect?.(s.id)}>
                {s.short ?? `#${s.seq}`}
              </button>
            ) : (
              <span key={s.id} className="rmap-chip" title={s.label}>
                {s.short ?? `#${s.seq}`}
              </span>
            )
          })}
        </div>
      )}
      {showLegend && (
        <div className="rmap-legend">
          <span className="lp">
            <i className="sw ex" />
            {t('routeMap.legend.exact')}
          </span>
          <span className="lp">
            <i className="sw ap" />
            {t('routeMap.legend.approximate')}
          </span>
          <span className="lp">
            <i className="sw ma" />
            {t('routeMap.legend.manual')}
          </span>
          {legendStates.has('done') && (
            <span className="lp">
              <i className="sw st done" />
              {t('routeMap.state.done')}
            </span>
          )}
          {legendStates.has('failed') && (
            <span className="lp">
              <i className="sw st failed" />
              {t('routeMap.state.failed')}
            </span>
          )}
          {legendStates.has('current') && (
            <span className="lp">
              <i className="sw st current" />
              {t('routeMap.state.current')}
            </span>
          )}
          {hasLive && (
            <span className="lp">
              <i className="sw live" />
              {t('routeMap.legend.live')}
            </span>
          )}
          <span className="lp hint">{t('routeMap.schematic')}</span>
        </div>
      )}
      {onPick && projection && <p className="rmap-pickhint">{t('routeMap.pickHint')}</p>}
    </div>
  )
}
