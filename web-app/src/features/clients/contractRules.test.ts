// Lógica pura de los contratos (F-A2): fecha por omisión, PATCH de datos generales, plan de guardado del modelo de
// facturación, borrador del SLA (renglones, request, errores del servidor renumerados), tramos y servicios especiales.
import { describe, expect, it } from 'vitest'
import { ApiError } from '../../kernel/api/problem'
import {
  billingSchema,
  billingValuesOf,
  buildContractCreate,
  buildContractPatch,
  buildSlaRequest,
  buildSpecialCreate,
  buildTierPatch,
  contractSchema,
  contractValuesOf,
  defaultContractId,
  defaultEffectiveDate,
  isFilledRow,
  NEW_SPECIAL_TYPE,
  planBillingSave,
  remapSlaErrors,
  renameErrors,
  rethrowRenamed,
  slaRowsOf,
  slaSchema,
  specialSchema,
  tierRangeText,
  tierSchema,
  type ContractDetail,
} from './contractRules'

const DETAIL = {
  id: 1,
  publicId: 'k-1',
  clientPublicId: 'c-1',
  contractNumber: 'ACME-C1',
  title: 'Contrato marco',
  startDate: '2026-01-15',
  endDate: '2026-12-31',
  autoRenew: false,
  status: 'DRAFT',
  currency: 'USD',
  billingTrigger: 'BY_PICKUP',
  notes: 'nota',
  billingModel: { billPerService: true, billExtraPiece: false, billDispatchFee: true, billCodFee: true, billSpecialServices: false, summary: 'Por servicio' },
  dispatchFee: 3,
  codFee: { type: 'PERCENT', value: 2.5 },
  serviceLevels: [{ id: 1, serviceType: 'STANDARD', serviceTypeLabel: 'Estándar', maxTransitHours: 48, pickupWindowMin: null, onTimeTargetPct: 95, penaltyAmount: null }],
  canEdit: true,
  isActive: true,
  rowVersion: 'AAA=',
} as ContractDetail

const msg = {
  titleRequired: 'título',
  titleMax: 'título largo',
  numberMax: 'número largo',
  startRequired: 'inicio',
  endBeforeStart: 'fin antes',
}

describe('fechas', () => {
  it('«hoy» por omisión es el día más reciente entre la compañía y el servidor (UTC)', () => {
    // 21:30 en Puerto Rico del 9-oct = 01:30 UTC del 10-oct: el servidor ya está en el día 10
    expect(defaultEffectiveDate('2026-10-09', new Date('2026-10-10T01:30:00Z'))).toBe('2026-10-10')
    expect(defaultEffectiveDate('2026-10-10', new Date('2026-10-10T15:00:00Z'))).toBe('2026-10-10')
    // compañía por delante de UTC
    expect(defaultEffectiveDate('2026-10-11', new Date('2026-10-10T20:00:00Z'))).toBe('2026-10-11')
  })

  it('contrato por omisión: el vigente, si no el marcado, si no el primero', () => {
    const list = [{ publicId: 'a' }, { publicId: 'b', isCurrent: true }, { publicId: 'c' }]
    expect(defaultContractId('c', list)).toBe('c')
    expect(defaultContractId(null, list)).toBe('b')
    expect(defaultContractId('zzz', [{ publicId: 'a' }])).toBe('a')
    expect(defaultContractId(null, [])).toBeNull()
  })
})

describe('datos generales', () => {
  const schema = contractSchema(msg)
  const ok = { ...contractValuesOf(DETAIL) }

  it('valida título, inicio y que el fin no sea anterior al inicio', () => {
    expect(schema.safeParse(ok).success).toBe(true)
    expect(schema.safeParse({ ...ok, title: ' ' }).error?.issues[0].message).toBe('título')
    expect(schema.safeParse({ ...ok, title: 'x'.repeat(201) }).error?.issues[0].message).toBe('título largo')
    expect(schema.safeParse({ ...ok, startDate: '' }).error?.issues[0].message).toBe('inicio')
    const r = schema.safeParse({ ...ok, endDate: '2026-01-01' })
    expect(r.error?.issues[0]).toMatchObject({ message: 'fin antes', path: ['endDate'] })
    // sin fecha fin no hay comparación
    expect(schema.safeParse({ ...ok, endDate: '' }).success).toBe(true)
  })

  it('PATCH: solo lo que cambió, con rowVersion; fecha fin vacía = clearEndDate', () => {
    const original = contractValuesOf(DETAIL)
    expect(buildContractPatch(original, original, 'AAA=')).toEqual({ rowVersion: 'AAA=' })
    expect(buildContractPatch({ ...original, title: ' Nuevo ', autoRenew: true, endDate: '', notes: '' }, original, 'AAA=')).toEqual({
      rowVersion: 'AAA=',
      title: 'Nuevo',
      autoRenew: true,
      clearEndDate: true,
      notes: '',
    })
    expect(buildContractPatch({ ...original, endDate: '2027-01-01', startDate: '2026-02-01', currency: 'EUR', billingTrigger: 'MIXED' }, original, null)).toEqual({
      rowVersion: null,
      startDate: '2026-02-01',
      endDate: '2027-01-01',
      currency: 'EUR',
      billingTrigger: 'MIXED',
    })
  })

  it('alta: número vacío = null, SLA no se manda', () => {
    const req = buildContractCreate('c-1', { contractNumber: ' ', title: ' Marco ', startDate: '2026-10-10', endDate: '', autoRenew: false, currency: '', billingTrigger: '', notes: '' })
    expect(req).toEqual({ clientPublicId: 'c-1', contractNumber: null, title: 'Marco', startDate: '2026-10-10', endDate: null, autoRenew: false, currency: null, billingTrigger: null, notes: null })
    expect('serviceLevels' in req).toBe(false)
  })
})

