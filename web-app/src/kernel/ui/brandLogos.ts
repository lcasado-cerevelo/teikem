// Logos de la compañía (Ajustes → Marca): cuatro ranuras en el API (`/api/v1/tenant/brand/logos`) y un almacén mínimo (sin React)
// con las URL de objeto de los archivos ya descargados, para que el lockup de la barra lateral y la marca cuadrada los usen.
// Los archivos exigen sesión (Authorization), así que no se pueden poner directo en <img src>: `TenantBrand` los descarga y deja
// aquí una URL de objeto por ranura. Regla de la maqueta: tema oscuro → variante invertida; claro → la normal; si solo hay una
// de las dos, se reusa en ambos temas; sin ninguna, el respaldo es el logo de Teikem (`brandAssets.ts`).
import { useSyncExternalStore } from 'react'
import type { Theme } from './theme'

export type LogoSlot = 'lockup' | 'lockup-inverted' | 'mark' | 'mark-inverted'
export type LogoKind = 'lockup' | 'mark'

/** Las cuatro ranuras, en el orden de la pantalla (y del API). */
export const LOGO_SLOTS: readonly LogoSlot[] = ['lockup', 'lockup-inverted', 'mark', 'mark-inverted']
/** Tamaño máximo del archivo (el servidor aplica el mismo tope). */
export const LOGO_MAX_BYTES = 512 * 1024
/** Formatos que acepta el servidor (se valida el contenido real, no la extensión). */
export const LOGO_ACCEPT = 'image/svg+xml,image/png,image/jpeg,image/webp'

/** Las ranuras para fondo oscuro son las invertidas. */
export const isInvertedSlot = (slot: LogoSlot): boolean => slot.endsWith('-inverted')

export interface CompanyLogos {
  /** URL de objeto del archivo de cada ranura que la compañía tiene. */
  urls: Partial<Record<LogoSlot, string>>
  /** Nombre de la compañía (texto alternativo de su logo). */
  name: string | null
}

export const NO_COMPANY_LOGOS: CompanyLogos = { urls: {}, name: null }

let current: CompanyLogos = NO_COMPANY_LOGOS
const listeners = new Set<() => void>()

export function setCompanyLogos(next: CompanyLogos): void {
  if (current === next) return
  const previous = current
  current = next
  listeners.forEach((l) => l())
  // las URL de objeto que ya no se usan se liberan (después de avisar: las imágenes ya cambiaron de `src`)
  const kept = new Set(Object.values(next.urls))
  if (typeof URL.revokeObjectURL === 'function') for (const u of Object.values(previous.urls)) if (!kept.has(u)) URL.revokeObjectURL(u)
}

export function getCompanyLogos(): CompanyLogos {
  return current
}

export function subscribeCompanyLogos(l: () => void): () => void {
  listeners.add(l)
  return () => listeners.delete(l)
}

/** URL del logo de la compañía para la pieza y el tema (variante propia del tema o, si falta, la otra); null = usar el de Teikem. */
export function pickLogoUrl(urls: CompanyLogos['urls'], kind: LogoKind, theme: Theme): string | null {
  const own = theme === 'dark' ? (`${kind}-inverted` as const) : kind
  const other = theme === 'dark' ? kind : (`${kind}-inverted` as const)
  return urls[own] ?? urls[other] ?? null
}

/** Los logos de la compañía; el almacén cambia al subir o quitar uno y la interfaz se vuelve a pintar sin recargar. */
export function useCompanyLogos(): CompanyLogos {
  return useSyncExternalStore(subscribeCompanyLogos, getCompanyLogos, getCompanyLogos)
}
