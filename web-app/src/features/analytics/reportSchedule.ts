// Lote 92 (P12) — informes programados por correo: la programación vive en `scheduleCron` con una forma de cron restringida de 5 campos
// (`M H * * *` diario, `M H * * D` semanal con D de 0 a 6 y 0 = domingo, `M H D * *` mensual con D de 1 a 28), igual que en el servidor
// (`ReportScheduleRules`). Aquí se arma y se lee ese texto para la pantalla; el servidor valida de nuevo y manda el mensaje exacto.

export type ScheduleKind = 'DAILY' | 'WEEKLY' | 'MONTHLY'

export interface ScheduleForm {
  kind: ScheduleKind
  /** `HH:MM` (hora local de la compañía). */
  time: string
  /** 0 = domingo … 6 = sábado (solo semanal). */
  dayOfWeek: number
  /** 1 a 28 (solo mensual). */
  dayOfMonth: number
}

export const DEFAULT_SCHEDULE: ScheduleForm = { kind: 'DAILY', time: '07:00', dayOfWeek: 1, dayOfMonth: 1 }

/** Períodos que se pueden enviar (el servidor no acepta el personalizado). */
export const DELIVERY_RANGES = ['LAST7', 'LAST30', 'THIS_MONTH', 'ALL'] as const
export const DEFAULT_DELIVERY_RANGE = 'LAST7'
export const MAX_RECIPIENTS = 10

const pad = (n: number) => String(n).padStart(2, '0')

/** Cron → formulario; `null` si está vacío o no tiene la forma que la pantalla sabe armar. */
export function parseSchedule(cron: string | null | undefined): ScheduleForm | null {
  const m = /^(\d{1,2}) (\d{1,2}) (\*|\d{1,2}) \* (\*|\d)$/.exec((cron ?? '').trim())
  if (!m) return null
  const minute = Number(m[1])
  const hour = Number(m[2])
  if (minute > 59 || hour > 23) return null
  const time = `${pad(hour)}:${pad(minute)}`
  if (m[3] === '*' && m[4] === '*') return { ...DEFAULT_SCHEDULE, kind: 'DAILY', time }
  if (m[3] === '*') {
    const d = Number(m[4])
    return d > 7 ? null : { ...DEFAULT_SCHEDULE, kind: 'WEEKLY', time, dayOfWeek: d % 7 }
  }
  const day = Number(m[3])
  return m[4] === '*' && day >= 1 && day <= 28 ? { ...DEFAULT_SCHEDULE, kind: 'MONTHLY', time, dayOfMonth: day } : null
}

/** Formulario → cron. Una hora inválida se toma como las 07:00. */
export function buildCron(s: ScheduleForm): string {
  const t = /^(\d{1,2}):(\d{2})$/.exec(s.time)
  const hour = t && Number(t[1]) <= 23 ? Number(t[1]) : 7
  const minute = t && Number(t[2]) <= 59 ? Number(t[2]) : 0
  if (s.kind === 'WEEKLY') return `${minute} ${hour} * * ${s.dayOfWeek}`
  if (s.kind === 'MONTHLY') return `${minute} ${hour} ${s.dayOfMonth} * *`
  return `${minute} ${hour} * * *`
}

/** Destinatarios escritos: separados por coma, punto y coma, espacio o salto de línea. */
export function splitEmails(text: string | null | undefined): string[] {
  return (text ?? '')
    .split(/[,;\s]+/)
    .map((e) => e.trim())
    .filter(Boolean)
}

/** Correos con formato válido, sin repetir (sin distinguir mayúsculas), o el primero mal escrito. */
export function checkEmails(text: string | null | undefined): { emails: string[]; bad: string | null; tooMany: boolean } {
  const list: string[] = []
  for (const e of splitEmails(text)) {
    if (!/^[^@\s,;]+@[^@\s,;]+\.[^@\s,;]+$/.test(e) || e.length > 200) return { emails: list, bad: e, tooMany: false }
    if (!list.some((x) => x.toLowerCase() === e.toLowerCase())) list.push(e)
  }
  return { emails: list, bad: null, tooMany: list.length > MAX_RECIPIENTS }
}

export type DeliveryStatus = 'SENT' | 'SKIPPED' | 'FAILED'
export const deliveryTone = (status: string | null | undefined): 'ok' | 'warn' | 'bad' | 'none' =>
  status === 'SENT' ? 'ok' : status === 'SKIPPED' ? 'warn' : status === 'FAILED' ? 'bad' : 'none'
