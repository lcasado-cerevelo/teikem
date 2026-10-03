// Lote F6 — Pantalla A: ficha de almacén (zonas, posiciones y muelles). Lecturas con inventory.view; alta/edición/baja
// de almacén, zonas, posiciones y muelles (y estatus manual del muelle) con warehouse.manage.
// Lote 1 (cambios de Almacén):
// - Datos: Ciudad y código postal en UN combobox (`PostalLocalityPickerInput`, catálogo de localidades); Estado y País se
//   derivan de la localidad elegida y son de solo lectura.
// - Zonas: filtros Código, Nombre, Tipo (`SearchSelect`, en el cliente) + "Incluir inactivas"; clic en la fila abre
//   `ZoneModal` (código editable); baja/reactivación como ícono; columna Estatus y ocupación (ocupadas / posiciones).
// - Posiciones: paginación del servidor (`GET .../bins` devuelve `{ total, skip, take, items }`), filtros Código (`search`),
//   Zona (`zoneIds`), Pasillo, Rack, Nivel, Posición, "Incluir inactivas" y "Solo con existencia", todos al API (los de
//   texto con 300 ms de pausa); Exportar saca todo lo filtrado (`exportWarehouseBins`). Columnas Cupo y Ocupación; clic en
//   la fila abre `BinModal`; baja/reactivación como ícono. Abrir o cerrar un modal no cambia la consulta (no recarga).
// Lote 11: "Asignar cupo" (warehouse.manage) junto a "Nueva posición" abre `BinCapacityModal` (cupo máximo en bloque) con
//   la Zona y los textos de Pasillo/Rack/Nivel/Posición del filtro de la pestaña ya puestos.
// Lote 16: Datos → sección "Recepción": "Modo de recepción" (Con acomodo / Directo a posición, catálogo `ReceivingMode`) y
//   "Posición de recepción por defecto" (D12: posiciones STAGING/CROSSDOCK del almacén; vaciarla manda
//   `clearDefaultReceivingBin`). Cambiar el modo pide confirmación con cuántos recibos abiertos y con acomodo pendiente
//   tiene el almacén (D2: conservan su modo; los acomodos siguen).
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link, useParams } from 'react-router-dom'
import { z } from 'zod'
import { Can, useCan } from '../../kernel/access'
import { ApiError } from '../../kernel/api/problem'
import { StatusChip, StatusPipeline, useLookups } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import { formatQuantity } from '../../kernel/i18n/numberFormat'
import {
  Chip,
  ConfirmDialog,
  DataTable,
  type DataColumn,
  type RowAction,
  EmptyState,
  Field,
  Filters,
  Form,
  IconCheck,
  IconPower,
  IconRotateCcw,
  Modal,
  Panel,
  SearchSelect,
  Select,
  Spinner,
  Tabs,
  TextInput,
  toast,
  type ChipTone,
} from '../../kernel/ui'
import {
  exportWarehouseBins,
  useConfirmProvisionalBin,
  useDeactivateWarehouse,
  useReceipts,
  useSaveWarehouseBin,
  useSaveWarehouseDock,
  useSaveWarehouseZone,
  useUpdateWarehouse,
  useWarehouse,
  useWarehouseBins,
  useWarehouseDocks,
  useWarehouseZones,
  type WarehouseBinDto,
  type WarehouseDetailDto,
  type WarehouseDockDto,
  type WarehouseZoneDto,
} from './api'
import { IconWarehouse } from '../../kernel/ui/screenIcons'
import { BinCapacityModal } from './BinCapacityModal'
import { BinModal, ReadOnlyField } from './BinModal'
import { TextFilter, ToggleFilter } from './filterControls'
import { formatNumber, useDebounced } from './lineRules'
import { BinPickerInput } from './pickers'
import { problemText } from './problemText'
import { DerivedLocalityFields, PostalLocalityPickerInput } from './PostalLocalityPicker'
import { binsQuery, distinctOptions, EMPTY_BIN_TEXT, type BinTextFilters } from './warehouseFilters'
import {
  inSentence,
  normalizeReceivingMode,
  RECEIVING_ZONE_TYPES,
  receivingModeChanged,
  receivingModeLabel,
  warehouseReceivingPatch,
} from './receivingMode'
import { useReceivingModeOptions } from './useReceivingModeOptions'
import { ZoneModal } from './ZoneModal'
import './warehouse.css'

type TabKey = 'profile' | 'zones' | 'bins' | 'docks'

/** Dominios de estatus y códigos EntityType (CatalogDomains / EntityTypes). */
const WAREHOUSE_STATUS_DOMAIN = 'WarehouseStatus'
const WAREHOUSE_ENTITY_TYPE = 'WAREHOUSE'
const DOCK_STATUS_DOMAIN = 'DockStatus'
const DOCK_ENTITY_TYPE = 'WAREHOUSE_DOCK'

