// Lote F-B — Catálogo → Consignatarios (maqueta `consignatariosScreenHtml`): maestro-detalle. A la izquierda el panel
// «Consignatarios» (contador, buscador libre, filas con número, nombre, chip Activo/Inactivo y «pueblo · cliente dueño»);
// a la derecha la ficha. Arriba: filtro «Cliente», «Nuevo consignatario» e «Importar». La lista del API no pagina (devuelve
// todos los DELIVERY ordenados por nombre), por eso el buscador filtra en el cliente (como la maqueta) y no hay DataTable.
// Selección en la URL (`?consignee=<publicId>`); sin ella, el primero. El filtro «Compartidos» se resuelve por `isShared`.
import { useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { CARDS_QUERY, Chip, EmptyState, FilterScope, IconPlus, IconUpload, IconUsers, Panel, QBox, Spinner, SplitPane, useMediaQuery, RemountOnSearch } from '../../kernel/ui'
import { ExportMenu } from '../../kernel/ui/ExportMenu'
import { useClientOptions, useConsignees } from './api'
import { CustomFieldFilterBar, customFieldQuery, EMPTY_CUSTOM_FIELD_FILTERS, type CustomFieldFilterValues } from '../../kernel/custom-fields'
import { exportConsignees } from './consigneeExport'
import { ConsigneeDetailPanel } from './ConsigneeDetailPanel'
import { ConsigneeFormModal } from './ConsigneeFormModal'
import { ConsigneeImportModal } from './ConsigneeImportModal'
import { applyOwnerFilter, FILTER_ALL, FILTER_SHARED, LOCATION_ENTITY_TYPE, matchesConsignee, ownerLabel, SHARED, type Consignee } from './consigneeRules'
import './consignees.css'

const NO_ROWS: Consignee[] = []

function ConsigneeListScreenBody() {
  const t = useT()
  const f = useFormat()
  const [params, setParams] = useSearchParams()
  const [q, setQ] = useState(params.get('q') ?? '')   // `?q=<texto>` (búsqueda global): la lista ya filtrada
  // `?client=<publicId>` abre la lista ya filtrada por ese cliente (lo usa «Ver todos» del panel Consignatarios de la ficha del cliente)
  const [filter, setFilter] = useState(params.get('client') ?? FILTER_ALL)
  const [includeInactive, setIncludeInactive] = useState(false)
  const [custom, setCustom] = useState<CustomFieldFilterValues>(EMPTY_CUSTOM_FIELD_FILTERS)   // Lote 130: campos personalizados con «Mostrar en la búsqueda»
  const [creating, setCreating] = useState(false)
  const [importing, setImporting] = useState(false)
  const cards = useMediaQuery(CARDS_QUERY)
  const detailRef = useRef<HTMLDivElement>(null)

  const clientFilter = filter === FILTER_ALL || filter === FILTER_SHARED ? null : filter
  const { data: clients = [] } = useClientOptions(true)
  const list = useConsignees(clientFilter, includeInactive, customFieldQuery(custom))

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
          {/* mismo orden que en Clientes: Exportar (lo que la lista tiene a la vista), Importar y Nuevo */}
          <ExportMenu placement="down" size="md" count={shown.length} onExport={(format) => exportConsignees(format, shown, t, f.lang)} />
          <Can perm="locations.create">
            <button type="button" className="btn" onClick={() => setImporting(true)}>
              <IconUpload /> {t('consignees.importBtn')}
            </button>
            <button type="button" className="btn flow" onClick={() => setCreating(true)}>
              <IconPlus /> {t('consignees.new')}
            </button>
          </Can>
        </div>
      </div>

      {/* la lista arranca más ancha que la de la maqueta (unos 10 caracteres más) y la barra cambia el ancho; recuerda su posición */}
      <SplitPane storageKey="consignees" defaultRatio={0.36} minRatio={0.22} maxRatio={0.6} minPx={[300, 460]} label={t('consignees.splitLabel')} className="cs-md">
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
            <CustomFieldFilterBar entityType={LOCATION_ENTITY_TYPE} value={custom} onChange={setCustom} />
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
                        {c.contactName ? ` · ${c.contactName}` : ''}
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
      </SplitPane>

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

// `?q=` (búsqueda global) se lee al abrir: si cambia estando abierta, la lista se vuelve a abrir con el filtro nuevo
export default function ConsigneeListScreen() {
  return (
    <RemountOnSearch>
      <ConsigneeListScreenBody />
    </RemountOnSearch>
  )
}
