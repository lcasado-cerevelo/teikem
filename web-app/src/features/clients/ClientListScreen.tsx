// F-A1 — Catálogo → Clientes (maqueta `clientes()`): maestro-detalle en una sola pantalla. A la izquierda el panel
// «Clientes» (contador, buscador al API con `search`, «Mostrar inactivos») y a la derecha la ficha con paneles apilados.
// Selección en la URL (`?client=<publicId>`); sin ella (o si ya no está en la lista), el primero. Alta con clients.create.
// La lista del API no pagina (devuelve todos, ordenados por nombre), por eso es una lista de filas y no un DataTable.
import { useEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { StatusChip } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { applyProblemDetails } from '../../kernel/api/problem'
import { CARDS_QUERY, Chip, EmptyState, FilterScope, Panel, QBox, Spinner, SplitPane, useMediaQuery } from '../../kernel/ui'
import { IconUsers } from '../../kernel/ui/screenIcons'
import { useClients } from './api'
import { ClientCreateModal } from './ClientCreateModal'
import { ClientDetailPanel } from './ClientDetailPanel'
import { CLIENT_STATUS_DOMAIN, type ClientListItem } from './clientRules'
import './clients.css'

const NO_CLIENTS: ClientListItem[] = []
const SEARCH_DELAY_MS = 250

export default function ClientListScreen() {
  const t = useT()
  const [params, setParams] = useSearchParams()
  const [q, setQ] = useState('')
  const [debounced, setDebounced] = useState('')
  const [includeInactive, setIncludeInactive] = useState(false)
  const [creating, setCreating] = useState(false)
  const cards = useMediaQuery(CARDS_QUERY)
  const detailRef = useRef<HTMLDivElement>(null)

  // el API busca por código, nombre y razón social; se espera una pausa entre teclas
  useEffect(() => {
    const id = setTimeout(() => setDebounced(q.trim()), SEARCH_DELAY_MS)
    return () => clearTimeout(id)
  }, [q])

  const list = useClients(debounced, includeInactive)
  const rows = list.data ?? NO_CLIENTS

  const selectedId = params.get('client')
  const selected = useMemo(() => {
    if (selectedId) return selectedId
    return rows[0]?.publicId ?? null
  }, [selectedId, rows])

  const select = (publicId: string) => {
    const next = new URLSearchParams(params)
    next.set('client', publicId)
    setParams(next, { replace: true })
    // bajo 720 px la ficha queda debajo de la lista: se lleva a la vista
    if (cards) detailRef.current?.scrollIntoView?.({ behavior: 'smooth', block: 'start' })
  }

  return (
    <div className="wrap cl">
      <div className="head">
        <div>
          <h1>{t('clients.title')}</h1>
          <p>{t('clients.subtitle')}</p>
        </div>
        <div className="act">
          <Can perm="clients.create">
            <button type="button" className="btn flow" onClick={() => setCreating(true)}>
              {t('clients.new')}
            </button>
          </Can>
        </div>
      </div>

      <SplitPane storageKey="clients" defaultRatio={0.27} minRatio={0.2} maxRatio={0.5} minPx={[260, 480]} label={t('clients.splitLabel')} className="cl-split">
        <Panel flush icon={<IconUsers />} title={t('clients.list')} badge={list.isPending ? undefined : rows.length}>
          <FilterScope>
            <div className="qrow cl-q">
              <QBox value={q} onChange={setQ} placeholder={t('clients.searchPlaceholder')} />
              <label className="sw cl-inactive">
                <input type="checkbox" role="switch" checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} />
                <span className="tk" aria-hidden="true" />
                <span>{t('clients.showInactive')}</span>
              </label>
            </div>
          </FilterScope>
          {list.isError ? (
            <p className="pb ferr" role="alert">
              {applyProblemDetails(list.error).title}
            </p>
          ) : list.isPending ? (
            <Spinner block />
          ) : rows.length === 0 ? (
            <EmptyState icon={<IconUsers />} title={debounced ? t('clients.noResults') : t('clients.empty')} />
          ) : (
            <ul className="cl-list" aria-label={t('clients.list')}>
              {rows.map((c) => (
                <li key={c.publicId}>
                  <button
                    type="button"
                    className={['unrow', 'cl-row', c.publicId === selected ? 'on' : '', c.isActive ? '' : 'dim'].filter(Boolean).join(' ')}
                    aria-current={c.publicId === selected ? 'true' : undefined}
                    onClick={() => select(c.publicId)}
                  >
                    <span className="cl-main">
                      <span className="cl-top">
                        <b className="cl-name">{c.name}</b>
                        <span className="ref">{c.code}</span>
                      </span>
                      <span className="cl-chips">
                        <StatusChip domain={CLIENT_STATUS_DOMAIN} code={c.status} label={c.statusLabel} />
                        {!c.isActive && <Chip tone="cap">{t('clients.inactive')}</Chip>}
                      </span>
                      {c.billingSummary && <span className="meta">{c.billingSummary}</span>}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </Panel>
        {/* ámbito propio: lo que haya en la ficha no depende del buscador de la lista */}
        <div ref={detailRef} className="cl-detail">
          <FilterScope>
            {selected ? (
              <ClientDetailPanel key={selected} publicId={selected} />
            ) : (
              <Panel>
                <EmptyState icon={<IconUsers />} title={list.isPending ? t('common.loading') : t('clients.selectClient')} />
              </Panel>
            )}
          </FilterScope>
        </div>
      </SplitPane>

      <ClientCreateModal
        open={creating}
        onClose={() => setCreating(false)}
        onCreated={(publicId) => {
          // se limpia el buscador para que el cliente nuevo aparezca en la lista
          setQ('')
          setDebounced('')
          select(publicId)
        }}
      />
    </div>
  )
}
