// Lote 14 (D11) — formulario del ajuste de inventario ("Subir" / "Bajar" + cantidad POSITIVA), compartido por
// InventoryAdjustModal (Transferencias y ajustes y Kárdex) y el bloque "Ajustar inventario" de la ficha del producto
// (ProductEditorModal, con el producto fijo). `useAdjustmentForm` guarda en estado lo que decide las reglas (dirección,
// producto y su rastreo, posición y lote: lo avisan los controles con `onPicked`), consulta lo disponible del producto en la
// posición y las series disponibles (al bajar un SERIAL), y arma el esquema con esas reglas (`adjustFormSchema`). El cuerpo
// del API (cantidad con signo) lo arma `adjustmentBody` de `movementForms.ts`.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm, useWatch, type UseFormReturn } from 'react-hook-form'
import { z } from 'zod'
import { useLang, useT, type TParams } from '../../kernel/i18n'
import type { ComboOption } from '../../kernel/ui/comboMatch'
import type { AdjustDirection } from './adjustmentReasons'
import {
  useInventoryAdjustment,
  useInventoryBalances,
  useProduct,
  useProductSerials,
  type BalanceDto,
  type ProductListItemDto,
  type WarehouseBinDto,
} from './api'
import { formatNumber, parseSerials } from './lineRules'
import { adjustmentBody, availableAt, serialsAt, type AdjustFormValues } from './movementForms'
import { adjustDirectionSchema, adjustNotesSchema, decimals } from './productRules'

type Translate = (key: string, params?: TParams) => string

/** Valores iniciales del ajuste (p. ej. el almacén y la posición por defecto del producto). */
export interface AdjustInitial {
  productPublicId?: string | null
  warehousePublicId?: string | null
  binId?: number | null
  direction?: AdjustDirection
}

/** Lo que decide las reglas y no es un valor de texto del formulario. */
export interface AdjustRules {
  /** Rastreo del producto: 'NONE' | 'LOT' | 'SERIAL' | '' (sin producto). */
  tracking: string
  /** 'up' | 'down' | ''. */
  direction: string
  /** Disponible del producto (y lote) en la posición; null = sin dato (no se topa). */
  available: number | null
}

const M = 'warehouse.inventory.adjustModal.errors'
const NO_BALANCES: BalanceDto[] = []

/**
 * Esquema del ajuste: dirección obligatoria, producto, almacén, posición, cantidad > 0 con ≤ 3 decimales (salvo SERIAL: la
 * cantidad es el número de series) y, al bajar, no más de lo disponible ('No puede bajar más de lo disponible en la
 * posición ({qty}).'); lote (LOT: número al subir, lote con saldo al bajar); series (SERIAL); motivo y nota obligatorios.
 */
export function adjustFormSchema(t: Translate, lang: string, rules: AdjustRules) {
  const { tracking, direction, available } = rules
  const exceeds = (qty: number) => t(`${M}.exceedsAvailable`, { qty: formatNumber(qty, lang) })
  return z.object({
    direction: adjustDirectionSchema(t),
    productPublicId: z
      .string()
      .nullable()
      .refine((v) => Boolean(v), t(`${M}.productRequired`)),
    warehousePublicId: z
      .string()
      .nullable()
      .refine((v) => Boolean(v), t(`${M}.warehouseRequired`)),
    binId: z.string().min(1, t(`${M}.binRequired`)),
    quantity: z
      .number(t(`${M}.quantityRequired`))
      .nullable()
      .superRefine((v, c) => {
        if (tracking === 'SERIAL') return // la cantidad es el número de series
        if (v === null) c.addIssue({ code: 'custom', message: t(`${M}.quantityRequired`) })
        else if (v <= 0) c.addIssue({ code: 'custom', message: t(`${M}.quantityPositive`) })
        else if (decimals(v) > 3) c.addIssue({ code: 'custom', message: t(`${M}.quantityDecimals`) })
        else if (direction === 'down' && available != null && v > available) c.addIssue({ code: 'custom', message: exceeds(available) })
      }),
    reason: z.string().min(1, t(`${M}.reasonRequired`)),
    notes: adjustNotesSchema(t),
    lotNumber: z.string().superRefine((v, c) => {
      if (tracking === 'LOT' && direction === 'up' && !v.trim()) c.addIssue({ code: 'custom', message: t(`${M}.lotNumberRequired`) })
    }),
    lotId: z.string().superRefine((v, c) => {
      if (tracking === 'LOT' && direction === 'down' && !v) c.addIssue({ code: 'custom', message: t(`${M}.lotRequired`) })
    }),
    serialText: z.string().superRefine((v, c) => {
      if (tracking === 'SERIAL' && direction === 'up' && parseSerials(v).length === 0) c.addIssue({ code: 'custom', message: t(`${M}.serialNumbersRequired`) })
    }),
    serials: z.array(z.string()).superRefine((v, c) => {
      if (tracking !== 'SERIAL' || direction !== 'down') return
      if (v.length === 0) c.addIssue({ code: 'custom', message: t(`${M}.serialNumbersRequired`) })
      else if (available != null && v.length > available) c.addIssue({ code: 'custom', message: exceeds(available) })
    }),
  })
}

