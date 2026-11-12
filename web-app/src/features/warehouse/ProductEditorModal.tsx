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
//   obligatoria (va en `notes`). Lote 14 (D11): "Subir"/"Bajar" + cantidad positiva y motivos según la dirección, con las
//   mismas piezas que InventoryAdjustModal (`useAdjustmentForm` + `AdjustmentFields`, POST /inventory/adjustments) y el
//   producto fijo.
//   Al aplicar, el Total se refresca y el bloque se vuelve a ocultar limpio; si el API lo rechaza (409 insufficient_stock:
//   dejaría el inventario negativo) el mensaje del servidor queda dentro del bloque, que sigue abierto.
// - "Ver lotes" / "Ver series" llevan a la vista de solo lectura de la ficha (`/warehouse/products/{id}?tab=lots|serials`).
// - Lote 230: ventana ancha (`xl`) con los campos en bloques con título —Identificación, Inventario, Precios, Medidas y peso— en una
//   rejilla de 4 columnas en escritorio (3, 2 y UNA a 360 px; `.pe-grid`); "Más datos del producto" desapareció (código de barras y
//   mínimos de picking van en Inventario; peso y volumen en Medidas y peso; los campos personalizados al final). La ayuda larga
//   (empaque, rotación) va plegada en el «?» de la etiqueta (`Field tip`). La categoría se ve como ruta «Categoría → Subcategoría».
//   Medidas y peso: largo, ancho, alto y su unidad (IN, CM, FT, M; la del producto o la sugerida por la región con
//   GET /products/units-suggestion); con las tres el volumen se calcula y no se edita, si falta alguna se escribe a mano (en la
//   unidad cúbica, viaja en m³); peso con su unidad (LB/KG). Reglas puras en `productMeasures.ts`.
// - Lote 232 (kit ARMADO): el alta pregunta arriba «¿Lo que va a crear es un kit?» (No por omisión, campo `kitChoice`); con «Sí» aparecen ahí mismo
//   la composición y «¿Cuántos kits arma ahora?» (con `inventory.adjust`: cantidad, almacén y posición; «se pueden armar N»), el rastreo y el costo de
//   compra pasan a informativos (los deciden las piezas) y viajan `isAssembledKit`, `kitPieces` y `assemble*` (AssembledKitEditor.tsx). En edición de
//   un kit armado: la composición (editable mientras el servidor no la bloquee; se guarda con PUT …/assembled-kit), rastreo y dueño fijos, y el acceso
//   a «Armar y desarmar». Un producto normal no ve nada de esto.
import { zodResolver } from '@hookform/resolvers/zod'
import { useQueryClient } from '@tanstack/react-query'
import { useContext, useEffect, useId, useMemo, useState, type ReactNode } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { Link } from 'react-router-dom'
import { z } from 'zod'
import { useCan, useModule } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useLookups, useTenantSettings } from '../../kernel/catalogs'
import { CustomFieldsForm, useSaveCustomFields } from '../../kernel/custom-fields'
import { useFormat } from '../../kernel/format'
import { useLang, useT } from '../../kernel/i18n'
import { ClientPickerInput, ComboSelectInput, Field, Form, Modal, NumberInput, Select, Spinner, TextInput, toast, type SelectOption } from '../../kernel/ui'
import { IconCheck } from '../../kernel/ui/icons'
import { IconLayers } from '../../kernel/ui/screenIcons'
import { SessionContext } from '../../app/session'
import {
  invalidateStock,
  useCreateProduct,
  useProduct,
  useProductBrands,
  useProductCategories,
  useSetProductActive,
  useSetProductTaxCategory,
  useInventoryBalances,
  useUnitsSuggestion,
  useUpdateProduct,
  useWarehouseBins,
  type ProductDetailDto,
  type WarehouseBinDto,
} from './api'
import { useAdjustmentForm, useSubmitAdjustment } from './adjustmentForm'
import { AdjustmentFields } from './InventoryAdjustModal'
import { EditKitBlock, KitInfoField, KitQuestion, NewKitBlock } from './AssembledKitEditor'
import { trackingOfPieces, useEditKitComposition, usePieceInfos, usePiecesProblemText } from './assembledKitHooks'
import { useSetKitComposition } from './assembledKitApi'
import { assembledKitCreateFields, assembledPiecesInput, assembledPiecesProblem, kitQuantityProblem } from './assembledKitRules'
import { newKitPiece, type KitPiece } from './kitPieces'
import { formatNumber } from './lineRules'
import { BinPickerInput, WarehousePickerInput } from './pickers'
import { ProductPhotoBlock } from './ProductPhotoBlock'
import { useProductPhotosEnabled } from './StoredImage'
import { BRAND_MAX, MODEL_MAX, brandModelSchema, moneySchema } from './productRules'
import {
  DIMENSION_UNITS,
  WEIGHT_UNITS,
  dimensionSchema,
  manualVolumeSchema,
  measuresCreateBody,
  measuresDefaults,
  measuresIssues,
  measuresPatchBody,
  volumeView,
  weightSchema,
  type UnitsSuggestionLike,
} from './productMeasures'
import './warehouse.css'

