// Lote F18 (Rentas F-R2) — cola de proceso de los equipos devueltos. `/warehouse/rental-processes` (Almacén → Proceso de equipos;
// `rental.view` + RENTAL_EQUIPMENT por la ruta; manual 11 §6). Filtros al API: Estatus (los de la compañía, con sus etiquetas),
// Abiertos / Terminados / Todos (abiertos por defecto), Almacén y el buscador libre (serie, SKU, número de devolución o de renta;
// `?search=` llega desde la ficha de la devolución). Columnas ordenables: serie, producto, almacén · posición, estatus (color del
// catálogo), días en proceso (día de la compañía), condición con que volvió, devolución y renta de origen (enlaces), inicio y fin.
// Acciones por equipo (proceso abierto, `rental.maintenance`): Avanzar, Completar y Dar de baja (además `inventory.adjust`: sin
// él la acción no se pinta y una nota lo explica); Historial siempre.
import { useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useCan } from '../../kernel/access'
import { tenantToday, localDayOf } from '../../kernel/api/tenantZone'
import { StatusChip, useLookups, useStatuses } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format/useFormat'
import { useT } from '../../kernel/i18n'
import { DataTable, EmptyState, Filters, Panel, QBox, SearchSelect, SelectFilter, type DataColumn, type RowAction } from '../../kernel/ui'
import { IconCheckCircle, IconPlay, IconXCircle } from '../../kernel/ui/actionIcons'
import { IconClock } from '../../kernel/ui/screenIcons'
import { useDebounced } from '../warehouse/lineRules'
import { WarehousePicker } from '../warehouse/pickers'
import { exportRentalProcesses, useRentalProcesses } from './api'
import { AdvanceProcessModal, CompleteProcessModal, ProcessHistoryModal, ScrapProcessModal, type ProcessDialogKind } from './ProcessDialogs'
import { RentalTabs } from './RentalTabs'
import {
  DEFAULT_PROCESS_FILTERS,
  daysInProcess,
  PROCESS_STATUS_DOMAIN,
  processFiltersFromUrl,
  processListQuery,
  RETURN_CONDITION_DOMAIN,
  type OpenFilter,
  type ProcessFilterState,
  type RentalProcessDto,
} from './returnRules'
import './rentals.css'

const PAGE_SIZE = 25
const NO_ROWS: RentalProcessDto[] = []

