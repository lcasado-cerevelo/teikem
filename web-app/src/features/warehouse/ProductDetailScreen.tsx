// Pantalla B (Lote F6) — ficha de producto. `/warehouse/products/:publicId`, pestañas Datos / Lotes / Series.
// PATCH /api/v1/products/{publicId} (inventory.manage); baja/reactivación POST .../deactivate|reactivate.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useController, useForm, useFormContext, useWatch } from 'react-hook-form'
import { Link, useParams } from 'react-router-dom'
import { z } from 'zod'
import { Can, useCan } from '../../kernel/access'
import { ApiError } from '../../kernel/api/problem'
import { StatusChip, useLookups, useStatuses } from '../../kernel/catalogs'
import { CustomFieldsForm, useSaveCustomFields } from '../../kernel/custom-fields'
import { useT } from '../../kernel/i18n'
import { useFieldInfo } from '../../kernel/ui/formContext'
import {
  Chip,
  ClientPickerInput,
  ConfirmDialog,
  DataTable,
  EmptyState,
  Field,
  Filters,
  Form,
  NumberInput,
  Panel,
  QBox,
  Select,
  Spinner,
  Tabs,
  TextInput,
  matchesQ,
  toast,
  type DataColumn,
} from '../../kernel/ui'
import {
  useProduct,
  useProductLots,
  useProductSerials,
  useSetProductActive,
  useUpdateProduct,
  useWarehouseBins,
  type LotDto,
  type ProductDetailDto,
  type SerialDto,
} from './api'
import { WarehousePicker } from './pickers'

type TabKey = 'data' | 'lots' | 'serials'
const PICKING_ZONE = 'PICKING'

function decimals(n: number): number {
  const s = String(n)
  const i = s.indexOf('.')
  return i === -1 ? 0 : s.length - i - 1
}

