// Vista previa de la marca (Ajustes → Marca sin guardar) y escritura de sus variables CSS en <html>. Sin React.
import { BRAND_CSS_VARS, brandCssVars, type BrandSettings } from './brandTheme'

let preview: BrandSettings | null = null
const listeners = new Set<() => void>()

/** Vista previa de la marca (null = la guardada de la compañía). */
export function setBrandPreview(brand: BrandSettings | null): void {
  if (preview === brand) return
  preview = brand
  listeners.forEach((l) => l())
}

export function getBrandPreview(): BrandSettings | null {
  return preview
}

export function subscribeBrandPreview(l: () => void): () => void {
  listeners.add(l)
  return () => listeners.delete(l)
}

/** Escribe las variables de la marca en el elemento (o las quita con la marca de siempre). */
export function applyBrandVars(el: HTMLElement, brand: BrandSettings, mode: 'dark' | 'light'): void {
  const vars = brandCssVars(brand, mode)
  for (const k of BRAND_CSS_VARS) if (!(k in vars)) el.style.removeProperty(k)
  for (const [k, v] of Object.entries(vars)) el.style.setProperty(k, v)
}
