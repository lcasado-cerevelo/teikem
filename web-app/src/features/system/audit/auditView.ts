// Seguridad y auditoría (/system/audit, lote F10; maqueta `auditoriaScreen`): lógica pura de la pantalla. Pestañas y su
// `?tab=`, consulta de la Actividad (tipo, texto y fechas al API), etiqueta Cambio / Evento / Alerta, texto del detalle (los
// JSON de la bitácora y de los eventos de seguridad se leen como "Campo: antes → después"), orden local de las columnas y
// las reglas de la política de sesiones (los mismos límites que valida el servidor).
import type { components } from '../../../kernel/api/schema'
import { utcFromZonedInput } from '../../../kernel/api/tenantZone'
import type { DateRange } from '../../../kernel/ui/dateRange'

export type ActivityRowDto = components['schemas']['ActivityRowDto']
export type CompanySessionDto = components['schemas']['CompanySessionDto']

// ------------------------------------------------------------------------------------------------ pestañas

export const AUDIT_TABS = ['activity', 'sessions', 'legal'] as const
export type AuditTab = (typeof AUDIT_TABS)[number]

/** `?tab=` → pestaña (desconocida o sin parámetro = Actividad). `?tab=sessions` lo usa Ajustes → General. */
export function auditTabFromParam(v: string | null): AuditTab {
  return v === 'sessions' ? 'sessions' : v === 'legal' ? 'legal' : 'activity'
}

// ------------------------------------------------------------------------------------------------ actividad

/** Filtro de tipo de la maqueta (Todo / Cambios / Seguridad) = `kind` del API. */
export const ACTIVITY_KINDS = ['all', 'changes', 'security'] as const
export type ActivityKind = (typeof ACTIVITY_KINDS)[number]

export interface ActivityFilters {
  kind: ActivityKind
  /** Texto libre (el API busca en tipo, detalle, usuario e IP). */
  text: string
  /** Días locales de la compañía ('YYYY-MM-DD' o ''). */
  range: DateRange
}

export const EMPTY_ACTIVITY_FILTERS: ActivityFilters = { kind: 'all', text: '', range: { from: '', to: '' } }

/** Día siguiente de un 'YYYY-MM-DD' (calendario puro, sin zona). */
function nextDay(day: string): string {
  const [y, m, d] = day.split('-').map(Number)
  const next = new Date(Date.UTC(y, m - 1, d + 1))
  return next.toISOString().slice(0, 10)
}

/**
 * Consulta de `GET /api/v1/audit/activity`. Las fechas son días LOCALES de la compañía: `from` = medianoche local del primer
 * día en UTC y `to` = medianoche local del día SIGUIENTE al último (el API filtra `< to`). `zone` solo para pruebas.
 */
export function activityQuery(f: ActivityFilters, skip: number, take: number, zone?: string) {
  const text = f.text.trim()
  return {
    kind: f.kind,
    text: text || undefined,
    from: f.range.from ? utcFromZonedInput(`${f.range.from}T00:00`, zone) || undefined : undefined,
    to: f.range.to ? utcFromZonedInput(`${nextDay(f.range.to)}T00:00`, zone) || undefined : undefined,
    skip,
    take,
  }
}

export type ActivityBadge = 'change' | 'event' | 'alert'

/** Eventos que son alerta aunque el resultado diga éxito (permiso denegado, bloqueo de cuenta). */
const ALERT_EVENT_TYPES = new Set(['PERMISSION_DENIED', 'LOCKOUT'])

/**
 * Etiqueta de la maqueta (`secRowBadge`): un cambio es "Cambio"; un evento de seguridad es "Alerta" si falló o se bloqueó
 * (resultado distinto de SUCCESS) o si es permiso denegado o bloqueo; si no, "Evento". Decide por código, no por texto.
 */
export function activityBadge(row: Pick<ActivityRowDto, 'kind' | 'typeCode' | 'outcomeCode'>): ActivityBadge {
  if (row.kind === 'change') return 'change'
  const type = (row.typeCode ?? '').toUpperCase()
  const outcome = (row.outcomeCode ?? '').toUpperCase()
  if (ALERT_EVENT_TYPES.has(type) || (outcome !== '' && outcome !== 'SUCCESS')) return 'alert'
  return 'event'
}

/** Textos que el detalle necesita (ya traducidos). */
export interface DetailTexts {
  yes: string
  no: string
  empty: string
}

const MAX_VALUE = 80

