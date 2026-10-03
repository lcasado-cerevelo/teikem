// Aplica la marca de la compañía (`Tenant.BrandingJson` y logos, Ajustes → Marca).
// Colores: sobrescribe las variables de `tokens.css` en <html>, según el tema claro/oscuro activo. Con la marca de siempre (preset
// Teikem) no toca nada. Va en línea en <html> para ganar sobre `:root` y `[data-theme]` sin pelear con especificidad (como
// `applyTenantBrand` de la maqueta). Ajustes → Marca pone una VISTA PREVIA (`setBrandPreview`, brandPreview.ts) mientras se edita.
// Logos: descarga el archivo de cada ranura (necesita la sesión) y deja una URL de objeto por ranura en `brandLogos.ts`; el lockup
// y la marca cuadrada de la barra lateral las usan. Sin sesión o sin logos, el respaldo es el logo de Teikem.
import { useEffect, useLayoutEffect, useMemo, useSyncExternalStore } from 'react'
import { useTenantSettings } from '../catalogs'
import { applyBrandVars, getBrandPreview, subscribeBrandPreview } from './brandPreview'
import { NO_COMPANY_LOGOS, setCompanyLogos, type LogoSlot } from './brandLogos'
import { fetchBrandLogoBlob, useBrandLogoList } from './brandLogosApi'
import { parseBranding } from './brandTheme'
import { useTheme } from './theme'

/** Lo monta la sesión una vez: la marca guardada de la compañía (o la vista previa de Ajustes → Marca) y sus logos. */
export function TenantBrand({ enabled }: { enabled: boolean }) {
  const { data } = useTenantSettings(enabled)
  const logoList = useBrandLogoList(enabled)
  const theme = useTheme()
  const draft = useSyncExternalStore(subscribeBrandPreview, getBrandPreview, getBrandPreview)
  const saved = useMemo(() => parseBranding(enabled ? data?.brandingJson : null), [enabled, data?.brandingJson])
  const effective = draft ?? saved
  useLayoutEffect(() => {
    if (typeof document === 'undefined') return
    applyBrandVars(document.documentElement, effective, theme === 'light' ? 'light' : 'dark')
  }, [effective, theme])

  // Los archivos se vuelven a bajar solo si cambia la lista (react-query conserva la identidad si no cambió; el ETag es el SHA-256).
  const logos = enabled ? logoList.data : undefined
  const companyName = enabled ? (data?.name ?? null) : null
  useEffect(() => {
    if (!logos || logos.length === 0) {
      setCompanyLogos(NO_COMPANY_LOGOS)
      return
    }
    let cancelled = false
    void (async () => {
      const urls: Partial<Record<LogoSlot, string>> = {}
      await Promise.all(
        logos.map(async (l) => {
          try {
            urls[l.slot as LogoSlot] = URL.createObjectURL(await fetchBrandLogoBlob(l.slot as LogoSlot))
          } catch {
            // sin ese archivo se usa la otra variante o el logo de Teikem
          }
        }),
      )
      if (cancelled) {
        if (typeof URL.revokeObjectURL === 'function') Object.values(urls).forEach((u) => URL.revokeObjectURL(u))
        return
      }
      setCompanyLogos({ urls, name: companyName })
    })()
    return () => {
      cancelled = true
    }
  }, [logos, companyName])

  // al desmontar (cerrar sesión) se vuelve al respaldo
  useEffect(() => () => setCompanyLogos(NO_COMPANY_LOGOS), [])
  return null
}
