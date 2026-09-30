// Lote 14 (D6) — panel "Necesita tu atención" del Pulso (clave ATTENTION, permiso pulse.attention; GET /api/v1/analytics/attention).
// A la maqueta ("Necesita tu decisión", .inbox/.work): cabecera con el conteo de pendientes, una fila por aviso (los 5 más
// antiguos que devuelve el servidor) con ícono en su tono, qué pasa, dónde, cifras y desde cuándo, y "Revisar" que abre la
// ruta y los parámetros del aviso (un descuadre abre el Kárdex en Conciliación con ese descuadre). Pie: "Ver todos (N)" con la
// ruta del grupo. Nada pendiente: "Todo en orden". Sin acceso (403) el panel no se pinta.
import { useId, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useLang, useT } from '../../kernel/i18n/useT'
import { IconAlert, IconCheck } from '../../kernel/ui/icons'
import { Spinner } from '../../kernel/ui/Spinner'
import { IconWarehouse } from '../../kernel/ui/screenIcons'
import { useAttention } from './api'
import { attentionGroupLabel, attentionHref, attentionRowText, attentionToneClass, type AttentionItemDto } from './attention'
import { numberLocale } from '../../kernel/i18n'

/** Códigos de error con los que el panel simplemente no se pinta (el usuario no puede ver los avisos). */
const HIDDEN_ON = new Set(['forbidden', 'module_disabled'])

/** Ícono de la fila por módulo de negocio del aviso (el de peligro si no hay uno propio). */
function rowIcon(item: AttentionItemDto): ReactNode {
  return (item.module ?? '').toUpperCase() === 'WAREHOUSE' ? <IconWarehouse /> : <IconAlert />
}

export function AttentionPanel() {
  const t = useT()
  const lang = useLang()
  const titleId = useId()
  const query = useAttention()

  const problem = query.error ? applyProblemDetails(query.error) : null
  if (!query.data && problem && HIDDEN_ON.has(problem.code)) return null

  const total = query.data?.total ?? 0
  const items = query.data?.items ?? []
  const groups = (query.data?.groups ?? []).filter((g) => (g.total ?? 0) > 0 && g.route)

  let body: ReactNode
  if (query.isPending) body = <Spinner block label={t('common.loading')} />
  else if (!query.data)
    body = (
      <p className="inbox-msg" role="alert">
        {problem?.title ?? t('errors.generic')}
      </p>
    )
  else if (total === 0 || items.length === 0)
    body = (
      <div className="work tone-ok">
        <div className="ico" aria-hidden="true">
          <IconCheck />
        </div>
        <div className="tx">
          <b>{t('analytics.attention.allClear')}</b>
          <p>{t('analytics.attention.allClearBody')}</p>
        </div>
      </div>
    )
  else
    body = (
      <ul className="inbox-list" aria-label={t('analytics.attention.listLabel')}>
        {items.map((item, i) => {
          const text = attentionRowText(item, t, lang)
          const href = attentionHref(item.route, item.query)
          return (
            <li key={`${item.code ?? ''}-${item.params?.publicId ?? i}`} className={`work ${attentionToneClass(item.tone)}`}>
              <div className="ico" aria-hidden="true">
                {rowIcon(item)}
              </div>
              <div className="tx">
                <b>{text.title}</b>
                {text.detail && <p>{text.detail}</p>}
                {(text.figures.length > 0 || text.since) && (
                  <p className="figs">
                    {text.figures.map((f) => (
                      <span key={f.label} className={f.emphasis ? 'fig em' : 'fig'}>
                        {f.label} <strong>{f.value}</strong>
                      </span>
                    ))}
                    {text.since && <span className="fig since">{text.since}</span>}
                  </p>
                )}
              </div>
              {href && (
                <Link className="btn sm" to={href} aria-label={text.reviewLabel}>
                  {t('analytics.attention.review')}
                </Link>
              )}
            </li>
          )
        })}
      </ul>
    )

  const footer =
    query.data && total > 0 && groups.length > 0 ? (
      <div className="inbox-ft">
        {groups.map((g) => (
          <Link key={g.code ?? g.route} className="btn sm" to={attentionHref(g.route, g.query) ?? '/'}>
            {groups.length === 1
              ? t('analytics.attention.viewAll', { count: (g.total ?? 0).toLocaleString(numberLocale(lang)) })
              : t('analytics.attention.viewAllGroup', { group: attentionGroupLabel(g, t), count: (g.total ?? 0).toLocaleString(numberLocale(lang)) })}
          </Link>
        ))}
      </div>
    ) : null

  return (
    <section className="inbox" aria-labelledby={titleId}>
      <div className="ih">
        <span className={total > 0 ? 'ih-ic on' : 'ih-ic'} aria-hidden="true">
          <IconAlert />
        </span>
        <h2 id={titleId}>{t('analytics.pulse.panels.ATTENTION')}</h2>
        {query.data && total > 0 && (
          <span className="ct">
            {total === 1 ? t('analytics.attention.pendingOne') : t('analytics.attention.pending', { count: total.toLocaleString(numberLocale(lang)) })}
          </span>
        )}
      </div>
      {body}
      {footer}
    </section>
  )
}