const PICKING_ZONE = 'PICKING'
const AK = 'warehouse.products.assembled'
const DEFAULT_UOM = 'UN'
/** Lote 237: categoría de impuesto del producto (las mismas de Contabilidad → Impuestos → Productos). */
const TAX_CATEGORIES = ['TAXABLE', 'EXEMPT', 'ZERO_RATE'] as const
const DEFAULT_TRACKING = 'NONE'
// eslint-disable-next-line no-control-regex -- intencional: el SKU no admite caracteres de control (manual §2).
const CONTROL_CHARS_OR_SPACES = /[\s\x00-\x1F\x7F]/
/** Sin piezas (producto normal): referencia estable para los hooks. */
const NO_PIECES: KitPiece[] = []
/** Unidad cúbica (IN3…) como se escribe en pantalla. */
const CUBIC_LABEL: Record<string, string> = { IN3: 'in³', CM3: 'cm³', FT3: 'ft³', M3: 'm³' }
/** Ruta de la categoría «Raíz / Hija» del API como «Raíz → Hija». */
const categoryPathLabel = (path: string) => path.split(' / ').join(' → ')

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
    <Modal open title={<ModalTitle text={t('warehouse.products.editor.editTitle')} />} size="xl" onClose={onClose}>
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
  // Lote 230: la unidad sugerida de medidas y peso (región de la compañía); si no llega, las de Puerto Rico
  const suggestion = useUnitsSuggestion()
  const [busy, setBusy] = useState(false)
  const titleKey = !isEdit ? 'newTitle' : canManage ? 'editTitle' : 'viewData'
  const title = <ModalTitle text={t(`warehouse.products.editor.${titleKey}`)} />

  // las listas se esperan antes de montar el formulario: un <select> sin sus opciones no muestra el valor inicial
  if (trackingTypes.isLoading || uoms.isLoading || categories.isLoading || suggestion.isLoading) {
    return (
      <Modal open title={title} size="xl" onClose={onClose}>
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
        .map((c) => ({ value: String(c.id), label: categoryPathLabel(c.path || c.name || '') }))}
      // 2026-10-11: un producto NUEVO llega con la categoría por defecto de la compañía (Ajustes → Operación); al editar no se toca
      defaultCategoryId={isEdit ? undefined : (categories.data ?? []).find((c) => c.isDefault && c.isActive)?.id}
      suggestion={suggestion.data}
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
  /** Categoría con la que arranca un producto nuevo (la de los ajustes de la compañía, si está activa). */
  defaultCategoryId?: number
  /** Lote 230: unidades sugeridas por la región (GET /products/units-suggestion). */
  suggestion?: UnitsSuggestionLike
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
  defaultCategoryId,
  suggestion,
  busy,
  setBusy,
  onClose,
  onCreated,
}: ProductEditorFormProps) {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const formId = useId()
  const totalId = useId()
  const isEdit = detail !== null
  const product = detail?.product ?? null
  const publicId = product?.publicId ?? ''
  const create = useCreateProduct()
  const update = useUpdateProduct()
  const setActive = useSetProductActive()
  // Lote 237: campo «Impuesto» (bloque Precios) — solo con el módulo de Impuestos; lo cambia quien tiene tax.manage (los demás lo ven en solo lectura)
  const setTaxCategory = useSetProductTaxCategory()
  const taxModule = useModule('TAX')
  const canManageTax = useCan('tax.manage')
  const taxEditable = editable && taxModule && canManageTax
  const taxCategoryOptions = useMemo<SelectOption[]>(() => TAX_CATEGORIES.map((c) => ({ value: c, label: t(`tax.categories.${c}`) })), [t])
  const { save: saveCustomFields } = useSaveCustomFields('PRODUCT')
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
          rotationMethod: z.string(),
          model: brandModelSchema(t, 'model'),
          packUom: z.string(),
          packQty: z.number(t('warehouse.products.errors.numberInvalid')).nullable(),
          categoryId: z.string(),
          trackingType: z.string(),
          ownerClientPublicId: z.string().nullable(),
          purchaseCost: moneySchema(t, 'cost'),
          salePrice: moneySchema(t, 'price'),
          taxCategory: z.string(),
          preferredWarehousePublicId: z.string().nullable(),
          preferredBinId: z.string(),
          minQty: z.number(t('warehouse.products.errors.numberInvalid')).min(0, t('warehouse.products.errors.minNegative')).nullable(),
          isActive: z.boolean(),
          barcode: z.string().trim().max(60, t('warehouse.products.errors.barcodeMax')),
          // Lote 230: medidas y peso con su unidad (productMeasures.ts)
          length: dimensionSchema(t, 'length'),
          width: dimensionSchema(t, 'width'),
          height: dimensionSchema(t, 'height'),
          dimensionUnit: z.string(),
          volume: manualVolumeSchema(t),
          weight: weightSchema(t),
          weightUnit: z.string(),
          minPickQty: z.number(t('warehouse.products.errors.numberInvalid')).min(0, t('warehouse.products.errors.minNegative')).nullable(),
          maxPickQty: z.number(t('warehouse.products.errors.numberInvalid')).min(0, t('warehouse.products.errors.minNegative')).nullable(),
          // Lote 232: ¿es un kit armado? (solo en el alta) y cuántos armar al crearlo
          kitChoice: z.enum(['no', 'yes']),
          assembleQuantity: z.number(t(`${AK}.err.kitQtyInvalid`)).nullable(),
          assembleWarehousePublicId: z.string().nullable(),
          assembleToBinId: z.string(),
        })
        .superRefine((v, ctx) => {
          // Lote 232: armar al crear = cantidad entera (1 a 100,000), almacén y posición destino
          if (v.kitChoice === 'yes' && v.assembleQuantity != null) {
            const qp = kitQuantityProblem(v.assembleQuantity)
            if (qp) ctx.addIssue({ code: 'custom', path: ['assembleQuantity'], message: t(`${AK}.err.${qp}`) })
            if (!v.assembleWarehousePublicId) ctx.addIssue({ code: 'custom', path: ['assembleWarehousePublicId'], message: t(`${AK}.err.warehouseRequired`) })
            if (!v.assembleToBinId) ctx.addIssue({ code: 'custom', path: ['assembleToBinId'], message: t(`${AK}.err.createBinRequired`) })
          }
          // empaque: unidad y cantidad van juntas, la cantidad es mayor que 0 y la unidad no es la base
          if (v.packUom && v.packQty == null) ctx.addIssue({ code: 'custom', path: ['packQty'], message: t('warehouse.products.errors.packUnitWithoutQty') })
          if (!v.packUom && v.packQty != null) ctx.addIssue({ code: 'custom', path: ['packUom'], message: t('warehouse.products.errors.packQtyWithoutUnit') })
          if (v.packQty != null && !(v.packQty > 0)) ctx.addIssue({ code: 'custom', path: ['packQty'], message: t('warehouse.products.errors.packQtyInvalid') })
          if (v.packUom && v.packUom === v.baseUom) ctx.addIssue({ code: 'custom', path: ['packUom'], message: t('warehouse.products.errors.packSameAsBase') })
          // el volumen (calculado o manual, en m³) y el peso (en kg) deben caber, según la unidad
          for (const issue of measuresIssues(v, t)) ctx.addIssue({ code: 'custom', path: [issue.path], message: issue.message })
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
      rotationMethod: detail?.rotationMethod ?? '',
      model: product?.model ?? '',
      packUom: product?.packUomCode ?? '',
      packQty: product?.packQty ?? null,
      categoryId: product?.categoryId != null ? String(product.categoryId) : defaultCategoryId != null ? String(defaultCategoryId) : '',
      trackingType: product?.trackingTypeCode ?? (hasTrackingOption(DEFAULT_TRACKING) ? DEFAULT_TRACKING : ''),
      ownerClientPublicId: product?.ownerClientPublicId ?? null,
      purchaseCost: product?.purchaseCost ?? null,
      salePrice: product?.salePrice ?? null,
      taxCategory: detail?.taxCategory ?? 'TAXABLE',
      preferredWarehousePublicId: detail?.preferredWarehousePublicId ?? null,
      preferredBinId: detail?.preferredBinId != null ? String(detail.preferredBinId) : '',
      minQty: product?.minQty ?? null,
      isActive: product?.isActive ?? true,
      barcode: product?.barcode ?? '',
      ...measuresDefaults(detail, suggestion),
      minPickQty: detail?.minPickQty ?? null,
      maxPickQty: detail?.maxPickQty ?? null,
      kitChoice: 'no' as const,
      assembleQuantity: null,
      assembleWarehousePublicId: null,
      assembleToBinId: '',
    },
  })

  // Lote 232: kit armado — en el alta, si se contestó «Sí»; en edición, si el producto lo es
  const qc = useQueryClient()
  const canAdjustInventory = useCan('inventory.adjust')
  const kitChoice = useWatch({ control: form.control, name: 'kitChoice' })
  // Lote 233: la pregunta solo sale si la compañía trabaja con kits armados (Ajustes → Operación → Kits; apagado por omisión). Mientras el ajuste carga, no se pinta.
  // Editar un kit armado que ya existe no depende del ajuste.
  const usesAssembledKits = useTenantSettings(!isEdit).data?.usesAssembledKits === true
  const photosEnabled = useProductPhotosEnabled() // Lote 240: la compañía puede no manejar fotos de productos
  const isKitNew = !isEdit && usesAssembledKits && kitChoice === 'yes'
  const isKitEdit = isEdit && product?.isAssembledKit === true
  const isKit = isKitNew || isKitEdit
  const [newPieces, setNewPieces] = useState<KitPiece[]>(() => [newKitPiece('kp-new-0')])
  const composition = useEditKitComposition(isEdit ? publicId : null, isKitEdit)
  const setComposition = useSetKitComposition()
  const kitPieces = isKitNew ? newPieces : isKitEdit ? composition.pieces : NO_PIECES
  const { infos: pieceInfos } = usePieceInfos(kitPieces)
  const [piecesProblem, setPiecesProblem] = useState<string | null>(null)
  const problemText = usePiecesProblemText()
  const kitTracking = isKitNew ? trackingOfPieces(newPieces, pieceInfos) : isKitEdit ? (product?.trackingTypeCode === 'LOT' ? 'LOT' : 'NONE') : null

  // 2026-10-20: la excepción de rotación depende del tipo: con lote FEFO o FIFO; sin lote ni series FIFO o por posición
  const trackingChosen = useWatch({ control: form.control, name: 'trackingType' })
  // Lote 232: en un kit armado el rastreo lo deciden las piezas
  const lotTracked = isKit ? kitTracking === 'LOT' : trackingChosen === 'LOT'
  const rotationOptions = useMemo(
    () => [
      { value: '', label: t('warehouse.products.editor.rotationCompany') },
      ...(lotTracked
        ? [
            { value: 'FEFO', label: t('warehouse.products.editor.rotationFefo') },
            { value: 'FIFO', label: t('warehouse.products.editor.rotationFifo') },
          ]
        : [
            { value: 'FIFO', label: t('warehouse.products.editor.rotationFifo') },
            { value: 'POSITION', label: t('warehouse.products.editor.rotationPosition') },
          ]),
    ],
    [lotTracked, t],
  )
  const rotationChosen = useWatch({ control: form.control, name: 'rotationMethod' })
  useEffect(() => {
    // al cambiar el tipo, una excepción que ya no aplica se quita (vuelve a «Igual que la compañía»)
    if (rotationChosen && !rotationOptions.some((o) => o.value === rotationChosen)) form.setValue('rotationMethod', '', { shouldDirty: true })
  }, [rotationChosen, rotationOptions, form])
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
      // Lote 232: la composición se revisa antes de enviar (el servidor repite todo)
      if (isKitNew) {
        const problem = problemText(assembledPiecesProblem(newPieces, pieceInfos, v.ownerClientPublicId))
        setPiecesProblem(problem)
        if (problem) return
      }
      const kitFields = isKitNew
        ? assembledKitCreateFields({ pieces: newPieces, assembleQuantity: v.assembleQuantity, assembleWarehousePublicId: v.assembleWarehousePublicId, assembleToBinId: v.assembleToBinId })
        : null
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
        ...measuresCreateBody(v),
        minPickQty: v.minPickQty,
        maxPickQty: v.maxPickQty,
        brand: v.brand || null,
        model: v.model || null,
        rotationMethod: v.rotationMethod || null,   // vacío = la de la compañía
        packUom: v.packUom || null,
        packQty: v.packUom ? v.packQty : null,
        // kit armado: rastreo y costo los deciden las piezas (van null), composición y armar al crear
        ...(kitFields ?? {}),
      })
      // armar al crear movió inventario (salieron las piezas, entraron los kits)
      if (kitFields?.assembleQuantity != null) await invalidateStock(qc)
      const id = created.product?.id
      if (typeof id === 'number' && id > 0) {
        const problem = await saveCustomFields(id, form)
        if (problem) toast.error(problem.title)
        // Lote 237: el impuesto se guarda después de crear (gravable es lo que el producto ya trae); si falla, el producto queda creado y se avisa
        if (taxEditable && v.taxCategory !== 'TAXABLE') {
          try {
            await setTaxCategory.mutateAsync({ productId: id, category: v.taxCategory })
          } catch (e) {
            toast.error(t('warehouse.products.editor.taxCategoryNotSaved', { error: applyProblemDetails(e).title }))
          }
        }
      }
      toast.success(
        kitFields
          ? kitFields.assembleQuantity != null
            ? t(`${AK}.createdAssembled`, { n: kitFields.assembleQuantity })
            : t(`${AK}.created`)
          : t('warehouse.products.created'),
      )
      onClose()
      const newId = created.product?.publicId
      if (newId) onCreated?.(newId)
      return
    }

    // Lote 232: la composición de un kit armado (si cambió y el servidor la deja cambiar) va primero, con su propio PUT
    let compositionSaved = false
    if (isKitEdit && composition.changed && !composition.locked) {
      const problem = problemText(assembledPiecesProblem(composition.pieces, pieceInfos, product?.ownerClientPublicId ?? null))
      setPiecesProblem(problem)
      if (problem) return
      await setComposition.mutateAsync({ publicId, body: { pieces: assembledPiecesInput(composition.pieces) } })
      compositionSaved = true
    }

    // copia: el reset de más abajo reemplaza dirtyFields
    const dirty: Partial<Record<string, unknown>> = { ...form.formState.dirtyFields }
    const dataDirty = Object.keys(dirty).some((k) => k !== 'isActive' && k !== 'customFields' && k !== 'taxCategory')
    // Lote 237: el impuesto va por su propio endpoint (PUT /tax/products/{id}/category), no en el PATCH del producto
    const taxDirty = taxEditable && Boolean(dirty.taxCategory) && typeof product?.id === 'number' && product.id > 0
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
          // kit armado (Lote 232): rastreo y dueño fijos (los deciden las piezas)
          trackingType: hasMovements || isKitEdit ? null : v.trackingType || null,
          baseUom: hasMovements ? null : v.baseUom || null,
          ownerClientPublicId: hasMovements || isKitEdit ? null : v.ownerClientPublicId,
          clearOwner: hasMovements || isKitEdit || v.ownerClientPublicId ? null : true,
          purchaseCost: v.purchaseCost,
          salePrice: v.salePrice,
          // Lote 230: solo lo que se tocó de medidas y peso (weight + weightUnit; weightKg ya no se manda)
          ...measuresPatchBody(v, dirty),
          minQty: v.minQty,
          minPickQty: v.minPickQty,
          maxPickQty: v.maxPickQty,
          preferredWarehousePublicId: v.preferredWarehousePublicId,
          preferredBinId: v.preferredWarehousePublicId && v.preferredBinId ? Number(v.preferredBinId) : null,
          clearPreferred: dirty.preferredWarehousePublicId && !v.preferredWarehousePublicId ? true : null,
          // PATCH: null = sin cambio, '' = quitar (solo se mandan si cambiaron)
          brand: dirty.brand ? v.brand : null,
          model: dirty.model ? v.model : null,
          rotationMethod: dirty.rotationMethod ? v.rotationMethod : null,   // PATCH: null = sin cambio, '' = quitar la excepción (vuelve a la de la compañía)
          // empaque: se manda completo si cambió algo; quitarlo = clearPack
          packUom: (dirty.packUom || dirty.packQty) && v.packUom ? v.packUom : null,
          packQty: (dirty.packUom || dirty.packQty) && v.packUom ? v.packQty : null,
          clearPack: (dirty.packUom || dirty.packQty) && !v.packUom ? true : null,
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
    if (taxDirty) {
      try {
        await setTaxCategory.mutateAsync({ productId: product!.id!, category: v.taxCategory })
      } catch (e) {
        toast.error(t('warehouse.products.editor.taxCategoryNotSaved', { error: applyProblemDetails(e).title }))
        return
      }
    }
    if (activeChanged && !v.isActive) await setActive.mutateAsync({ publicId, active: false })
    if (dataDirty || activeChanged || dirty.customFields || compositionSaved || taxDirty) {
      toast.success(
        activeChanged && !dataDirty
          ? t(v.isActive ? 'warehouse.products.reactivated' : 'warehouse.products.deactivated')
          : t('warehouse.products.editor.updated'),
      )
    }
    onClose()
  }

  // Lote 230: medidas al día para el volumen calculado (o el manual) y las opciones de unidad
  const [length, width, height, dimensionUnit, manualVolume] = useWatch({ control: form.control, name: ['length', 'width', 'height', 'dimensionUnit', 'volume'] })
  const baseUomChosen = useWatch({ control: form.control, name: 'baseUom' })
  const measures = { length: length ?? null, width: width ?? null, height: height ?? null, dimensionUnit: dimensionUnit ?? '', volume: manualVolume ?? null }
  const volume = volumeView(measures)
  const volumeLabel = t('warehouse.products.measures.volume', { unit: CUBIC_LABEL[volume.unit] ?? volume.unit })
  const dimensionUnitOptions: SelectOption[] = DIMENSION_UNITS.map((u) => ({ value: u, label: t(`warehouse.products.measures.units.${u}`) }))
  const weightUnitOptions: SelectOption[] = WEIGHT_UNITS.map((u) => ({ value: u, label: t(`warehouse.products.measures.units.${u}`) }))

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
    <Modal open title={title} size="xl" onClose={onClose} dismissible={!submitting} footer={footer}>
      <Form id={formId} form={form} onSubmit={submit}>
        <fieldset className="pe-fields" disabled={!editable}>
          {/* Lote 232: ¿es un kit armado? (solo en el alta y si la compañía trabaja con kits armados, Lote 233; No por omisión) y, si lo es, su composición ahí mismo */}
          {!isEdit && usesAssembledKits && <KitQuestion idPrefix={formId} />}
          {isKitNew && (
            <NewKitBlock idPrefix={formId} pieces={newPieces} setPieces={setNewPieces} infos={pieceInfos} problem={piecesProblem} canAdjust={canAdjustInventory} />
          )}
          {isKitEdit && <EditKitBlock idPrefix={formId} publicId={publicId} editable={editable} composition={composition} problem={piecesProblem} onNavigate={onClose} />}
          {/* Lote 229: bloque «Foto» del producto (autocontenido; un producto nuevo solo avisa que la foto va después de guardar) */}
          {photosEnabled && <ProductPhotoBlock publicId={product?.publicId ?? null} titleId={`${formId}-photo`} />}
          <EditorBlock id={`${formId}-ident`} title={t('warehouse.products.editor.blocks.identification')}>
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
            <Field name="name" label={t('warehouse.products.editor.name')} required className="span2">
              <TextInput maxLength={200} />
            </Field>
            <Field name="baseUom" label={t('warehouse.products.editor.uom')}>
              <ComboSelectInput options={uomCodes} placeholder={nonePlaceholder} disabled={isEdit && hasMovements} />
            </Field>
            <Field name="categoryId" label={t('warehouse.products.editor.category')} className="span2">
              <ComboSelectInput options={categoryOptions} placeholder={nonePlaceholder} />
            </Field>
            <Field name="brand" label={t('warehouse.products.editor.brand')}>
              <TextInput maxLength={BRAND_MAX} list={brandListId} autoComplete="off" placeholder={t('warehouse.products.editor.brandPlaceholder')} />
            </Field>
            <Field name="model" label={t('warehouse.products.editor.model')}>
              <TextInput maxLength={MODEL_MAX} autoComplete="off" />
            </Field>
            {/* sugerencias de las marcas ya usadas en la compañía; se puede escribir una nueva */}
            <datalist id={brandListId}>
              {brands.map((b) => (
                <option key={b} value={b} />
              ))}
            </datalist>
          </EditorBlock>

          <EditorBlock id={`${formId}-inv`} title={t('warehouse.products.editor.blocks.inventory')}>
            {/* un solo aviso para los tres campos que el API bloquea con movimientos (unidad, rastreo y dueño) */}
            {lockHelp && <p className="help pe-span-all">{lockHelp}</p>}
            {/* Lote 232: en un kit armado el rastreo y el costo los deciden las piezas (informativos) */}
            {isKit && <p className="help pe-span-all">{t(`${AK}.notApplyNote`)}</p>}
            {isKit ? (
              <KitInfoField
                id={`${formId}-kit-tracking`}
                label={t(`${AK}.trackingInfo`)}
                value={t(kitTracking === 'LOT' ? `${AK}.trackingLot` : kitTracking === 'NONE' ? `${AK}.trackingNone` : `${AK}.trackingPending`)}
              />
            ) : (
              <Field name="trackingType" label={t('warehouse.products.editor.tracking')}>
                <ComboSelectInput options={trackingCodes} placeholder={nonePlaceholder} disabled={isEdit && hasMovements} />
              </Field>
            )}
            <Field name="ownerClientPublicId" label={t('warehouse.products.editor.owner')} className="span2">
              <ClientPickerInput placeholder={ownLabel} disabled={(isEdit && hasMovements) || isKitEdit} />
            </Field>
            <Field name="rotationMethod" label={t('warehouse.products.editor.rotation')} tip={t('warehouse.products.editor.rotationHelp')}>
              <Select options={rotationOptions} />
            </Field>
            <Field name="packUom" label={t('warehouse.products.editor.packUom')}>
              <ComboSelectInput options={uomCodes.filter((o) => o.value !== baseUomChosen)} placeholder={nonePlaceholder} />
            </Field>
            <Field name="packQty" label={t('warehouse.products.editor.packQty')} tip={t('warehouse.products.editor.packHelp')}>
              <NumberInput className="mono" min={0} />
            </Field>
            <Field name="barcode" label={t('warehouse.products.fields.barcode')}>
              <TextInput maxLength={60} />
            </Field>
            <Field name="minQty" label={t('warehouse.products.editor.reorder')}>
              <NumberInput className="mono" min={0} />
            </Field>
            <Field name="preferredWarehousePublicId" label={t('warehouse.products.editor.preferredWarehouse')}>
              <WarehousePickerInput placeholder={t('warehouse.products.fields.none')} />
            </Field>
            <Field name="preferredBinId" label={t('warehouse.products.editor.preferredBin')}>
              <BinPickerInput warehousePublicId={preferredWarehousePublicId} placeholder={t('warehouse.products.fields.none')} onPicked={setPickedBin} />
            </Field>
            <Field name="minPickQty" label={t('warehouse.products.fields.minPickQty')}>
              <NumberInput min={0} />
            </Field>
            <Field name="maxPickQty" label={t('warehouse.products.fields.maxPickQty')}>
              <NumberInput min={0} />
            </Field>
            <div className="f">
              <label htmlFor={totalId}>
                {t('warehouse.products.editor.total')}{' '}
                <span className="pe-faint">
                  ({isEdit ? t('warehouse.products.editor.useAdjust') : t('warehouse.products.editor.totalOnCreate')})
                </span>
              </label>
              <input id={totalId} className="mono" value={formatNumber(onHand, lang)} disabled readOnly />
            </div>
            {isEdit && (
              <div className="pe-active span3">
                <label className="sw">
                  <input type="checkbox" role="switch" disabled={activeLocked} {...form.register('isActive')} />
                  <span className="tk" aria-hidden="true" />
                  <span>{t('warehouse.products.editor.activeLabel')}</span>
                </label>
                {activeLocked && <div className="note">{t('warehouse.products.editor.hasStockNote')}</div>}
              </div>
            )}
          </EditorBlock>

          <EditorBlock id={`${formId}-price`} title={t('warehouse.products.editor.blocks.prices')}>
            {isKit ? (
              <KitInfoField id={`${formId}-kit-cost`} label={t(`${AK}.costInfo`)} value={t(`${AK}.costDecided`)} />
            ) : (
              <Field name="purchaseCost" label={t('warehouse.products.editor.purchaseCost')}>
                <NumberInput className="mono" min={0} />
              </Field>
            )}
            <Field name="salePrice" label={t('warehouse.products.editor.salePrice')}>
              <NumberInput className="mono" min={0} />
            </Field>
            {taxModule && (
              <Field
                name="taxCategory"
                label={t('warehouse.products.editor.taxCategory')}
                help={t(taxEditable ? 'warehouse.products.editor.taxCategoryHelp' : 'warehouse.products.editor.taxCategoryReadOnly')}
              >
                <Select options={taxCategoryOptions} disabled={!taxEditable} />
              </Field>
            )}
          </EditorBlock>

          <EditorBlock id={`${formId}-meas`} title={t('warehouse.products.editor.blocks.measures')}>
            <p className="help pe-span-all">{t('warehouse.products.measures.help')}</p>
            <Field name="dimensionUnit" label={t('warehouse.products.measures.dimensionUnit')} required>
              <Select options={dimensionUnitOptions} />
            </Field>
            <Field name="length" label={t('warehouse.products.measures.length')}>
              <NumberInput className="mono" min={0} />
            </Field>
            <Field name="width" label={t('warehouse.products.measures.width')}>
              <NumberInput className="mono" min={0} />
            </Field>
            <Field name="height" label={t('warehouse.products.measures.height')}>
              <NumberInput className="mono" min={0} />
            </Field>
            {volume.calculated ? (
              <div className="f">
                <label htmlFor={`${formId}-vol`}>{volumeLabel}</label>
                <input id={`${formId}-vol`} className="mono" value={volume.volume != null ? f.number(volume.volume, { maximumFractionDigits: 4 }) : ''} disabled readOnly aria-describedby={`${formId}-vol-help`} />
                <p id={`${formId}-vol-help`} className="help">
                  {t('warehouse.products.measures.volumeCalculated', { m3: f.number(volume.m3 ?? 0, { maximumFractionDigits: 4 }) })}
                </p>
              </div>
            ) : (
              <Field
                name="volume"
                label={volumeLabel}
                help={
                  volume.m3 != null && measures.dimensionUnit !== 'M'
                    ? t('warehouse.products.measures.volumeStoredAs', { m3: f.number(volume.m3, { maximumFractionDigits: 4 }) })
                    : t('warehouse.products.measures.volumeManual')
                }
              >
                <NumberInput className="mono" min={0} />
              </Field>
            )}
            <Field name="weight" label={t('warehouse.products.measures.weight')}>
              <NumberInput className="mono" min={0} />
            </Field>
            <Field name="weightUnit" label={t('warehouse.products.measures.weightUnit')} required>
              <Select options={weightUnitOptions} />
            </Field>
          </EditorBlock>

          {/* campos personalizados del producto (si la compañía tiene): el bloque se oculta solo si no hay ninguno */}
          <section className="pe-block pe-custom" aria-labelledby={`${formId}-custom`}>
            <h3 id={`${formId}-custom`} className="pe-block-title">
              {t('warehouse.products.editor.blocks.custom')}
            </h3>
            <CustomFieldsForm entityType="PRODUCT" entityId={isEdit ? product?.id : undefined} form={form} disabled={!editable} />
          </section>
        </fieldset>
      </Form>

      {isEdit && publicId && <StockByBin publicId={publicId} />}

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

