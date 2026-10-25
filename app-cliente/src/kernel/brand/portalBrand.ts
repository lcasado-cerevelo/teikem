// La marca de la compañía en el portal (2026-10-22): al entrar se leen sus colores (los de Ajustes → Marca del equipo), su nombre y su logo; al salir se quitan. Si algo falla, el portal se queda con la marca de siempre.
import { useSyncExternalStore } from 'react'
import { Asset } from 'expo-asset'
import { Platform } from 'react-native'

import { api, unwrap } from '../api/client'
import { setBrand } from '../ui/theme'
import { parseBranding } from '../ui/brandTheme'
import { rememberLastTenant } from './publicBrand'

export interface PortalBrandInfo {
  /** Ranuras de logo que subió la compañía (`lockup`, `lockup-inverted`, `mark`, `mark-inverted`); vacío = sin logo propio. */
  logoSlots: string[]
  companyName: string | null
  /** Imagen lista para pintar (data URI) o null si la compañía no subió logo (o es SVG en el celular, que no lo pinta). */
  logoUri: string | null
}

const EMPTY: PortalBrandInfo = { logoSlots: [], companyName: null, logoUri: null }
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

/** Anota la compañía de la sesión para la pantalla de entrada de la próxima vez. Nunca lanza. */
async function rememberTenantOfSession(): Promise<void> {
  try {
    const res = await (api as unknown as { GET: (path: string) => Promise<{ data?: { tenantPublicId?: string | null } }> }).GET('/api/v1/auth/me')
    await rememberLastTenant(res.data?.tenantPublicId)
  } catch {
    // sin recordarla: la próxima vez manda el dominio o la única compañía
  }
}

/** Lee la marca de la compañía de la sesión y la aplica. Nunca lanza. */
export async function loadPortalBrand(): Promise<void> {
  try {
    const [brand, me] = await Promise.all([unwrap(api.GET('/api/v1/portal/brand')), unwrap(api.GET('/api/v1/portal/me'))])
    setBrand(parseBranding(brand.brandingJson))
    const slot = pickLogoSlot(brand.logoSlots ?? [])
    const logoUri = slot ? await loadLogo(slot).catch(() => null) : null
    info = { logoSlots: [...(brand.logoSlots ?? [])], companyName: me.currentClient?.tenantName ?? null, logoUri }
    emit()
    // la próxima vez que se abra la pantalla de entrada saldrá el logo de esta compañía (Lote 219)
    void rememberTenantOfSession()
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

/**
 * El logo para el encabezado de un Excel o PDF (hoja BLANCA): el de la compañía —el normal, que se lee sobre blanco; si solo subió el invertido (letras claras), va sobre una placa azul marino— o,
 * si la compañía no subió ninguno, el de Teikem. null si ni eso se pudo leer (el archivo sale igual, sin logo). Nunca lanza.
 */
export async function loadPdfLogoSource(): Promise<{ src: string; plate: 'navy' | null } | null> {
  try {
    const slots = info.logoSlots
    for (const s of ['lockup', 'mark']) {
      if (!slots.includes(s)) continue
      const src = await loadLogo(s).catch(() => null)
      if (src) return { src, plate: null }
    }
    for (const s of ['lockup-inverted', 'mark-inverted']) {
      if (!slots.includes(s)) continue
      const src = await loadLogo(s).catch(() => null)
      if (src) return { src, plate: 'navy' }
    }
    // sin logo propio: el de Teikem (versión para fondo claro)
    const uri = Asset.fromModule(require('../../../assets/teikem-pdf-logo.png')).uri
    return uri ? { src: uri, plate: null } : null
  } catch {
    return null
  }
}
