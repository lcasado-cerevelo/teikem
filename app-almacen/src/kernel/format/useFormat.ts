// Hook de formatos: las funciones de format.ts/phone.ts ya atadas a los formatos vigentes de la compañía y al idioma del
// aparato. La pantalla se vuelve a pintar sola cuando llegan formatos nuevos (sincronización) o cambia el idioma.
import { useMemo, useSyncExternalStore } from 'react'

import { useT } from '../i18n/useT'
import {
  datePlaceholder,
  formatDate,
  formatDateTime,
  formatDayMonth,
  formatMoney,
  formatNumber,
  formatQuantity,
  formatTime,
  formatWhen,
  parseDateInput,
  todayIso,
  type DateValue,
  type MoneyOptions,
  type NumberOptions,
} from './format'
import { formatPhone } from './phone'
import type { FormatSettings } from './settings'
import { getFormatSettings, subscribeFormat } from './store'

/** Formatos vigentes como estado de React (cambia de referencia solo cuando cambia algún valor o la compañía). */
export function useFormatSettings(): FormatSettings {
  return useSyncExternalStore(subscribeFormat, getFormatSettings, getFormatSettings)
}

export function useFormat() {
  const s = useFormatSettings()
  const { lang } = useT()
  return useMemo(
    () => ({
      settings: s,
      number: (n: number, opts?: NumberOptions) => formatNumber(n, opts, s),
      qty: (n: number) => formatQuantity(n, s),
      money: (n: number, opts?: MoneyOptions) => formatMoney(n, opts, s),
      date: (v: DateValue) => formatDate(v, s),
      dayMonth: (v: DateValue) => formatDayMonth(v, s),
      time: (v: DateValue, opts?: { seconds?: boolean }) => formatTime(v, lang, opts, s),
      dateTime: (v: DateValue) => formatDateTime(v, lang, s),
      when: (v: DateValue, now?: Date) => formatWhen(v, lang, now, s),
      today: (now?: Date) => todayIso(now, s),
      phone: (v: string | null | undefined) => formatPhone(v, s),
      datePlaceholder: () => datePlaceholder(lang, s),
      parseDate: (text: string) => parseDateInput(text, s),
    }),
    [s, lang],
  )
}
