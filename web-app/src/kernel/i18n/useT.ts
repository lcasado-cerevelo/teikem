import { useCallback, useSyncExternalStore } from 'react'
import { getLang, subscribeLang, translate, type Lang, type TParams } from './i18n'

/** Idioma actual; re-renderiza el componente cuando cambia. */
export function useLang(): Lang {
  return useSyncExternalStore(subscribeLang, getLang, getLang)
}

/** `const t = useT()` → `t('clave', { param })`. Se vuelve a pintar al cambiar el idioma sin desmontar la pantalla. */
export function useT(): (key: string, params?: TParams) => string {
  const lang = useLang()
  return useCallback((key: string, params?: TParams) => translate(lang, key, params), [lang])
}
