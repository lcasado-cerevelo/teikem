// Fase 5 de la reconciliación con la maqueta — modal único "Nuevo producto" / "Editar producto" (maqueta
// `renderProductModalHtml`, textos `inv2`). Sustituye al modal de alta de la lista y a la pestaña "Datos" de la ficha.
// - Campos en el orden de la maqueta: SKU (bloqueado al editar) · Unidad, Nombre, Marca · Modelo (Lote 12), Categoría ·
//   Rastreo, Dueño del inventario, Costo de compra · Precio de venta, Almacén por defecto · Posición por defecto, Total · Punto
//   de reorden y, al editar, el interruptor "Producto activo" (baja/reactivación: POST .../deactivate|reactivate) y el bloque
//   "Ajustar inventario". Unidad, Categoría y Rastreo son desplegables con buscador (`ComboSelectInput`); Marca es texto
//   libre con sugerencias de las marcas del tenant (`<datalist>` sobre GET /products/brands); Modelo, texto libre.
// - El Total se lee de la ficha en caché (`useProduct`): tras aplicar un ajuste se refresca solo.
// - Lo que la maqueta no modela pero el producto real necesita (código de barras, peso, volumen, mínimo/máximo de picking,
//   campos personalizados) va en "Más datos del producto", plegado, después de los campos de la maqueta.
// - Alta: POST /api/v1/products; edición: PATCH /api/v1/products/{publicId} con rowVersion (`inventory.manage`).
// - "Ajustar inventario" (`inventory.adjust`): oculto tras "Añadir ajuste" (Lote 12). Al abrirlo: Cantidad (+/-), Motivo
//   (con buscador, sin los motivos reservados al sistema), Almacén y Posición (por defecto los del producto) y Nota
//   obligatoria (va en `notes`). Misma mutación que InventoryAdjustModal (POST /inventory/adjustments) con el producto fijo.
//   Al aplicar, el Total se refresca y el bloque se vuelve a ocultar limpio; si el API lo rechaza (409 insufficient_stock:
//   dejaría el inventario negativo) el mensaje del servidor queda dentro del bloque, que sigue abierto.
// - "Ver lotes" / "Ver series" llevan a la vista de solo lectura de la ficha (`/warehouse/products/{id}?tab=lots|serials`).
import { zodResolver } from '@hookform/resolvers/zod'
import { useContext, useId, useMemo, useState, type ReactNode } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { Link } from 'react-router-dom'
import { z } from 'zod'
import { useCan } from '../../kernel/access'
import { useLookups } from '../../kernel/catalogs'
import { CustomFieldsForm, useSaveCustomFields } from '../../kernel/custom-fields'
import { useLang, useT } from '../../kernel/i18n'
import { ClientPickerInput, ComboSelectInput, Field, Form, Modal, NumberInput, Spinner, TextArea, TextInput, toast } from '../../kernel/ui'
import { IconCheck } from '../../kernel/ui/icons'
import { IconLayers } from '../../kernel/ui/screenIcons'
import { SessionContext } from '../../app/session'
import { selectableAdjustmentReasons } from './adjustmentReasons'
import {
  useCreateProduct,
  useInventoryAdjustment,
  useProduct,
  useProductBrands,
  useProductCategories,
  useSetProductActive,
  useUpdateProduct,
  useWarehouseBins,
  type ProductDetailDto,
  type WarehouseBinDto,
} from './api'
import { formatNumber, parseSerials } from './lineRules'
import { BinPickerInput, WarehousePickerInput } from './pickers'
import {
  ADJUST_NOTES_MAX,
  BRAND_MAX,
  MODEL_MAX,
  adjustNotesSchema,
  adjustQuantitySchema,
  brandModelSchema,
  moneySchema,
  volumeM3Schema,
  weightKgSchema,
} from './productRules'
import './warehouse.css'

