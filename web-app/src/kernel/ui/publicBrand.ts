// Logo en la pantalla de entrada (sin sesión), 2026-10-22 (Luis). Qué compañía se muestra, en este orden:
//   1. la última con la que entró esta computadora (por login o al cambiarla en el encabezado) — se recuerda su identificador público;
//   2. si no, la que tiene como dominio (o subdominio) la dirección con la que se abrió la página — lo decide el servidor;
//   3. si no, el logo de Teikem.
// Solo se recuerda el identificador público (nada secreto); sin almacenamiento del navegador simplemente cae al dominio.
import { useEffect } from 'react'
import { API_BASE_URL } from '../api/client'
import { setCompanyLogos, type LogoSlot } from './brandLogos'

const LAST_TENANT_KEY = 'teikem.lastTenant'

export function readLastTenant(): string | null {
  try {
    return window.localStorage.getItem(LAST_TENANT_KEY)
  } catch {
    return null
  }
}

/** Recuerda la compañía con la que se está trabajando (la llama la sesión al cargar `me`: sirve tanto al entrar como al cambiar de compañía). */
export function rememberLastTenant(publicId: string | null | undefined): void {
  if (!publicId) return
  try {
    window.localStorage.setItem(LAST_TENANT_KEY, publicId)
  } catch {
    // sin almacenamiento: la próxima vez manda el dominio
  }
}

interface PublicBrandResponse {
  source?: string
  tenantPublicId?: string | null
  name?: string | null
  logos?: { slot?: string; stampUtc?: string }[] | null
}

/** Pide la marca pública y deja las URL de sus logos en el almacén de logos; sin marca no toca nada (queda el logo de Teikem). */
export async function loadPublicBrand(signal?: AbortSignal): Promise<void> {
  const params = new URLSearchParams({ host: window.location.hostname })
  const last = readLastTenant()
  if (last) params.set('tenant', last)
  const res = await fetch(`${API_BASE_URL}/api/v1/public/brand?${params}`, { signal, headers: { Accept: 'application/json' } })
  if (!res.ok) return
  const brand = (await res.json()) as PublicBrandResponse
  const logos = brand.logos ?? []
  if (!brand.tenantPublicId || logos.length === 0) return
  const urls: Partial<Record<LogoSlot, string>> = {}
  for (const l of logos) {
    if (!l.slot) continue
    const q = new URLSearchParams({ tenant: brand.tenantPublicId, host: window.location.hostname, v: l.stampUtc ?? '' })
    urls[l.slot as LogoSlot] = `${API_BASE_URL}/api/v1/public/brand/logo/${l.slot}?${q}`
  }
  if (!signal?.aborted) setCompanyLogos({ urls, name: brand.name ?? null })
}

/** Lo monta la pantalla de entrada: carga la marca pública una vez. Un error (sin red, servidor viejo) deja el logo de Teikem. */
export function usePublicBrand(): void {
  useEffect(() => {
    const ctl = new AbortController()
    loadPublicBrand(ctl.signal).catch(() => undefined)
    return () => ctl.abort()
  }, [])
}
