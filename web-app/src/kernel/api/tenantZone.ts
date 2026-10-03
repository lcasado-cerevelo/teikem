// Hora de la compañía (Lote 14, compartido desde el Lote 15): espejo de `TenantClock`/`LocalDay` del backend. "Hoy", los
// días de un rango y la agrupación por día de un gráfico se cuentan en la zona de la compañía: el día empieza a medianoche
// local, no UTC. Un solo punto para toda la web. Región y formatos (lote F9): la zona sale de los ajustes de la compañía
// (`Tenant.TimeZoneId`, `kernel/format`); sin ajustes, Puerto Rico.
import { tenantTimeZone } from '../format/store'
import { parseApiDate } from './dates'

export { tenantTimeZone }

/** Zona de Puerto Rico: la POR DEFECTO (sin ajustes). La vigente de la compañía es `tenantTimeZone()`. */
export const TENANT_TIME_ZONE = 'America/Puerto_Rico'

/** Desplazamiento (ms) de la zona respecto de UTC en ese instante (negativo al oeste). */
function zoneOffsetMs(utcMs: number, timeZone: string): number {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone,
    hourCycle: 'h23',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).formatToParts(new Date(utcMs))
  const get = (type: string) => Number(parts.find((p) => p.type === type)?.value ?? 0)
  const asUtc = Date.UTC(get('year'), get('month') - 1, get('day'), get('hour'), get('minute'), get('second'))
  return asUtc - Math.floor(utcMs / 1000) * 1000
}

const pad = (n: number) => String(n).padStart(2, '0')

/** Instante (ms) → 'YYYY-MM-DDTHH:mm' en la zona. */
export function zonedParts(ms: number, timeZone: string): string {
  const local = new Date(ms + zoneOffsetMs(ms, timeZone))
  return `${local.getUTCFullYear()}-${pad(local.getUTCMonth() + 1)}-${pad(local.getUTCDate())}T${pad(local.getUTCHours())}:${pad(local.getUTCMinutes())}`
}

/** Instante UTC del API → 'YYYY-MM-DDTHH:mm' en la zona de la compañía ('' si no se puede leer). */
export function zonedInputFromUtc(iso: string | null | undefined, timeZone = tenantTimeZone()): string {
  if (!iso) return ''
  const ms = parseApiDate(iso).getTime()
  if (Number.isNaN(ms)) return ''
  return zonedParts(ms, timeZone)
}

/** 'YYYY-MM-DDTHH:mm' en la zona de la compañía → ISO UTC ('…Z'), o null si está vacío o no es válido. */
export function utcFromZonedInput(text: string, timeZone = tenantTimeZone()): string | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2}))?$/.exec(text.trim())
  if (!m) return null
  const guess = Date.UTC(Number(m[1]), Number(m[2]) - 1, Number(m[3]), Number(m[4]), Number(m[5]), Number(m[6] ?? 0))
  if (Number.isNaN(guess)) return null
  // se corrige con el desplazamiento del instante resultante (cambio de horario, si la zona lo tuviera)
  let utc = guess - zoneOffsetMs(guess, timeZone)
  utc = guess - zoneOffsetMs(utc, timeZone)
  return new Date(utc).toISOString()
}

/**
 * Día 'YYYY-MM-DD' de un valor de fecha del API, como `AnalyticsEngine.GroupKey`/`LocalDay.DayOf`: un día de calendario
 * ('2026-09-30', un `DateOnly`) tal cual; un instante con hora (UTC, con o sin zona escrita) → su día LOCAL en la zona.
 * null si no es una fecha.
 */
export function localDayOf(value: unknown, timeZone = tenantTimeZone()): string | null {
  if (typeof value !== 'string') return null
  const s = value.trim()
  if (/^\d{4}-\d{2}-\d{2}$/.test(s)) return s
  if (!/^\d{4}-\d{2}-\d{2}T/.test(s)) return null
  const ms = parseApiDate(s).getTime()
  if (Number.isNaN(ms)) return null
  return zonedParts(ms, timeZone).slice(0, 10)
}

/** "Hoy" 'YYYY-MM-DD' en la zona de la compañía. */
export function tenantToday(now: Date = new Date(), timeZone = tenantTimeZone()): string {
  return zonedParts(now.getTime(), timeZone).slice(0, 10)
}
