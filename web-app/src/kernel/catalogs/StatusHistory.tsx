import { parseApiDate } from '../api/dates'
import { applyProblemDetails } from '../api/problem'
import { useLang, useT } from '../i18n'
import { useStatuses, useStatusHistory } from './api'
import { statusChipStyle } from './chipStyle'
import { sameCode } from './types'
import './catalogs.css'

export interface StatusHistoryProps {
  /** Código EntityType (ej. `TRANSPORT_ORDER`). */
  entityType: string
  entityId: number
  /** Dominio de estatus para pintar el color de cada píldora (opcional). */
  domain?: string
}

/** Historial de cambios de estatus del registro (el más reciente arriba), con quién, cuándo y comentario. */
export function StatusHistory({ entityType, entityId, domain }: StatusHistoryProps) {
  const t = useT()
  const lang = useLang()
  const history = useStatusHistory(entityType, entityId)
  const statuses = useStatuses(domain, { includeDisabled: true })

  if (history.isPending) return <div className="spin" role="status" aria-label={t('common.loading')} />
  if (history.isError) return <p className="note">{applyProblemDetails(history.error).title}</p>
  if (history.data.length === 0) return <p className="note">{t('status.historyEmpty')}</p>

  const format = new Intl.DateTimeFormat(lang, { dateStyle: 'medium', timeStyle: 'short' })
  const colorOf = (code: string | null | undefined) => statuses.data?.find((s) => sameCode(s.code, code))?.color
  const rows = [...history.data].reverse()

  return (
    <ol className="sthist" aria-label={t('status.history')}>
      {rows.map((h) => (
        <li key={h.id}>
          <div className="mv">
            {h.fromCode ? (
              <>
                <span className="chip" style={statusChipStyle(colorOf(h.fromCode))}>
                  {h.fromLabel || h.fromCode}
                </span>
                <span aria-hidden="true">→</span>
              </>
            ) : null}
            <span className="chip" style={statusChipStyle(colorOf(h.toCode))}>
              {h.toLabel || h.toCode}
            </span>
          </div>
          <div className="meta">
            {h.changedAtUtc ? format.format(parseApiDate(h.changedAtUtc)) : ''}
            {' · '}
            {h.changedByName || t('status.system')}
          </div>
          {h.comment && <div className="cmt">{h.comment}</div>}
        </li>
      ))}
    </ol>
  )
}
