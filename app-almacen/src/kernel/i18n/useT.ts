import { useCallback, useSyncExternalStore } from 'react'

import { getFormatSettings, subscribeFormat } from '../format/store'
import { getLang, type Lang, setLang, subscribeLang, translate, type TParams } from './i18n'

/** Hook de traducción: se vuelve a renderizar cuando cambia el idioma (setLang), sin recargar la pantalla, y cuando llegan
 *  formatos nuevos de la compañía (los números dentro de un texto llevan sus separadores). */
export function useT(): { t: (key: string, params?: TParams) => string; lang: Lang; setLang: (lang: Lang) => void } {
  const lang = useSyncExternalStore(subscribeLang, getLang, getLang)
  const format = useSyncExternalStore(subscribeFormat, getFormatSettings, getFormatSettings)
  // `format` no se usa dentro: translate() lee los formatos vigentes; está en las dependencias para renovar `t`
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const t = useCallback((key: string, params?: TParams) => translate(lang, key, params), [lang, format])
  return { t, lang, setLang }
}
