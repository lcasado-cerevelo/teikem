// La marca de la compañía en el portal (2026-10-22): al entrar se leen sus colores (los de Ajustes → Marca del equipo), su nombre y su logo; al salir se quitan. Si algo falla, el portal se queda con la marca de siempre.
import { useSyncExternalStore } from 'react'
import { Platform } from 'react-native'

import { api, unwrap } from '../api/client'
import { setBrand } from '../ui/theme'
import { parseBranding } from '../ui/brandTheme'

export interface PortalBrandInfo {
  companyName: string | null
  /** Imagen lista para pintar (data URI) o null si la compañía no subió logo (o es SVG en el celular, que no lo pinta). */
  logoUri: string | null
}

const EMPTY: PortalBrandInfo = { companyName: null, logoUri: null }
let info: PortalBrandInfo = EMPTY
const listeners = new Set<() => void>()
const emit = () => listeners.forEach((l) => l())

export const getPortalBrandInfo = (): PortalBrandInfo => info
const subscribe = (l: () => void) => {
  listeners.add(l)
  return () => listeners.delete(l)
}
export function usePortalBrandInfo(): PortalBrandInfo {
  return useSyncExternalStore(subscribe, getPortalBrandInfo, getPortalBrandInfo)
}

/** Ranura del logo para la barra (fondo oscuro): el invertido primero, luego el normal, luego las marcas cuadradas. */
export function pickLogoSlot(slots: readonly string[]): string | null {
  for (const s of ['lockup-inverted', 'lockup', 'mark-inverted', 'mark']) if (slots.includes(s)) return s
  return null
}

function blobToDataUri(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const r = new FileReader()
    r.onload = () => resolve(String(r.result))
    r.onerror = () => reject(new Error('No se pudo leer el logo.'))
    r.readAsDataURL(blob)
  })
}

async function loadLogo(slot: string): Promise<string | null> {
  const res = await api.GET('/api/v1/portal/brand/logos/{slot}', { params: { path: { slot } }, parseAs: 'blob' })
  const blob = res.data as Blob | undefined
  if (!res.response.ok || !blob) return null
  // el celular no pinta SVG con <Image>: sin logo antes que roto
  if (Platform.OS !== 'web' && (blob.type || '').includes('svg')) return null
  return blobToDataUri(blob)
}

/** Lee la marca de la compañía de la sesión y la aplica. Nunca lanza. */
export async function loadPortalBrand(): Promise<void> {
  try {
    const [brand, me] = await Promise.all([unwrap(api.GET('/api/v1/portal/brand')), unwrap(api.GET('/api/v1/portal/me'))])
    setBrand(parseBranding(brand.brandingJson))
    const slot = pickLogoSlot(brand.logoSlots ?? [])
    const logoUri = slot ? await loadLogo(slot).catch(() => null) : null
    info = { companyName: me.currentClient?.tenantName ?? null, logoUri }
    emit()
  } catch {
    // sin marca: se queda la de siempre
  }
}

/** Al salir: la marca de siempre. */
export function clearPortalBrand(): void {
  setBrand(null)
  info = EMPTY
  emit()
}
