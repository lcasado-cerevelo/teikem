// Lote 50 — reglas puras de «Trabajos programados»: el esquema del modal del horario (mismos límites y mensajes que el servidor,
// JobSchedule), la conversión intervalo ↔ (valor, unidad) y el request del PATCH.
import { z } from 'zod'
import type { ScheduledJobDto, ScheduledJobRequest } from './jobsApi'

type Translate = (key: string, params?: Record<string, string | number>) => string

export const MIN_INTERVAL_SECONDS = 60
export const MAX_INTERVAL_SECONDS = 604_800
export const UNIT_SECONDS = { m: 60, h: 3600, d: 86_400 } as const
export type IntervalUnit = keyof typeof UNIT_SECONDS

/** El intervalo en la unidad más grande que lo divide exacto (3600 → 1 hora; 5400 → 90 minutos). */
export function intervalParts(seconds: number | null | undefined): { value: number; unit: IntervalUnit } {
  const s = seconds && seconds > 0 ? seconds : 3600
  if (s % UNIT_SECONDS.d === 0) return { value: s / UNIT_SECONDS.d, unit: 'd' }
  if (s % UNIT_SECONDS.h === 0) return { value: s / UNIT_SECONDS.h, unit: 'h' }
  return { value: Math.max(1, Math.round(s / UNIT_SECONDS.m)), unit: 'm' }
}

export function jobScheduleSchema(t: Translate) {
  return z
    .object({
      scheduleKind: z.enum(['INTERVAL', 'DAILY']),
      intervalValue: z.number().nullable(),
      intervalUnit: z.enum(['m', 'h', 'd']),
      dailyAt: z.string(),
    })
    .superRefine((v, ctx) => {
      if (v.scheduleKind === 'INTERVAL') {
        const seconds = (v.intervalValue ?? 0) * UNIT_SECONDS[v.intervalUnit]
        if (!Number.isInteger(seconds) || seconds < MIN_INTERVAL_SECONDS || seconds > MAX_INTERVAL_SECONDS)
          ctx.addIssue({ code: 'custom', path: ['intervalValue'], message: t('jobs.errors.interval') })
      } else {
        if (!v.dailyAt.trim()) ctx.addIssue({ code: 'custom', path: ['dailyAt'], message: t('jobs.errors.dailyRequired') })
        else if (!/^([01]?\d|2[0-3]):[0-5]\d$/.test(v.dailyAt.trim())) ctx.addIssue({ code: 'custom', path: ['dailyAt'], message: t('jobs.errors.dailyInvalid') })
      }
    })
}
export type JobScheduleValues = z.infer<ReturnType<typeof jobScheduleSchema>>

export function jobScheduleValuesOf(job: ScheduledJobDto): JobScheduleValues {
  const parts = intervalParts(job.intervalSeconds)
  return {
    scheduleKind: job.scheduleKind === 'DAILY' ? 'DAILY' : 'INTERVAL',
    intervalValue: parts.value,
    intervalUnit: parts.unit,
    dailyAt: job.dailyAt ?? '03:00',
  }
}

/** PATCH del horario completo (el servidor valida el tipo y la hora o el intervalo) con el rowVersion de la ficha. */
export function buildScheduleRequest(v: JobScheduleValues, job: ScheduledJobDto): ScheduledJobRequest {
  return v.scheduleKind === 'INTERVAL'
    ? { scheduleKind: 'INTERVAL', intervalSeconds: Math.round((v.intervalValue ?? 0) * UNIT_SECONDS[v.intervalUnit]), rowVersion: job.rowVersion }
    : { scheduleKind: 'DAILY', dailyAt: v.dailyAt.trim(), rowVersion: job.rowVersion }
}

/** «Cada 15 minutos» / «Cada hora» / «Todos los días a las 03:00» (la hora ya formateada la pone quien llama). */
export function scheduleText(job: ScheduledJobDto, t: Translate, timeOfDay: (hhmm: string) => string): string {
  if (job.scheduleKind === 'DAILY') return t('jobs.schedule.daily', { time: timeOfDay(job.dailyAt ?? '00:00') })
  const { value, unit } = intervalParts(job.intervalSeconds)
  return t(value === 1 ? `jobs.schedule.every1.${unit}` : `jobs.schedule.everyN.${unit}`, { n: value })
}
