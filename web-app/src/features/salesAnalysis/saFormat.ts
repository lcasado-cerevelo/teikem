// Lote 241 — formatos del Análisis de ventas sobre `useFormat` (moneda, unidades, porcentajes y moneda compacta de los ejes).
import { useMemo } from 'react'
import { currencySymbol, formatNumber, useFormat, withCurrencySymbol, type FormatSettings } from '../../kernel/format'

/** Porcentaje de una fracción (0.2534 → '25.3%'); null → '—'. */
export function formatPct(fraction: number | null | undefined, s?: FormatSettings, decimals = 1): string {
  if (fraction === null || fraction === undefined || !Number.isFinite(fraction)) return '—'
  return `${formatNumber(fraction * 100, { minimumFractionDigits: decimals, maximumFractionDigits: decimals }, s)}%`
}

/** Porcentaje con signo (+12.0% / -3.5%); 0 sin signo. */
export function formatSignedPct(fraction: number | null | undefined, s?: FormatSettings, decimals = 1): string {
  if (fraction === null || fraction === undefined || !Number.isFinite(fraction)) return '—'
  const body = formatPct(Math.abs(fraction), s, decimals)
  const zero = !/[1-9]/.test(body)
  return zero ? body : `${fraction < 0 ? '-' : '+'}${body}`
}

/** Moneda compacta para los ejes de los gráficos ($58K, $1.2M). */
export function formatCompactMoney(n: number, s?: FormatSettings): string {
  const body = formatNumber(Math.abs(n), { notation: 'compact', maximumFractionDigits: 1 }, s)
  return withCurrencySymbol(body, n < 0 ? '-' : '', currencySymbol(undefined, s), s)
}

export interface SaFormat {
  money: (n: number | null | undefined) => string
  money0: (n: number | null | undefined) => string
  units: (n: number | null | undefined) => string
  dec1: (n: number | null | undefined) => string
  pct: (n: number | null | undefined) => string
  signedPct: (n: number | null | undefined) => string
  compactMoney: (n: number) => string
  date: (v: string | null | undefined) => string
}

/** Formateadores del análisis con los ajustes de la compañía (null → '—'). */
export function useSaFormat(): SaFormat {
  const f = useFormat()
  return useMemo(() => {
    const s = f.settings
    const dash = (fn: (n: number) => string) => (n: number | null | undefined) => (n === null || n === undefined || !Number.isFinite(n) ? '—' : fn(n))
    return {
      money: dash((n) => f.money(n)),
      money0: dash((n) => f.money(n, { decimals: 0 })),
      units: dash((n) => f.number(n, { maximumFractionDigits: 0 })),
      dec1: dash((n) => f.number(n, { minimumFractionDigits: 1, maximumFractionDigits: 1 })),
      pct: (n) => formatPct(n, s),
      signedPct: (n) => formatSignedPct(n, s),
      compactMoney: (n) => formatCompactMoney(n, s),
      date: (v) => (v ? f.date(v) : '—'),
    }
  }, [f])
}
