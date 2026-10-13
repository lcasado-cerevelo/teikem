// Lote 14 (P6) — lógica pura de 'Transferencias y ajustes': un estado de filtros por pestaña (`MovementFilterState`) y sus
// consultas del Kárdex (`adjustmentsQuery` → types=ADJUSTMENT; `transfersQuery` → types=TRANSFER), que listan TODOS los
// movimientos de ese tipo, también los del sistema (D12: conteo, recibo, acomodo, reabasto), con "Solo manuales"
// (`manualOnly`) y filtro por motivo. `describeMovementFilters` arma los "Filtros aplicados" del Reporte de ajustes.
import type { DateRange } from '../../kernel/ui/dateRange'
import type { GetQuery } from './api'
import { ownerQuery, userQuery, type BinFilterItem, type KardexDirection } from './kardexView'
import type { ProductFilterItem } from './pickers'
import { ADJUSTMENT_TXN_TYPE } from './productFilters'

/** InternalCode del tipo de movimiento 'Transferencia' (catálogo InventoryTxnType). */
export const TRANSFER_TXN_TYPE = 'TRANSFER'

export type MovementTab = 'adjustments' | 'transfers'

/** `?tab=`: Ajustes es la primera (sin parámetro); `transfers` = Transferencias; desconocido = la primera. */
export function movementTabFromParam(value: string | null): MovementTab {
  return value === 'transfers' ? 'transfers' : 'adjustments'
}

export interface MovementFilterState {
  range: DateRange
  /** Ajustes (2026-10-12: antes Almacén): posiciones elegidas («Código · Zona · Almacén»). */
  bins: BinFilterItem[]
  /** Transferencias (2026-10-12: antes almacén de origen y de destino): posición de origen y de destino. */
  fromBins: BinFilterItem[]
  toBins: BinFilterItem[]
  products: ProductFilterItem[]
  /** `OWN_OWNER` y/o clientPublicId. */
  owners: string[]
  /** `SYSTEM_USER` y/o ids de usuario: quién hizo el movimiento. */
  users: string[]
  /** Ajustes: motivos (AdjustmentReason). */
  reasons: string[]
  /** Ajustes: Subir (IN) / Bajar (OUT). */
  direction: KardexDirection
  manualOnly: boolean
}

export const EMPTY_MOVEMENT_FILTERS: MovementFilterState = {
  range: { from: '', to: '' },
  bins: [],
  fromBins: [],
  toBins: [],
  products: [],
  owners: [],
  users: [],
  reasons: [],
  direction: '',
  manualOnly: false,
}

const nonEmpty = <T>(list: readonly T[]): T[] | undefined => (list.length > 0 ? [...list] : undefined)

function common(f: MovementFilterState) {
  return {
    from: f.range.from || undefined,
    to: f.range.to || undefined,
    productPublicIds: nonEmpty(f.products.map((p) => p.publicId)),
    ...ownerQuery(f.owners),
    ...userQuery(f.users),
    manualOnly: f.manualOnly || undefined,
  }
}

/** Ajustes: tipo ADJUSTMENT + fechas, posición, producto, dueño, motivo, dirección y "Solo manuales" (sin skip/take). */
export function adjustmentsQuery(f: MovementFilterState): GetQuery<'/api/v1/inventory/transactions'> {
  return {
    types: [ADJUSTMENT_TXN_TYPE],
    ...common(f),
    binIds: nonEmpty(f.bins.map((b) => b.id)),
    reasons: nonEmpty(f.reasons),
    direction: f.direction || undefined,
  }
}

/** Transferencias: tipo TRANSFER + fechas, posición de origen y de destino, producto, dueño, usuario y "Solo manuales". */
export function transfersQuery(f: MovementFilterState): GetQuery<'/api/v1/inventory/transactions'> {
  return {
    types: [TRANSFER_TXN_TYPE],
    ...common(f),
    fromBinIds: nonEmpty(f.fromBins.map((b) => b.id)),
    toBinIds: nonEmpty(f.toBins.map((b) => b.id)),
  }
}

type Translate = (key: string, params?: Record<string, string | number>) => string

/** Nombres para los "Filtros aplicados": almacén (publicId → "Código · Nombre"), dueño y motivo (código → etiqueta). */
export interface MovementFilterNames {
  warehouses: ReadonlyMap<string, string>
  owners: ReadonlyMap<string, string>
  reasons: ReadonlyMap<string, string>
  /** Usuario (`SYSTEM_USER` o id → nombre); sin él se muestra el id. */
  users?: ReadonlyMap<string, string>
}

/**
 * "Filtros aplicados" del Reporte de ajustes con los filtros de la pestaña Ajustes, en el orden de la pantalla: tipo
 * (Ajuste), fechas, almacén, producto, dueño, dirección, motivo y "Solo manuales". Un id sin nombre se muestra tal cual.
 */
export function describeMovementFilters(f: MovementFilterState, names: MovementFilterNames, t: Translate): { label: string; value: string }[] {
  const T = 'warehouse.transfersAdjustments.filters'
  const out: { label: string; value: string }[] = [
    { label: t('warehouse.products.reports.filters.type'), value: t('warehouse.products.reports.filters.typeAdjustment') },
  ]
  const list = (values: string[]) => values.join(', ')
  if (f.range.from || f.range.to) {
    out.push({ label: t(`${T}.range`), value: `${f.range.from || '…'} – ${f.range.to || '…'}` })
  }
  if (f.bins.length > 0) out.push({ label: t(`${T}.bin`), value: list(f.bins.map((b) => b.label)) })
  if (f.products.length > 0) out.push({ label: t(`${T}.product`), value: list(f.products.map((p) => p.label || p.sku || p.publicId)) })
  if (f.owners.length > 0) out.push({ label: t(`${T}.owner`), value: list(f.owners.map((o) => names.owners.get(o) ?? o)) })
  if (f.users.length > 0) out.push({ label: t(`${T}.user`), value: list(f.users.map((u) => names.users?.get(u) ?? u)) })
  if (f.direction) out.push({ label: t(`${T}.direction`), value: t(f.direction === 'IN' ? `${T}.up` : `${T}.down`) })
  if (f.reasons.length > 0) out.push({ label: t(`${T}.reason`), value: list(f.reasons.map((r) => names.reasons.get(r) ?? r)) })
  if (f.manualOnly) out.push({ label: t(`${T}.manualOnly`), value: t(`${T}.yes`) })
  return out
}