/** Estado de ocupación de una posición (BinOccupancies del dominio) → tono del chip. */
const OCCUPANCY_TONE: Record<string, ChipTone> = { EMPTY: 'neutral', PARTIAL: 'route', FULL: 'fail', NO_CAPACITY: 'wh' }

const NO_BINS: WarehouseBinDto[] = []
const NO_ZONES: WarehouseZoneDto[] = []

/** Chip Activo/Inactivo de la columna Estatus (zonas, posiciones). */
function ActiveChip({ active }: { active: boolean | undefined }) {
  const t = useT()
  return <Chip tone={active ? 'deliv' : 'warn'}>{active ? t('warehouse.zones.active') : t('warehouse.zones.inactive')}</Chip>
}

// =====================================================================================================================
// Pestaña Datos
// =====================================================================================================================
function ProfileTab({ detail }: { detail: WarehouseDetailDto }) {
  const t = useT()
  const lang = useLang()
  const w = detail.warehouse ?? {}
  const publicId = w.publicId ?? ''
  const canEdit = useCan('warehouse.manage')
  const update = useUpdateWarehouse()
  const modeOptions = useReceivingModeOptions()

  const schema = useMemo(
    () =>
      z.object({
        name: z.string().trim().min(1, t('warehouse.list.errors.nameRequired')),
        line1: z.string().trim(),
        city: z.string().trim(),
        state: z.string().trim(),
        postalCode: z.string().trim(),
        country: z.string().trim(),
        receivingMode: z.string(),
        defaultReceivingBinId: z.string(),
      }),
    [t],
  )
  const values = useMemo(
    () => ({
      name: w.name ?? '',
      line1: w.line1 ?? '',
      city: w.city ?? '',
      state: w.state ?? '',
      postalCode: w.postalCode ?? '',
      country: w.countryCode ?? '',
      receivingMode: normalizeReceivingMode(w.receivingModeCode),
      defaultReceivingBinId: w.defaultReceivingBinId != null ? String(w.defaultReceivingBinId) : '',
    }),
    [w.name, w.line1, w.city, w.state, w.postalCode, w.countryCode, w.receivingModeCode, w.defaultReceivingBinId],
  )
  const form = useForm({ resolver: zodResolver(schema), values })
  type Values = z.infer<typeof schema>

  // Cambio de modo pendiente de confirmar, con los conteos del almacén (solo mientras el diálogo está abierto)
  const [pendingMode, setPendingMode] = useState<Values | null>(null)
  const counts = { enabled: pendingMode !== null, handleAccessDenied: false }
  const openQ = useReceipts({ warehousePublicId: publicId, phase: 'OPEN', take: 1 }, counts)
  const putawayQ = useReceipts({ warehousePublicId: publicId, phase: 'PENDING_PUTAWAY', take: 1 }, counts)
  const countText = (n: number | undefined) => (n == null ? '…' : formatQuantity(n, lang))

  const save = async (v: Values) => {
    await update.mutateAsync({
      publicId,
      body: {
        name: v.name,
        line1: v.line1,
        city: v.city,
        state: v.state,
        postalCode: v.postalCode,
        country: v.country,
        ...warehouseReceivingPatch(w, v),
        rowVersion: w.rowVersion ?? null,
      },
    })
    toast.success(t('warehouse.detail.saved'))
  }

  return (
    <>
      <Form
        form={form}
        onSubmit={async (v) => {
          // D2: cambiar el modo no toca los recibos abiertos ni los acomodos pendientes; se confirma con sus conteos
          if (receivingModeChanged(w.receivingModeCode, v.receivingMode)) {
            setPendingMode(v)
            return
          }
          await save(v)
        }}
      >
        <fieldset disabled={!canEdit} style={{ border: 0, padding: 0, margin: 0, minWidth: 0 }}>
          <div className="r2">
            <ReadOnlyField label={t('warehouse.detail.code')} value={w.code ?? ''} help={t('warehouse.detail.codeHelp')} />
            <Field name="name" label={t('warehouse.detail.name')} required>
              <TextInput />
            </Field>
          </div>
          <Field name="line1" label={t('warehouse.detail.line1')}>
            <TextInput />
          </Field>
          <Field name="city" label={t('warehouse.postalPicker.label')} help={t('warehouse.postalPicker.help')}>
            <PostalLocalityPickerInput disabled={!canEdit} />
          </Field>
          <DerivedLocalityFields />
          <fieldset className="whs-receiving">
            <legend>{t('warehouse.detail.receiving')}</legend>
            <div className="r2">
              <Field name="receivingMode" label={t('warehouse.detail.receivingMode')} help={t('warehouse.detail.receivingModeHelp')}>
                <Select options={modeOptions} />
              </Field>
              <Field name="defaultReceivingBinId" label={t('warehouse.detail.defaultReceivingBin')} help={t('warehouse.detail.defaultReceivingBinHelp')}>
                <BinPickerInput
                  warehousePublicId={publicId}
                  zoneTypeCodes={RECEIVING_ZONE_TYPES}
                  placeholder={t('warehouse.detail.defaultReceivingBinNone')}
                  disabled={!canEdit}
                />
              </Field>
            </div>
          </fieldset>
        </fieldset>
        <Can perm="warehouse.manage">
          <div className="form-acts">
            <button type="submit" className="btn flow" disabled={form.formState.isSubmitting || !form.formState.isDirty}>
              {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
            </button>
          </div>
        </Can>
      </Form>
      <ConfirmDialog
        open={pendingMode !== null}
        title={t('warehouse.detail.receivingChangeTitle')}
        message={t('warehouse.detail.receivingChangeBody', {
          code: w.code ?? '',
          mode: inSentence(receivingModeLabel(pendingMode?.receivingMode, modeOptions.map((o) => ({ code: o.value, label: o.label })))),
          open: countText(openQ.data?.total),
          pending: countText(putawayQ.data?.total),
        })}
        confirmLabel={t('warehouse.detail.receivingChangeConfirm')}
        onConfirm={async () => {
          if (pendingMode) await save(pendingMode)
        }}
        onClose={() => setPendingMode(null)}
      />
    </>
  )
}

// =====================================================================================================================
// Pestaña Zonas
// =====================================================================================================================
function ZonesTab({ publicId }: { publicId: string }) {
  const t = useT()
  const canManage = useCan('warehouse.manage')
  const [includeInactive, setIncludeInactive] = useState(false)
  const [codes, setCodes] = useState<string[]>([])
  const [names, setNames] = useState<string[]>([])
  const [types, setTypes] = useState<string[]>([])
  const { data = NO_ZONES, isLoading } = useWarehouseZones(publicId, { includeInactive })
  const { data: zoneTypes = [] } = useLookups('ZoneType')
  const save = useSaveWarehouseZone()
  const [editing, setEditing] = useState<WarehouseZoneDto | null | 'new'>(null)
  const [confirmAction, setConfirmAction] = useState<{ zone: WarehouseZoneDto; action: 'deactivate' | 'reactivate' } | null>(null)

  const codeOptions = useMemo(() => distinctOptions(data.map((z) => z.code)), [data])
  const nameOptions = useMemo(() => distinctOptions(data.map((z) => z.name)), [data])
  const typeOptions = useMemo(() => zoneTypes.map((z) => ({ value: z.code, label: z.label })), [zoneTypes])

  const rows = useMemo(
    () =>
      data.filter(
        (z) =>
          (codes.length === 0 || codes.includes(z.code ?? '')) &&
          (names.length === 0 || names.includes(z.name ?? '')) &&
          (types.length === 0 || types.includes(z.zoneTypeCode ?? '')),
      ),
    [data, codes, names, types],
  )

  const columns = useMemo<DataColumn<WarehouseZoneDto>[]>(
    () => [
      { id: 'code', header: t('warehouse.zones.code'), cell: (z) => <span className="ref">{z.code}</span>, sortValue: (z) => z.code, card: 'title' },
      { id: 'name', header: t('warehouse.zones.name'), cell: (z) => z.name, sortValue: (z) => z.name },
      { id: 'type', header: t('warehouse.zones.type'), cell: (z) => z.zoneType ?? '—', sortValue: (z) => z.zoneType },
      { id: 'bins', header: t('warehouse.zones.bins'), cell: (z) => z.binCount ?? 0, sortValue: (z) => z.binCount, align: 'end' },
      { id: 'occupied', header: t('warehouse.zones.occupied'), cell: (z) => z.occupiedBinCount ?? 0, sortValue: (z) => z.occupiedBinCount, align: 'end' },
      {
        id: 'status',
        header: t('warehouse.zones.status'),
        cell: (z) => <ActiveChip active={z.isActive} />,
        sortValue: (z) => (z.isActive ? t('warehouse.zones.active') : t('warehouse.zones.inactive')),
      },
    ],
    [t],
  )

  const actions = useMemo<RowAction<WarehouseZoneDto>[]>(
    () => [
      {
        key: 'deactivate',
        label: t('warehouse.zones.deactivate'),
        perm: 'warehouse.manage',
        visible: (z) => z.isActive === true,
        onClick: (z) => setConfirmAction({ zone: z, action: 'deactivate' }),
        tone: 'danger',
        icon: <IconPower />,
      },
      {
        key: 'reactivate',
        label: t('warehouse.zones.reactivate'),
        perm: 'warehouse.manage',
        visible: (z) => z.isActive !== true,
        onClick: (z) => setConfirmAction({ zone: z, action: 'reactivate' }),
        icon: <IconRotateCcw />,
      },
    ],
    [t],
  )

  return (
    <>
      <Filters
        onClear={() => {
          setCodes([])
          setNames([])
          setTypes([])
          setIncludeInactive(false)
        }}
      >
        <SearchSelect label={t('warehouse.zones.code')} options={codeOptions} value={codes} onChange={setCodes} />
        <SearchSelect label={t('warehouse.zones.name')} options={nameOptions} value={names} onChange={setNames} />
        <SearchSelect label={t('warehouse.zones.type')} options={typeOptions} value={types} onChange={setTypes} />
        <ToggleFilter label={t('warehouse.zones.includeInactive')} checked={includeInactive} onChange={setIncludeInactive} />
      </Filters>
      <Panel
        flush
        icon={<IconWarehouse />}
        title={t('warehouse.zones.title')}
        badge={isLoading ? undefined : rows.length}
        actions={
          <Can perm="warehouse.manage">
            <button type="button" className="btn sm flow" onClick={() => setEditing('new')}>
              {t('warehouse.zones.newPlus')}
            </button>
          </Can>
        }
      >
        <DataTable
          label={t('warehouse.zones.title')}
          columns={columns}
          rows={rows}
          rowKey={(z) => z.id ?? 0}
          defaultSort={{ id: 'code', desc: false }}
          loading={isLoading}
          rowActions={canManage ? actions : []}
          onRowClick={canManage ? (z) => setEditing(z) : undefined}
          rowClassName={(z) => (z.isActive ? undefined : 'dim')}
        />
      </Panel>

      <ZoneModal publicId={publicId} zone={editing === 'new' || editing === null ? null : editing} open={editing !== null} onClose={() => setEditing(null)} />

      <ConfirmDialog
        open={confirmAction !== null}
        tone={confirmAction?.action === 'deactivate' ? 'danger' : 'flow'}
        title={confirmAction?.action === 'deactivate' ? t('warehouse.zones.deactivateTitle') : t('warehouse.zones.reactivateTitle')}
        message={t(confirmAction?.action === 'deactivate' ? 'warehouse.zones.deactivateBody' : 'warehouse.zones.reactivateBody', {
          code: confirmAction?.zone.code ?? '',
        })}
        confirmLabel={confirmAction?.action === 'deactivate' ? t('warehouse.zones.deactivate') : t('warehouse.zones.reactivate')}
        onConfirm={async () => {
          if (!confirmAction) return
          await save.mutateAsync({ publicId, action: confirmAction.action, zoneId: confirmAction.zone.id ?? 0 })
          toast.success(confirmAction.action === 'deactivate' ? t('warehouse.zones.deactivated') : t('warehouse.zones.reactivated'))
        }}
        onClose={() => setConfirmAction(null)}
      />
    </>
  )
}

// =====================================================================================================================
// Pestaña Posiciones
// =====================================================================================================================

const BIN_PAGE_SIZE = 25

function BinsTab({ publicId, zones }: { publicId: string; zones: readonly WarehouseZoneDto[] }) {
  const t = useT()
  const lang = useLang()
  const canManage = useCan('warehouse.manage')
  const [text, setText] = useState<BinTextFilters>(EMPTY_BIN_TEXT)
  const [zoneIds, setZoneIds] = useState<string[]>([])
  const [includeInactive, setIncludeInactive] = useState(false)
  const [onlyWithStock, setOnlyWithStock] = useState(false)
  // Lote F12: posiciones creadas desde un conteo que el supervisor aún no confirma
  const [onlyProvisional, setOnlyProvisional] = useState(false)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(BIN_PAGE_SIZE)
  // los textos van al API con una pausa (el objeto `text` solo cambia al teclear: identidad estable entre renders)
  const debouncedText = useDebounced(text)

  const filterQuery = useMemo(
    () => binsQuery(debouncedText, zoneIds, includeInactive, onlyWithStock, onlyProvisional),
    [debouncedText, zoneIds, includeInactive, onlyWithStock, onlyProvisional],
  )
  const query = useMemo(() => ({ ...filterQuery, skip: (page - 1) * pageSize, take: pageSize }), [filterQuery, page, pageSize])
  const { data, isLoading, isFetching } = useWarehouseBins(publicId, query)
  const rows = data?.items ?? NO_BINS
  const save = useSaveWarehouseBin()
  const confirmProvisional = useConfirmProvisionalBin()
  const [editing, setEditing] = useState<WarehouseBinDto | null | 'new'>(null)
  const [confirmAction, setConfirmAction] = useState<{ bin: WarehouseBinDto; action: 'deactivate' | 'reactivate' } | null>(null)
  const [settingCapacity, setSettingCapacity] = useState(false)

  const zoneOptions = useMemo(() => zones.map((z) => ({ value: String(z.id), label: [z.code, z.name].filter(Boolean).join(' · ') })), [zones])
  const activeZones = useMemo(() => zones.filter((z) => z.isActive !== false), [zones])

  const setTextField = (key: keyof BinTextFilters) => (v: string) => {
    setText((prev) => ({ ...prev, [key]: v }))
    setPage(1)
  }

  const columns = useMemo<DataColumn<WarehouseBinDto>[]>(
    () => [
      {
        id: 'code',
        header: t('warehouse.bins.code'),
        cell: (b) => (
          <span className="cc-bincell">
            <span className="ref">{b.code}</span>
            {b.isProvisional && (
              <Chip tone="warn" title={t('warehouse.bins.provisionalHelp')}>
                {t('warehouse.bins.provisional')}
              </Chip>
            )}
          </span>
        ),
        sortValue: (b) => b.code,
        exportValue: (b) => (b.isProvisional ? `${b.code ?? ''} (${t('warehouse.bins.provisional')})` : (b.code ?? '')),
        card: 'title',
      },
      { id: 'zone', header: t('warehouse.bins.zone'), cell: (b) => b.zoneCode, sortValue: (b) => b.zoneCode },
      {
        id: 'location',
        header: t('warehouse.bins.location'),
        cell: (b) => [b.aisle, b.rack, b.level, b.position].filter(Boolean).join(' / ') || '—',
        sortValue: (b) => [b.aisle, b.rack, b.level, b.position].filter(Boolean).join(' / '),
      },
      {
        id: 'capacity',
        header: t('warehouse.bins.capacity'),
        cell: (b) => (b.maxCapacityQty != null ? formatNumber(b.maxCapacityQty, lang) : '—'),
        sortValue: (b) => b.maxCapacityQty,
        align: 'end',
      },
      { id: 'onHand', header: t('warehouse.bins.onHand'), cell: (b) => formatNumber(b.qtyOnHand ?? 0, lang), sortValue: (b) => b.qtyOnHand, align: 'end' },
      {
        id: 'occupancy',
        header: t('warehouse.bins.occupancy'),
        cell: (b) => {
          const code = b.occupancy ?? 'EMPTY'
          const pct = b.maxCapacityQty ? Math.round((100 * (b.qtyOnHand ?? 0)) / b.maxCapacityQty) : null
          return (
            <span className="whs-occ">
              <Chip tone={OCCUPANCY_TONE[code] ?? 'neutral'}>{t(`warehouse.bins.occupancyStates.${code}`)}</Chip>
              {pct != null && code !== 'EMPTY' && <span className="whs-pct">{pct} %</span>}
            </span>
          )
        },
        sortValue: (b) => (b.maxCapacityQty ? (b.qtyOnHand ?? 0) / b.maxCapacityQty : b.qtyOnHand ? -1 : -2),
        exportValue: (b) => t(`warehouse.bins.occupancyStates.${b.occupancy ?? 'EMPTY'}`),
      },
      {
        id: 'product',
        header: t('warehouse.bins.product'),
        cell: (b) =>
          b.singleProductSku ? (
            <span title={b.singleProductName ?? undefined}>{b.singleProductSku}</span>
          ) : (b.productCount ?? 0) > 1 ? (
            t('warehouse.bins.productsCount', { count: b.productCount ?? 0 })
          ) : (
            '—'
          ),
        sortValue: (b) => b.singleProductSku ?? ((b.productCount ?? 0) > 1 ? `~${b.productCount}` : null),
      },
      { id: 'maxWeight', header: t('warehouse.bins.maxWeight'), cell: (b) => (b.maxWeightKg != null ? formatNumber(b.maxWeightKg, lang) : '—'), sortValue: (b) => b.maxWeightKg, align: 'end' },
      {
        id: 'status',
        header: t('warehouse.bins.status'),
        cell: (b) => <ActiveChip active={b.isActive} />,
        sortValue: (b) => (b.isActive ? t('warehouse.zones.active') : t('warehouse.zones.inactive')),
      },
    ],
    [t, lang],
  )

  const actions = useMemo<RowAction<WarehouseBinDto>[]>(
    () => [
      {
        key: 'confirmProvisional',
        label: t('warehouse.bins.confirmProvisional'),
        perm: 'warehouse.manage',
        visible: (b) => b.isProvisional === true,
        disabled: () => confirmProvisional.isPending,
        onClick: (b) => {
          confirmProvisional.mutate(
            { publicId, binId: b.id ?? 0 },
            {
              onSuccess: () => toast.success(t('warehouse.bins.provisionalConfirmed', { code: b.code ?? '' })),
              onError: (err) => toast.error(problemText(err)),
            },
          )
        },
        tone: 'flow',
        icon: <IconCheck />,
      },
      {
        key: 'deactivate',
        label: t('warehouse.bins.deactivate'),
        perm: 'warehouse.manage',
        visible: (b) => b.isActive === true,
        onClick: (b) => setConfirmAction({ bin: b, action: 'deactivate' }),
        tone: 'danger',
        icon: <IconPower />,
      },
      {
        key: 'reactivate',
        label: t('warehouse.bins.reactivate'),
        perm: 'warehouse.manage',
        visible: (b) => b.isActive !== true,
        onClick: (b) => setConfirmAction({ bin: b, action: 'reactivate' }),
        icon: <IconRotateCcw />,
      },
    ],
    [t, confirmProvisional, publicId],
  )

  return (
    <>
      <Filters
        onClear={() => {
          setText(EMPTY_BIN_TEXT)
          setZoneIds([])
          setIncludeInactive(false)
          setOnlyWithStock(false)
          setOnlyProvisional(false)
          setPage(1)
        }}
      >
        <TextFilter label={t('warehouse.bins.code')} value={text.code} onChange={setTextField('code')} />
        <SearchSelect
          label={t('warehouse.bins.zone')}
          options={zoneOptions}
          value={zoneIds}
          onChange={(v) => {
            setZoneIds(v)
            setPage(1)
          }}
        />
        <TextFilter label={t('warehouse.bins.aisle')} value={text.aisle} onChange={setTextField('aisle')} />
        <TextFilter label={t('warehouse.bins.rack')} value={text.rack} onChange={setTextField('rack')} />
        <TextFilter label={t('warehouse.bins.level')} value={text.level} onChange={setTextField('level')} />
        <TextFilter label={t('warehouse.bins.position')} value={text.position} onChange={setTextField('position')} />
        <ToggleFilter
          label={t('warehouse.bins.includeInactive')}
          checked={includeInactive}
          onChange={(v) => {
            setIncludeInactive(v)
            setPage(1)
          }}
        />
        <ToggleFilter
          label={t('warehouse.bins.onlyWithStock')}
          checked={onlyWithStock}
          onChange={(v) => {
            setOnlyWithStock(v)
            setPage(1)
          }}
        />
        <ToggleFilter
          label={t('warehouse.bins.onlyProvisional')}
          checked={onlyProvisional}
          onChange={(v) => {
            setOnlyProvisional(v)
            setPage(1)
          }}
        />
      </Filters>
      <Panel
        flush
        icon={<IconWarehouse />}
        title={t('warehouse.bins.title')}
        badge={data ? formatNumber(data.total ?? 0, lang) : undefined}
        actions={
          <Can perm="warehouse.manage">
            <button type="button" className="btn sm" onClick={() => setSettingCapacity(true)}>
              {t('warehouse.binCapacity.open')}
            </button>
            <button type="button" className="btn sm flow" onClick={() => setEditing('new')}>
              {t('warehouse.bins.new')}
            </button>
          </Can>
        }
      >
        <DataTable
          label={t('warehouse.bins.title')}
          columns={columns}
          rows={rows}
          rowKey={(b) => b.id ?? 0}
          page={page}
          pageSize={pageSize}
          total={data?.total ?? 0}
          onPage={setPage}
          onPageSize={(n) => {
            setPageSize(n)
            setPage(1)
          }}
          exportRows={() => exportWarehouseBins(publicId, filterQuery)}
          loading={isLoading || (isFetching && rows.length === 0)}
          rowActions={canManage ? actions : []}
          onRowClick={canManage ? (b) => setEditing(b) : undefined}
          rowClassName={(b) => (b.isActive ? undefined : 'dim')}
        />
      </Panel>

      <BinModal publicId={publicId} zones={activeZones} bin={editing === 'new' || editing === null ? null : editing} open={editing !== null} onClose={() => setEditing(null)} />

      {/* cupo en bloque: arranca con la Zona y los textos de ubicación del filtro de la pestaña (el Código no aplica) */}
      <BinCapacityModal
        publicId={publicId}
        open={settingCapacity}
        onClose={() => setSettingCapacity(false)}
        initial={{ zoneIds, aisle: text.aisle, rack: text.rack, level: text.level, position: text.position }}
      />

      <ConfirmDialog
        open={confirmAction !== null}
        tone={confirmAction?.action === 'deactivate' ? 'danger' : 'flow'}
        title={confirmAction?.action === 'deactivate' ? t('warehouse.bins.deactivateTitle') : t('warehouse.bins.reactivateTitle')}
        message={t(confirmAction?.action === 'deactivate' ? 'warehouse.bins.deactivateBody' : 'warehouse.bins.reactivateBody', {
          code: confirmAction?.bin.code ?? '',
        })}
        confirmLabel={confirmAction?.action === 'deactivate' ? t('warehouse.bins.deactivate') : t('warehouse.bins.reactivate')}
        onConfirm={async () => {
          if (!confirmAction) return
          await save.mutateAsync({ publicId, action: confirmAction.action, binId: confirmAction.bin.id ?? 0 })
          toast.success(confirmAction.action === 'deactivate' ? t('warehouse.bins.deactivated') : t('warehouse.bins.reactivated'))
        }}
        onClose={() => setConfirmAction(null)}
      />
    </>
  )
}

// =====================================================================================================================
// Pestaña Muelles
// =====================================================================================================================
function DockModal({
  publicId,
  dock,
  open,
  onClose,
}: {
  publicId: string
  dock: WarehouseDockDto | null
  open: boolean
  onClose: () => void
}) {
  const t = useT()
  const save = useSaveWarehouseDock()
  const { data: dockTypes = [] } = useLookups('DockType')
  const isEdit = dock !== null

  const schema = useMemo(
    () =>
      z.object({
        code: isEdit ? z.string() : z.string().trim().min(1, t('warehouse.docks.errors.codeRequired')),
        dockType: z.string(),
      }),
    [t, isEdit],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    values: { code: dock?.code ?? '', dockType: dock?.dockTypeCode ?? '' },
  })
  const formId = 'warehouse-dock-save'

  const close = () => {
    form.reset()
    onClose()
  }

  return (
    <Modal
      open={open}
      title={isEdit ? t('warehouse.docks.edit') : t('warehouse.docks.new')}
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
          if (isEdit && dock) {
            await save.mutateAsync({ publicId, action: 'update', dockId: dock.id ?? 0, body: { dockType: v.dockType || null } })
            toast.success(t('warehouse.docks.saved'))
          } else {
            await save.mutateAsync({ publicId, action: 'create', body: { code: v.code, dockType: v.dockType || null } })
            toast.success(t('warehouse.docks.created'))
          }
          close()
        }}
      >
        {isEdit ? (
          <ReadOnlyField label={t('warehouse.docks.code')} value={dock?.code ?? ''} help={t('warehouse.docks.codeHelp')} />
        ) : (
          <Field name="code" label={t('warehouse.docks.code')} required>
            <TextInput />
          </Field>
        )}
        <Field name="dockType" label={t('warehouse.docks.type')}>
          <Select options={dockTypes.map((d) => ({ value: d.code, label: d.label }))} placeholder="" />
        </Field>
      </Form>
    </Modal>
  )
}