const PICKING_ZONE = 'PICKING'
const DEFAULT_UOM = 'UN'
const DEFAULT_TRACKING = 'NONE'
// eslint-disable-next-line no-control-regex -- intencional: el SKU no admite caracteres de control (manual §2).
const CONTROL_CHARS_OR_SPACES = /[\s\x00-\x1F\x7F]/
/** Campos de "Más datos del producto": si alguno trae error, el bloque se abre solo. */
const MORE_FIELDS = ['barcode', 'weightKg', 'volumeM3', 'minPickQty', 'maxPickQty', 'customFields'] as const

export interface ProductEditorModalProps {
  open: boolean
  /** null = alta ("Nuevo producto"); la ficha (`GET /products/{publicId}`) = edición ("Editar producto"). */
  product: ProductDetailDto | null
  onClose: () => void
  /** Tras el alta, con el publicId del producto nuevo (el modal ya se cerró). */
  onCreated?: (publicId: string) => void
}

/** Modal único de alta y edición de producto. Sin `inventory.manage` la edición es de solo lectura. */
export function ProductEditorModal(props: ProductEditorModalProps) {
  if (!props.open) return null
  // se monta al abrir: el formulario nace con los valores de la ficha de ese momento
  return <ProductEditorDialog key={props.product?.product?.publicId ?? 'new'} {...props} />
}

/** El mismo modal en edición a partir del publicId (pide la ficha); `publicId` null = cerrado. */
export function ProductEditorByIdModal({ publicId, onClose }: { publicId: string | null; onClose: () => void }) {
  const t = useT()
  const { data, isLoading, error } = useProduct(publicId, { handleAccessDenied: false })
  if (!publicId) return null
  if (data?.product) return <ProductEditorModal open product={data} onClose={onClose} />
  return (
    <Modal open title={<ModalTitle text={t('warehouse.products.editor.editTitle')} />} size="sm" onClose={onClose}>
      {isLoading || !error ? (
        <Spinner block />
      ) : (
        <p className="ferr" role="alert">
          {t('warehouse.products.editor.loadError')}
        </p>
      )}
    </Modal>
  )
}

function ModalTitle({ text }: { text: string }) {
  return (
    <span className="pe-title">
      <IconLayers />
      {text}
    </span>
  )
}

function ProductEditorDialog({ product: detail, onClose, onCreated }: ProductEditorModalProps) {
  const t = useT()
  const isEdit = detail !== null
  const canManage = useCan('inventory.manage')
  const canAdjust = useCan('inventory.adjust')
  const trackingTypes = useLookups('TrackingType')
  const uoms = useLookups('UnitOfMeasure')
  const categories = useProductCategories()
  const [busy, setBusy] = useState(false)
  const titleKey = !isEdit ? 'newTitle' : canManage ? 'editTitle' : 'viewData'
  const title = <ModalTitle text={t(`warehouse.products.editor.${titleKey}`)} />

  // las listas se esperan antes de montar el formulario: un <select> sin sus opciones no muestra el valor inicial
  if (trackingTypes.isLoading || uoms.isLoading || categories.isLoading) {
    return (
      <Modal open title={title} size="sm" onClose={onClose}>
        <Spinner block />
      </Modal>
    )
  }

  return (
    <ProductEditorForm
      detail={detail}
      title={title}
      editable={!isEdit || canManage}
      canAdjust={isEdit && canAdjust}
      trackingCodes={(trackingTypes.data ?? []).map((o) => ({ value: o.code, label: o.label }))}
      uomCodes={(uoms.data ?? []).map((o) => ({ value: o.code, label: o.label }))}
      // la ruta completa ("Raíz / Hija") como etiqueta: se busca por cualquier nivel y no hay dos iguales
      categoryOptions={(categories.data ?? [])
        .filter((c) => c.isActive || c.id === detail?.product?.categoryId)
        .map((c) => ({ value: String(c.id), label: c.path || c.name || '' }))}
      busy={busy}
      setBusy={setBusy}
      onClose={onClose}
      onCreated={onCreated}
    />
  )
}

interface Option {
  value: string
  label: string
}