/** Un valor del JSON como texto corto: vacío, Sí/No, número, texto (recortado) u objeto compacto. */
export function detailValue(v: unknown, tx: DetailTexts): string {
  if (v === null || v === undefined || v === '') return tx.empty
  if (typeof v === 'boolean') return v ? tx.yes : tx.no
  const s = typeof v === 'string' ? v : typeof v === 'number' ? String(v) : JSON.stringify(v)
  return s.length > MAX_VALUE ? `${s.slice(0, MAX_VALUE - 1)}…` : s
}

function isFromTo(v: unknown): v is { from?: unknown; to?: unknown } {
  if (typeof v !== 'object' || v === null || Array.isArray(v)) return false
  const keys = Object.keys(v)
  return keys.length > 0 && keys.every((k) => k === 'from' || k === 'to')
}

/**
 * Partes legibles del detalle (JSON de la bitácora o del evento): una por campo, "Campo: antes → después" en un cambio
 * (`{"Campo":{"from":…,"to":…}}`) y "Campo: valor" en un alta, una baja o un evento. La llave interna `$key` no se muestra.
 * Un detalle que no es un objeto JSON se devuelve tal cual (una parte).
 */
export function activityDetailParts(detail: string | null | undefined, tx: DetailTexts): string[] {
  const raw = (detail ?? '').trim()
  if (!raw) return []
  let parsed: unknown
  try {
    parsed = JSON.parse(raw)
  } catch {
    return [raw]
  }
  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) return [detailValue(parsed, tx)]
  return Object.entries(parsed as Record<string, unknown>)
    .filter(([k]) => k !== '$key')
    .map(([k, v]) => (isFromTo(v) ? `${k}: ${detailValue(v.from, tx)} → ${detailValue(v.to, tx)}` : `${k}: ${detailValue(v, tx)}`))
}

/** Cuántas partes del detalle se ven en la tabla (el CSV las lleva todas). */
export const DETAIL_PARTS_SHOWN = 4

/** Texto completo del detalle para el archivo: "Tipo: parte; parte; …". */
export function activityDetailText(row: Pick<ActivityRowDto, 'type' | 'detail'>, tx: DetailTexts): string {
  const parts = activityDetailParts(row.detail, tx)
  return parts.length ? `${row.type}: ${parts.join('; ')}` : row.type
}

// ------------------------------------------------------------------------------------------------ orden local

export type ActivitySortId = 'when' | 'type' | 'user' | 'detail'
export interface SortState {
  id: string
  desc: boolean
}

const collator = new Intl.Collator('es', { numeric: true, sensitivity: 'base' })

/**
 * Orden de las filas por una columna (la tabla lo aplica a la página que llegó y la exportación a todo lo filtrado). Vacíos
 * al final; sin orden, el del servidor (lo más reciente primero). `value` da el valor de orden de cada columna.
 */
export function sortRows<T>(rows: readonly T[], sort: SortState | null, value: (row: T, id: string) => string | number | null): T[] {
  if (!sort) return [...rows]
  return rows
    .map((row, i) => ({ row, i, v: value(row, sort.id) }))
    .sort((a, b) => {
      if (a.v === null || a.v === '') return b.v === null || b.v === '' ? a.i - b.i : 1
      if (b.v === null || b.v === '') return -1
      const c = typeof a.v === 'number' && typeof b.v === 'number' ? a.v - b.v : collator.compare(String(a.v), String(b.v))
      return (sort.desc ? -c : c) || a.i - b.i
    })
    .map((x) => x.row)
}

// ------------------------------------------------------------------------------------------------ sesiones

/** Ubicación de una sesión: la IP desde la que se abrió o renovó (no hay geolocalización). */
export function sessionLocation(s: Pick<CompanySessionDto, 'ipAddress'>): string | null {
  return s.ipAddress?.trim() || null
}

// ------------------------------------------------------------------------------------------------ política

/** Ventanas de reautenticación de la maqueta (minutos). El servidor acepta de 5 a 240. */
export const REAUTH_WINDOWS = [15, 30, 60] as const
export const SESSION_DAYS_MIN = 1
export const SESSION_DAYS_MAX = 365

/** Opciones de la ventana: las de la maqueta y, si el valor guardado es otro (puesto por el API), también ese. */
export function reauthOptions(current: number | null | undefined): number[] {
  const list: number[] = [...REAUTH_WINDOWS]
  if (current != null && Number.isInteger(current) && !list.includes(current)) list.push(current)
  return list.sort((a, b) => a - b)
}

export interface PolicyValues {
  mfaRequired: boolean
  /** Minutos como texto (valor del `<select>`). */
  aal2WindowMinutes: string
  /** 2026-10-12: duración de la sesión en HORAS, con decimales (0.25 = 15 minutos; 24 = 1 día). */
  sessionHours: number | null
  deviceSessionHours: number | null
  /** 2026-10-23: sesión del chofer en la app, en HORAS; null = vale la sesión general. */
  driverSessionHours: number | null
}