function DockStatusModal({ publicId, dock, open, onClose }: { publicId: string; dock: WarehouseDockDto | null; open: boolean; onClose: () => void }) {
  const t = useT()
  const save = useSaveWarehouseDock()
  const canManage = useCan('warehouse.manage')
  if (!dock) return null
  return (
    <Modal open={open} title={t('warehouse.docks.changeStatus')} onClose={onClose} size="sm">
      <StatusPipeline
        domain={DOCK_STATUS_DOMAIN}
        entityType={DOCK_ENTITY_TYPE}
        entityId={dock.id}
        currentCode={dock.statusCode}
        disabled={!canManage}
        onTransition={(toCode, comment) =>
          save.mutateAsync({ publicId, action: 'status', dockId: dock.id ?? 0, body: { status: toCode, comment: comment ?? null } })
        }
      />
    </Modal>
  )
}

function DocksTab({ publicId }: { publicId: string }) {
  const t = useT()
  const canManage = useCan('warehouse.manage')
  const [includeInactive, setIncludeInactive] = useState(false)
  const { data, isLoading } = useWarehouseDocks(publicId, { includeInactive })
  const save = useSaveWarehouseDock()
  const [editing, setEditing] = useState<WarehouseDockDto | null | 'new'>(null)
  const [statusDock, setStatusDock] = useState<WarehouseDockDto | null>(null)
  const [confirmAction, setConfirmAction] = useState<{ dock: WarehouseDockDto; action: 'deactivate' | 'reactivate' } | null>(null)

  const columns = useMemo<DataColumn<WarehouseDockDto>[]>(
    () => [
      { id: 'code', header: t('warehouse.docks.code'), cell: (d) => <span className="ref">{d.code}</span>, sortValue: (d) => d.code, card: 'title' },
      { id: 'type', header: t('warehouse.docks.type'), cell: (d) => d.dockType, sortValue: (d) => d.dockType },
      {
        id: 'status',
        header: t('warehouse.docks.status'),
        cell: (d) => <StatusChip domain={DOCK_STATUS_DOMAIN} code={d.statusCode} label={d.status} />,
        sortValue: (d) => d.status,
      },
      {
        id: 'active',
        header: t('warehouse.docks.active'),
        cell: (d) => <Chip tone={d.isActive ? 'deliv' : 'warn'}>{d.isActive ? t('warehouse.docks.active') : t('warehouse.docks.inactive')}</Chip>,
        sortValue: (d) => d.isActive,
      },
    ],
    [t],
  )

  const actions = useMemo<RowAction<WarehouseDockDto>[]>(
    () => [
      { key: 'edit', label: t('warehouse.docks.edit'), perm: 'warehouse.manage', onClick: (d) => setEditing(d) },
      { key: 'status', label: t('warehouse.docks.changeStatus'), perm: 'warehouse.manage', onClick: (d) => setStatusDock(d) },
      {
        key: 'deactivate',
        label: t('warehouse.docks.deactivate'),
        perm: 'warehouse.manage',
        visible: (d) => d.isActive === true,
        onClick: (d) => setConfirmAction({ dock: d, action: 'deactivate' }),
        tone: 'danger',
      },
      {
        key: 'reactivate',
        label: t('warehouse.docks.reactivate'),
        perm: 'warehouse.manage',
        visible: (d) => d.isActive !== true,
        onClick: (d) => setConfirmAction({ dock: d, action: 'reactivate' }),
      },
    ],
    [t],
  )

  return (
    <>
      <div className="head">
        <div>
          <p>{t('warehouse.docks.count', { count: data?.length ?? 0 })}</p>
        </div>
        <div className="act">
          <Can perm="warehouse.manage">
            <button type="button" className="btn flow" onClick={() => setEditing('new')}>
              {t('warehouse.docks.new')}
            </button>
          </Can>
        </div>
      </div>
      <Filters onClear={() => setIncludeInactive(false)}>
        <ToggleFilter label={t('warehouse.docks.includeInactive')} checked={includeInactive} onChange={setIncludeInactive} />
      </Filters>
      <Panel flush>
        <DataTable
          label={t('warehouse.docks.title')}
          columns={columns}
          rows={data ?? []}
          rowKey={(d) => d.id ?? 0}
          defaultSort={{ id: 'code', desc: false }}
          loading={isLoading}
          rowActions={canManage ? actions : []}
        />
      </Panel>

      <DockModal publicId={publicId} dock={editing === 'new' || editing === null ? null : editing} open={editing !== null} onClose={() => setEditing(null)} />
      <DockStatusModal publicId={publicId} dock={statusDock} open={statusDock !== null} onClose={() => setStatusDock(null)} />

      <ConfirmDialog
        open={confirmAction !== null}
        tone={confirmAction?.action === 'deactivate' ? 'danger' : 'flow'}
        title={confirmAction?.action === 'deactivate' ? t('warehouse.docks.deactivateTitle') : t('warehouse.docks.reactivateTitle')}
        message={t(confirmAction?.action === 'deactivate' ? 'warehouse.docks.deactivateBody' : 'warehouse.docks.reactivateBody', {
          code: confirmAction?.dock.code ?? '',
        })}
        confirmLabel={confirmAction?.action === 'deactivate' ? t('warehouse.docks.deactivate') : t('warehouse.docks.reactivate')}
        onConfirm={async () => {
          if (!confirmAction) return
          await save.mutateAsync({ publicId, action: confirmAction.action, dockId: confirmAction.dock.id ?? 0 })
          toast.success(confirmAction.action === 'deactivate' ? t('warehouse.docks.deactivated') : t('warehouse.docks.reactivated'))
        }}
        onClose={() => setConfirmAction(null)}
      />
    </>
  )
}