// ---- Pestaña Datos ----
function DataTab({ publicId, detail }: { publicId: string; detail: ProductDetailDto }) {
  const t = useT()
  const canEdit = useCan('inventory.manage')
  const product = detail.product!
  const update = useUpdateProduct()
  const { data: trackingTypes = [] } = useLookups('TrackingType')
  const { data: uoms = [] } = useLookups('UnitOfMeasure')
  const hasMovements = detail.hasMovements === true

  const schema = useMemo(
    () =>
      z
        .object({
          name: z.string().trim().min(1, t('warehouse.products.errors.nameRequired')).max(200, t('warehouse.products.errors.nameMax')),
          barcode: z.string().trim().max(60, t('warehouse.products.errors.barcodeMax')),
          trackingType: z.string(),
          baseUom: z.string(),
          ownerClientPublicId: z.string().nullable(),
          purchaseCost: z
            .number(t('warehouse.products.errors.numberInvalid'))
            .min(0, t('warehouse.products.errors.negative'))
            .refine((v) => decimals(v) <= 4, t('warehouse.products.errors.decimals4'))
            .nullable(),
          salePrice: z
            .number(t('warehouse.products.errors.numberInvalid'))
            .min(0, t('warehouse.products.errors.negative'))
            .refine((v) => decimals(v) <= 4, t('warehouse.products.errors.decimals4'))
            .nullable(),
          weightKg: z.number(t('warehouse.products.errors.numberInvalid')).min(0, t('warehouse.products.errors.negative')).nullable(),
          volumeM3: z.number(t('warehouse.products.errors.numberInvalid')).min(0, t('warehouse.products.errors.negative')).nullable(),
          minQty: z.number(t('warehouse.products.errors.numberInvalid')).min(0, t('warehouse.products.errors.minNegative')).nullable(),
          minPickQty: z.number(t('warehouse.products.errors.numberInvalid')).min(0, t('warehouse.products.errors.minNegative')).nullable(),
          maxPickQty: z.number(t('warehouse.products.errors.numberInvalid')).min(0, t('warehouse.products.errors.minNegative')).nullable(),
          preferredWarehousePublicId: z.string().nullable(),
          preferredBinId: z.string(),
        })
        .refine((v) => v.maxPickQty == null || v.minPickQty == null || v.maxPickQty >= v.minPickQty, {
          path: ['maxPickQty'],
          message: t('warehouse.products.errors.maxPickLtMin'),
        }),
    [t],
  )

  const form = useForm({
    resolver: zodResolver(schema),
    values: {
      name: product.name ?? '',
      barcode: product.barcode ?? '',
      trackingType: product.trackingTypeCode ?? '',
      baseUom: product.baseUomCode ?? '',
      ownerClientPublicId: product.ownerClientPublicId ?? null,
      purchaseCost: product.purchaseCost ?? null,
      salePrice: product.salePrice ?? null,
      weightKg: detail.weightKg ?? null,
      volumeM3: detail.volumeM3 ?? null,
      minQty: product.minQty ?? null,
      minPickQty: detail.minPickQty ?? null,
      maxPickQty: detail.maxPickQty ?? null,
      preferredWarehousePublicId: detail.preferredWarehousePublicId ?? null,
      preferredBinId: detail.preferredBinId != null ? String(detail.preferredBinId) : '',
    },
  })

  const preferredWarehousePublicId = useWatch({ control: form.control, name: 'preferredWarehousePublicId' })
  const { data: bins = [] } = useWarehouseBins(preferredWarehousePublicId, {}, { enabled: Boolean(preferredWarehousePublicId) })
  const binOptions = useMemo(() => bins.filter((b) => b.isActive).map((b) => ({ value: String(b.id), label: b.code ?? '' })), [bins])
  const { save: saveCustomFields } = useSaveCustomFields('PRODUCT')

  return (
    <Form
      form={form}
      onSubmit={async (v) => {
        const dirty = form.formState.dirtyFields
        const minPickQty = v.minPickQty
        if (minPickQty != null && v.preferredBinId) {
          const bin = bins.find((b) => String(b.id) === v.preferredBinId)
          if (bin && bin.zoneTypeCode !== PICKING_ZONE) {
            form.setError('preferredBinId', { type: 'server', message: t('warehouse.products.errors.pickZoneRequired') })
            return
          }
        }
        await update.mutateAsync({
          publicId,
          body: {
            name: v.name,
            barcode: v.barcode || null,
            clearBarcode: v.barcode.trim() === '' ? true : null,
            trackingType: hasMovements ? null : v.trackingType || null,
            baseUom: hasMovements ? null : v.baseUom || null,
            ownerClientPublicId: hasMovements ? null : v.ownerClientPublicId,
            clearOwner: hasMovements || v.ownerClientPublicId ? null : true,
            purchaseCost: v.purchaseCost,
            salePrice: v.salePrice,
            weightKg: v.weightKg,
            volumeM3: v.volumeM3,
            minQty: v.minQty,
            minPickQty: v.minPickQty,
            maxPickQty: v.maxPickQty,
            preferredWarehousePublicId: v.preferredWarehousePublicId,
            preferredBinId: v.preferredBinId ? Number(v.preferredBinId) : null,
            clearPreferred: dirty.preferredWarehousePublicId && !v.preferredWarehousePublicId ? true : null,
            rowVersion: detail.rowVersion,
          },
        })
        const id = product.id
        if (typeof id === 'number' && id > 0) {
          const problem = await saveCustomFields(id, form)
          if (problem) toast.error(problem.title)
        }
        toast.success(t('warehouse.products.saved'))
      }}
    >
      <fieldset disabled={!canEdit} style={{ border: 0, padding: 0, margin: 0, minWidth: 0 }}>
        <div className="r2">
          <div className="f">
            <label htmlFor="product-sku-locked">{t('warehouse.products.fields.sku')}</label>
            <input id="product-sku-locked" value={product.sku ?? ''} disabled />
            <p className="help">{t('warehouse.products.skuLocked')}</p>
          </div>
          <Field name="name" label={t('warehouse.products.fields.name')} required>
            <TextInput maxLength={200} />
          </Field>
        </div>
        <div className="r2">
          <Field name="barcode" label={t('warehouse.products.fields.barcode')}>
            <TextInput maxLength={60} />
          </Field>
          <Field
            name="trackingType"
            label={t('warehouse.products.fields.trackingType')}
            help={hasMovements ? t('warehouse.products.movementsLock') : undefined}
          >
            <Select options={trackingTypes.map((o) => ({ value: o.code, label: o.label }))} disabled={hasMovements} />
          </Field>
        </div>
        <div className="r2">
          <Field
            name="baseUom"
            label={t('warehouse.products.fields.baseUom')}
            help={hasMovements ? t('warehouse.products.movementsLock') : undefined}
          >
            <Select options={uoms.map((o) => ({ value: o.code, label: o.label }))} disabled={hasMovements} />
          </Field>
          <Field
            name="ownerClientPublicId"
            label={t('warehouse.products.fields.owner')}
            help={hasMovements ? t('warehouse.products.movementsLock') : undefined}
          >
            <ClientPickerInput disabled={hasMovements} />
          </Field>
        </div>
        <div className="r2">
          <Field name="purchaseCost" label={t('warehouse.products.fields.purchaseCost')}>
            <NumberInput min={0} />
          </Field>
          <Field name="salePrice" label={t('warehouse.products.fields.salePrice')}>
            <NumberInput min={0} />
          </Field>
        </div>
        <div className="r2">
          <Field name="weightKg" label={t('warehouse.products.fields.weightKg')}>
            <NumberInput min={0} />
          </Field>
          <Field name="volumeM3" label={t('warehouse.products.fields.volumeM3')}>
            <NumberInput min={0} />
          </Field>
        </div>
        <div className="r2">
          <Field name="minQty" label={t('warehouse.products.fields.minQty')}>
            <NumberInput min={0} />
          </Field>
          <div className="r2">
            <Field name="minPickQty" label={t('warehouse.products.fields.minPickQty')}>
              <NumberInput min={0} />
            </Field>
            <Field name="maxPickQty" label={t('warehouse.products.fields.maxPickQty')}>
              <NumberInput min={0} />
            </Field>
          </div>
        </div>
        <div className="r2">
          <Field name="preferredWarehousePublicId" label={t('warehouse.products.fields.preferredWarehouse')}>
            <WarehousePickerField />
          </Field>
          <Field name="preferredBinId" label={t('warehouse.products.fields.preferredBin')}>
            <Select options={binOptions} placeholder={t('warehouse.products.fields.none')} disabled={!preferredWarehousePublicId} />
          </Field>
        </div>
        <CustomFieldsForm entityType="PRODUCT" entityId={product.id} form={form} disabled={!canEdit} />
      </fieldset>
      <Can perm="inventory.manage">
        <div className="form-acts">
          <button type="submit" className="btn flow" disabled={form.formState.isSubmitting || !form.formState.isDirty}>
            {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
          </button>
        </div>
      </Can>
    </Form>
  )
}

/** WarehousePicker (de este módulo) no tiene variante ...Input: se conecta a mano dentro de un <Field>. */
function WarehousePickerField() {
  const info = useFieldInfo('WarehousePickerAdapter')
  const { control } = useFormContext()
  const { field } = useController({ name: info.name, control })
  return (
    <WarehousePicker
      id={info.id}
      value={(field.value as string | null | undefined) ?? null}
      onChange={(publicId) => field.onChange(publicId)}
      invalid={info.invalid}
    />
  )
}

// ---- Pestaña Lotes ----
function LotsTab({ publicId }: { publicId: string }) {
  const t = useT()
  const { data: lots = [], isLoading } = useProductLots(publicId)
  const columns = useMemo<DataColumn<LotDto>[]>(
    () => [
      { id: 'lotNumber', header: t('warehouse.products.lots.lotNumber'), cell: (l) => l.lotNumber, sortValue: (l) => l.lotNumber, card: 'title' },
      { id: 'expiryDate', header: t('warehouse.products.lots.expiryDate'), cell: (l) => l.expiryDate ?? '', sortValue: (l) => l.expiryDate },
      {
        id: 'active',
        header: t('warehouse.products.fields.active'),
        cell: (l) => <Chip tone={l.isActive ? 'neutral' : 'fail'}>{l.isActive ? t('warehouse.products.active') : t('warehouse.products.inactive')}</Chip>,
      },
    ],
    [t],
  )
  return (
    <DataTable
      label={t('warehouse.products.tabLots')}
      columns={columns}
      rows={lots}
      rowKey={(l) => l.id ?? 0}
      loading={isLoading}
      pageSize={25}
      empty={<EmptyState title={t('warehouse.products.lots.empty')} />}
    />
  )
}

// ---- Pestaña Series ----
function SerialsTab({ publicId }: { publicId: string }) {
  const t = useT()
  const [status, setStatus] = useState('')
  const [q, setQ] = useState('')
  const { data: statuses = [] } = useStatuses('SerialStatus')
  const { data: serials = [], isLoading } = useProductSerials(publicId, { status: status || undefined })
  const rows = useMemo(() => serials.filter((s) => matchesQ(q, s.serialNumber, s.binCode)), [serials, q])

  const columns = useMemo<DataColumn<SerialDto>[]>(
    () => [
      { id: 'serialNumber', header: t('warehouse.products.serials.serialNumber'), cell: (s) => s.serialNumber, sortValue: (s) => s.serialNumber, card: 'title' },
      {
        id: 'status',
        header: t('warehouse.products.serials.status'),
        cell: (s) => <StatusChip domain="SerialStatus" code={s.statusCode} label={s.status} />,
      },
      { id: 'bin', header: t('warehouse.products.serials.bin'), cell: (s) => s.binCode ?? '' },
    ],
    [t],
  )

  return (
    <>
      <Filters onClear={() => setStatus('')}>
        <div className="f">
          <label>{t('warehouse.products.serials.status')}</label>
          <select value={status} onChange={(e) => setStatus(e.target.value)}>
            <option value="">{t('ui.filters.all')}</option>
            {statuses.map((s) => (
              <option key={s.code} value={s.code}>
                {s.label}
              </option>
            ))}
          </select>
        </div>
      </Filters>
      <div className="qrow">
        <QBox value={q} onChange={setQ} />
      </div>
      <DataTable
        label={t('warehouse.products.tabSerials')}
        columns={columns}
        rows={rows}
        rowKey={(s) => s.id ?? 0}
        loading={isLoading}
        pageSize={25}
        empty={<EmptyState title={t('warehouse.products.serials.empty')} />}
      />
    </>
  )
}

// ---- Pantalla ----
export default function ProductDetailScreen() {
  const t = useT()
  const { publicId = '' } = useParams()
  const { data: detail, isLoading, error } = useProduct(publicId)
  const setActive = useSetProductActive()
  const [tab, setTab] = useState<TabKey>('data')
  const [confirm, setConfirm] = useState<'deactivate' | 'reactivate' | null>(null)

  if (isLoading) return <Spinner block />
  if (error || !detail || !detail.product) {
    const notFound = error instanceof ApiError && error.code === 'not_found'
    return (
      <EmptyState
        title={notFound ? t('warehouse.products.notFound') : (error?.message ?? t('errors.generic'))}
        action={
          <Link className="btn" to="/warehouse/products">
            {t('warehouse.products.back')}
          </Link>
        }
      />
    )
  }
  const product = detail.product

  return (
    <div className="wrap">
      <div className="head">
        <div style={{ minWidth: 0 }}>
          <h1>
            <span className="ref">{product.sku}</span> · {product.name}
          </h1>
          <p>{!product.isActive && <Chip tone="fail">{t('warehouse.products.inactive')}</Chip>}</p>
        </div>
        <div className="act">
          <Can perm="inventory.manage">
            {product.isActive ? (
              <button type="button" className="btn danger" onClick={() => setConfirm('deactivate')}>
                {t('warehouse.products.deactivate')}
              </button>
            ) : (
              <button type="button" className="btn" onClick={() => setConfirm('reactivate')}>
                {t('warehouse.products.reactivate')}
              </button>
            )}
          </Can>
        </div>
      </div>

      <div style={{ marginBottom: 14 }}>
        <Tabs<TabKey>
          label={t('warehouse.products.title')}
          value={tab}
          onChange={setTab}
          tabs={[
            { key: 'data', label: t('warehouse.products.tabData') },
            { key: 'lots', label: t('warehouse.products.tabLots') },
            { key: 'serials', label: t('warehouse.products.tabSerials') },
          ]}
        />
      </div>

      {tab === 'data' && (
        <Panel title={t('warehouse.products.tabData')}>
          <DataTab publicId={publicId} detail={detail} />
        </Panel>
      )}
      {tab === 'lots' && (
        <Panel flush title={t('warehouse.products.tabLots')}>
          <LotsTab publicId={publicId} />
        </Panel>
      )}
      {tab === 'serials' && (
        <Panel flush title={t('warehouse.products.tabSerials')}>
          <SerialsTab publicId={publicId} />
        </Panel>
      )}

      <ConfirmDialog
        open={confirm !== null}
        tone={confirm === 'deactivate' ? 'danger' : 'flow'}
        title={confirm === 'deactivate' ? t('warehouse.products.deactivateTitle') : t('warehouse.products.reactivateTitle')}
        message={t(confirm === 'deactivate' ? 'warehouse.products.deactivateBody' : 'warehouse.products.reactivateBody', {
          name: product.name ?? '',
        })}
        confirmLabel={confirm === 'deactivate' ? t('warehouse.products.deactivate') : t('warehouse.products.reactivate')}
        onConfirm={async () => {
          await setActive.mutateAsync({ publicId, active: confirm === 'reactivate' })
          toast.success(confirm === 'deactivate' ? t('warehouse.products.deactivated') : t('warehouse.products.reactivated'))
        }}
        onClose={() => setConfirm(null)}
      />
    </div>
  )
}
