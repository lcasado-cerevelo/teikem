// Rutas de los archivos de la marca Teikem (Lote F8a P7): copia del paquete `Logos/` en `public/brand/`.
// Regla de la maqueta (`brandLogoFor`): tema oscuro → variante `-inv`; claro → la normal; el idioma elige el lema.
import type { Lang } from '../i18n/i18n'
import type { Theme } from './theme'

const BRAND_DIR = `${import.meta.env.BASE_URL}brand/`

/** Lockup horizontal con lema, por idioma (el lema va en el idioma de la interfaz). */
const LOCKUP_WITH_TAGLINE: Record<Lang, string> = {
  es: 'teikem-1b-horizontal-tagline-es',
  en: 'teikem-1a-horizontal-tagline-en',
}
const LOCKUP_NO_TAGLINE = 'teikem-2-horizontal-notagline'

/** Símbolo (hexágono + T) sin texto: `Logos/teikem-symbol.svg`, fuente única del ícono. */
export const BRAND_SYMBOL_SRC = `${BRAND_DIR}teikem-symbol.svg`

/** Ruta del lockup para un idioma y tema (lógica pura). `tagline: false` = sin lema (igual en ambos idiomas). */
export function brandLockupSrc(lang: Lang, theme: Theme, tagline = true): string {
  const base = tagline ? LOCKUP_WITH_TAGLINE[lang] : LOCKUP_NO_TAGLINE
  return `${BRAND_DIR}${base}${theme === 'dark' ? '-inv' : ''}.svg`
}