/** Bloque con título del editor (Lote 230): `section` con su `h3` y la rejilla `.pe-grid` (4 columnas en escritorio, 1 a 360 px). */
function EditorBlock({ id, title, children }: { id: string; title: string; children: ReactNode }) {
  return (
    <section className="pe-block" aria-labelledby={id}>
      <h3 id={id} className="pe-block-title">
        {title}
      </h3>
      <div className="pe-grid">{children}</div>
    </section>
  )
}

// ---- Panel colapsable "Dónde está": existencia del producto por almacén, posición y lote (pedido del dueño 2026-10-05) ----
// Se pide solo al abrirlo (`GET /inventory/balances?productPublicIds=…`, solo con existencia): en mano, reservado y disponible por
// posición, con el lote y su vencimiento si lo lleva, y el total. Disponible = en mano − reservado (lo que se puede despachar).
function StockByBin({ publicId }: { publicId: string }) {
  const t = useT()
  const lang = useLang()
  const [open, setOpen] = useState(false)
  return (
    <details className="pe-more pe-where" open={open} onToggle={(e) => setOpen(e.currentTarget.open)}>
      <summary>{t('warehouse.products.editor.where')}</summary>
      {open && <StockByBinRows publicId={publicId} lang={lang} />}
    </details>
  )
}

