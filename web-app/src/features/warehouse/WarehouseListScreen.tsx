// Lote F6 — Pantalla A: lista de almacenes (GET /api/v1/warehouses?includeInactive=true). Alta con warehouse.manage.
// Lote 1 (cambios de Almacén), maqueta `almacenes()`:
// - Maestro-detalle: a la izquierda la tabla (Código, Nombre, Dirección, Zonas, Estatus); a la derecha (420 px; una
//   columna bajo 720 px) el almacén elegido: cabecera con nombre y código, dirección, estatus y botón editar (abre la ficha),
//   y "Zonas de este almacén" con "+ Nueva zona" y una fila por zona activa (código · nombre · tipo, "ocupadas/total
//   posiciones", papelera = dar de baja, deshabilitada si la zona tiene posiciones). Clic en una zona abre `ZoneModal`.
//   La selección va en la URL (`?warehouse=<publicId>`); sin ella (o si ya no existe), el primero por código.
// - Filtros encima (en el cliente: la lista es corta): Código, Nombre, Tipo (tipo de zona, `zoneTypeCodes`) y Estatus
//   como `SearchSelect`; Dirección como texto (dirección, ciudad, estado y código postal). Sin buscador dentro de la tabla.
// - Alta: Ciudad o código postal en un combobox (`PostalLocalityPickerInput`) que llena Ciudad, ZIP, Estado y País (País
//   y Estado de solo lectura); casilla "Activo" marcada e informativa (todo almacén nace activo).
// - Lote 16: columna "Recepción" (Con acomodo / Directo a posición) y "Modo de recepción" en el alta (por defecto Con
//   acomodo); la posición de recepción por defecto se elige después en la ficha (al crear aún no hay posiciones).
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { z } from 'zod'
import { Can, useCan } from '../../kernel/access'
import { StatusChip, useLookups, useStatuses } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import {
  CARDS_QUERY,
  Chip,
  ConfirmDialog,
  DataTable,
  type DataColumn,
  EmptyState,
  Field,
  Filters,
  Form,
  IconEdit,
  IconGrid,
  IconTrash,
  Modal,
  Panel,
  SearchSelect,
  Select,
  Spinner,
  TextInput,
  toast,
  useMediaQuery,
} from '../../kernel/ui'
import { IconWarehouse } from '../../kernel/ui/screenIcons'
import { useCreateWarehouse, useSaveWarehouseZone, useWarehouseZones, useWarehouses, type WarehouseDto, type WarehouseZoneDto } from './api'
import { TextFilter } from './filterControls'
import { DerivedLocalityFields, PostalLocalityPickerInput } from './PostalLocalityPicker'
import { isDirectMode, RECEIVING_MODES, receivingModeLabel } from './receivingMode'
import { useReceivingModeOptions } from './useReceivingModeOptions'
import { distinctOptions, EMPTY_WAREHOUSE_FILTERS, filterWarehouseRows, warehouseAddress, type WarehouseListFilters } from './warehouseFilters'
import { ZoneModal } from './ZoneModal'
import './warehouse.css'

/** Dominio de estatus del almacén (CatalogDomains.WarehouseStatus). */
const STATUS_DOMAIN = 'WarehouseStatus'
/** Código solo permite letras, números, guion y guion bajo (WarehouseCreateRequest.Code, máx. 30). */
const CODE_PATTERN = /^[A-Za-z0-9_-]+$/
const NO_WAREHOUSES: WarehouseDto[] = []
const NO_ZONES: WarehouseZoneDto[] = []
const byCode = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' })

function CreateWarehouseModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const t = useT()
  const create = useCreateWarehouse()
  const modeOptions = useReceivingModeOptions()
  const schema = useMemo(
    () =>
      z.object({
        code: z
          .string()
          .trim()
          .min(1, t('warehouse.list.errors.codeRequired'))
          .max(30, t('warehouse.list.errors.codePattern'))
          .regex(CODE_PATTERN, t('warehouse.list.errors.codePattern')),
        name: z.string().trim().min(1, t('warehouse.list.errors.nameRequired')),
        line1: z.string().trim(),
        city: z.string().trim(),
        postalCode: z.string().trim(),
        state: z.string().trim(),
        country: z.string().trim(),
        receivingMode: z.string(),
      }),
    [t],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    // sin localidad elegida el país es Puerto Rico (WarehouseRules.DefaultCountry); el modo, Con acomodo
    defaultValues: { code: '', name: '', line1: '', city: '', postalCode: '', state: '', country: 'PR', receivingMode: RECEIVING_MODES.putaway as string },
  })
  const formId = 'warehouse-create'

  const close = () => {
    form.reset()
    onClose()
  }

  return (
    <Modal
      open={open}
      title={t('warehouse.list.new')}
      onClose={close}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={close}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          await create.mutateAsync({
            code: v.code,
            name: v.name,
            line1: v.line1 || null,
            city: v.city || null,
            postalCode: v.postalCode || null,
            state: v.state || null,
            country: v.country || null,
            receivingMode: v.receivingMode || null,
          })
          toast.success(t('warehouse.list.created'))
          close()
        }}
      >
        <div className="r2">
          <Field name="code" label={t('warehouse.list.code')} required>
            <TextInput maxLength={30} />
          </Field>
          <Field name="name" label={t('warehouse.list.name')} required>
            <TextInput />
          </Field>
        </div>
        <Field name="line1" label={t('warehouse.list.line1')}>
          <TextInput />
        </Field>
        <Field name="city" label={t('warehouse.postalPicker.label')} help={t('warehouse.postalPicker.help')}>
          <PostalLocalityPickerInput />
        </Field>
        <DerivedLocalityFields />
        <Field name="receivingMode" label={t('warehouse.detail.receivingMode')} help={t('warehouse.detail.receivingModeHelp')}>
          <Select options={modeOptions} />
        </Field>
        <div className="f">
          <label className="sw">
            <input type="checkbox" role="switch" checked disabled readOnly aria-describedby="warehouse-create-active-help" />
            <span className="tk" aria-hidden="true" />
            <span>{t('warehouse.list.active')}</span>
          </label>
          <p id="warehouse-create-active-help" className="help">
            {t('warehouse.list.activeHelp')}
          </p>
        </div>
      </Form>
    </Modal>
  )
}

/** Panel derecho: el almacén elegido y sus zonas activas. */
function WarehouseSidePanel({ warehouse }: { warehouse: WarehouseDto }) {
  const t = useT()
  const navigate = useNavigate()
  const canManage = useCan('warehouse.manage')
  const publicId = warehouse.publicId ?? ''
  const { data: zones = NO_ZONES, isLoading } = useWarehouseZones(publicId, { includeInactive: false })
  const save = useSaveWarehouseZone()
  const [editing, setEditing] = useState<WarehouseZoneDto | null | 'new'>(null)
  const [deleting, setDeleting] = useState<WarehouseZoneDto | null>(null)
  const address = warehouseAddress(warehouse)
  const sortedZones = useMemo(() => [...zones].sort((a, b) => byCode.compare(a.code ?? '', b.code ?? '')), [zones])

  return (
    <Panel
      icon={<IconWarehouse />}
      title={warehouse.name ?? warehouse.code ?? ''}
      badge={warehouse.code ?? undefined}
      actions={
        <button
          type="button"
          className="rowbtn"
          aria-label={canManage ? t('warehouse.list.editWarehouse') : t('warehouse.list.openWarehouse')}
          title={canManage ? t('warehouse.list.editWarehouse') : t('warehouse.list.openWarehouse')}
          onClick={() => navigate(`/warehouse/warehouses/${publicId}`)}
        >
          <IconEdit />
        </button>
      }
    >
      <div className="whs-side">
        <div className="whs-addr">{address || '—'}</div>
        <StatusChip domain={STATUS_DOMAIN} code={warehouse.statusCode} label={warehouse.status} />

        <div className="whs-zones-h">
          <b>{t('warehouse.list.zonesTitle')}</b>
          <Can perm="warehouse.manage">
            <button type="button" className="btn sm flow" disabled={warehouse.isActive === false} onClick={() => setEditing('new')}>
              {t('warehouse.zones.newPlus')}
            </button>
          </Can>
        </div>
        {isLoading ? (
          <Spinner block />
        ) : sortedZones.length === 0 ? (
          <EmptyState icon={<IconGrid />} title={t('warehouse.list.noZonesYet')} />
        ) : (
          <ul className="whs-zones" aria-label={t('warehouse.list.zonesTitle')}>
            {sortedZones.map((z) => {
              const hasBins = (z.binCount ?? 0) > 0
              const main = (
                <>
                  <span className="whs-zone-top">
                    <span className="ref">{z.code}</span>
                    <b className="whs-zone-name">{z.name}</b>
                    {z.zoneType && <span className="tag">{z.zoneType}</span>}
                  </span>
                  <span className="meta">{t('warehouse.list.zoneUsage', { used: z.occupiedBinCount ?? 0, total: z.binCount ?? 0 })}</span>
                </>
              )
              return (
                <li key={z.id} className="unrow whs-zone">
                  {canManage ? (
                    <button type="button" className="whs-zone-main" aria-label={t('warehouse.list.editZone', { code: z.code ?? '' })} onClick={() => setEditing(z)}>
                      {main}
                    </button>
                  ) : (
                    <div className="whs-zone-main">{main}</div>
                  )}
                  <Can perm="warehouse.manage">
                    {/* aria-disabled (no `disabled`) para que el motivo siga visible en el tooltip y el botón se pueda enfocar */}
                    <button
                      type="button"
                      className={hasBins ? 'rowbtn danger muted' : 'rowbtn danger'}
                      aria-disabled={hasBins || undefined}
                      aria-label={hasBins ? t('warehouse.list.zoneHasBins') : t('warehouse.list.deleteZone', { code: z.code ?? '' })}
                      title={hasBins ? t('warehouse.list.zoneHasBins') : t('warehouse.zones.deactivate')}
                      onClick={() => {
                        if (!hasBins) setDeleting(z)
                      }}
                    >
                      <IconTrash />
                    </button>
                  </Can>
                </li>
              )
            })}
          </ul>
        )}
      </div>

      <ZoneModal publicId={publicId} zone={editing === 'new' || editing === null ? null : editing} open={editing !== null} onClose={() => setEditing(null)} />
      <ConfirmDialog
        open={deleting !== null}
        tone="danger"
        title={t('warehouse.zones.deactivateTitle')}
        message={t('warehouse.zones.deactivateBody', { code: deleting?.code ?? '' })}
        confirmLabel={t('warehouse.zones.deactivate')}
        onConfirm={async () => {
          if (!deleting) return
          await save.mutateAsync({ publicId, action: 'deactivate', zoneId: deleting.id ?? 0 })
          toast.success(t('warehouse.zones.deactivated'))
        }}
        onClose={() => setDeleting(null)}
      />
    </Panel>
  )
}

