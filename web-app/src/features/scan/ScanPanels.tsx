// Lote F-E1 — Los dos paneles de la Estación de escaneo (maqueta `scan()`): «Escaneados en esta sesión» (lista local del navegador) y
// «Pendientes por escanear» (órdenes sin asignar del API). Cada fila abre la ventana de la orden.
import { useMemo, useState } from 'react'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, EmptyState, IconCheck, IconClock, IconScan, Panel, QBox, Spinner } from '../../kernel/ui'
import { IconAlert, IconClose } from '../../kernel/ui/icons'
import type { PeekTarget } from './ScanOrderModal'
import {
  entryMatchesFilters,
  entryMatchesQuery,
  entryRef,
  knownFromPending,
  outcomeInfo,
  pendingMatchesQuery,
  type ScanEntry,
  type ScanFilters,
  type UnassignedOrder,
} from './scanRules'

const TONE_ICON = { ok: IconCheck, dup: IconCheck, unassigned: IconAlert, ineligible: IconAlert, beyond: IconAlert, notfound: IconClose } as const

export function ScanFeedPanel({
  entries,
  filters,
  onOpen,
}: {
  entries: readonly ScanEntry[]
  filters: ScanFilters
  onOpen: (target: PeekTarget) => void
}) {
  const t = useT()
  const f = useFormat()
  const [q, setQ] = useState('')
  const rows = useMemo(
    () => entries.filter((e) => entryMatchesFilters(e, filters) && entryMatchesQuery(e, q, t(outcomeInfo(e.result.outcome).labelKey))),
    [entries, filters, q, t],
  )
  return (
    <Panel icon={<IconCheck />} title={t('scan.feed.title')} badge={rows.length}>
      <div className="qrow">
        <QBox value={q} onChange={setQ} placeholder={t('scan.feed.search')} />
      </div>
      <p className="scn-note">{t('scan.feed.note')}</p>
      {rows.length === 0 ? (
        <EmptyState icon={<IconScan />} title={t('scan.feed.empty')} />
      ) : (
        <ul className="feed scn-feed" aria-label={t('scan.feed.title')}>
          {rows.map((e) => {
            const info = outcomeInfo(e.result.outcome)
            const Icon = TONE_ICON[info.tone]
            const ref = entryRef(e)
            const meta = [e.known.clientName, e.known.consigneeName, e.known.city, e.result.zoneCode].filter(Boolean).join(' · ')
            const body = (
              <>
                <span className={`ok ${info.tone}`} aria-hidden="true">
                  <Icon />
                </span>
                <span className="scn-fi-main">
                  <span className="scn-fi-top">
                    <span className="ref">{ref}</span>
                    <Chip tone={info.chip}>{t(info.labelKey)}</Chip>
                    {e.result.tripCode && <span className="tag">{e.result.tripCode}</span>}
                  </span>
                  {meta && <span className="meta">{meta}</span>}
                  {e.result.message && <span className="meta">{e.result.message}</span>}
                </span>
                <span className="tm">{f.time(e.at)}</span>
              </>
            )
            const id = e.result.orderPublicId
            return (
              <li key={e.id}>
                {id ? (
                  <button
                    type="button"
                    className="fi scn-fi"
                    aria-label={t('scan.feed.open', { ref })}
                    onClick={() => onOpen({ orderPublicId: id, ref, tripCode: e.result.tripCode, known: e.known })}
                  >
                    {body}
                  </button>
                ) : (
                  <div className="fi scn-fi static">{body}</div>
                )}
              </li>
            )
          })}
        </ul>
      )}
    </Panel>
  )
}

export function ScanPendingPanel({
  items,
  total,
  loading,
  failed,
  filtered,
  onRetry,
  onOpen,
}: {
  items: readonly UnassignedOrder[]
  total: number
  loading: boolean
  failed: boolean
  filtered: boolean
  onRetry: () => void
  onOpen: (target: PeekTarget) => void
}) {
  const t = useT()
  const [q, setQ] = useState('')
  const rows = useMemo(() => items.filter((o) => pendingMatchesQuery(o, q)), [items, q])
  return (
    <Panel icon={<IconClock />} title={t('scan.pending.title')} badge={failed ? undefined : q ? rows.length : total}>
      <div className="qrow">
        <QBox value={q} onChange={setQ} placeholder={t('scan.pending.search')} />
      </div>
      {loading && items.length === 0 ? (
        <Spinner label={t('common.loading')} block />
      ) : failed ? (
        <EmptyState
          title={t('scan.pending.error')}
          action={
            <button type="button" className="btn sm" onClick={onRetry}>
              {t('common.retry')}
            </button>
          }
        />
      ) : rows.length === 0 ? (
        <EmptyState icon={<IconCheck />} title={filtered || q ? t('scan.pending.emptyFiltered') : t('scan.pending.empty')} />
      ) : (
        <>
          <ul className="chipsgrid scn-chips" aria-label={t('scan.pending.title')}>
            {rows.map((o) => {
              const ref = o.orderNumber ?? ''
              const town = [o.city, o.postalCode].filter(Boolean).join(' ')
              const zone = o.zoneAmbiguous ? t('scan.pending.zoneAmbiguous') : (o.zoneCode ?? t('scan.pending.noZone'))
              return (
                <li key={o.publicId}>
                  <button
                    type="button"
                    className="cg"
                    aria-label={t('scan.feed.open', { ref })}
                    disabled={!o.publicId}
                    onClick={() => o.publicId && onOpen({ orderPublicId: o.publicId, ref, known: knownFromPending(o) })}
                  >
                    {ref}
                    <small>{[o.clientName, town, zone].filter(Boolean).join(' · ')}</small>
                  </button>
                </li>
              )
            })}
          </ul>
          {total > items.length && <p className="scn-note">{t('scan.pending.more', { shown: items.length, total })}</p>}
        </>
      )}
    </Panel>
  )
}
