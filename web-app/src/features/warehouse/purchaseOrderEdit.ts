// Lógica pura de la ficha de la orden de compra (decisión del 2026-09-30): proveedor y almacén editables solo en Borrador.
// El PATCH acepta `supplierId` y `warehousePublicId` (null = sin cambio); la pantalla manda solo lo que cambió. Los 404/422
// de esos dos datos llegan sin campo ('Proveedor no encontrado.', 'El almacén está dado de baja; …'): si cambió uno solo, el
// error es de ese campo; si cambiaron los dos no se puede saber cuál falló y va al aviso del formulario. El 409
// ('…solo se cambian mientras la orden de compra está en borrador.') siempre va al aviso.
import type { ComboOption } from '../../kernel/ui'
import { ApiError } from '../../kernel/api/problem'
import type { PurchaseOrderDto, SupplierDto } from './api'

/** Estatus en que proveedor y almacén se pueden cambiar (`PurchaseOrderStatuses.Draft`). */
export const PO_DRAFT = 'DRAFT'

/** true si la orden está en Borrador (sin distinguir mayúsculas). */
export function isDraftPurchaseOrder(po: Pick<PurchaseOrderDto, 'statusCode'>): boolean {
  return (po.statusCode ?? '').toUpperCase() === PO_DRAFT
}

/**
 * Opciones del proveedor en la ficha: los activos y, si el actual ya no está entre ellos (dado de baja después de crear la
 * orden), el actual con la marca `inactiveHint`, para que el campo muestre su nombre y se pueda guardar sin cambiarlo.
 */
export function supplierOptionsWithCurrent(
  suppliers: readonly SupplierDto[],
  current: { id?: number | null; name?: string | null },
  inactiveHint: string,
): ComboOption[] {
  const options: ComboOption[] = suppliers.map((s) => ({ value: String(s.id), label: s.name ?? '' }))
  if (current.id != null && !options.some((o) => o.value === String(current.id)))
    options.unshift({ value: String(current.id), label: current.name ?? String(current.id), hint: inactiveHint })
  return options
}

/** Proveedor y almacén que cambiaron respecto de la orden (lo que va en el PATCH; sin cambio = no se manda). */
export function purchaseOrderPartyChanges(
  po: Pick<PurchaseOrderDto, 'supplierId' | 'warehousePublicId'>,
  values: { supplierId?: string | null; warehousePublicId?: string | null },
): { supplierId?: number; warehousePublicId?: string } {
  const out: { supplierId?: number; warehousePublicId?: string } = {}
  if (values.supplierId && Number(values.supplierId) !== po.supplierId) out.supplierId = Number(values.supplierId)
  if (values.warehousePublicId && values.warehousePublicId.toLowerCase() !== (po.warehousePublicId ?? '').toLowerCase())
    out.warehousePublicId = values.warehousePublicId
  return out
}

/**
 * Campo al que pertenece un error del PATCH sobre proveedor/almacén: 404 o 422 con un solo dato cambiado → ese campo; si
 * no (409, otro error o los dos cambiados), null = el aviso del formulario.
 */
export function partyErrorField(
  err: unknown,
  changes: { supplierId?: number; warehousePublicId?: string },
): 'supplierId' | 'warehousePublicId' | null {
  if (!(err instanceof ApiError) || (err.status !== 404 && err.status !== 422)) return null
  const supplier = changes.supplierId !== undefined
  const warehouse = changes.warehousePublicId !== undefined
  if (supplier === warehouse) return null
  return supplier ? 'supplierId' : 'warehousePublicId'
}
