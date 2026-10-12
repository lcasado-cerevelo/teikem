import { describe, expect, it } from 'vitest'
import { allowedTransitions, lastPipelineStage, lateralEntryAllowed, lateralMoves, stepStates } from './transitions'
import type { StatusLateralEntryDto, StatusOption } from './types'

function st(code: string, stageKind: string, sortOrder: number, extra: Partial<StatusOption> = {}): StatusOption {
  return { code, label: code, color: null, stageKind, isInitial: false, isEnabled: true, sortOrder, icon: null, ...extra }
}

const STATUSES: StatusOption[] = [
  st('CAPTURED', 'PIPELINE', 10, { isInitial: true }),
  st('CONFIRMED', 'PIPELINE', 20),
  st('IN_ROUTE', 'PIPELINE', 30),
  st('ARRIVED', 'PIPELINE', 40),
  st('DELIVERED', 'TERMINAL', 50),
  st('ON_HOLD', 'LATERAL', 60),
  st('FAILED', 'LATERAL', 70),
  st('CANCELLED', 'TERMINAL', 80),
]

const codes = (list: StatusOption[]) => list.map((s) => s.code)

describe('allowedTransitions (réplica de StatusService)', () => {
  it('registro nuevo: solo etapas iniciales', () => {
    expect(codes(allowedTransitions({ statuses: STATUSES, currentCode: null }))).toEqual(['CAPTURED'])
  })

  it('desde el pipeline: la siguiente etapa y laterales/terminales sin reglas; nunca saltos del pipeline', () => {
    expect(codes(allowedTransitions({ statuses: STATUSES, currentCode: 'CONFIRMED' }))).toEqual([
      'IN_ROUTE',
      'DELIVERED',
      'ON_HOLD',
      'FAILED',
      'CANCELLED',
    ])
  })

  it('la siguiente etapa puede ser un terminal de cierre (ARRIVED → DELIVERED)', () => {
    const rules: StatusLateralEntryDto[] = [{ lateralStatusCode: 'DELIVERED', fromStatusCode: 'IN_ROUTE', isAllowed: true, isTenantRule: false }]
    expect(codes(allowedTransitions({ statuses: STATUSES, currentCode: 'arrived', lateralEntries: rules }))).toContain('DELIVERED')
  })

  it('una etapa deshabilitada se salta al buscar la siguiente', () => {
    const statuses = STATUSES.map((s) => (s.code === 'IN_ROUTE' ? { ...s, isEnabled: false } : s))
    const result = codes(allowedTransitions({ statuses, currentCode: 'CONFIRMED' }))
    expect(result).toContain('ARRIVED')
    expect(result).not.toContain('IN_ROUTE')
  })

  it('reglas laterales: las del tenant sustituyen a las globales para ese lateral', () => {
    const global: StatusLateralEntryDto[] = [{ lateralStatusCode: 'CANCELLED', fromStatusCode: 'CAPTURED', isAllowed: true, isTenantRule: false }]
    expect(codes(allowedTransitions({ statuses: STATUSES, currentCode: 'CONFIRMED', lateralEntries: global }))).not.toContain('CANCELLED')
    expect(codes(allowedTransitions({ statuses: STATUSES, currentCode: 'CAPTURED', lateralEntries: global }))).toContain('CANCELLED')

    const tenant: StatusLateralEntryDto[] = [
      ...global,
      { lateralStatusCode: 'CANCELLED', fromStatusCode: 'CONFIRMED', isAllowed: true, isTenantRule: true },
    ]
    expect(lateralEntryAllowed(tenant, 'CANCELLED', 'CONFIRMED')).toBe(true)
    expect(lateralEntryAllowed(tenant, 'CANCELLED', 'CAPTURED')).toBe(false)
    expect(lateralEntryAllowed([{ ...tenant[1], isAllowed: false }], 'CANCELLED', 'CONFIRMED')).toBe(false)
  })

  it('desde un lateral: volver a la última etapa del pipeline o a la siguiente', () => {
    const result = codes(
      allowedTransitions({ statuses: STATUSES, currentCode: 'ON_HOLD', historyToCodes: ['CAPTURED', 'CONFIRMED', 'ON_HOLD'] }),
    )
    expect(result).toEqual(['CONFIRMED', 'IN_ROUTE', 'DELIVERED', 'FAILED', 'CANCELLED'])
  })

  it('desde un lateral sin pipeline en el historial: solo la etapa inicial', () => {
    const result = codes(allowedTransitions({ statuses: STATUSES, currentCode: 'ON_HOLD', historyToCodes: ['ON_HOLD'] }))
    expect(result.filter((c) => STATUSES.find((s) => s.code === c)?.stageKind === 'PIPELINE')).toEqual(['CAPTURED'])
  })

  it('un terminal no admite más transiciones; un estatus desconocido tampoco ofrece', () => {
    expect(allowedTransitions({ statuses: STATUSES, currentCode: 'DELIVERED' })).toEqual([])
    expect(allowedTransitions({ statuses: STATUSES, currentCode: 'NOPE' })).toEqual([])
  })
})