interface ProductEditorFormProps {
  detail: ProductDetailDto | null
  title: ReactNode
  editable: boolean
  canAdjust: boolean
  trackingCodes: Option[]
  uomCodes: Option[]
  categoryOptions: Option[]
  busy: boolean
  setBusy: (v: boolean) => void
  onClose: () => void
  onCreated?: (publicId: string) => void
}

function ProductEditorForm({
  detail,
  title,
  editable,
  canAdjust,
  trackingCodes,
  uomCodes,
  categoryOptions,
  busy,
  setBusy,
  onClose,
  onCreated,
}: ProductEditorFormProps) {
  const t = useT()
  const lang = useLang()
  const formId = useId()
  const totalId = useId()
  const isEdit = detail !== null
  const product = detail?.product ?? null
  const publicId = product?.publicId ?? ''
  const create = useCreateProduct()
  const update = useUpdateProduct()
  const setActive = useSetProductActive()
  const { save: saveCustomFields } = useSaveCustomFields('PRODUCT')
  const [moreOpen, setMoreOpen] = useState(false)
  const [pickedBin, setPickedBin] = useState<WarehouseBinDto | null>(null)
  // la ficha en caché (misma clave que ProductEditorByIdModal): tras un ajuste se invalida y el Total se refresca solo
  const live = useProduct(isEdit ? publicId : null, { handleAccessDenied: false })
  const { data: brands = [] } = useProductBrands('', { enabled: editable })
  const brandListId = `${formId}-brands`
  // "Propio — <compañía>" como la maqueta (sin sesión, p. ej. en pruebas, solo "Propio")
  const tenantName = useContext(SessionContext)?.me?.tenantName
  const ownLabel = tenantName ? `${t('warehouse.products.own')} — ${tenantName}` : t('warehouse.products.own')
  const hasMovements = detail?.hasMovements === true
  const onHand = live.data?.product?.qtyOnHand ?? product?.qtyOnHand ?? 0
  // el API no deja dar de baja con saldo en mano (409 DeactivateWithStock): el interruptor se bloquea con la nota de la maqueta
  const hasStock = onHand !== 0
  const activeLocked = product?.isActive === true && hasStock
  const hasTrackingOption = (code: string) => trackingCodes.some((o) => o.value === code)
  const hasUomOption = (code: string) => uomCodes.some((o) => o.value === code)

  const schema = useMemo(
    () =>
      z
        .object({
          sku: isEdit
            ? z.string()
            : z
                .string()
                .trim()
                .min(1, t('warehouse.products.errors.skuRequired'))
                .max(60, t('warehouse.products.errors.skuMax'))
                .refine((v) => !CONTROL_CHARS_OR_SPACES.test(v), t('warehouse.products.errors.skuChars')),
          baseUom: z.string(),
          name: z.string().trim().min(1, t('warehouse.products.errors.nameRequired')).max(200, t('warehouse.products.errors.nameMax')),
          brand: brandModelSchema(t, 'brand'),
          model: brandModelSchema(t, 'model'),
          categoryId: z.string(),
          trackingType: z.string(),
          ownerClientPublicId: z.string().nullable(),
          purchaseCost: moneySchema(t, 'cost'),
          salePrice: moneySchema(t, 'price'),
          preferredWarehousePublicId: z.string().nullable(),
          preferredBinId: z.string(),
          minQty: z.number(t('warehouse.products.errors.numberInvalid')).min(0, t('warehouse.products.errors.minNegative')).nullable(),
          isActive: z.boolean(),
          barcode: z.string().trim().max(60, t('warehouse.products.errors.barcodeMax')),
          weightKg: weightKgSchema(t),
          volumeM3: volumeM3Schema(t),
          minPickQty: z.number(t('warehouse.products.errors.numberInvalid')).min(0, t('warehouse.products.errors.minNegative')).nullable(),
          maxPickQty: z.number(t('warehouse.products.errors.numberInvalid')).min(0, t('warehouse.products.errors.minNegative')).nullable(),
        })
        .refine((v) => v.maxPickQty == null || v.minPickQty == null || v.maxPickQty >= v.minPickQty, {
          path: ['maxPickQty'],
          message: t('warehouse.products.errors.maxPickLtMin'),
        }),
    [t, isEdit],
  )

  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: {
      sku: product?.sku ?? '',
      baseUom: product?.baseUomCode ?? (hasUomOption(DEFAULT_UOM) ? DEFAULT_UOM : ''),
      name: product?.name ?? '',
      brand: product?.brand ?? '',
      model: product?.model ?? '',
      categoryId: product?.categoryId != null ? String(product.categoryId) : '',
      trackingType: product?.trackingTypeCode ?? (hasTrackingOption(DEFAULT_TRACKING) ? DEFAULT_TRACKING : ''),
      ownerClientPublicId: product?.ownerClientPublicId ?? null,
      purchaseCost: product?.purchaseCost ?? null,
      salePrice: product?.salePrice ?? null,
      preferredWarehousePublicId: detail?.preferredWarehousePublicId ?? null,
      preferredBinId: detail?.preferredBinId != null ? String(detail.preferredBinId) : '',
      minQty: product?.minQty ?? null,
      isActive: product?.isActive ?? true,
      barcode: product?.barcode ?? '',
      weightKg: detail?.weightKg ?? null,
      volumeM3: detail?.volumeM3 ?? null,
      minPickQty: detail?.minPickQty ?? null,
      maxPickQty: detail?.maxPickQty ?? null,
    },
  })

  const preferredWarehousePublicId = useWatch({ control: form.control, name: 'preferredWarehousePublicId' })
  const preferredBinId = useWatch({ control: form.control, name: 'preferredBinId' })
  // Solo hace falta el tipo de zona de la posición por defecto ya guardada (la elegida en el picker llega en `pickedBin`):
  // se pide esa posición por id; el listado del almacén llega paginado y ya no trae todas.
  const preferredBinNumber = preferredBinId ? Number(preferredBinId) : NaN
  const hasPreferredBin = Boolean(preferredWarehousePublicId) && Number.isInteger(preferredBinNumber) && !(pickedBin && pickedBin.id === preferredBinNumber)
  const { data: preferredBinPage } = useWarehouseBins(
    preferredWarehousePublicId,
    { binIds: hasPreferredBin ? [preferredBinNumber] : undefined, includeInactive: true, take: 1 },
    { enabled: hasPreferredBin, handleAccessDenied: false },
  )
  const bins = preferredBinPage?.items ?? []
  const errors = form.formState.errors
  const moreHasError = MORE_FIELDS.some((k) => k in errors)
  const submitting = form.formState.isSubmitting || busy

  // un solo aviso (bajo Dueño) para los tres campos que el API bloquea con movimientos: la maqueta no lo tiene y así no crece el modal
  const lockHelp = isEdit && hasMovements ? t('warehouse.products.editor.lockedByMovements') : undefined
  const nonePlaceholder = t('warehouse.products.fields.none')

  /** Réplica de la regla del servidor: con mínimo de picking, la posición por defecto debe estar en una zona PICKING. */
  function pickZoneViolated(v: { minPickQty?: number | null; preferredBinId: string }): boolean {
    if (v.minPickQty == null || !v.preferredBinId) return false
    const bin = pickedBin && String(pickedBin.id) === v.preferredBinId ? pickedBin : bins.find((b) => String(b.id) === v.preferredBinId)
    return Boolean(bin && bin.zoneTypeCode !== PICKING_ZONE)
  }

  async function submit(v: z.output<typeof schema>) {
    if (pickZoneViolated(v)) {
      form.setError('preferredBinId', { type: 'server', message: t('warehouse.products.errors.pickZoneRequired') })
      return
    }
    if (!isEdit) {
      const created = await create.mutateAsync({
        sku: v.sku,
        name: v.name,
        baseUom: v.baseUom || null,
        categoryId: v.categoryId ? Number(v.categoryId) : null,
        trackingType: v.trackingType || null,
        ownerClientPublicId: v.ownerClientPublicId || null,
        purchaseCost: v.purchaseCost,
        salePrice: v.salePrice,
        preferredWarehousePublicId: v.preferredWarehousePublicId || null,
        preferredBinId: v.preferredWarehousePublicId && v.preferredBinId ? Number(v.preferredBinId) : null,
        minQty: v.minQty,
        barcode: v.barcode || null,
        weightKg: v.weightKg,
        volumeM3: v.volumeM3,
        minPickQty: v.minPickQty,
        maxPickQty: v.maxPickQty,
        brand: v.brand || null,
        model: v.model || null,
      })
      const id = created.product?.id
      if (typeof id === 'number' && id > 0) {
        const problem = await saveCustomFields(id, form)
        if (problem) toast.error(problem.title)
      }
      toast.success(t('warehouse.products.created'))
      onClose()
      const newId = created.product?.publicId
      if (newId) onCreated?.(newId)
      return
    }

    // copia: el reset de más abajo reemplaza dirtyFields
    const dirty: Partial<Record<string, unknown>> = { ...form.formState.dirtyFields }
    const dataDirty = Object.keys(dirty).some((k) => k !== 'isActive' && k !== 'customFields')
    const activeChanged = product !== null && v.isActive !== (product.isActive ?? true)
    // reactivar va primero (luego se guardan los datos); dar de baja va al final, tras guardar los datos
    if (activeChanged && v.isActive) await setActive.mutateAsync({ publicId, active: true })
    if (dataDirty) {
      await update.mutateAsync({
        publicId,
        body: {
          name: v.name,
          barcode: v.barcode || null,
          clearBarcode: v.barcode.trim() === '' ? true : null,
          categoryId: v.categoryId ? Number(v.categoryId) : null,
          clearCategory: v.categoryId ? null : true,
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
          preferredBinId: v.preferredWarehousePublicId && v.preferredBinId ? Number(v.preferredBinId) : null,
          clearPreferred: dirty.preferredWarehousePublicId && !v.preferredWarehousePublicId ? true : null,
          // PATCH: null = sin cambio, '' = quitar (solo se mandan si cambiaron)
          brand: dirty.brand ? v.brand : null,
          model: dirty.model ? v.model : null,
          rowVersion: detail?.rowVersion,
        },
      })
      // los datos ya quedaron: si la baja de abajo falla, un segundo "Guardar" no los vuelve a mandar
      form.reset({ ...form.getValues(), isActive: product?.isActive ?? true }, { keepErrors: true })
      if (activeChanged) form.setValue('isActive', v.isActive, { shouldDirty: true })
    }
    if (dirty.customFields && typeof product?.id === 'number' && product.id > 0) {
      const problem = await saveCustomFields(product.id, form)
      if (problem) {
        toast.error(problem.title)
        return
      }
    }
    if (activeChanged && !v.isActive) await setActive.mutateAsync({ publicId, active: false })
    if (dataDirty || activeChanged || dirty.customFields) {
      toast.success(
        activeChanged && !dataDirty
          ? t(v.isActive ? 'warehouse.products.reactivated' : 'warehouse.products.deactivated')
          : t('warehouse.products.editor.updated'),
      )
    }
    onClose()
  }

  const footer = editable ? (
    <>
      <button type="button" className="btn" onClick={onClose} disabled={submitting}>
        {t('warehouse.products.editor.cancel')}
      </button>
      <button type="submit" form={formId} className="btn flow" disabled={submitting}>
        <IconCheck />
        {form.formState.isSubmitting ? t('common.loading') : t('warehouse.products.editor.save')}
      </button>
    </>
  ) : (
    <button type="button" className="btn" onClick={onClose} disabled={busy}>
      {t('warehouse.products.editor.close')}
    </button>
  )

  return (
    <Modal open title={title} size="sm" onClose={onClose} dismissible={!submitting} footer={footer}>
      <Form id={formId} form={form} onSubmit={submit}>
        <fieldset className="pe-fields" disabled={!editable}>
          <div className="r2">
            {isEdit ? (
              <div className="f">
                <label htmlFor={`${formId}-sku`}>{t('warehouse.products.editor.sku')}</label>
                <input id={`${formId}-sku`} className="mono" value={product?.sku ?? ''} disabled readOnly />
              </div>
            ) : (
              <Field name="sku" label={t('warehouse.products.editor.sku')} required>
                <TextInput className="mono" maxLength={60} placeholder="SKU-000" autoComplete="off" />
              </Field>
            )}
            <Field name="baseUom" label={t('warehouse.products.editor.uom')}>
              <ComboSelectInput options={uomCodes} placeholder={nonePlaceholder} disabled={isEdit && hasMovements} />
            </Field>
          </div>
          <Field name="name" label={t('warehouse.products.editor.name')} required>
            <TextInput maxLength={200} />
          </Field>
          <div className="r2">
            <Field name="brand" label={t('warehouse.products.editor.brand')}>
              <TextInput maxLength={BRAND_MAX} list={brandListId} autoComplete="off" placeholder={t('warehouse.products.editor.brandPlaceholder')} />
            </Field>
            <Field name="model" label={t('warehouse.products.editor.model')}>
              <TextInput maxLength={MODEL_MAX} autoComplete="off" />
            </Field>
          </div>
          {/* sugerencias de las marcas ya usadas en la compañía; se puede escribir una nueva */}
          <datalist id={brandListId}>
            {brands.map((b) => (
              <option key={b} value={b} />
            ))}
          </datalist>
          <div className="r2">
            <Field name="categoryId" label={t('warehouse.products.editor.category')}>
              <ComboSelectInput options={categoryOptions} placeholder={nonePlaceholder} />
            </Field>
            <Field name="trackingType" label={t('warehouse.products.editor.tracking')}>
              <ComboSelectInput options={trackingCodes} placeholder={nonePlaceholder} disabled={isEdit && hasMovements} />
            </Field>
          </div>
          <Field name="ownerClientPublicId" label={t('warehouse.products.editor.owner')} help={lockHelp}>
            <ClientPickerInput placeholder={ownLabel} disabled={isEdit && hasMovements} />
          </Field>
          <div className="r2">
            <Field name="purchaseCost" label={t('warehouse.products.editor.purchaseCost')}>
              <NumberInput className="mono" min={0} />
            </Field>
            <Field name="salePrice" label={t('warehouse.products.editor.salePrice')}>
              <NumberInput className="mono" min={0} />
            </Field>
          </div>
          <div className="r2">
            <Field name="preferredWarehousePublicId" label={t('warehouse.products.editor.preferredWarehouse')}>
              <WarehousePickerInput placeholder={t('warehouse.products.fields.none')} />
            </Field>
            <Field name="preferredBinId" label={t('warehouse.products.editor.preferredBin')}>
              <BinPickerInput warehousePublicId={preferredWarehousePublicId} placeholder={t('warehouse.products.fields.none')} onPicked={setPickedBin} />
            </Field>
          </div>
          <div className="r2">
            <div className="f">
              <label htmlFor={totalId}>
                {t('warehouse.products.editor.total')}{' '}
                <span className="pe-faint">
                  ({isEdit ? t('warehouse.products.editor.useAdjust') : t('warehouse.products.editor.totalOnCreate')})
                </span>
              </label>
              <input id={totalId} className="mono" value={formatNumber(onHand, lang)} disabled readOnly />
            </div>
            <Field name="minQty" label={t('warehouse.products.editor.reorder')}>
              <NumberInput className="mono" min={0} />
            </Field>
          </div>
          {isEdit && (
            <div className="pe-active">
              <label className="sw">
                <input type="checkbox" role="switch" disabled={activeLocked} {...form.register('isActive')} />
                <span className="tk" aria-hidden="true" />
                <span>{t('warehouse.products.editor.activeLabel')}</span>
              </label>
              {activeLocked && <div className="note">{t('warehouse.products.editor.hasStockNote')}</div>}
            </div>
          )}
          <details className="pe-more" open={moreOpen || moreHasError} onToggle={(e) => setMoreOpen(e.currentTarget.open)}>
            <summary>{t('warehouse.products.editor.more')}</summary>
            <div className="r2">
              <Field name="barcode" label={t('warehouse.products.fields.barcode')}>
                <TextInput maxLength={60} />
              </Field>
              <div aria-hidden="true" />
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
              <Field name="minPickQty" label={t('warehouse.products.fields.minPickQty')}>
                <NumberInput min={0} />
              </Field>
              <Field name="maxPickQty" label={t('warehouse.products.fields.maxPickQty')}>
                <NumberInput min={0} />
              </Field>
            </div>
            <CustomFieldsForm entityType="PRODUCT" entityId={isEdit ? product?.id : undefined} form={form} disabled={!editable} />
          </details>
        </fieldset>
      </Form>

      {detail && canAdjust && product && <AdjustBlock detail={detail} setBusy={setBusy} disabled={submitting} />}

      {isEdit && product && (product.trackingTypeCode === 'LOT' || product.trackingTypeCode === 'SERIAL') && (
        <div className="pe-trace">
          <span className="pe-section">{t('warehouse.products.editor.traceTitle')}</span>
          <Link className="btn sm" to={`/warehouse/products/${publicId}?tab=lots`} onClick={onClose}>
            {t('warehouse.products.editor.viewLots')}
          </Link>
          {product.trackingTypeCode === 'SERIAL' && (
            <Link className="btn sm" to={`/warehouse/products/${publicId}?tab=serials`} onClick={onClose}>
              {t('warehouse.products.editor.viewSerials')}
            </Link>
          )}
        </div>
      )}
    </Modal>
  )
}