export default function RentalProcessListScreen() {
  const t = useT()
  const f = useFormat()
  const [params] = useSearchParams()
  const [filters, setFilters] = useState<ProcessFilterState>(() => processFiltersFromUrl(params))
  const [q, setQ] = useState(filters.search)
  const search = useDebounced(q, 300)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(PAGE_SIZE)
  const [dialog, setDialog] = useState<{ kind: ProcessDialogKind; process: RentalProcessDto } | null>(null)
  const canMaintain = useCan('rental.maintenance')
  const canAdjust = useCan('inventory.adjust')

  const { data: statuses = [] } = useStatuses(PROCESS_STATUS_DOMAIN, { includeDisabled: true })
  const statusOptions = useMemo(() => statuses.map((s) => ({ value: s.code, label: s.label })), [statuses])
  const { data: conditions = [] } = useLookups(RETURN_CONDITION_DOMAIN, { includeDisabled: true })
  const conditionLabel = useMemo(() => {
    const map = new Map(conditions.map((c) => [c.code.toUpperCase(), c.label]))
    return (code: string | null | undefined) => (code ? (map.get(code.toUpperCase()) ?? code) : '')
  }, [conditions])
  const today = tenantToday()

  const change = (patch: Partial<ProcessFilterState>) => {
    setPage(1)
    setFilters((cur) => ({ ...cur, ...patch }))
  }

  const baseQuery = useMemo(() => processListQuery(filters, search), [filters, search])
  const query = useMemo(() => ({ ...baseQuery, skip: (page - 1) * pageSize, take: pageSize }), [baseQuery, page, pageSize])
  const { data, isLoading, error } = useRentalProcesses(query)

  const columns = useMemo<DataColumn<RentalProcessDto>[]>(
    () => [
      { id: 'serial', header: t('rentals.lines.serial'), cell: (p) => <span className="mono">{p.serialNumber}</span>, sortValue: (p) => p.serialNumber, card: 'title' },
      {
        id: 'product',
        header: t('rentalProcesses.columns.product'),
        cell: (p) => (
          <>
            <span className="ref">{p.sku}</span> · {p.productName}
          </>
        ),
        sortValue: (p) => p.sku,
        exportValue: (p) => `${p.sku ?? ''} · ${p.productName ?? ''}`,
      },
      {
        id: 'where',
        header: t('rentalProcesses.columns.where'),
        cell: (p) => [p.warehouseCode, p.binCode].filter(Boolean).join(' · '),
        sortValue: (p) => `${p.warehouseCode ?? ''} ${p.binCode ?? ''}`,
      },
      {
        id: 'status',
        header: t('rentalProcesses.columns.status'),
        cell: (p) => <StatusChip domain={PROCESS_STATUS_DOMAIN} code={p.statusCode} label={p.status} />,
        sortValue: (p) => p.status ?? p.statusCode,
        exportValue: (p) => p.status ?? p.statusCode ?? '',
      },
      {
        id: 'days',
        header: t('rentalProcesses.columns.days'),
        cell: (p) => {
          const d = daysInProcess(p, today, localDayOf)
          return d === null ? '—' : f.number(d)
        },
        sortValue: (p) => daysInProcess(p, today, localDayOf),
        align: 'end',
      },
      { id: 'condition', header: t('rentalProcesses.columns.condition'), cell: (p) => conditionLabel(p.conditionCode), sortValue: (p) => conditionLabel(p.conditionCode) },
      {
        id: 'return',
        header: t('rentalProcesses.columns.return'),
        cell: (p) =>
          p.returnPublicId ? (
            <Link className="ref" to={`/warehouse/rental-returns/${p.returnPublicId}`}>
              {p.returnNumber}
            </Link>
          ) : (
            '—'
          ),
        sortValue: (p) => p.returnNumber,
        exportValue: (p) => p.returnNumber ?? '',
      },
      {
        id: 'rental',
        header: t('rentalProcesses.columns.rental'),
        cell: (p) =>
          p.rentalPublicId ? (
            <Link className="ref" to={`/warehouse/rentals/${p.rentalPublicId}`}>
              {p.rentalNumber}
            </Link>
          ) : (
            '—'
          ),
        sortValue: (p) => p.rentalNumber,
        exportValue: (p) => p.rentalNumber ?? '',
      },
      { id: 'started', header: t('rentalProcesses.columns.started'), cell: (p) => f.date(p.startedAtUtc), sortValue: (p) => p.startedAtUtc, exportValue: (p) => f.dateTime(p.startedAtUtc) },
      {
        id: 'completed',
        header: t('rentalProcesses.columns.completed'),
        cell: (p) => (p.completedAtUtc ? f.date(p.completedAtUtc) : '—'),
        sortValue: (p) => p.completedAtUtc,
        exportValue: (p) => (p.completedAtUtc ? f.dateTime(p.completedAtUtc) : ''),
        card: 'hidden',
      },
    ],
    [t, f, today, conditionLabel],
  )

  const actions = useMemo<RowAction<RentalProcessDto>[]>(() => {
    const open = (kind: ProcessDialogKind) => (process: RentalProcessDto) => setDialog({ kind, process })
    return [
      { key: 'advance', label: t('rentalProcesses.actions.advance'), icon: <IconPlay />, perm: 'rental.maintenance', visible: (p) => !p.isFinished, onClick: open('advance') },
      { key: 'complete', label: t('rentalProcesses.actions.complete'), icon: <IconCheckCircle />, tone: 'flow', perm: 'rental.maintenance', visible: (p) => !p.isFinished, onClick: open('complete') },
      {
        key: 'scrap',
        label: t('rentalProcesses.actions.scrap'),
        icon: <IconXCircle />,
        tone: 'danger',
        perm: 'rental.maintenance',
        visible: (p) => !p.isFinished && canAdjust,
        onClick: open('scrap'),
      },
      { key: 'history', label: t('rentalProcesses.actions.history'), icon: <IconClock />, onClick: open('history') },
    ]
  }, [t, canAdjust])

  const openOptions = useMemo(
    () => [
      { value: 'open', label: t('rentalProcesses.filters.open') },
      { value: 'finished', label: t('rentalProcesses.filters.finished') },
      { value: 'all', label: t('rentalProcesses.filters.all') },
    ],
    [t],
  )

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('rentalProcesses.title')}</h1>
          <p>{t('rentalProcesses.subtitle')}</p>
        </div>
      </div>

      <RentalTabs current="processes" />

      <Filters
        onClear={() => {
          setPage(1)
          setFilters(DEFAULT_PROCESS_FILTERS)
          setQ('')
        }}
      >
        <SearchSelect label={t('rentalProcesses.filters.status')} options={statusOptions} value={filters.status} onChange={(status) => change({ status })} />
        <SelectFilter label={t('rentalProcesses.filters.state')} value={filters.open} options={openOptions} allLabel={null} onChange={(v) => change({ open: v as OpenFilter })} />
        <div className="f">
          <label htmlFor="processes-warehouse">{t('rentalProcesses.filters.warehouse')}</label>
          <WarehousePicker
            id="processes-warehouse"
            value={filters.warehousePublicId}
            onChange={(warehousePublicId) => change({ warehousePublicId })}
            placeholder={t('rentalProcesses.filters.allWarehouses')}
            filterLabel={t('rentalProcesses.filters.warehouse')}
          />
        </div>
      </Filters>
      {canMaintain && !canAdjust && <p className="filters-note">{t('rentalProcesses.scrapNeedsAdjust')}</p>}

      <Panel flush icon={<IconClock />} title={t('rentalProcesses.title')} badge={data ? (data.total ?? 0) : undefined}>
        <div className="qrow">
          <QBox
            value={q}
            onChange={(v) => {
              setQ(v)
              setPage(1)
            }}
            placeholder={t('rentalProcesses.filters.searchPlaceholder')}
          />
        </div>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('rentalProcesses.title')}
            columns={columns}
            rows={data?.items ?? NO_ROWS}
            rowKey={(p) => p.id ?? 0}
            rowActions={actions}
            rowClassName={(p) => (p.isFinished ? 'dim' : undefined)}
            loading={isLoading}
            page={page}
            pageSize={pageSize}
            total={data?.total ?? 0}
            onPage={setPage}
            onPageSize={(size) => {
              setPageSize(size)
              setPage(1)
            }}
            exportRows={() => exportRentalProcesses(baseQuery)}
            empty={<EmptyState title={filters.open === 'open' ? t('rentalProcesses.emptyOpen') : t('rentalProcesses.empty')} body={t('rentalProcesses.emptyBody')} />}
          />
        )}
      </Panel>

      {dialog?.kind === 'advance' && <AdvanceProcessModal process={dialog.process} onClose={() => setDialog(null)} />}
      {dialog?.kind === 'complete' && <CompleteProcessModal process={dialog.process} onClose={() => setDialog(null)} />}
      {dialog?.kind === 'scrap' && <ScrapProcessModal process={dialog.process} onClose={() => setDialog(null)} />}
      {dialog?.kind === 'history' && <ProcessHistoryModal process={dialog.process} onClose={() => setDialog(null)} />}
    </div>
  )
}
