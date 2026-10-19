// Lote F-D1 — Catálogo → Choferes y tarifas (maqueta `choferes()`): la fórmula de pago arriba (selector + vista previa), a la izquierda
// el panel «Choferes» (contador, buscador, filtros de estatus y zona, «Mostrar inactivos», filas con código, nombre, chip y «zona · área»)
// y a la derecha la ficha del elegido. El API no pagina (devuelve todos los choferes, ordenados por código), por eso no hay `DataTable`.
// Selección en la URL (`?driver=<publicId>`); sin ella, el primero. Manual: docs/manual/frontend/fd1-choferes.md.
import { useEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { CARDS_QUERY, Chip, EmptyState, FilterScope, IconDoc, IconPlus, IconUsers, Panel, QBox, SelectFilter, Spinner, useMediaQuery } from '../../kernel/ui'
import { CustomFieldFilterBar, EMPTY_CUSTOM_FIELD_FILTERS, withCustomFields, type CustomFieldFilterValues } from '../../kernel/custom-fields'
import { DRIVER_ENTITY_TYPE, useDispatchZones, useDriverList, type DriverListItem, type DriverListQuery } from './api'
import { DriverCreateModal } from './DriverCreateModal'
import { DriverDetailPanel } from './DriverDetailPanel'
import { DriverStatuses, driverState, FILTER_ANY, zoneLine } from './driverRules'
import { PayPolicyPanel } from './PayPolicyPanel'
import { zoneLabel } from './zoneOptions'
import './drivers.css'

const NO_ROWS: DriverListItem[] = []

/** Valor que se devuelve tras `ms` sin cambios (el buscador no pide al servidor en cada tecla). */
function useDebounced<T>(value: T, ms: number): T {
  const [v, setV] = useState(value)
  useEffect(() => {
    const id = setTimeout(() => setV(value), ms)
    return () => clearTimeout(id)
  }, [value, ms])
  return v
}

export default function DriverListScreen() {
  const t = useT()
  const [params, setParams] = useSearchParams()
  const [q, setQ] = useState('')
  const [status, setStatus] = useState(FILTER_ANY)
  const [zone, setZone] = useState(FILTER_ANY)
  const [includeInactive, setIncludeInactive] = useState(false)
  const [custom, setCustom] = useState<CustomFieldFilterValues>(EMPTY_CUSTOM_FIELD_FILTERS)   // Lote 130: campos personalizados con «Mostrar en la búsqueda»
  const [creating, setCreating] = useState(false)
  const cards = useMediaQuery(CARDS_QUERY)
  const detailRef = useRef<HTMLDivElement>(null)
  const search = useDebounced(q.trim(), 250)

  const query = useMemo<DriverListQuery>(
    () =>
      withCustomFields<DriverListQuery>(
        {
          includeInactive: includeInactive || status === DriverStatuses.Inactive,
          ...(search ? { search } : {}),
          ...(status ? { status: [status] } : {}),
          ...(zone ? { dispatchZoneId: Number(zone) } : {}),
        },
        custom,
      ),
    [includeInactive, search, status, zone, custom],
  )
  const list = useDriverList(query)
  const zones = useDispatchZones()
  const rows = list.data ?? NO_ROWS
  const filtered = Boolean(search || status || zone)

  const selectedParam = params.get('driver')
  // sin `?driver=` se abre el primero y se queda ahí: si ese chofer sale de la lista (se desactiva, se elimina) la ficha no salta a otro
  const [firstId, setFirstId] = useState<string | null>(null)
  const selected = selectedParam ?? firstId ?? rows[0]?.publicId ?? null
  useEffect(() => {
    if (!selectedParam && !firstId && rows[0]?.publicId) setFirstId(rows[0].publicId)
  }, [selectedParam, firstId, rows])

  const choose = (publicId: string | null, scroll = false) => {
    const next = new URLSearchParams(params)
    if (publicId) next.set('driver', publicId)
    else next.delete('driver')
    setParams(next, { replace: true })
    // bajo 720 px la ficha queda debajo de la lista: se lleva a la vista
    if (scroll && publicId && cards) detailRef.current?.scrollIntoView?.({ behavior: 'smooth', block: 'start' })
  }

  const zoneOptions = useMemo(() => (zones.data ?? []).map((z) => ({ value: String(z.id), label: zoneLabel(z) })), [zones.data])
  const statusOptions = useMemo(
    () => [
      { value: DriverStatuses.Active, label: t('drivers.available') },
      { value: DriverStatuses.Unavailable, label: t('drivers.unavailable') },
      { value: DriverStatuses.Inactive, label: t('drivers.removed') },
    ],
    [t],
  )

  const chip = (d: DriverListItem) => {
    switch (driverState(d)) {
      case 'removed':
        return <Chip tone="fail">{t('drivers.removed')}</Chip>
      case 'inactive':
        return <Chip tone="cap">{t('drivers.inactive')}</Chip>
      case 'unavailable':
        return <Chip tone="warn">{t('drivers.unavailable')}</Chip>
      default:
        return <Chip tone="deliv">{t('drivers.active')}</Chip>
    }
  }

  return (
    <div className="wrap drv">
      <div className="head">
        <div>
          <h1>{t('drivers.title')}</h1>
          <p>{t('drivers.subtitle')}</p>
        </div>
        <div className="act">
          <Can perm="fleet.manage">
            <button type="button" className="btn flow" onClick={() => setCreating(true)}>
              <IconPlus /> {t('drivers.new')}
            </button>
          </Can>
        </div>
      </div>
      <div className="note drv-note">
        <IconDoc /> {t('drivers.note')}
      </div>

      <PayPolicyPanel driverPublicId={selected} />

      <div className="drv-md" style={{ marginTop: 14 }}>
        <Panel flush icon={<IconUsers />} title={t('drivers.roster')} badge={list.isPending ? undefined : rows.length}>
          <FilterScope>
            <div className="drv-q">
              <QBox value={q} onChange={setQ} placeholder={t('ui.qbox.placeholder')} />
              <div className="drv-qf">
                <SelectFilter label={t('drivers.status')} value={status} onChange={setStatus} options={statusOptions} allLabel={t('drivers.allStatuses')} />
                <SelectFilter label={t('drivers.zone')} value={zone} onChange={setZone} options={zoneOptions} allLabel={t('drivers.allZones')} />
              </div>
              <label className="sw">
                <input type="checkbox" role="switch" checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} />
                <span className="tk" aria-hidden="true" />
                <span>{t('drivers.showInactive')}</span>
              </label>
            </div>
            <CustomFieldFilterBar entityType={DRIVER_ENTITY_TYPE} value={custom} onChange={setCustom} />
          </FilterScope>
          {list.isError ? (
            <p className="pb ferr" role="alert">
              {applyProblemDetails(list.error).title}
            </p>
          ) : list.isPending ? (
            <Spinner block />
          ) : rows.length === 0 ? (
            <EmptyState icon={<IconUsers />} title={filtered ? t('drivers.noResults') : t('drivers.emptyList')} />
          ) : (
            <ul className="drv-list" aria-label={t('drivers.roster')}>
              {rows.map((d) => (
                <li key={d.publicId}>
                  <button
                    type="button"
                    className={['unrow', 'drv-row', d.publicId === selected ? 'on' : '', d.isActive ? '' : 'dim'].filter(Boolean).join(' ')}
                    aria-current={d.publicId === selected ? 'true' : undefined}
                    onClick={() => choose(d.publicId, true)}
                  >
                    <span className="drv-main">
                      <span className="drv-top">
                        {d.code && <span className="ref">{d.code}</span>}
                        <b className="drv-name">{d.fullName}</b>
                        <span className="chips">{chip(d)}</span>
                      </span>
                      <span className="meta">{zoneLine(d)}</span>
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </Panel>
        {/* ámbito propio: lo que haya en la ficha no depende del buscador de la lista */}
        <div ref={detailRef} className="drv-detail">
          <FilterScope>
            {selected ? (
              <DriverDetailPanel key={selected} publicId={selected} />
            ) : (
              <Panel>
                <EmptyState icon={<IconUsers />} title={list.isPending ? t('common.loading') : rows.length === 0 && !filtered ? t('drivers.noneYet') : t('drivers.selectOne')} />
              </Panel>
            )}
          </FilterScope>
        </div>
      </div>

      <DriverCreateModal
        open={creating}
        onClose={() => setCreating(false)}
        onSaved={(publicId) => {
          // se limpian los filtros para que el nuevo aparezca en la lista
          setQ('')
          setStatus(FILTER_ANY)
          setZone(FILTER_ANY)
          choose(publicId, true)
        }}
      />
    </div>
  )
}