// ---- Bloque "Ajustar inventario" (maqueta: Cantidad (+/-) · Motivo · "Aplicar ajuste"; Lote 12: oculto tras "Añadir ajuste") ----
function AdjustBlock({ detail, setBusy, disabled }: { detail: ProductDetailDto; setBusy: (v: boolean) => void; disabled: boolean }) {
  const t = useT()
  const [open, setOpen] = useState(false)
  if (!open) {
    return (
      <div className="pe-adjust pe-adjust-closed">
        <button type="button" className="btn sm" onClick={() => setOpen(true)} disabled={disabled}>
          <span aria-hidden="true">+</span> {t('warehouse.products.editor.addAdjust')}
        </button>
      </div>
    )
  }
  // el formulario se monta al abrir y se desmonta al aplicar o cancelar: vuelve a abrirse limpio
  return <AdjustForm detail={detail} setBusy={setBusy} disabled={disabled} onDone={() => setOpen(false)} />
}

function AdjustForm({
  detail,
  setBusy,
  disabled,
  onDone,
}: {
  detail: ProductDetailDto
  setBusy: (v: boolean) => void
  disabled: boolean
  onDone: () => void
}) {
  const t = useT()
  const lang = useLang()
  const product = detail.product!
  const adjust = useInventoryAdjustment()
  const reasonsQ = useLookups('AdjustmentReason')
  const reasonOptions = useMemo(() => selectableAdjustmentReasons(reasonsQ.data ?? []).map((r) => ({ value: r.code, label: r.label })), [reasonsQ.data])
  const tracking = product.trackingTypeCode ?? ''

  const schema = useMemo(
    () =>
      z.object({
        quantity: adjustQuantitySchema(t),
        reason: z.string().min(1, t('warehouse.inventory.adjustModal.errors.reasonRequired')),
        warehousePublicId: z
          .string()
          .nullable()
          .refine((v) => Boolean(v), t('warehouse.inventory.adjustModal.errors.warehouseRequired')),
        binId: z.string().min(1, t('warehouse.inventory.adjustModal.errors.binRequired')),
        lotNumber: z.string(),
        serialNumbers: z.string(),
        notes: adjustNotesSchema(t),
      }),
    [t],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: {
      quantity: null as number | null,
      reason: '',
      // por defecto, el almacén y la posición por defecto del producto (los guardados, no los que se estén editando)
      warehousePublicId: detail.preferredWarehousePublicId ?? null,
      binId: detail.preferredBinId != null ? String(detail.preferredBinId) : '',
      lotNumber: '',
      serialNumbers: '',
      notes: '',
    },
  })
  const warehousePublicId = useWatch({ control: form.control, name: 'warehousePublicId' })
  const submitting = form.formState.isSubmitting

  return (
    <Form
      form={form}
      className="pe-adjust"
      onSubmit={async (v) => {
        setBusy(true)
        try {
          const serials = parseSerials(v.serialNumbers)
          // 409 insufficient_stock (dejaría el inventario negativo): `Form` pone el mensaje del servidor arriba de este bloque
          await adjust.mutateAsync({
            productPublicId: product.publicId,
            warehousePublicId: v.warehousePublicId,
            binId: Number(v.binId),
            quantity: v.quantity,
            reason: v.reason,
            notes: v.notes,
            lot: tracking === 'LOT' && v.lotNumber.trim() ? { number: v.lotNumber.trim() } : undefined,
            serialNumbers: tracking === 'SERIAL' && serials.length > 0 ? serials : undefined,
          })
          const q = v.quantity ?? 0
          toast.success(t('warehouse.products.editor.adjustApplied', { qty: `${q > 0 ? '+' : ''}${formatNumber(q, lang)}`, sku: product.sku ?? '' }))
          // la mutación ya refrescó la ficha (Total); el modal sigue abierto y el bloque se oculta limpio
          onDone()
        } finally {
          setBusy(false)
        }
      }}
    >
      <h3 className="pe-section">{t('warehouse.products.editor.adjustTitle')}</h3>
      <div className="r2">
        <Field name="quantity" label={t('warehouse.products.editor.adjustDelta')} required>
          <NumberInput className="mono" step="0.001" />
        </Field>
        <Field name="reason" label={t('warehouse.products.editor.adjustReason')} required>
          <ComboSelectInput options={reasonOptions} loading={reasonsQ.isLoading} />
        </Field>
      </div>
      <div className="r2">
        <Field name="warehousePublicId" label={t('warehouse.inventory.adjustModal.fields.warehouse')} required>
          <WarehousePickerInput />
        </Field>
        <Field name="binId" label={t('warehouse.inventory.adjustModal.fields.bin')} required>
          <BinPickerInput warehousePublicId={warehousePublicId} />
        </Field>
      </div>
      {tracking === 'LOT' && (
        <Field name="lotNumber" label={t('warehouse.inventory.adjustModal.fields.lotNumber')}>
          <TextInput maxLength={60} />
        </Field>
      )}
      {tracking === 'SERIAL' && (
        <Field name="serialNumbers" label={t('warehouse.inventory.adjustModal.fields.serialNumbers')}>
          <TextArea rows={3} />
        </Field>
      )}
      <Field name="notes" label={t('warehouse.products.editor.adjustNotes')} required>
        <TextArea rows={2} maxLength={ADJUST_NOTES_MAX} placeholder={t('warehouse.products.editor.adjustNotesPlaceholder')} />
      </Field>
      <div className="pe-adjust-actions">
        <button type="button" className="btn sm" onClick={onDone} disabled={submitting}>
          {t('warehouse.products.editor.cancelAdjust')}
        </button>
        <button type="submit" className="btn sm flow pe-apply" disabled={disabled || submitting}>
          <IconCheck /> {submitting ? t('common.loading') : t('warehouse.products.editor.applyAdjust')}
        </button>
      </div>
    </Form>
  )
}
