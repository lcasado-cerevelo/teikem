// Opciones de "Filas por página" del pie de `DataTable` y `ListPager`.

/** Opciones del selector "Filas por página". */
export const PAGE_SIZE_OPTIONS: readonly number[] = [10, 25, 50, 100]

/** Opciones del selector con el tamaño actual incluido (p. ej. una lista que arranca en 5). */
export function pageSizeOptions(pageSize: number, options: readonly number[] = PAGE_SIZE_OPTIONS): readonly number[] {
  return options.includes(pageSize) ? options : [...options, pageSize].sort((a, b) => a - b)
}
