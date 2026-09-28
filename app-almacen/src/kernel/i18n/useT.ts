import { useCallback, useSyncExternalStore } from 'react'

import { getLang, type Lang, setLang, subscribeLang, translate, type TParams } from './i18n'

/** Hook de traducción: se vuelve a renderizar cuando cambia el idioma (setLang), sin recargar la pantalla. */
export function useT(): { t: (key: string, params?: TParams) => string; lang: Lang; setLang: (lang: Lang) => void } {
  const lang = useSyncExternalStore(subscribeLang, getLang, getLang)
  const t = useCallback((key: string, params?: TParams) => translate(lang, key, params), [lang])
  return { t, lang, setLang }
}
