// Lote F17 (Rentas F-R1) — lista de rentas. `/warehouse/rentals` (Almacén → Rentas; `rental.view` + RENTAL_EQUIPMENT por la
// ruta). Filtros al API: Estatus, Cliente, "Vencen en N días" (`dueWithinDays`), "Solo vencidas" (`overdue`) y el buscador
// libre (`search`: número de renta o de contrato, cliente, localidad o serie). Los filtros iniciales salen de la URL una vez
// (así llega "Ver todos" de "Necesita tu atención": `?dueWithinDays=7&overdue=true`); `?rental=<publicId>` ("Revisar" del
// aviso RENTAL_DUE) abre la ficha. Tabla paginada en el servidor con orden por columna en la página, insignia de estatus con
// su color y texto, "Vencida"/"Vence en N días" como dato calculado (no es estatus) y Exportar con todo lo filtrado.
// "Nueva renta" con `rental.manage`.
import { useMemo, useRef, useState } from 'react'
import { Navigate, useNavigate, useSearchParams } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { StatusChip, useStatuses } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format/useFormat'
import { useT } from '../../kernel/i18n'
import { ClientPicker, DataTable, EmptyState, Filters, Panel, QBox, SearchSelect, useRegisterFilter, type DataColumn } from '../../kernel/ui'
import { IconWarehouse } from '../../kernel/ui/screenIcons'
import { ToggleFilter } from '../warehouse/filterControls'
import { useDebounced } from '../warehouse/lineRules'
import { exportRentals, useRentals } from './api'
import { DueChip } from './DueChip'
import { RentalFormModal } from './RentalFormModal'
import {
  dueLabel,
  dueState,
  EMPTY_RENTAL_FILTERS,
  parseDueDays,
  RENTAL_STATUS_DOMAIN,
  rentalFiltersFromUrl,
  rentalListQuery,
  type RentalFilterState,
  type RentalListItemDto,
} from './rentalRules'
import './rentals.css'

const PAGE_SIZE = 25
const NO_ROWS: RentalListItemDto[] = []

/** "Vencen en N días": número con etiqueta; se anota en la línea de filtros de las exportaciones. */
function DueDaysFilter({ label, value, onChange, hint }: { label: string; value: string; onChange: (v: string) => void; hint: string }) {
  const ref = useRef<HTMLDivElement>(null)
  const days = parseDueDays(value)
  useRegisterFilter(label, days === null ? null : hint, ref)
  return (
    <div className="f" ref={ref}>
      <label htmlFor="rentals-due-days">{label}</label>
      <input id="rentals-due-days" type="number" inputMode="numeric" min={0} max={3650} step={1} value={value} onChange={(e) => onChange(e.target.value)} />
    </div>
  )
}

export default function RentalListScreen() {
  const [params] = useSearchParams()
  const pinned = params.get('rental')
  // "Revisar" del aviso RENTAL_DUE: la ficha de esa renta
  if (pinned) return <Navigate to={`/warehouse/rentals/${encodeURIComponent(pinned)}`} replace />
  return <RentalList initial={params} />
}