describe('modelo de facturación, despacho y COD', () => {
  const m = { number: 'n', dispatchMin: 'dmin', dispatchRequired: 'dreq', codFixedMin: 'cfix', codPercentRange: 'cpct', codRequired: 'creq' }
  const original = billingValuesOf(DETAIL)

  it('lee los valores del contrato (COD sin configurar = FIXED sin valor)', () => {
    expect(original).toMatchObject({ billPerService: true, billCodFee: true, dispatchAmount: 3, codType: 'PERCENT', codValue: 2.5 })
    expect(billingValuesOf({ ...DETAIL, codFee: null, dispatchFee: null })).toMatchObject({ dispatchAmount: null, codType: 'FIXED', codValue: null })
  })

  it('valida montos y por ciento solo cuando su check está marcado', () => {
    const schema = billingSchema(m, original)
    expect(schema.safeParse(original).success).toBe(true)
    expect(schema.safeParse({ ...original, dispatchAmount: -1 }).error?.issues[0].message).toBe('dmin')
    expect(schema.safeParse({ ...original, codValue: 101 }).error?.issues[0].message).toBe('cpct')
    expect(schema.safeParse({ ...original, codValue: -1 }).error?.issues[0].message).toBe('cpct')
    expect(schema.safeParse({ ...original, codType: 'FIXED', codValue: -1 }).error?.issues[0].message).toBe('cfix')
    expect(schema.safeParse({ ...original, codType: 'FIXED', codValue: 500 }).success).toBe(true)
    // con el check apagado el monto no se valida
    expect(schema.safeParse({ ...original, billCodFee: false, codValue: 999 }).success).toBe(true)
  })

  it('con el check recién marcado el monto es obligatorio; un contrato que ya estaba así sin monto no bloquea', () => {
    const off = billingValuesOf({ ...DETAIL, billingModel: { ...DETAIL.billingModel, billDispatchFee: false, billCodFee: false }, dispatchFee: null, codFee: null })
    const schema = billingSchema(m, off)
    expect(schema.safeParse({ ...off, billDispatchFee: true }).error?.issues[0]).toMatchObject({ message: 'dreq', path: ['dispatchAmount'] })
    expect(schema.safeParse({ ...off, billCodFee: true }).error?.issues[0]).toMatchObject({ message: 'creq', path: ['codValue'] })
    const legacy = billingValuesOf({ ...DETAIL, dispatchFee: null, codFee: null })
    expect(billingSchema(m, legacy).safeParse(legacy).success).toBe(true)
  })

  it('plan de guardado: checks que cambiaron y montos solo con su check marcado', () => {
    expect(planBillingSave(original, original)).toEqual({})
    expect(planBillingSave({ ...original, billExtraPiece: true, dispatchAmount: 4 }, original)).toEqual({ flags: { billExtraPiece: true }, dispatch: { amount: 4 } })
    expect(planBillingSave({ ...original, codType: 'FIXED', codValue: 1.5 }, original)).toEqual({ cod: { type: 'FIXED', value: 1.5 } })
    // desmarcar no manda el monto (el contrato lo conserva)
    expect(planBillingSave({ ...original, billDispatchFee: false, dispatchAmount: 9 }, original)).toEqual({ flags: { billDispatchFee: false } })
  })
})

