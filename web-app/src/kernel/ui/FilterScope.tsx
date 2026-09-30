// Ámbito de filtros y compañía de las exportaciones (pedido del dueño del producto, 2026-09-30).
// - `FilterScope`: un registro de filtros (`filterRegistry.ts`) para lo que envuelve. El shell pone uno alrededor de la
//   pantalla; `Modal` pone uno apagado (`off`): una tabla dentro de un modal no lleva la línea de filtros y los filtros de un
//   modal no se anotan en la pantalla. Una zona de la pantalla cuyas tablas NO dependen de la barra (p. ej. el detalle de
//   un maestro-detalle) se envuelve en `<FilterScope>` propio (sin controles = sin línea).
// - `ExportCompanyProvider`: compañía activa arriba del título de los PDF y Excel (el shell la pone con `me.tenantName`).
// Los contextos y los hooks (`useRegisterFilter`, `useExportHeading`) están en filterScopeContext.ts.
import { useState, type ReactNode } from 'react'
import { createFilterRegistry } from './filterRegistry'
import { ExportCompanyContext, FilterScopeContext } from './filterScopeContext'

export interface FilterScopeProps {
  children?: ReactNode
  /** true = apagado: dentro no se anotan filtros y las tablas no llevan la línea de filtros (lo usa `Modal`). */
  off?: boolean
}

/** `<FilterScope>…</FilterScope>`: ámbito propio de filtros (la barra y las tablas que dependen de ella). */
export function FilterScope({ children, off }: FilterScopeProps) {
  const [registry] = useState(createFilterRegistry)
  return <FilterScopeContext.Provider value={off ? null : registry}>{children}</FilterScopeContext.Provider>
}

/** `<ExportCompanyProvider company={me?.tenantName}>`: compañía que va arriba del título en los PDF y Excel exportados. */
export function ExportCompanyProvider({ company, children }: { company: string | null | undefined; children?: ReactNode }) {
  return <ExportCompanyContext.Provider value={company?.trim() || null}>{children}</ExportCompanyContext.Provider>
}