export interface PolicySettings {
  mfaRequired?: boolean
  aal2WindowMinutes?: number
  /** Duración efectiva en minutos (el servidor la calcula con los días si no está configurada). */
  sessionMinutes?: number
  deviceSessionMinutes?: number
  /** Valor propio del chofer en minutos; null/ausente = vale la sesión general. */
  driverSessionMinutes?: number | null
}

/** Mínimo y máximo de la duración de la sesión en minutos (1 minuto a 365 días), como `SessionRules` del servidor. */
export const SESSION_MINUTES_MIN = 1
export const SESSION_MINUTES_MAX = 525_600

/** Minutos → horas para mostrar (hasta 2 decimales: 90 → 1.5; 15 → 0.25; 20 → 0.33, que vuelve a 20 minutos). */
export function minutesToHours(minutes: number | null | undefined): number | null {
  if (minutes == null || !Number.isFinite(minutes)) return null
  return Math.round((minutes / 60) * 100) / 100
}

/** Horas (con decimales) → minutos enteros; null si no es un número. */
export function hoursToMinutes(hours: number | null | undefined): number | null {
  if (hours == null || !Number.isFinite(hours)) return null
  return Math.round(hours * 60)
}

/** Valores del formulario a partir de los ajustes de la compañía. */
export function toPolicyValues(s: PolicySettings): PolicyValues {
  return {
    mfaRequired: s.mfaRequired ?? false,
    aal2WindowMinutes: String(s.aal2WindowMinutes ?? 30),
    sessionHours: minutesToHours(s.sessionMinutes ?? 30 * 1440),
    deviceSessionHours: minutesToHours(s.deviceSessionMinutes ?? 30 * 1440),
    driverSessionHours: minutesToHours(s.driverSessionMinutes),
  }
}

/** true si las horas dan un número entero de minutos entre 1 minuto y 365 días. */
export function validSessionHours(hours: number | null | undefined): hours is number {
  const m = hoursToMinutes(hours)
  return m != null && m >= SESSION_MINUTES_MIN && m <= SESSION_MINUTES_MAX
}

/** Campos con error (nombre → clave i18n) antes de mandar; vacío = válido. */
export function policyErrors(v: PolicyValues): Partial<Record<keyof PolicyValues, 'hoursRange' | 'reauthInvalid'>> {
  const errors: Partial<Record<keyof PolicyValues, 'hoursRange' | 'reauthInvalid'>> = {}
  const minutes = Number(v.aal2WindowMinutes)
  if (!Number.isInteger(minutes) || minutes < 5 || minutes > 240) errors.aal2WindowMinutes = 'reauthInvalid'
  if (!validSessionHours(v.sessionHours)) errors.sessionHours = 'hoursRange'
  if (!validSessionHours(v.deviceSessionHours)) errors.deviceSessionHours = 'hoursRange'
  if (v.driverSessionHours != null && !validSessionHours(v.driverSessionHours)) errors.driverSessionHours = 'hoursRange'   // vacío es válido: vale la general
  return errors
}

/** Cuerpo del `PUT /tenant/settings` parcial: solo lo que cambió (la duración viaja en minutos; null = sin cambio en el API). */
export function policyRequestBody(saved: PolicySettings, v: PolicyValues) {
  const body: { mfaRequired?: boolean; aal2WindowMinutes?: number; sessionMinutes?: number; deviceSessionMinutes?: number; driverSessionMinutes?: number } = {}
  if (v.mfaRequired !== (saved.mfaRequired ?? false)) body.mfaRequired = v.mfaRequired
  const minutes = Number(v.aal2WindowMinutes)
  if (minutes !== saved.aal2WindowMinutes) body.aal2WindowMinutes = minutes
  const session = hoursToMinutes(v.sessionHours)
  if (session != null && session !== (saved.sessionMinutes ?? 30 * 1440)) body.sessionMinutes = session
  const device = hoursToMinutes(v.deviceSessionHours)
  if (device != null && device !== (saved.deviceSessionMinutes ?? 30 * 1440)) body.deviceSessionMinutes = device
  // sesión del chofer: vacío = quitarla (0 en el API: vale la general)
  const driver = hoursToMinutes(v.driverSessionHours)
  if ((driver ?? 0) !== (saved.driverSessionMinutes ?? 0)) body.driverSessionMinutes = driver ?? 0
  return body
}
