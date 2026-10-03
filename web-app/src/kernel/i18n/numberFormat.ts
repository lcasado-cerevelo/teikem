// Formato único de números y dinero de la interfaz. Desde Región y formatos (lote F9) los separadores, el símbolo, su
// posición y los decimales de la moneda son de la COMPAÑÍA (`kernel/format`, alimentado por `GET /tenant/settings`; Puerto
// Rico por defecto: "61,023.5" · "$1,234.50"), no del idioma: el idioma de la interfaz ya no cambia los números. Este archivo
// conserva las firmas de siempre (`formatQuantity(n, lang)`, `formatMoney(n, lang)`) para no tocar cada llamada.
import { formatMoney as formatMoneyWith, formatNumber, type MoneyOptions } from '../format/format'
import { getFormatSettings } from '../format/store'

/**
 * Locale de `Intl` para nombres de meses/días según el idioma de la interfaz (es → es-PR, en → en-US). NO lo use para
 * números: los separadores son de la compañía (`formatQuantity`/`formatNumber`).
 */
export function numberLocale(lang: string | null | undefined): string {
  const base = (lang ?? '').toLowerCase().split('-')[0]
  return base === 'en' ? 'en-US' : 'es-PR'
}

/** Moneda POR DEFECTO (Puerto Rico); la vigente de la compañía es `getFormatSettings().currencyCode`. */
export const TENANT_CURRENCY = 'USD'

/** Cantidad con los miles de la compañía y hasta 3 decimales solo si los tiene (61023 → "61,023"). `lang` no cambia nada. */
export function formatQuantity(n: number, _lang?: string | null, opts: Intl.NumberFormatOptions = {}): string {
  return formatNumber(n, opts, getFormatSettings())
}

/** Dinero con el símbolo y los decimales de la compañía: "$1,234.50" (o "-$12.00"); `unitPrice` admite hasta 4 decimales. */
export function formatMoney(n: number, lang?: string | null, opts: MoneyOptions = {}): string {
  return formatMoneyWith(n, lang, opts, getFormatSettings())
}