function defaults(initial: AdjustInitial | undefined, fixedProduct: ProductListItemDto | null | undefined): AdjustFormValues {
  return {
    direction: initial?.direction ?? '',
    productPublicId: fixedProduct?.publicId ?? initial?.productPublicId ?? null,
    warehousePublicId: initial?.warehousePublicId ?? null,
    binId: initial?.binId != null ? String(initial.binId) : '',
    quantity: null,
    reason: '',
    notes: '',
    lotNumber: '',
    lotId: '',
    serialText: '',
    serials: [],
  }
}

/** Avisos de los controles (`onPicked`) que actualizan las reglas. */
export interface AdjustPickHandlers {
  direction: (d: AdjustDirection) => void
  product: (p: ProductListItemDto | null) => void
  bin: (b: WarehouseBinDto | null) => void
  lot: (o: ComboOption | null) => void
}

export interface AdjustmentFormState extends AdjustRules {
  form: UseFormReturn<AdjustFormValues>
  warehousePublicId: string | null
  /** Saldos del producto en la posición elegida. */
  balances: readonly BalanceDto[]
  /** Series disponibles en la posición (solo al bajar un SERIAL). */
  serials: string[]
  fixedProduct: ProductListItemDto | null
  picked: AdjustPickHandlers
}

/** Formulario del ajuste (valores, reglas y datos de apoyo: rastreo, saldos de la posición y series). */
export function useAdjustmentForm(opts: { initial?: AdjustInitial; fixedProduct?: ProductListItemDto | null } = {}): AdjustmentFormState {
  const t = useT()
  const lang = useLang()
  const fixedProduct = opts.fixedProduct ?? null
  const [direction, setDirection] = useState<string>(opts.initial?.direction ?? '')
  const [product, setProduct] = useState<{ publicId: string | null; tracking: string | null }>({
    publicId: fixedProduct?.publicId ?? opts.initial?.productPublicId ?? null,
    tracking: fixedProduct?.trackingTypeCode ?? null,
  })
  const [binId, setBinId] = useState<number | null>(opts.initial?.binId ?? null)
  const [lotId, setLotId] = useState<number | null>(null)

  // un producto que llegó sin su fila (valor inicial) toma el rastreo de su ficha
  const productQ = useProduct(product.tracking == null ? product.publicId : null, { handleAccessDenied: false })
  const tracking = product.tracking ?? productQ.data?.product?.trackingTypeCode ?? ''
  const balancesQ = useInventoryBalances(
    { binIds: binId != null ? [binId] : undefined, productPublicIds: product.publicId ? [product.publicId] : undefined, take: 200 },
    { enabled: binId != null && Boolean(product.publicId), handleAccessDenied: false },
  )
  const balances = binId != null && product.publicId ? (balancesQ.data?.items ?? NO_BALANCES) : NO_BALANCES
  const available = binId != null && product.publicId && balancesQ.isSuccess ? availableAt(balances, tracking === 'LOT' ? lotId : null) : null
  const serialsQ = useProductSerials(product.publicId, { status: 'AVAILABLE' }, { enabled: tracking === 'SERIAL' && direction === 'down', handleAccessDenied: false })
  const serials = useMemo(() => serialsAt(serialsQ.data ?? [], binId), [serialsQ.data, binId])

  const schema = useMemo(() => adjustFormSchema(t, lang, { tracking, direction, available }), [t, lang, tracking, direction, available])
  const form = useForm<AdjustFormValues>({
    resolver: zodResolver(schema) as never,
    defaultValues: defaults(opts.initial, fixedProduct),
  })
  const warehousePublicId = useWatch({ control: form.control, name: 'warehousePublicId' })

  const picked: AdjustPickHandlers = {
    direction: setDirection,
    product: (p) => {
      setProduct({ publicId: p?.publicId ?? null, tracking: p ? (p.trackingTypeCode ?? '') : null })
      setLotId(null)
    },
    bin: (b) => {
      setBinId(b?.id ?? null)
      setLotId(null)
    },
    lot: (o) => setLotId(o?.value ? Number(o.value) : null),
  }

  return { form, tracking, direction, available, warehousePublicId, balances, serials, fixedProduct, picked }
}

/** Envía el ajuste (cuerpo con signo). Devuelve la cantidad con signo aplicada. */
export function useSubmitAdjustment() {
  const adjust = useInventoryAdjustment()
  return async (v: AdjustFormValues, tracking: string) => {
    const body = adjustmentBody(v, tracking)
    await adjust.mutateAsync(body)
    return body.quantity ?? 0
  }
}
