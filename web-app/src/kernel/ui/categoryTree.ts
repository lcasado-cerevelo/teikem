// Lógica pura de CategoryProductPicker: árbol de categorías aplanado con su nivel y el valor elegido (categoría o producto).
import { matchesQ } from './matchesQ'

/** Valor de CategoryProductPicker: una categoría (id), un producto (publicId) o nada (todos). */
export type CategoryProductValue = { kind: 'category'; id: number } | { kind: 'product'; publicId: string } | null

/** Lo mínimo que el árbol necesita de una categoría (`ProductCategoryDto`). */
export interface CategoryNode {
  id?: number
  name?: string | null
  parentId?: number | null
  path?: string | null
  productCount?: number
}

export interface CategoryTreeRow<C extends CategoryNode = CategoryNode> {
  category: C
  /** 0 = raíz; cada nivel agrega sangría. */
  level: number
}

/**
 * Árbol aplanado en orden de recorrido (padre y luego sus hijos, hermanos en el orden en que llegan: el API los ordena por
 * ruta). Una categoría cuyo padre no está en la lista (padre inactivo) se pinta como raíz; un ciclo no cuelga el recorrido.
 */
export function categoryTree<C extends CategoryNode>(categories: readonly C[]): CategoryTreeRow<C>[] {
  const ids = new Set(categories.map((c) => c.id))
  const children = new Map<number | null, C[]>()
  for (const c of categories) {
    const parent = c.parentId != null && ids.has(c.parentId) && c.parentId !== c.id ? c.parentId : null
    const list = children.get(parent) ?? []
    list.push(c)
    children.set(parent, list)
  }
  const out: CategoryTreeRow<C>[] = []
  const seen = new Set<number | undefined>()
  const walk = (parent: number | null, level: number) => {
    for (const c of children.get(parent) ?? []) {
      if (seen.has(c.id)) continue
      seen.add(c.id)
      out.push({ category: c, level })
      if (c.id != null) walk(c.id, level + 1)
    }
  }
  walk(null, 0)
  // lo que quedó fuera (solo en un ciclo de padres) se agrega como raíz
  for (const c of categories) if (!seen.has(c.id)) out.push({ category: c, level: 0 })
  return out
}

/**
 * Filas del árbol que coinciden con el texto (nombre o ruta, sin mayúsculas ni acentos); texto vacío = todas. Se agregan
 * los ancestros de cada coincidencia (aunque no coincidan ellos) para que ninguna fila quede huérfana, en el mismo orden
 * de recorrido del árbol (p. ej. buscar "Analgésicos" también muestra "Farmacia" arriba, como en la maqueta).
 */
export function filterCategoryTree<C extends CategoryNode>(rows: readonly CategoryTreeRow<C>[], q: string): CategoryTreeRow<C>[] {
  if (!q.trim()) return [...rows]
  const byId = new Map(rows.map((r) => [r.category.id, r.category]))
  const keep = new Set<number | undefined>()
  for (const r of rows) {
    if (!matchesQ(q, r.category.name, r.category.path)) continue
    keep.add(r.category.id)
    let parentId = r.category.parentId
    while (parentId != null && byId.has(parentId)) {
      keep.add(parentId)
      parentId = byId.get(parentId)?.parentId
    }
  }
  return rows.filter((r) => keep.has(r.category.id))
}

/** Etiqueta de una categoría: su ruta completa si el API la trae ("Farmacia › Analgésicos"), si no el nombre. */
export function categoryLabel(c: CategoryNode | null | undefined): string {
  if (!c) return ''
  return c.path || c.name || ''
}

/** true si `raw` tiene la forma de un CategoryProductValue (lectura segura de lo guardado en localStorage). */
export function isCategoryProductValue(raw: unknown): raw is CategoryProductValue {
  if (raw === null) return true
  if (typeof raw !== 'object') return false
  const v = raw as Record<string, unknown>
  if (v.kind === 'category') return typeof v.id === 'number' && Number.isInteger(v.id)
  if (v.kind === 'product') return typeof v.publicId === 'string' && v.publicId.length > 0
  return false
}

/** Mismo valor (misma categoría o mismo producto). */
export function sameCategoryProduct(a: CategoryProductValue, b: CategoryProductValue): boolean {
  if (a === null || b === null) return a === b
  if (a.kind === 'category' && b.kind === 'category') return a.id === b.id
  if (a.kind === 'product' && b.kind === 'product') return a.publicId === b.publicId
  return false
}

/**
 * Productos de cada categoría contando sus subcategorías (id → suma de `productCount` de todo el subárbol). Es lo que
 * filtra el API con `categoryIds` (saldos y bajo mínimo expanden a los descendientes), mientras `productCount` del DTO
 * cuenta solo los productos directos. Un padre ausente de la lista corta el subárbol; un ciclo no cuelga el recorrido.
 */
export function categoryProductTotals(categories: readonly CategoryNode[]): Map<number, number> {
  const ids = new Set(categories.map((c) => c.id))
  const children = new Map<number, CategoryNode[]>()
  for (const c of categories) {
    if (c.parentId == null || !ids.has(c.parentId) || c.parentId === c.id) continue
    const list = children.get(c.parentId) ?? []
    list.push(c)
    children.set(c.parentId, list)
  }
  const totals = new Map<number, number>()
  const visit = (c: CategoryNode, path: Set<number>): number => {
    if (c.id == null) return c.productCount ?? 0
    const cached = totals.get(c.id)
    if (cached != null) return cached
    if (path.has(c.id)) return 0
    path.add(c.id)
    let sum = c.productCount ?? 0
    for (const child of children.get(c.id) ?? []) sum += visit(child, path)
    path.delete(c.id)
    totals.set(c.id, sum)
    return sum
  }
  for (const c of categories) visit(c, new Set())
  return totals
}
