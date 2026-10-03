// Hora de la compañía: partes de fecha y hora de un instante en una zona IANA (espejo de `zonedParts` de la web,
// web-app/src/kernel/api/tenantZone.ts). Se usa `Intl.DateTimeFormat` SOLO para convertir de zona (Hermes lo trae en
// Android); el texto final (orden de la fecha, 12/24 h, a. m./p. m.) lo arma format.ts a mano, así no depende de los datos
// de idioma del motor. Si el motor no puede con la zona (sin `Intl` o zona desconocida), se usa la hora del aparato: es
// el mejor respaldo posible y en los Zebra de la compañía coincide con la de la compañía.

export interface ZonedParts {
  year: number
  /** 1 a 12 */
  month: number
  day: number
  /** 0 a 23 */
  hour: number
  minute: number
  second: number
}

const formatters = new Map<string, Intl.DateTimeFormat | null>()

function formatterFor(timeZone: string): Intl.DateTimeFormat | null {
  if (formatters.has(timeZone)) return formatters.get(timeZone) ?? null
  let f: Intl.DateTimeFormat | null = null
  try {
    f = new Intl.DateTimeFormat('en-US', {
      timeZone,
      hour12: false,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    })
  } catch {
    f = null
  }
  formatters.set(timeZone, f)
  return f
}

function deviceParts(d: Date): ZonedParts {
  return { year: d.getFullYear(), month: d.getMonth() + 1, day: d.getDate(), hour: d.getHours(), minute: d.getMinutes(), second: d.getSeconds() }
}

/** Partes de `instant` en `timeZone`. */
export function zonedParts(instant: Date, timeZone: string): ZonedParts {
  const f = formatterFor(timeZone)
  if (!f) return deviceParts(instant)
  try {
    let get: (type: string) => number
    if (typeof f.formatToParts === 'function') {
      const parts = f.formatToParts(instant)
      get = (type) => Number(parts.find((p) => p.type === type)?.value ?? NaN)
    } else {
      // "10/02/2026, 21:30:05" (en-US): respaldo si el motor no trae formatToParts
      const m = /(\d{1,2})\/(\d{1,2})\/(\d{4}),?\s+(\d{1,2}):(\d{2}):(\d{2})/.exec(f.format(instant))
      if (!m) return deviceParts(instant)
      const map: Record<string, number> = { month: +m[1], day: +m[2], year: +m[3], hour: +m[4], minute: +m[5], second: +m[6] }
      get = (type) => map[type] ?? NaN
    }
    const p: ZonedParts = { year: get('year'), month: get('month'), day: get('day'), hour: get('hour') % 24, minute: get('minute'), second: get('second') }
    return Object.values(p).some((v) => Number.isNaN(v)) ? deviceParts(instant) : p
  } catch {
    return deviceParts(instant)
  }
}

const pad2 = (n: number) => String(n).padStart(2, '0')

/** Día 'YYYY-MM-DD' de `instant` en `timeZone` (NO UTC: a las 9:30 p. m. en Puerto Rico `toISOString()` ya da mañana). */
export function zonedDay(instant: Date, timeZone: string): string {
  const p = zonedParts(instant, timeZone)
  return `${p.year}-${pad2(p.month)}-${pad2(p.day)}`
}