function RentalList({ initial }: { initial: URLSearchParams }) {
  const t = useT()
  const f = useFormat()
  const navigate = useNavigate()
  const [filters, setFilters] = useState<RentalFilterState>(() => rentalFiltersFromUrl(initial))
  const [q, setQ] = useState(filters.search)
  const search = useDebounced(q, 300)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(PAGE_SIZE)
  const [creating, setCreating] = useState(false)

  const { data: statuses = [] } = useStatuses(RENTAL_STATUS_DOMAIN)
  const statusOptions = useMemo(() => statuses.map((s) => ({ value: s.code, label: s.label })), [statuses])

  const change = (patch: Partial<RentalFilterState>) => {
    setPage(1)
    setFilters((cur) => ({ ...cur, ...patch }))
  }

  const baseQuery = useMemo(() => rentalListQuery(filters, search), [filters, search])
  const query = useMemo(() => ({ ...baseQuery, skip: (page - 1) * pageSize, take: pageSize }), [baseQuery, page, pageSize])
  const { data, isLoading, error } = useRentals(query)
  const dueDays = parseDueDays(filters.dueWithinDays)

  const columns = useMemo<DataColumn<RentalListItemDto>[]>(
    () => [
      { id: 'number', header: t('rentals.columns.number'), cell: (r) => <span className="ref">{r.number}</span>, sortValue: (r) => r.number, card: 'title' },
      { id: 'client', header: t('rentals.columns.client'), cell: (r) => r.clientName, sortValue: (r) => r.clientName },
      {
        id: 'location',
        header: t('rentals.columns.location'),
        cell: (r) => [r.locationName, r.locationCity].filter(Boolean).join(' · '),
        sortValue: (r) => r.locationName,
      },
      { id: 'warehouse', header: t('rentals.columns.warehouse'), cell: (r) => r.warehouseCode, sortValue: (r) => r.warehouseCode },
      { id: 'startDate', header: t('rentals.columns.startDate'), cell: (r) => f.date(r.startDate), sortValue: (r) => r.startDate, exportValue: (r) => f.date(r.startDate) },
      { id: 'pickupDate', header: t('rentals.columns.pickupDate'), cell: (r) => f.date(r.pickupDate), sortValue: (r) => r.pickupDate, exportValue: (r) => f.date(r.pickupDate) },
      {
        id: 'due',
        header: t('rentals.columns.due'),
        cell: (r) => <DueChip rental={r} />,
        sortValue: (r) => (dueState(r) ? (r.daysToPickup ?? 0) : null),
        exportValue: (r) => {
          const state = dueState(r)
          return state ? dueLabel(t, state, r.daysToPickup) : ''
        },
      },
      {
        id: 'status',
        header: t('rentals.columns.status'),
        cell: (r) => <StatusChip domain={RENTAL_STATUS_DOMAIN} code={r.statusCode} label={r.status} />,
        sortValue: (r) => r.status ?? r.statusCode,
        exportValue: (r) => r.status ?? r.statusCode ?? '',
      },
      { id: 'units', header: t('rentals.columns.units'), cell: (r) => f.number(r.units ?? 0), sortValue: (r) => r.units ?? 0, align: 'end' },
      { id: 'contract', header: t('rentals.columns.contract'), cell: (r) => r.contractNumber ?? '', sortValue: (r) => r.contractNumber, card: 'hidden' },
    ],
    [t, f],
  )

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('rentals.title')}</h1>
          <p>{t('rentals.subtitle')}</p>
        </div>
        <div className="act">
          <Can perm="rental.manage">
            <button type="button" className="btn flow" onClick={() => setCreating(true)}>
              {t('rentals.new')}
            </button>
          </Can>
        </div>
      </div>

      <Filters
        onClear={() => {
          setPage(1)
          setFilters(EMPTY_RENTAL_FILTERS)
          setQ('')
        }}
      >
        <SearchSelect label={t('rentals.filters.status')} options={statusOptions} value={filters.status} onChange={(status) => change({ status })} />
        <div className="f">
          <label htmlFor="rentals-client">{t('rentals.filters.client')}</label>
          <ClientPicker
            id="rentals-client"
            value={filters.clientPublicId}
            onChange={(clientPublicId) => change({ clientPublicId })}
            includeInactive
            filterLabel={t('rentals.filters.client')}
            placeholder={t('rentals.filters.allClients')}
          />
        </div>
        <DueDaysFilter
          label={t('rentals.filters.dueWithinDays')}
          hint={t('rentals.filters.dueWithinDaysValue', { days: dueDays ?? 0 })}
          value={filters.dueWithinDays}
          onChange={(dueWithinDays) => change({ dueWithinDays })}
        />
        <ToggleFilter label={t('rentals.filters.overdue')} checked={filters.overdue} onChange={(overdue) => change({ overdue })} />
      </Filters>
      {dueDays !== null && filters.overdue && <p className="filters-note">{t('rentals.filters.bothNote', { days: dueDays })}</p>}

      <Panel flush icon={<IconWarehouse />} title={t('rentals.title')} badge={data ? (data.total ?? 0) : undefined}>
        <div className="qrow">
          <QBox
            value={q}
            onChange={(v) => {
              setQ(v)
              setPage(1)
            }}
            placeholder={t('rentals.filters.searchPlaceholder')}
          />
        </div>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('rentals.title')}
            columns={columns}
            rows={data?.items ?? NO_ROWS}
            rowKey={(r) => r.publicId ?? String(r.id)}
            loading={isLoading}
            page={page}
            pageSize={pageSize}
            total={data?.total ?? 0}
            onPage={setPage}
            onPageSize={(size) => {
              setPageSize(size)
              setPage(1)
            }}
            exportRows={() => exportRentals(baseQuery)}
            onRowClick={(r) => navigate(`/warehouse/rentals/${r.publicId}`)}
            empty={<EmptyState title={t('rentals.empty')} body={t('rentals.emptyBody')} />}
          />
        )}
      </Panel>

      <RentalFormModal open={creating} rental={null} onClose={() => setCreating(false)} />
    </div>
  )
}
