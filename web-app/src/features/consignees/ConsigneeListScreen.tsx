// Lote F-B — Catálogo → Consignatarios (maqueta `consignatariosScreenHtml`): maestro-detalle. A la izquierda el panel
// «Consignatarios» (contador, buscador libre, filas con número, nombre, chip Activo/Inactivo y «pueblo · cliente dueño»);
// a la derecha la ficha. Arriba: filtro «Cliente», «Nuevo consignatario» e «Importar». La lista del API no pagina (devuelve
// todos los DELIVERY ordenados por nombre), por eso el buscador filtra en el cliente (como la maqueta) y no hay DataTable.
// Selección en la URL (`?consignee=<publicId>`); sin ella, el primero. El filtro «Compartidos» se resuelve por `isShared`.
import { useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { CARDS_QUERY, Chip, EmptyState, FilterScope, IconPlus, IconUpload, IconUsers, Panel, QBox, Spinner, useMediaQuery } from '../../kernel/ui'
import { useClientOptions, useConsignees } from './api'
import { ConsigneeDetailPanel } from './ConsigneeDetailPanel'
import { ConsigneeFormModal } from './ConsigneeFormModal'
import { ConsigneeImportModal } from './ConsigneeImportModal'
import { applyOwnerFilter, FILTER_ALL, FILTER_SHARED, matchesConsignee, ownerLabel, SHARED, type Consignee } from './consigneeRules'
import './consignees.css'

const NO_ROWS: Consignee[] = []

export default function ConsigneeListScreen() {
  const t = useT()
  const [params, setParams] = useSearchParams()
  const [q, setQ] = useState('')
  const [filter, setFilter] = useState(FILTER_ALL)
  const [includeInactive, setIncludeInactive] = useState(false)
  const [creating, setCreating] = useState(false)
  const [importing, setImporting] = useState(false)
  const cards = useMediaQuery(CARDS_QUERY)
  const detailRef = useRef<HTMLDivElement>(null)

  const clientFilter = filter === FILTER_ALL || filter === FILTER_SHARED ? null : filter
  const { data: clients = [] } = useClientOptions(true)
  const list = useConsignees(clientFilter, includeInactive)

  const labels = useMemo(
    () => ({ shared: t('consignees.shared'), active: t('consignees.active'), inactive: t('consignees.inactive') }),
    [t],
  )
  const pool = useMemo(() => applyOwnerFilter(list.data ?? NO_ROWS, filter), [list.data, filter])
  const shown = useMemo(() => pool.filter((c) => matchesConsignee(c, q, labels)), [pool, q, labels])

  const selectedParam = params.get('consignee')
  const selected = selectedParam ?? shown[0]?.publicId ?? null

  const select = (publicId: string | null) => {
    const next = new URLSearchParams(params)
    if (publicId) next.set('consignee', publicId)
    else next.delete('consignee')
    setParams(next, { replace: true })
    // bajo 720 px la ficha queda debajo de la lista: se lleva a la vista
    if (publicId && cards) detailRef.current?.scrollIntoView?.({ behavior: 'smooth', block: 'start' })
  }

  // el dueño con el que arrancan el alta y la importación: el cliente del filtro (o compartido)
  const defaultOwner = clientFilter ?? SHARED

  return (
    <div className="wrap cs">
      <div className="head">
        <div>
          <h1>{t('consignees.title')}</h1>
          <p>{t('consignees.subtitle')}</p>
        </div>
        <div className="act">
          <label className="cs-filter">
            {t('consignees.filterClient')}
            <select
              value={filter}
              onChange={(e) => {
                setFilter(e.target.value)
                select(null)
              }}
            >
              <option value={FILTER_ALL}>{t('consignees.filterAll')}</option>
              <option value={FILTER_SHARED}>{t('consignees.filterShared')}</option>
              {clients.map((c) => (
                <option key={c.publicId} value={c.publicId}>
                  {c.name}
                </option>
              ))}
            </select>
          </label>
          <Can perm="locations.create">
            <button type="button" className="btn flow" onClick={() => setCreating(true)}>
              <IconPlus /> {t('consignees.new')}
            </button>
            <button type="button" className="btn" onClick={() => setImporting(true)}>
              <IconUpload /> {t('consignees.importBtn')}
            </button>
          </Can>
        </div>
      </div>

      <div className="md cs-md">
        <Panel flush icon={<IconUsers />} title={t('consignees.list')} badge={list.isPending ? undefined : shown.length}>
          <FilterScope>
            <div className="qrow cs-q">
              <QBox value={q} onChange={setQ} placeholder={t('ui.qbox.placeholder')} />
              <label className="sw cs-inactive">
                <input type="checkbox" role="switch" checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} />
                <span className="tk" aria-hidden="true" />
                <span>{t('consignees.showInactive')}</span>
              </label>
            </div>
          </FilterScope>
          {list.isError ? (
            <p className="pb ferr" role="alert">
              {applyProblemDetails(list.error).title}
            </p>
          ) : list.isPending ? (
            <Spinner block />
          ) : shown.length === 0 ? (
            <EmptyState icon={<IconUsers />} title={q ? t('consignees.noResults') : t('consignees.noneYet')} />
          ) : (
            <ul className="cs-list" aria-label={t('consignees.list')}>
              {shown.map((c) => (
                <li key={c.publicId}>
                  <button
                    type="button"
                    className={['unrow', 'cs-row', c.publicId === selected ? 'on' : '', c.isActive ? '' : 'dim'].filter(Boolean).join(' ')}
                    aria-current={c.publicId === selected ? 'true' : undefined}
                    onClick={() => select(c.publicId ?? null)}
                  >
                    <span className="cs-main">
                      <span className="cs-top">
                        {c.code && <span className="ref">{c.code}</span>}
                        <b className="cs-name">{c.name}</b>
                        <Chip tone={c.isActive ? 'deliv' : 'cap'}>{c.isActive ? t('consignees.active') : t('consignees.inactive')}</Chip>
                      </span>
                      <span className="meta">
                        {c.city || '—'} · {ownerLabel(c, t('consignees.shared'))}
                      </span>
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </Panel>
        {/* ámbito propio: lo que haya en la ficha no depende del buscador de la lista */}
        <div ref={detailRef} className="cs-detail">
          <FilterScope>
            {selected ? (
              <ConsigneeDetailPanel key={selected} publicId={selected} defaultOwner={defaultOwner} />
            ) : (
              <Panel>
                <EmptyState icon={<IconUsers />} title={list.isPending ? t('common.loading') : t('consignees.selectOne')} />
              </Panel>
            )}
          </FilterScope>
        </div>
      </div>

      <ConsigneeFormModal
        key={creating ? `new-${defaultOwner}` : 'closed'}
        open={creating}
        consignee={null}
        phone={undefined}
        defaultOwner={defaultOwner}
        onClose={() => setCreating(false)}
        onSaved={(publicId) => {
          // se limpia el buscador para que el nuevo aparezca en la lista
          setQ('')
          select(publicId)
        }}
      />
      {importing && <ConsigneeImportModal defaultClient={clientFilter ?? SHARED} onClose={() => setImporting(false)} />}
    </div>
  )
}