describe('SLA', () => {
  const types = [
    { code: 'STANDARD', label: 'Estándar' },
    { code: 'EXPRESS', label: 'Exprés' },
  ]

  it('un renglón por tipo de servicio; empieza vacío y trae lo guardado', () => {
    const empty = slaRowsOf(types, [])
    expect(empty.levels).toHaveLength(2)
    expect(empty.levels.some(isFilledRow)).toBe(false)
    const rows = slaRowsOf(types, DETAIL.serviceLevels).levels
    expect(rows[0]).toMatchObject({ serviceType: 'STANDARD', maxTransitHours: 48, onTimeTargetPct: 95, pickupWindowMin: null })
    expect(rows[1].maxTransitHours).toBeNull()
  })

  it('un nivel de un tipo que ya no está en el catálogo se conserva', () => {
    const rows = slaRowsOf(types, [{ id: 9, serviceType: 'OLD', serviceTypeLabel: 'Viejo', maxTransitHours: 12 }]).levels
    expect(rows).toHaveLength(3)
    expect(rows[2]).toMatchObject({ serviceType: 'OLD', label: 'Viejo', maxTransitHours: 12 })
  })

  it('el request envía solo los renglones con datos y recuerda su posición', () => {
    const rows = slaRowsOf(types, []).levels
    rows[1].maxTransitHours = 24
    rows[1].penaltyAmount = 10
    const { body, indexMap } = buildSlaRequest(rows)
    expect(body).toEqual([{ serviceType: 'EXPRESS', maxTransitHours: 24, pickupWindowMin: null, onTimeTargetPct: null, penaltyAmount: 10 }])
    expect(indexMap).toEqual([1])
  })

  it('los errores del servidor `serviceLevels[k].campo` pasan al renglón del formulario', () => {
    const errors = { 'serviceLevels[0].maxTransitHours': ['Las horas máximas de tránsito deben ser mayores que cero.'], title: ['otro'] }
    expect(remapSlaErrors(errors, [1])).toEqual({ 'levels.1.maxTransitHours': ['Las horas máximas de tránsito deben ser mayores que cero.'], title: ['otro'] })
    expect(remapSlaErrors({ 'ServiceLevels[2].OnTimeTargetPct': ['x'] }, [0, 3, 4])).toEqual({ 'levels.4.onTimeTargetPct': ['x'] })
  })

  it('valida horas, ventana, meta y penalidad', () => {
    const schema = slaSchema({ number: 'n', hoursInt: 'hint', hoursMin: 'hmin', windowInt: 'wint', windowMin: 'wmin', pctRange: 'pct', penaltyMin: 'pen' })
    const row = { serviceType: 'STANDARD', label: 'Estándar', maxTransitHours: null, pickupWindowMin: null, onTimeTargetPct: null, penaltyAmount: null }
    const first = (r: object) => schema.safeParse({ levels: [{ ...row, ...r }] }).error?.issues[0].message
    expect(schema.safeParse({ levels: [row] }).success).toBe(true)
    expect(first({ maxTransitHours: 0 })).toBe('hmin')
    expect(first({ maxTransitHours: 1.5 })).toBe('hint')
    expect(first({ pickupWindowMin: -1 })).toBe('wmin')
    expect(first({ onTimeTargetPct: 101 })).toBe('pct')
    expect(first({ penaltyAmount: -5 })).toBe('pen')
    expect(schema.safeParse({ levels: [{ ...row, maxTransitHours: 24, pickupWindowMin: 0, onTimeTargetPct: 100, penaltyAmount: 0 }] }).success).toBe(true)
  })
})

describe('errores del servidor renombrados', () => {
  it('renameErrors cambia los campos indicados y deja el resto', () => {
    expect(renameErrors({ amount: ['a'], Value: ['b'], other: ['c'] }, { amount: 'dispatchAmount', value: 'codValue' })).toEqual({ dispatchAmount: ['a'], codValue: ['b'], other: ['c'] })
  })

  it('rethrowRenamed conserva estatus, título y código', () => {
    const err = new ApiError(400, { title: 'Solicitud inválida', code: 'validation', errors: { amount: ['x'] } })
    try {
      rethrowRenamed(err, (e) => renameErrors(e, { amount: 'dispatchAmount' }))
      throw new Error('no lanzó')
    } catch (e) {
      expect(e).toBeInstanceOf(ApiError)
      expect(e).toMatchObject({ status: 400, title: 'Solicitud inválida', code: 'validation', errors: { dispatchAmount: ['x'] } })
    }
    // lo que no es ApiError sale tal cual
    expect(() => rethrowRenamed(new TypeError('red'), (e) => e)).toThrow(TypeError)
  })
})