describe('lateralMoves (solo laterales y el regreso exacto)', () => {
  it('desde el pipeline: solo los laterales permitidos; nunca avance, cierre ni confirmación', () => {
    expect(codes(lateralMoves({ statuses: STATUSES, currentCode: 'CONFIRMED' }))).toEqual(['ON_HOLD', 'FAILED'])
    // la regla del tenant sustituye a la global: CANCELLED/FAILED según desde dónde
    const rules: StatusLateralEntryDto[] = [{ lateralStatusCode: 'FAILED', fromStatusCode: 'IN_ROUTE', isAllowed: true, isTenantRule: true }]
    expect(codes(lateralMoves({ statuses: STATUSES, currentCode: 'CONFIRMED', lateralEntries: rules }))).toEqual(['ON_HOLD'])
    expect(codes(lateralMoves({ statuses: STATUSES, currentCode: 'IN_ROUTE', lateralEntries: rules }))).toEqual(['ON_HOLD', 'FAILED'])
  })

  it('desde un lateral: el regreso es SOLO a la etapa de la que salió (no a la siguiente)', () => {
    const result = codes(lateralMoves({ statuses: STATUSES, currentCode: 'ON_HOLD', historyToCodes: ['CAPTURED', 'CONFIRMED', 'ON_HOLD'] }))
    expect(result).toEqual(['CONFIRMED', 'FAILED'])
    expect(result).not.toContain('IN_ROUTE')
    expect(result).not.toContain('CANCELLED')
    expect(result).not.toContain('DELIVERED')
  })

  it('un lateral que nunca pasó por el pipeline vuelve a la etapa inicial; terminales y desconocidos, nada', () => {
    expect(codes(lateralMoves({ statuses: STATUSES, currentCode: 'ON_HOLD', historyToCodes: ['ON_HOLD'] }))).toEqual(['CAPTURED', 'FAILED'])
    expect(lateralMoves({ statuses: STATUSES, currentCode: 'DELIVERED' })).toEqual([])
    expect(lateralMoves({ statuses: STATUSES, currentCode: 'NOPE' })).toEqual([])
  })

  it('lastPipelineStage ignora laterales y etapas deshabilitadas', () => {
    expect(lastPipelineStage(STATUSES, ['CAPTURED', 'CONFIRMED', 'ON_HOLD'])?.code).toBe('CONFIRMED')
    expect(lastPipelineStage(STATUSES, ['ON_HOLD'])).toBeUndefined()
    expect(lastPipelineStage(STATUSES)).toBeUndefined()
  })
})

describe('stepStates', () => {
  const pipeline = STATUSES.filter((s) => s.stageKind === 'PIPELINE')

  it('en el pipeline: anteriores hechas, actual y pendientes', () => {
    const states = stepStates(pipeline, pipeline[1])
    expect([...states.values()]).toEqual(['done', 'current', 'upcoming', 'upcoming'])
  })

  it('en un lateral o terminal: hasta la última etapa del historial quedan hechas', () => {
    const onHold = STATUSES.find((s) => s.code === 'ON_HOLD')
    const states = stepStates(pipeline, onHold, ['CAPTURED', 'CONFIRMED', 'IN_ROUTE', 'ON_HOLD'])
    expect([...states.values()]).toEqual(['done', 'done', 'done', 'upcoming'])
    expect([...stepStates(pipeline, undefined).values()]).toEqual(['upcoming', 'upcoming', 'upcoming', 'upcoming'])
  })
})
