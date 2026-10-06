// Lote F18 (Rentas F-R2) — devoluciones de renta. `/warehouse/rental-returns` (Almacén → Devoluciones de renta; `rental.view` +
// RENTAL_EQUIPMENT por la ruta; manual 11 §5). Filtros al API: Motivo (varios), Cliente, Renta (`?rentalPublicId=` desde la
// ficha de la renta, se quita con ✕), Fecha de devolución (rango), Anticipada (sí/no) y el buscador libre (número DRN, número de
// renta o serie). Tabla paginada en el servidor con orden por columna en la página, enlaces cruzados devolución ↔ renta y
// Exportar con todo lo filtrado. Un clic en la fila abre la ficha de la devolución.
import { useMemo, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { useLookups } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format/useFormat'
import { useT } from '../../kernel/i18n'
import {
  Chip,
  ClientPicker,
  DataTable,
  DateRangeFilter,
  EmptyState,
  Filters,
  Panel,
  QBox,
  SearchSelect,
  SelectFilter,
  type DataColumn,
} from '../../kernel/ui'
import { IconDoc } from '../../kernel/ui/screenIcons'
import { useDebounced } from '../warehouse/lineRules'
import { exportRentalReturns, useRental, useRentalReturns } from './api'
import { RentalTabs } from './RentalTabs'
import {
  EMPTY_RETURN_FILTERS,
  RETURN_REASON_DOMAIN,
  returnFiltersFromUrl,
  returnListQuery,
  type EarlyFilter,
  type RentalReturnListItemDto,
  type ReturnFilterState,
} from './returnRules'
import './rentals.css'

const PAGE_SIZE = 25
const NO_ROWS: RentalReturnListItemDto[] = []

export default function RentalReturnListScreen() {
  const t = useT()
  const f = useFormat()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const [filters, setFilters] = useState<ReturnFilterState>(() => returnFiltersFromUrl(params))
  const [q, setQ] = useState(filters.search)
  const search = useDebounced(q, 300)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(PAGE_SIZE)

  const { data: reasons = [] } = useLookups(RETURN_REASON_DOMAIN, { includeDisabled: true })
  const reasonOptions = useMemo(() => reasons.map((r) => ({ value: r.code, label: r.label })), [reasons])
  const pinnedRental = useRental(filters.rentalPublicId, { handleAccessDenied: false })

  const change = (patch: Partial<ReturnFilterState>) => {
    setPage(1)
    setFilters((cur) => ({ ...cur, ...patch }))
  }

  const baseQuery = useMemo(() => returnListQuery(filters, search), [filters, search])
  const query = useMemo(() => ({ ...baseQuery, skip: (page - 1) * pageSize, take: pageSize }), [baseQuery, page, pageSize])
  const { data, isLoading, error } = useRentalReturns(query)

  const columns = useMemo<DataColumn<RentalReturnListItemDto>[]>(
    () => [
      { id: 'number', header: t('rentalReturns.columns.number'), cell: (x) => <span className="ref">{x.number}</span>, sortValue: (x) => x.number, card: 'title' },
      { id: 'returnedOn', header: t('rentalReturns.columns.returnedOn'), cell: (x) => f.date(x.returnedOn), sortValue: (x) => x.returnedOn, exportValue: (x) => f.date(x.returnedOn) },
      {
        id: 'rental',
        header: t('rentalReturns.columns.rental'),
        cell: (x) => (
          <Link className="ref" to={`/warehouse/rentals/${x.rentalPublicId}`} onClick={(e) => e.stopPropagation()}>
            {x.rentalNumber}
          </Link>
        ),
        sortValue: (x) => x.rentalNumber,
        exportValue: (x) => x.rentalNumber ?? '',
      },
      { id: 'client', header: t('rentalReturns.columns.client'), cell: (x) => x.clientName, sortValue: (x) => x.clientName },
      {
        id: 'reason',
        header: t('rentalReturns.columns.reason'),
        cell: (x) => x.reason ?? x.reasonCode,
        sortValue: (x) => x.reason ?? x.reasonCode,
      },
      {
        id: 'early',
        header: t('rentalReturns.columns.early'),
        cell: (x) => (x.isEarly ? <Chip tone="warn">{t('rentalReturns.earlyChip')}</Chip> : t('rentalReturns.onTime')),
        sortValue: (x) => (x.isEarly ? 1 : 0),
        exportValue: (x) => (x.isEarly ? t('rentalReturns.yes') : t('rentalReturns.no')),
      },
      { id: 'units', header: t('rentalReturns.columns.units'), cell: (x) => f.number(x.units ?? 0), sortValue: (x) => x.units ?? 0, align: 'end' },
      {
        id: 'openProcesses',
        header: t('rentalReturns.columns.openProcesses'),
        cell: (x) => f.number(x.openProcesses ?? 0),
        sortValue: (x) => x.openProcesses ?? 0,
        align: 'end',
      },
      { id: 'createdAt', header: t('rentalReturns.columns.createdAt'), cell: (x) => f.dateTime(x.createdAtUtc), sortValue: (x) => x.createdAtUtc, card: 'hidden' },
    ],
    [t, f],
  )

  const earlyOptions = useMemo(
    () => [
      { value: 'true', label: t('rentalReturns.filters.earlyYes') },
      { value: 'false', label: t('rentalReturns.filters.earlyNo') },
    ],
    [t],
  )

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('rentalReturns.title')}</h1>
          <p>{t('rentalReturns.subtitle')}</p>
        </div>
      </div>

      <RentalTabs current="returns" />

      <Filters
        onClear={() => {
          setPage(1)
          setFilters(EMPTY_RETURN_FILTERS)
          setQ('')
        }}
      >
        <SearchSelect label={t('rentalReturns.filters.reason')} options={reasonOptions} value={filters.reasons} onChange={(reasons) => change({ reasons })} />
        <div className="f">
          <label htmlFor="returns-client">{t('rentalReturns.filters.client')}</label>
          <ClientPicker
            id="returns-client"
            value={filters.clientPublicId}
            onChange={(clientPublicId) => change({ clientPublicId })}
            includeInactive
            filterLabel={t('rentalReturns.filters.client')}
            placeholder={t('rentals.filters.allClients')}
          />
        </div>
        <DateRangeFilter label={t('rentalReturns.filters.returnedOn')} value={filters.range} onChange={(range) => change({ range })} />
        <SelectFilter label={t('rentalReturns.filters.early')} value={filters.early} options={earlyOptions} onChange={(v) => change({ early: v as EarlyFilter })} />
      </Filters>
      {filters.rentalPublicId && (
        <p className="filters-note ren-links">
          <span>{t('rentalReturns.filters.onlyRental', { number: pinnedRental.data?.rental?.number ?? '…' })}</span>
          <button type="button" className="btn sm" onClick={() => change({ rentalPublicId: null })}>
            {t('rentalReturns.filters.removeRental')}
          </button>
        </p>
      )}

      <Panel flush icon={<IconDoc />} title={t('rentalReturns.title')} badge={data ? (data.total ?? 0) : undefined}>
        <div className="qrow">
          <QBox
            value={q}
            onChange={(v) => {
              setQ(v)
              setPage(1)
            }}
            placeholder={t('rentalReturns.filters.searchPlaceholder')}
          />
        </div>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('rentalReturns.title')}
            columns={columns}
            rows={data?.items ?? NO_ROWS}
            rowKey={(x) => x.publicId ?? String(x.id)}
            loading={isLoading}
            page={page}
            pageSize={pageSize}
            total={data?.total ?? 0}
            onPage={setPage}
            onPageSize={(size) => {
              setPageSize(size)
              setPage(1)
            }}
            exportRows={() => exportRentalReturns(baseQuery)}
            onRowClick={(x) => navigate(`/warehouse/rental-returns/${x.publicId}`)}
            empty={<EmptyState title={t('rentalReturns.empty')} body={t('rentalReturns.emptyBody')} />}
          />
        )}
      </Panel>
    </div>
  )
}