describe('tramos de pieza extra', () => {
  const m = { fromRequired: 'from', rangeInvalid: 'range', rateRequired: 'rate', rateMin: 'rmin', dateRequired: 'date', dateBeforeToday: 'past' }
  const schema = tierSchema(m, { minDate: '2026-10-10' })
  const ok = { fromUnit: 2, toUnit: 5, rate: 1, effectiveFrom: '2026-10-10' }

  it('desde ≥ 2, hasta ≥ desde o vacío, tarifa ≥ 0 y vigencia no anterior a hoy', () => {
    expect(schema.safeParse(ok).success).toBe(true)
    expect(schema.safeParse({ ...ok, toUnit: null }).success).toBe(true)
    expect(schema.safeParse({ ...ok, fromUnit: 1 }).error?.issues[0].message).toBe('range')
    expect(schema.safeParse({ ...ok, fromUnit: null }).error?.issues[0].message).toBe('from')
    expect(schema.safeParse({ ...ok, toUnit: 1 }).error?.issues[0].message).toBe('range')
    expect(schema.safeParse({ ...ok, rate: -1 }).error?.issues[0].message).toBe('rmin')
    expect(schema.safeParse({ ...ok, rate: null }).error?.issues[0].message).toBe('rate')
    expect(schema.safeParse({ ...ok, effectiveFrom: '2026-10-09' }).error?.issues[0].message).toBe('past')
    // el alta no limita la fecha
    expect(tierSchema(m, {}).safeParse({ ...ok, effectiveFrom: '2020-01-01' }).success).toBe(true)
  })

  it('nueva versión de un tramo: solo lo que cambió; «hasta» vacío = clearToUnit', () => {
    const tier = { id: 7, fromUnit: 2, toUnit: 5, rate: 1, effectiveFrom: '2026-01-01', isCurrent: true }
    expect(buildTierPatch({ ...ok }, tier)).toEqual({ effectiveFrom: '2026-10-10' })
    expect(buildTierPatch({ ...ok, toUnit: null, rate: 0.75 }, tier)).toEqual({ effectiveFrom: '2026-10-10', clearToUnit: true, rate: 0.75 })
    expect(buildTierPatch({ ...ok, fromUnit: 3, toUnit: 8 }, tier)).toEqual({ effectiveFrom: '2026-10-10', fromUnit: 3, toUnit: 8 })
    expect(buildTierPatch({ ...ok, toUnit: null }, { ...tier, toUnit: null })).toEqual({ effectiveFrom: '2026-10-10' })
  })

  it('texto del rango', () => {
    expect(tierRangeText({ fromUnit: 2, toUnit: 5 })).toBe('2–5')
    expect(tierRangeText({ fromUnit: 6, toUnit: null })).toBe('6+')
  })
})

describe('servicios especiales', () => {
  const m = { typeRequired: 'type', nameRequired: 'name', nameMax: 'max', rateRequired: 'rate', rateMin: 'rmin', dateRequired: 'date', dateBeforeToday: 'past' }
  const ok = { typeId: '5', newTypeName: '', rate: 150, effectiveFrom: '2026-10-10' }

  it('alta: tipo obligatorio; con «nuevo tipo», el nombre (≤ 120)', () => {
    const schema = specialSchema(m, { needsType: true })
    expect(schema.safeParse(ok).success).toBe(true)
    expect(schema.safeParse({ ...ok, typeId: '' }).error?.issues[0].message).toBe('type')
    expect(schema.safeParse({ ...ok, typeId: NEW_SPECIAL_TYPE, newTypeName: '  ' }).error?.issues[0]).toMatchObject({ message: 'name', path: ['newTypeName'] })
    expect(schema.safeParse({ ...ok, typeId: NEW_SPECIAL_TYPE, newTypeName: 'x'.repeat(121) }).error?.issues[0].message).toBe('max')
    expect(schema.safeParse({ ...ok, rate: -1 }).error?.issues[0].message).toBe('rmin')
    expect(schema.safeParse({ ...ok, rate: null }).error?.issues[0].message).toBe('rate')
  })

  it('edición: no pide tipo y la vigencia no puede ser anterior a hoy', () => {
    const schema = specialSchema(m, { needsType: false, minDate: '2026-10-10' })
    expect(schema.safeParse({ ...ok, typeId: '' }).success).toBe(true)
    expect(schema.safeParse({ ...ok, typeId: '', effectiveFrom: '2026-10-01' }).error?.issues[0].message).toBe('past')
  })

  it('request de alta: typeId o newTypeName, nunca los dos', () => {
    expect(buildSpecialCreate(ok)).toEqual({ typeId: 5, rate: 150, effectiveFrom: '2026-10-10' })
    expect(buildSpecialCreate({ ...ok, typeId: NEW_SPECIAL_TYPE, newTypeName: ' Vagón del muelle ' })).toEqual({ newTypeName: 'Vagón del muelle', rate: 150, effectiveFrom: '2026-10-10' })
  })
})
