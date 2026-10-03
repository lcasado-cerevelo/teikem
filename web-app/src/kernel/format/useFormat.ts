// Hooks de los formatos de la compañía: leen los ajustes vigentes (`store.ts`, alimentado por `FormatProvider`) y el idioma
// de la interfaz, y devuelven los formateadores ya atados a ellos. Se vuelven a pintar al cambiar cualquiera de los dos.
import { useMemo, useSyncExternalStore } from 'react'
import { useLang } from '../i18n/useT'
import {
  formatDate,
  formatDateLong,
  formatDateTime,
  formatDayMonth,
  formatMoney,
  formatNumber,
  formatTime,
  formatTimeOfDay,
  todayIso,
  type DateValue,
  type MoneyOptions,
} from './format'
import { formatPhone, formatPhoneInput, isValidPhone, normalizePhone, phonePlaceholder } from './phone'
import type { FormatSettings } from './settings'
import { getFormatSettings, subscribeFormat } from './store'

/** Ajustes de formato vigentes de la compañía (Puerto Rico mientras cargan). */
export function useFormatSettings(): FormatSettings {
  return useSyncExternalStore(subscribeFormat, getFormatSettings, getFormatSettings)
}

export interface Formatters {
  settings: FormatSettings
  lang: string
  number: (n: number, opts?: Intl.NumberFormatOptions) => string
  money: (n: number, opts?: MoneyOptions) => string
  date: (v: DateValue) => string
  dayMonth: (v: DateValue) => string
  time: (v: DateValue, opts?: { seconds?: boolean }) => string
  timeOfDay: (hhmm: string | null | undefined) => string
  dateTime: (v: DateValue) => string
  dateLong: (v: DateValue, opts?: Intl.DateTimeFormatOptions) => string
  phone: (v: string | null | undefined) => string
  phoneInput: (v: string | null | undefined) => string
  normalizePhone: (v: string | null | undefined) => string
  isValidPhone: (v: string | null | undefined) => boolean
  phonePlaceholder: () => string
  /** "Hoy" 'YYYY-MM-DD' en la zona de la compañía. */
  today: (now?: Date) => string
}

/** `const f = useFormat()` → `f.money(12.5)`, `f.date(dto.createdAtUtc)`, `f.time(new Date())`, `f.today()`… */
export function useFormat(): Formatters {
  const s = useFormatSettings()
  const lang = useLang()
  return useMemo<Formatters>(
    () => ({
      settings: s,
      lang,
      number: (n, opts) => formatNumber(n, opts, s),
      money: (n, opts) => formatMoney(n, lang, opts, s),
      date: (v) => formatDate(v, s),
      dayMonth: (v) => formatDayMonth(v, s),
      time: (v, opts) => formatTime(v, lang, opts, s),
      timeOfDay: (hhmm) => formatTimeOfDay(hhmm, lang, s),
      dateTime: (v) => formatDateTime(v, lang, s),
      dateLong: (v, opts) => formatDateLong(v, lang, opts, s),
      phone: (v) => formatPhone(v, s),
      phoneInput: (v) => formatPhoneInput(v, s),
      normalizePhone: (v) => normalizePhone(v, s),
      isValidPhone: (v) => isValidPhone(v, s),
      phonePlaceholder: () => phonePlaceholder(s),
      today: (now) => todayIso(now, s),
    }),
    [s, lang],
  )
}
