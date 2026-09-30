import { createContext, useContext } from 'react'

/** Título (texto) del `Panel` que contiene al componente; `null` fuera de un panel o si el título no es texto. */
export const PanelTitleContext = createContext<string | null>(null)

/** Lo usa `DataTable` para nombrar el archivo exportado sin que cada pantalla lo repita. */
export function usePanelTitle(): string | null {
  return useContext(PanelTitleContext)
}
