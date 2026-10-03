// Lectura de los logos de la compañía: lista (`GET /tenant/brand/logos`: ranura, tipo, tamaño, ETag) y archivo por ranura
// (`GET /tenant/brand/logos/{slot}`, binario con ETag: el navegador revalida con If-None-Match). Solo exige sesión.
import { useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../api/client'
import type { components } from '../api/schema'
import type { LogoSlot } from './brandLogos'

export type BrandLogoDto = components['schemas']['BrandLogoDto']

export const brandLogoKeys = {
  list: ['/api/v1/tenant/brand/logos'] as const,
}

/** Los logos que tiene la compañía (sin el archivo). Un error (p. ej. sin red) deja el respaldo de Teikem, sin sacar de la pantalla. */
export function useBrandLogoList(enabled = true) {
  return useQuery({
    queryKey: brandLogoKeys.list,
    queryFn: (): Promise<BrandLogoDto[]> => unwrap(api.GET('/api/v1/tenant/brand/logos')),
    enabled,
    meta: { handleAccessDenied: false },
  })
}

/** El archivo de una ranura como Blob. */
export async function fetchBrandLogoBlob(slot: LogoSlot): Promise<Blob> {
  return unwrap<Blob>(api.GET('/api/v1/tenant/brand/logos/{slot}', { params: { path: { slot } }, parseAs: 'blob' }) as never)
}
