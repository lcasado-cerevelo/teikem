import { useCallback, useSyncExternalStore } from 'react'
import { getFormatSettings, subscribeFormat } from '../format/store'
import { getLang, subscribeLang, translate, type Lang, type TParams } from './i18n'

/**
 * Idioma actual; re-renderiza el componente cuando cambia. También cuando cambian los formatos de la compañía (región y
 * formatos, `kernel/format`): quien pinta fechas o números con el idioma se vuelve a pintar con los formatos nuevos sin
 * recargar ni desmontar.
 */
export function useLang(): Lang {
  useSyncExternalStore(subscribeFormat, getFormatSettings, getFormatSettings)
  return useSyncExternalStore(subscribeLang, getLang, getLang)
}

/**
 * `const t = useT()` → `t('clave', { param })`. Se vuelve a pintar al cambiar el idioma sin desmontar la pantalla. La función
 * cambia de identidad también al cambiar los formatos de la compañía (los números de los textos llevan sus separadores), así
 * que lo memorizado con `[t]` (columnas de tablas, esquemas) se recalcula con los formatos nuevos.
 */
export function useT(): (key: string, params?: TParams) => string {
  const lang = useLang()
  const format = useSyncExternalStore(subscribeFormat, getFormatSettings, getFormatSettings)
  return useCallback((key: string, params?: TParams) => translate(lang, key, params, format), [lang, format])
}
