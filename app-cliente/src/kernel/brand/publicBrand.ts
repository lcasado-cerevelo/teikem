// La marca antes de entrar (Lote 219, pedido de Luis): el logo de la compañía arriba de la pantalla de entrada. Qué compañía, en este orden (lo decide el SERVIDOR, igual que en la web del personal):
//   1. la última con la que entró este aparato o navegador (se recuerda su identificador público);  2. la que tiene como dominio o subdominio la dirección con la que se abrió la página;
//   3. si la instalación tiene una sola compañía, esa;  4. si no, el logo de Teikem.
// Solo se recuerda el identificador público (nada secreto). Un error (sin red, servidor viejo) deja el logo de Teikem.
import { useSyncExternalStore } from 'react'
import { Platform } from 'react-native'

import { getApiBaseUrl } from '../api/client'
import { storeGet, storeSet } from '../storage'

const LAST_TENANT_KEY = 'teikem.lastTenant'

export type LogoSlot = 'lockup' | 'lockup-inverted' | 'mark' | 'mark-inverted'

export interface PublicBrandInfo {
  companyName: string | null
  /** URL pública de cada ranura de logo que subió la compañía. */
  logos: Partial<Record<LogoSlot, string>>
}

const EMPTY: PublicBrandInfo = { companyName: null, logos: {} }
let info: PublicBrandInfo = EMPTY
const listeners = new Set<() => void>()
const subscribe = (l: () => void) => {
  listeners.add(l)
  return () => listeners.delete(l)
}
export const getPublicBrand = (): PublicBrandInfo => info
export function usePublicBrand(): PublicBrandInfo {
  return useSyncExternalStore(subscribe, getPublicBrand, getPublicBrand)
}

/** Recuerda la compañía con la que se está trabajando (la llama la sesión al cargar su marca). */
export async function rememberLastTenant(publicId: string | null | undefined): Promise<void> {
  if (publicId) await storeSet(LAST_TENANT_KEY, publicId)
}

/** Ranura del logo según el fondo de la pantalla: letras claras (invertido) sobre oscuro; si no hay, el otro; la marca cuadrada al final. */
export function pickPublicSlot(slots: readonly string[], dark: boolean): LogoSlot | null {
  const order: LogoSlot[] = dark ? ['lockup-inverted', 'lockup', 'mark-inverted', 'mark'] : ['lockup', 'lockup-inverted', 'mark', 'mark-inverted']
  return order.find((s) => slots.includes(s)) ?? null
}

interface PublicBrandResponse {
  tenantPublicId?: string | null
  name?: string | null
  logos?: { slot?: string; stampUtc?: string }[] | null
}

/** Pide la marca pública. Nunca lanza. */
export async function loadPublicBrand(signal?: AbortSignal): Promise<void> {
  try {
    const base = getApiBaseUrl()
    const params = new URLSearchParams()
    if (Platform.OS === 'web' && typeof window !== 'undefined') params.set('host', window.location.hostname)
    const last = await storeGet(LAST_TENANT_KEY)
    if (last) params.set('tenant', last)
    const res = await fetch(`${base}/api/v1/public/brand?${params}`, { signal, headers: { Accept: 'application/json' } })
    if (!res.ok) return
    const brand = (await res.json()) as PublicBrandResponse
    if (!brand.tenantPublicId) return
    const logos: PublicBrandInfo['logos'] = {}
    for (const l of brand.logos ?? []) {
      if (!l.slot) continue
      const q = new URLSearchParams({ tenant: brand.tenantPublicId, v: l.stampUtc ?? '' })
      if (params.get('host')) q.set('host', params.get('host') as string)
      logos[l.slot as LogoSlot] = `${base}/api/v1/public/brand/logo/${l.slot}?${q}`
    }
    if (signal?.aborted) return
    info = { companyName: brand.name ?? null, logos }
    listeners.forEach((l) => l())
  } catch {
    // sin marca: queda la de Teikem
  }
}
