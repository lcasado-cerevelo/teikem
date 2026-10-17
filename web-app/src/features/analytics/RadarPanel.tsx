// Lote 89 (P12) — Radar de órdenes abiertas del Pulso (panel RADAR, orden 60; pulse.orders + orders.view + LTL_GROUND). Como la maqueta: cuántas órdenes hay abiertas, un buscador (factura, orden, empaque o
// consignatario) y, sin búsqueda, los grupos con más órdenes por pueblo o por cliente; cada grupo abre Órdenes ya filtrado. «Solo lo abierto — lo entregado no estorba aquí».
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n/useT'
import { IconSearch } from '../../kernel/ui/icons'
import { RADAR_MIN_SEARCH, useRadar, useRadarSearch, type RadarGroupBy } from './radarApi'
import { radarGroupHref, radarOpenHref } from './radarRules'
import './pulse.css'

function useDebounced(value: string, ms = 300) {
  const [v, setV] = useState(value)
  useEffect(() => {
    const id = window.setTimeout(() => setV(value), ms)
    return () => window.clearTimeout(id)
  }, [value, ms])
  return v
}

export function RadarPanel() {
  const t = useT()
  const f = useFormat()
  const [groupBy, setGroupBy] = useState<RadarGroupBy>('TOWN')
  const [text, setText] = useState('')
  const search = useDebounced(text)
  const radar = useRadar(groupBy)
  const openStatuses = radar.data?.openStatuses ?? []
  const hits = useRadarSearch(search, openStatuses)
  const searching = search.trim().length >= RADAR_MIN_SEARCH
  const groups = radar.data?.groups ?? []

  return (
    <section className="radar" aria-label={t('analytics.pulse.radar.title')} data-panel-body="RADAR">
      <div className="rh">
        <IconSearch />
        <h2>{t('analytics.pulse.radar.title')}</h2>
        <span className="gtoggle" role="group" aria-label={t('analytics.pulse.radar.by')}>
          <span className="gt-label">{t('analytics.pulse.radar.by')}</span>
          <button type="button" className={groupBy === 'TOWN' ? 'on' : ''} aria-pressed={groupBy === 'TOWN'} onClick={() => setGroupBy('TOWN')}>
            {t('analytics.pulse.radar.town')}
          </button>
          <button type="button" className={groupBy === 'CLIENT' ? 'on' : ''} aria-pressed={groupBy === 'CLIENT'} onClick={() => setGroupBy('CLIENT')}>
            {t('analytics.pulse.radar.client')}
          </button>
        </span>
        <span className="r">{radar.isError ? '—' : t('analytics.pulse.radar.open', { n: f.number(radar.data?.openCount ?? 0) })}</span>
      </div>
      <label className="search">
        <IconSearch />
        <input
          type="search"
          value={text}
          onChange={(e) => setText(e.target.value)}
          placeholder={t('analytics.pulse.radar.placeholder')}
          aria-label={t('analytics.pulse.radar.placeholder')}
          autoComplete="off"
        />
      </label>

      {searching ? (
        (hits.data?.items ?? []).length === 0 ? (
          <p className="help radar-empty">{hits.isFetching ? t('common.loading') : t('analytics.pulse.radar.noResults')}</p>
        ) : (
          <ul className="radar-hits" aria-label={t('analytics.pulse.radar.results')}>
            {(hits.data?.items ?? []).map((o) => (
              <li key={o.publicId}>
                <Link className="rrow" to={`/orders/${o.publicId}`}>
                  <span className="ref">{o.clientInvoiceNumber || o.orderNumber}</span>
                  <span className="rcons">{o.consigneeName}</span>
                  <span className="meta">{o.consigneeCity}</span>
                  <span className="chip">{o.statusLabel}</span>
                  {(o.codAmount ?? 0) > 0 && <span className="cash">{f.money(o.codAmount ?? 0)}</span>}
                </Link>
              </li>
            ))}
          </ul>
        )
      ) : radar.isError ? (
        <p className="help radar-empty" role="alert">{t('errors.generic')}</p>
      ) : groups.length === 0 ? (
        <p className="help radar-empty">{radar.isPending ? t('common.loading') : t('analytics.pulse.radar.none')}</p>
      ) : (
        <div className="gchips">
          {groups.map((g) => (
            <Link key={g.key} className="gchip" to={radarGroupHref(groupBy, g.key ?? '', radar.data?.openStatuses ?? [])}>
              {g.label} <b>{f.number(g.count ?? 0)}</b>
              {(g.codAmount ?? 0) > 0 && <span className="cash">{f.money(g.codAmount ?? 0)}</span>}
            </Link>
          ))}
        </div>
      )}

      <div className="rfoot">
        <Link className="btn sm" to={radarOpenHref(radar.data?.openStatuses ?? [])}>
          {t('analytics.pulse.radar.all')}
        </Link>
      </div>
      <p className="help radar-hint">{t('analytics.pulse.radar.hint')}</p>
    </section>
  )
}
