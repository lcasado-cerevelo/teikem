// Aplica los colores de la marca de la compañía (`Tenant.BrandingJson`, Ajustes → Marca) sobre las variables de
// `tokens.css` en <html>, según el tema claro/oscuro activo. Con la marca de siempre (preset Teikem) no toca nada. Va en
// línea en <html> para ganar sobre `:root` y `[data-theme]` sin pelear con especificidad (como `applyTenantBrand` de la
// maqueta). Ajustes → Marca pone una VISTA PREVIA (`setBrandPreview`, brandPreview.ts) mientras se edita, sin guardar.
import { useLayoutEffect, useMemo, useSyncExternalStore } from 'react'
import { useTenantSettings } from '../catalogs/api'
import { applyBrandVars, getBrandPreview, subscribeBrandPreview } from './brandPreview'
import { parseBranding } from './brandTheme'
import { useTheme } from './theme'

/** Lo monta la sesión una vez: la marca guardada de la compañía (o la vista previa de Ajustes → Marca). */
export function TenantBrand({ enabled }: { enabled: boolean }) {
  const { data } = useTenantSettings(enabled)
  const theme = useTheme()
  const draft = useSyncExternalStore(subscribeBrandPreview, getBrandPreview, getBrandPreview)
  const saved = useMemo(() => parseBranding(enabled ? data?.brandingJson : null), [enabled, data?.brandingJson])
  const effective = draft ?? saved
  useLayoutEffect(() => {
    if (typeof document === 'undefined') return
    applyBrandVars(document.documentElement, effective, theme === 'light' ? 'light' : 'dark')
  }, [effective, theme])
  return null
}
