// Formato de números de la app (mismo criterio que la web, kernel/i18n/numberFormat.ts): coma de miles y punto decimal, como se
// usa en Puerto Rico, en español y en inglés. Sin Intl a propósito: el motor de React Native no siempre trae los datos de idioma.

/** Cantidad con coma de miles y hasta 3 decimales solo si los tiene: 61023 → "61,023"; 1.5 → "1.5"; -1250 → "-1,250". */
export function formatQuantity(n: number): string {
  if (!Number.isFinite(n)) return String(n)
  const rounded = Math.round(n * 1000) / 1000
  const [intPart, decPart] = Math.abs(rounded).toString().split('.')
  const grouped = intPart.replace(/\B(?=(\d{3})+(?!\d))/g, ',')
  return `${rounded < 0 ? '-' : ''}${grouped}${decPart ? `.${decPart}` : ''}`
}

/** Parámetros que son identificadores (número de documento, id, código): se pintan tal cual. */
export const RAW_NUMBER_PARAMS: ReadonlySet<string> = new Set(['id', 'number', 'code', 'order', 'ref', 'serial', 'lot', 'sku'])