// =====================================================================================================================
// Pantalla
// =====================================================================================================================
export default function WarehouseDetailScreen() {
  const t = useT()
  const { publicId = '' } = useParams()
  const { data, isLoading, error } = useWarehouse(publicId)
  const canManage = useCan('warehouse.manage')
  const deactivateWarehouse = useDeactivateWarehouse()
  const [tab, setTab] = useState<TabKey>('profile')

  if (isLoading) return <Spinner block />
  if (error || !data?.warehouse) {
    const notFound = error instanceof ApiError && error.code === 'not_found'
    return (
      <EmptyState
        title={notFound ? t('warehouse.detail.notFound') : (error?.message ?? t('errors.generic'))}
        action={
          <Link className="btn" to="/warehouse/warehouses">
            {t('warehouse.detail.back')}
          </Link>
        }
      />
    )
  }

  const w = data.warehouse

  return (
    <div className="wrap">
      <div className="head">
        <div style={{ minWidth: 0 }}>
          <h1>
            <span className="ref">{w.code}</span> · {w.name}
          </h1>
          <p>
            {!w.isActive && <Chip tone="fail">{t('warehouse.detail.inactive')}</Chip>} {w.city}
          </p>
        </div>
      </div>

      <div style={{ marginBottom: 14 }}>
        <StatusPipeline
          domain={WAREHOUSE_STATUS_DOMAIN}
          entityType={WAREHOUSE_ENTITY_TYPE}
          entityId={w.id}
          currentCode={w.statusCode}
          disabled={!canManage || !w.isActive}
          onTransition={(toCode, comment) => {
            if (toCode !== 'INACTIVE') return Promise.resolve()
            return deactivateWarehouse.mutateAsync({ publicId, body: { comment: comment ?? null, rowVersion: w.rowVersion ?? null } })
          }}
        />
      </div>

      <div style={{ marginBottom: 14 }}>
        <Tabs<TabKey>
          label={t('warehouse.list.title')}
          value={tab}
          onChange={setTab}
          tabs={[
            { key: 'profile', label: t('warehouse.detail.tabProfile') },
            { key: 'zones', label: t('warehouse.detail.tabZones') },
            { key: 'bins', label: t('warehouse.detail.tabBins') },
            { key: 'docks', label: t('warehouse.detail.tabDocks') },
          ]}
        />
      </div>

      {tab === 'profile' && (
        <Panel icon={<IconWarehouse />} title={t('warehouse.detail.tabProfile')}>
          <ProfileTab detail={data} />
        </Panel>
      )}
      {tab === 'zones' && <ZonesTab publicId={publicId} />}
      {tab === 'bins' && <BinsTab publicId={publicId} zones={data.zones ?? []} />}
      {tab === 'docks' && <DocksTab publicId={publicId} />}
    </div>
  )
}
