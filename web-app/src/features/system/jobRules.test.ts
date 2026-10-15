// Lote 50 — reglas puras de «Trabajos programados»: intervalo ↔ (valor, unidad), esquema del modal con los mensajes del servidor,
// request del PATCH y el texto del horario.
import { describe, expect, it } from 'vitest'
import { buildScheduleRequest, intervalParts, jobScheduleSchema, jobScheduleValuesOf, scheduleText } from './jobRules'
import type { ScheduledJobDto } from './jobsApi'

const t = (key: string, params?: Record<string, string | number>) => (params ? `${key}:${JSON.stringify(params)}` : key)
const job = (over: Partial<ScheduledJobDto> = {}): ScheduledJobDto => ({
  key: 'JOB_TEST',
  name: 'Trabajo de prueba',
  nameEn: 'Test job',
  description: 'd',
  descriptionEn: 'd',
  isEnabled: true,
  scheduleKind: 'INTERVAL',
  intervalSeconds: 3600,
  dailyAt: null,
  nextRunUtc: null,
  manualRequested: false,
  lastStartedUtc: null,
  lastFinishedUtc: null,
  lastStatus: 'NEVER',
  lastMessage: null,
  lastError: null,
  consecutiveFailures: 0,
  isRunning: false,
  rowVersion: 'AAA=',
  ...over,
})

describe('intervalParts', () => {
  it('usa la unidad más grande que divide exacto', () => {
    expect(intervalParts(60)).toEqual({ value: 1, unit: 'm' })
    expect(intervalParts(5400)).toEqual({ value: 90, unit: 'm' })
    expect(intervalParts(3600)).toEqual({ value: 1, unit: 'h' })
    expect(intervalParts(7200)).toEqual({ value: 2, unit: 'h' })
    expect(intervalParts(604_800)).toEqual({ value: 7, unit: 'd' })
    expect(intervalParts(null)).toEqual({ value: 1, unit: 'h' })
  })
})

describe('jobScheduleSchema', () => {
  const schema = jobScheduleSchema(t)
  const base = { scheduleKind: 'INTERVAL' as const, intervalValue: 15, intervalUnit: 'm' as const, dailyAt: '' }
  it('acepta un intervalo entre 1 minuto y 7 días', () => {
    expect(schema.safeParse(base).success).toBe(true)
    expect(schema.safeParse({ ...base, intervalValue: 7, intervalUnit: 'd' }).success).toBe(true)
  })
  it('rechaza un intervalo fuera de rango o vacío con el mensaje del servidor', () => {
    for (const v of [{ intervalValue: 0 }, { intervalValue: null }, { intervalValue: 8, intervalUnit: 'd' as const }]) {
      const r = schema.safeParse({ ...base, ...v })
      expect(r.success).toBe(false)
      expect(r.error?.issues[0].message).toBe('jobs.errors.interval')
    }
  })
  it('pide la hora diaria y la valida', () => {
    const daily = { ...base, scheduleKind: 'DAILY' as const }
    expect(schema.safeParse({ ...daily, dailyAt: '' }).error?.issues[0].message).toBe('jobs.errors.dailyRequired')
    expect(schema.safeParse({ ...daily, dailyAt: '25:00' }).error?.issues[0].message).toBe('jobs.errors.dailyInvalid')
    expect(schema.safeParse({ ...daily, dailyAt: '03:30' }).success).toBe(true)
  })
})

describe('buildScheduleRequest', () => {
  it('manda el intervalo en segundos con el rowVersion', () => {
    const v = { scheduleKind: 'INTERVAL' as const, intervalValue: 15, intervalUnit: 'm' as const, dailyAt: '03:00' }
    expect(buildScheduleRequest(v, job())).toEqual({ scheduleKind: 'INTERVAL', intervalSeconds: 900, rowVersion: 'AAA=' })
  })
  it('manda la hora diaria sin intervalo', () => {
    const v = { scheduleKind: 'DAILY' as const, intervalValue: 1, intervalUnit: 'h' as const, dailyAt: ' 03:05 ' }
    expect(buildScheduleRequest(v, job())).toEqual({ scheduleKind: 'DAILY', dailyAt: '03:05', rowVersion: 'AAA=' })
  })
  it('arranca el modal con el horario actual', () => {
    expect(jobScheduleValuesOf(job({ intervalSeconds: 7200 }))).toMatchObject({ scheduleKind: 'INTERVAL', intervalValue: 2, intervalUnit: 'h' })
    expect(jobScheduleValuesOf(job({ scheduleKind: 'DAILY', intervalSeconds: null, dailyAt: '02:00' }))).toMatchObject({ scheduleKind: 'DAILY', dailyAt: '02:00' })
  })
})

describe('scheduleText', () => {
  const time = (hhmm: string) => `@${hhmm}`
  it('describe cada horario', () => {
    expect(scheduleText(job({ intervalSeconds: 3600 }), t, time)).toBe('jobs.schedule.every1.h:{"n":1}')
    expect(scheduleText(job({ intervalSeconds: 900 }), t, time)).toBe('jobs.schedule.everyN.m:{"n":15}')
    expect(scheduleText(job({ scheduleKind: 'DAILY', intervalSeconds: null, dailyAt: '03:00' }), t, time)).toBe('jobs.schedule.daily:{"time":"@03:00"}')
  })
})