function StockByBinRows({ publicId, lang }: { publicId: string; lang: string }) {
  const t = useT()
  const q = useInventoryBalances({ productPublicIds: [publicId], includeZero: false, take: 200 }, { handleAccessDenied: false })
  if (q.isLoading) return <p className="note">{t('common.loading')}</p>
  if (q.error) return <p className="note ferr">{q.error.message || t('errors.generic')}</p>
  const rows = (q.data?.items ?? []).filter((b) => (b.qtyOnHand ?? 0) > 0)
  if (rows.length === 0) return <p className="note">{t('warehouse.products.editor.whereEmpty')}</p>
  const sum = (pick: (b: (typeof rows)[number]) => number | undefined) => rows.reduce((acc, b) => acc + (pick(b) ?? 0), 0)
  return (
    <div className="pe-where-wrap">
      <table className="pe-stock">
        <thead>
          <tr>
            <th scope="col">{t('warehouse.products.editor.whereBin')}</th>
            <th scope="col">{t('warehouse.products.editor.whereLot')}</th>
            <th scope="col" className="num">
              {t('warehouse.products.editor.whereOnHand')}
            </th>
            <th scope="col" className="num">
              {t('warehouse.products.editor.whereReserved')}
            </th>
            <th scope="col" className="num">
              {t('warehouse.products.editor.whereAvailable')}
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.map((b) => (
            <tr key={b.id}>
              <td>
                <span className="ref">{b.binCode ?? '—'}</span>
                <span className="sub">{[b.warehouseCode, b.zoneCode].filter(Boolean).join(' · ')}</span>
              </td>
              <td>{b.lotNumber ? [b.lotNumber, b.expiryDate].filter(Boolean).join(' · ') : '—'}</td>
              <td className="num mono">{formatNumber(b.qtyOnHand, lang)}</td>
              <td className="num mono">{formatNumber(b.qtyReserved, lang)}</td>
              <td className="num mono">
                <strong>{formatNumber(b.qtyAvailable, lang)}</strong>
              </td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <th scope="row" colSpan={2}>
              {t('warehouse.products.editor.whereTotal')}
            </th>
            <td className="num mono">{formatNumber(sum((b) => b.qtyOnHand), lang)}</td>
            <td className="num mono">{formatNumber(sum((b) => b.qtyReserved), lang)}</td>
            <td className="num mono">
              <strong>{formatNumber(sum((b) => b.qtyAvailable), lang)}</strong>
            </td>
          </tr>
        </tfoot>
      </table>
    </div>
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
  // Lote 14 (D11): las mismas piezas que el modal de ajuste (Subir/Bajar + cantidad positiva, motivos según la dirección),
  // con el producto fijo y, por defecto, el almacén y la posición por defecto del producto (los guardados).
  const state = useAdjustmentForm({
    fixedProduct: product,
    initial: { warehousePublicId: detail.preferredWarehousePublicId ?? null, binId: detail.preferredBinId ?? null },
  })
  const submit = useSubmitAdjustment()
  const submitting = state.form.formState.isSubmitting

  return (
    <Form
      form={state.form}
      className="pe-adjust"
      onSubmit={async (v) => {
        setBusy(true)
        try {
          // 409 insufficient_stock (dejaría el inventario negativo): `Form` pone el mensaje del servidor arriba de este bloque
          const q = await submit(v, state.tracking)
          toast.success(t('warehouse.products.editor.adjustApplied', { qty: `${q > 0 ? '+' : ''}${formatNumber(q, lang)}`, sku: product.sku ?? '' }))
          // la mutación ya refrescó la ficha (Total); el modal sigue abierto y el bloque se oculta limpio
          onDone()
        } finally {
          setBusy(false)
        }
      }}
    >
      <h3 className="pe-section">{t('warehouse.products.editor.adjustTitle')}</h3>
      <AdjustmentFields state={state} idPrefix="pe-adj" />
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
