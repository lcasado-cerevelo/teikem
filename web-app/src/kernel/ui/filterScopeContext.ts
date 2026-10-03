// Contextos y hooks del ámbito de filtros y del encabezado de las exportaciones (pedido del dueño del producto,
// 2026-09-30). Los componentes que los proveen (`FilterScope`, `ExportCompanyProvider`) están en FilterScope.tsx.
// - `useRegisterFilter(label, value, ref?)`: lo llaman los controles de filtro; `value` null = sin filtro.
// - `useExportHeading()`: compañía activa (la pone el shell con `me.tenantName`) y oración de filtros del ámbito, leídas al
//   momento de exportar por `DataTable` y `ListPager`.
import { createContext, useCallback, useContext, useEffect, useId, type RefObject } from 'react'
import { useT } from '../i18n/useT'
import { filtersSentence, type AppliedFilter, type FilterRegistry } from './filterRegistry'

/** Registro del ámbito actual; null = sin ámbito o apagado (los filtros no se anotan y la exportación no pone la línea). */
export const FilterScopeContext = createContext<FilterRegistry | null>(null)

/** Compañía activa para el encabezado de los archivos exportados (null = sin sesión, p. ej. en pruebas). */
export const ExportCompanyContext = createContext<string | null>(null)

/**
 * Anota un filtro en el ámbito mientras el componente está montado. `value`: texto legible del valor (null = vacío o
 * "Todos": cuenta como barra pero no sale en la oración; '' = solo la etiqueta, p. ej. un interruptor encendido). `ref`
 * (el `.f` del control) ordena la oración como la barra. `enabled` false = no se anota.
 */
export function useRegisterFilter(
  label: string,
  value: string | null | undefined,
  ref?: RefObject<Element | null>,
  enabled = true,
): void {
  const registry = useContext(FilterScopeContext)
  const id = useId()
  const text = value === undefined ? null : value
  useEffect(() => {
    if (!registry || !enabled) return
    registry.set(id, { label, value: text, element: ref?.current ?? null })
  }, [registry, enabled, id, label, text, ref])
  useEffect(() => {
    if (!registry || !enabled) return
    return () => registry.remove(id)
  }, [registry, enabled, id])
}

/** Lo que va arriba de la tabla en un PDF/Excel exportado (además del título y la fecha). */
export interface ExportHeading {
  company: string | null
  /** Oración de filtros ("Filtros: …" / "Sin filtros"); null = sin línea (tabla sin barra o en un modal). */
  filters: string | null
}

/** Función que arma el encabezado al exportar (lee el registro en ese momento). La usan `DataTable` y `ListPager`. */
export function useExportHeading(): () => ExportHeading {
  const registry = useContext(FilterScopeContext)
  const company = useContext(ExportCompanyContext)
  const t = useT()
  return useCallback(() => ({ company, filters: registry ? filtersSentence(registry.snapshot(), t) : null }), [registry, company, t])
}

/**
 * Lote F14 — filtros con valor del ámbito, leídos en el momento de llamar la función devuelta (en el orden de la barra):
 * el recuadro "Filtros aplicados" de un reporte de marca con exactamente lo que filtra la pantalla. Sin ámbito, `[]`.
 */
export function useAppliedFilters(): () => AppliedFilter[] {
  const registry = useContext(FilterScopeContext)
  return useCallback(() => (registry ? registry.snapshot().applied : []), [registry])
}