export default function WarehouseListScreen() {
  const t = useT()
  const [params, setParams] = useSearchParams()
  const [filters, setFilters] = useState<WarehouseListFilters>(EMPTY_WAREHOUSE_FILTERS)
  const [creating, setCreating] = useState(false)
  const cards = useMediaQuery(CARDS_QUERY)
  const sideRef = useRef<HTMLDivElement>(null)

  const { data = NO_WAREHOUSES, isLoading, error } = useWarehouses({ includeInactive: true })
  const { data: zoneTypes = [] } = useLookups('ZoneType')
  const { data: statuses = [] } = useStatuses(STATUS_DOMAIN)
  const modeOptions = useReceivingModeOptions()

  const rows = useMemo(() => data.filter((w) => w.code || w.name), [data])
  const filtered = useMemo(() => filterWarehouseRows(rows, filters), [rows, filters])

  const codeOptions = useMemo(() => distinctOptions(rows.map((w) => w.code)), [rows])
  const nameOptions = useMemo(() => distinctOptions(rows.map((w) => w.name)), [rows])
  // tipos de zona presentes en los almacenes, con la etiqueta del catálogo
  const typeOptions = useMemo(() => {
    const present = new Set(rows.flatMap((w) => w.zoneTypeCodes ?? []))
    return [...present].map((code) => ({ value: code, label: zoneTypes.find((z) => z.code === code)?.label ?? code }))
  }, [rows, zoneTypes])
  const statusOptions = useMemo(() => {
    const opts = statuses.map((s) => ({ value: s.code, label: s.label }))
    // un estatus que el catálogo ya no ofrece pero algún almacén conserva
    for (const w of rows) if (w.statusCode && !opts.some((o) => o.value === w.statusCode)) opts.push({ value: w.statusCode, label: w.status ?? w.statusCode })
    return opts
  }, [statuses, rows])

  // elegido: el de la URL si existe; si no, el primero (por código) de lo filtrado
  const selectedId = params.get('warehouse')
  const selected = useMemo(() => {
    const fromUrl = selectedId ? rows.find((w) => w.publicId === selectedId) : undefined
    if (fromUrl) return fromUrl
    return [...filtered].sort((a, b) => byCode.compare(a.code ?? '', b.code ?? ''))[0]
  }, [rows, filtered, selectedId])

  const select = (w: WarehouseDto) => {
    const next = new URLSearchParams(params)
    next.set('warehouse', w.publicId ?? '')
    setParams(next, { replace: true })
    // bajo 720 px el panel queda debajo de la lista: se lleva a la vista
    if (cards) sideRef.current?.scrollIntoView?.({ behavior: 'smooth', block: 'start' })
  }

  const setFilter = <K extends keyof WarehouseListFilters>(key: K, value: WarehouseListFilters[K]) => setFilters((f) => ({ ...f, [key]: value }))

  const columns = useMemo<DataColumn<WarehouseDto>[]>(
    () => [
      { id: 'code', header: t('warehouse.list.code'), cell: (w) => <span className="ref">{w.code}</span>, sortValue: (w) => w.code, card: 'title' },
      { id: 'name', header: t('warehouse.list.name'), cell: (w) => w.name, sortValue: (w) => w.name },
      {
        id: 'address',
        header: t('warehouse.list.address'),
        cell: (w) => <span className="whs-muted">{warehouseAddress(w) || '—'}</span>,
        sortValue: (w) => warehouseAddress(w),
        exportValue: (w) => warehouseAddress(w),
      },
      { id: 'zoneCount', header: t('warehouse.list.zones'), cell: (w) => w.zoneCount ?? 0, sortValue: (w) => w.zoneCount, align: 'end' },
      {
        // Lote 16: modo de recepción del almacén
        id: 'receiving',
        header: t('warehouse.list.receiving'),
        cell: (w) => (
          <Chip tone={isDirectMode(w.receivingModeCode) ? 'route' : 'neutral'}>
            {w.receivingMode ?? receivingModeLabel(w.receivingModeCode, modeOptions.map((o) => ({ code: o.value, label: o.label })))}
          </Chip>
        ),
        sortValue: (w) => w.receivingMode ?? w.receivingModeCode ?? undefined,
      },
      {
        id: 'status',
        header: t('warehouse.list.status'),
        cell: (w) => <StatusChip domain={STATUS_DOMAIN} code={w.statusCode} label={w.status} />,
        sortValue: (w) => w.status ?? w.statusCode,
      },
    ],
    [t, modeOptions],
  )

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('warehouse.list.title')}</h1>
          <p>{t('warehouse.list.subtitle')}</p>
        </div>
        <div className="act">
          <Can perm="warehouse.manage">
            <button type="button" className="btn flow" onClick={() => setCreating(true)}>
              {t('warehouse.list.new')}
            </button>
          </Can>
        </div>
      </div>

      <Filters onClear={() => setFilters(EMPTY_WAREHOUSE_FILTERS)}>
        <SearchSelect label={t('warehouse.list.code')} options={codeOptions} value={filters.codes} onChange={(v) => setFilter('codes', v)} />
        <SearchSelect label={t('warehouse.list.name')} options={nameOptions} value={filters.names} onChange={(v) => setFilter('names', v)} />
        <TextFilter label={t('warehouse.list.address')} value={filters.address} onChange={(v) => setFilter('address', v)} placeholder={t('warehouse.list.addressPlaceholder')} />
        <SearchSelect label={t('warehouse.list.zoneType')} options={typeOptions} value={filters.zoneTypes} onChange={(v) => setFilter('zoneTypes', v)} />
        <SearchSelect label={t('warehouse.list.status')} options={statusOptions} value={filters.statuses} onChange={(v) => setFilter('statuses', v)} />
      </Filters>

      <div className="whs-cols">
        <Panel flush icon={<IconWarehouse />} title={t('warehouse.list.title')} badge={isLoading ? undefined : filtered.length}>
          {error ? (
            <p className="pb ferr" role="alert">
              {error.message}
            </p>
          ) : (
            <DataTable
              label={t('warehouse.list.title')}
              columns={columns}
              rows={filtered}
              rowKey={(w) => w.publicId ?? String(w.id)}
              defaultSort={{ id: 'code', desc: false }}
              pageSize={25}
              loading={isLoading}
              onRowClick={select}
              rowClassName={(w) => (w.publicId === selected?.publicId ? 'sel' : w.isActive === false ? 'dim' : undefined)}
            />
          )}
        </Panel>
        <div ref={sideRef} className="whs-side-wrap">
          {selected ? (
            <WarehouseSidePanel key={selected.publicId} warehouse={selected} />
          ) : (
            <Panel>
              <EmptyState icon={<IconWarehouse />} title={isLoading ? t('common.loading') : t('warehouse.list.selectWarehouse')} />
            </Panel>
          )}
        </div>
      </div>

      <CreateWarehouseModal open={creating} onClose={() => setCreating(false)} />
    </div>
  )
}
