// Formato único de números y dinero de la interfaz (pedido del dueño del producto, 2026-09-30): miles SIEMPRE con coma y punto
// decimal, como se usa en Puerto Rico, aunque la interfaz esté en español ("es" a secas da "61.023" y no agrupa "1250", que el
// usuario lee como decimales). El dinero lleva el signo de dólar y dos decimales. Todo número que se pinte pasa por aquí.

/** Locale de números para un idioma de la interfaz: español → es-PR (61,023.5 · $1,234.50); inglés → en-US. */
export function numberLocale(lang: string | null | undefined): string {
  const base = (lang ?? '').toLowerCase().split('-')[0]
  return base === 'en' ? 'en-US' : 'es-PR'
}

/** Moneda de la compañía (hoy todas en dólares de EE. UU.). */
export const TENANT_CURRENCY = 'USD'

/** Cantidad con coma de miles y hasta 3 decimales solo si los tiene (unidades enteras sin ".0"): 61023 → "61,023". */
export function formatQuantity(n: number, lang: string | null | undefined, opts: Intl.NumberFormatOptions = {}): string {
  return new Intl.NumberFormat(numberLocale(lang), { maximumFractionDigits: 3, useGrouping: 'always', ...opts }).format(n)
}

/** Dinero: "$1,234.50" (o "-$12.00"); `unitPrice` admite hasta 4 decimales para costos unitarios finos. */
export function formatMoney(
  n: number,
  lang: string | null | undefined,
  opts: { currency?: string | null; unitPrice?: boolean; signed?: boolean } = {},
): string {
  return new Intl.NumberFormat(numberLocale(lang), {
    style: 'currency',
    currency: opts.currency || TENANT_CURRENCY,
    currencyDisplay: 'narrowSymbol',
    minimumFractionDigits: 2,
    maximumFractionDigits: opts.unitPrice ? 4 : 2,
    useGrouping: 'always',
    signDisplay: opts.signed ? 'exceptZero' : 'auto',
  }).format(n)
}
