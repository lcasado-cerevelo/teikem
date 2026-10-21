// Lote F-E1 — reglas puras de la Estación de escaneo: código, resultado → chip/voz/mensaje, medidor, filtros y consulta de pendientes.
import { describe, expect, it } from 'vitest'
import { translate } from '../../kernel/i18n/i18n'
import {
  assignedOrderIds,
  buildPendingQuery,
  codeProblemKey,
  entryMatchesFilters,
  entryMatchesQuery,
  entryRef,
  hasFilters,
  hintKey,
  MAX_CODE_LENGTH,
  MAX_SESSION_ENTRIES,
  meterOf,
  NO_FILTERS,
  normalizeCode,
  outcomeInfo,
  Outcomes,
  pendingMatchesQuery,
  pushEntry,
  Reasons,
  voiceKind,
  type ScanEntry,
  type ScanResult,
} from './scanRules'

const es = (key: string, params?: Record<string, string | number>) => translate('es', key, params)

const entry = (id: number, result: ScanResult, known: ScanEntry['known'] = {}, code = 'X'): ScanEntry => ({ id, code, at: '2026-10-12T14:00:00', result, known })

describe('código a escanear', () => {
  it('recorta espacios y exige un código de 1 a 40 caracteres, con los mensajes del servidor (manual 05 §7)', () => {
    expect(normalizeCode('  PKG-000123 \n')).toBe('PKG-000123')
    expect(codeProblemKey('')).toBe('scan.errors.empty')
    expect(codeProblemKey('   ')).toBe('scan.errors.empty')
    expect(codeProblemKey('A'.repeat(MAX_CODE_LENGTH))).toBeNull()
    expect(codeProblemKey('A'.repeat(MAX_CODE_LENGTH + 1))).toBe('scan.errors.tooLong')
    // 40 caracteres útiles con espacios alrededor: el servidor recorta antes de contar
    expect(codeProblemKey(` ${'A'.repeat(MAX_CODE_LENGTH)} `)).toBeNull()
    expect(es('scan.errors.empty')).toBe('Escanee o escriba un código.')
    expect(es('scan.errors.tooLong')).toBe('El código no puede exceder 40 caracteres.')
  })
})

describe('resultado → chip, tono, voz y qué hacer', () => {
  it('cada outcome tiene su propio color y nombre', () => {
    const infos = Object.values(Outcomes).map((o) => outcomeInfo(o))
    expect(new Set(infos.map((i) => i.chip)).size).toBe(5)
    expect(new Set(infos.map((i) => i.tone)).size).toBe(5)
    expect(outcomeInfo(Outcomes.FoundAssigned)).toMatchObject({ tone: 'ok', chip: 'deliv' })
    expect(outcomeInfo(Outcomes.AlreadyAssigned)).toMatchObject({ tone: 'dup' })
    expect(outcomeInfo(Outcomes.NotEligible)).toMatchObject({ tone: 'ineligible' })
    expect(outcomeInfo(Outcomes.NotFound)).toMatchObject({ tone: 'notfound', chip: 'fail' })
    expect(es(outcomeInfo(Outcomes.FoundAssigned).labelKey)).toBe('Asignado')
    expect(es(outcomeInfo(Outcomes.AlreadyAssigned).labelKey)).toBe('Ya estaba')
    // un valor que el servidor agregue mañana no rompe la pantalla
    expect(outcomeInfo('NUEVO')).toMatchObject({ tone: 'notfound', labelKey: 'scan.outcome.UNKNOWN' })
    expect(outcomeInfo(null).tone).toBe('notfound')
  })

  it('la voz sale de `voice`; si falta, del resultado (found / dup / notfound)', () => {
    expect(voiceKind({ outcome: Outcomes.FoundAssigned, voice: 'found' })).toBe('found')
    expect(voiceKind({ outcome: Outcomes.AlreadyAssigned, voice: 'dup' })).toBe('dup')
    expect(voiceKind({ outcome: Outcomes.NotEligible, voice: 'notfound' })).toBe('notfound')
    expect(voiceKind({ outcome: Outcomes.FoundUnassigned })).toBe('found')
    expect(voiceKind({ outcome: Outcomes.AlreadyAssigned, voice: 'raro' })).toBe('dup')
    expect(voiceKind({ outcome: Outcomes.NotFound })).toBe('notfound')
    expect(es('scan.voice.found')).toBe('Sí')
    expect(es('scan.voice.notfound')).toBe('No')
    expect(es('scan.voice.dup')).toBe('Ok')
  })

  it('cada motivo que no es «asignada» explica qué hacer; asignada no', () => {
    for (const reason of Object.values(Reasons)) {
      const key = hintKey({ outcome: Outcomes.NotEligible, reasonCode: reason })
      expect(key, reason).toBe(`scan.hint.${reason}`)
      expect(es(key!), reason).not.toBe(key)
    }
    expect(hintKey({ outcome: Outcomes.FoundAssigned, reasonCode: null })).toBeNull()
    expect(hintKey({ outcome: Outcomes.NotFound, reasonCode: 'MOTIVO_NUEVO' })).toBeNull()
    expect(es('scan.hint.NO_OPEN_ROUTE')).toContain('Sala de despacho')
  })
})

describe('sesión y medidor', () => {
  const assigned = (id: string): ScanResult => ({ outcome: Outcomes.FoundAssigned, orderPublicId: id, orderNumber: `OT-${id}`, tripCode: 'R-1' })

  it('el medidor cuenta órdenes DISTINTAS asignadas: repetir o un «ya estaba» no suma', () => {
    const list = [
      entry(1, assigned('a')),
      entry(2, { outcome: Outcomes.AlreadyAssigned, orderPublicId: 'a' }),
      entry(3, assigned('b')),
      entry(4, { outcome: Outcomes.NotFound }),
      entry(5, { outcome: Outcomes.FoundUnassigned, orderPublicId: 'c' }),
      entry(6, assigned('a')),
    ]
    expect([...assignedOrderIds(list)].sort()).toEqual(['a', 'b'])
  })

  it('«N de M»: M = asignadas + pendientes con el filtro; sin permiso de pendientes no hay M; porcentaje redondeado', () => {
    expect(meterOf(3, 9)).toEqual({ done: 3, total: 12, percent: 25 })
    expect(meterOf(1, 2)).toEqual({ done: 1, total: 3, percent: 33 })
    expect(meterOf(0, 0)).toEqual({ done: 0, total: 0, percent: 0 })
    expect(meterOf(4, null)).toEqual({ done: 4, total: null, percent: 0 })
  })

  it('la lista de la sesión pone lo último arriba y recuerda a lo más 200', () => {
    let list: ScanEntry[] = []
    for (let i = 1; i <= MAX_SESSION_ENTRIES + 5; i++) list = pushEntry(list, entry(i, { outcome: Outcomes.NotFound }))
    expect(list).toHaveLength(MAX_SESSION_ENTRIES)
    expect(list[0].id).toBe(MAX_SESSION_ENTRIES + 5)
  })

  it('la referencia es el número de la orden; si no se encontró, lo escrito', () => {
    expect(entryRef(entry(1, { outcome: Outcomes.FoundAssigned, orderNumber: 'OT-7' }, {}, 'PKG-1'))).toBe('OT-7')
    expect(entryRef(entry(2, { outcome: Outcomes.NotFound }, {}, 'ZZZ'))).toBe('ZZZ')
  })

  it('el buscador libre mira referencia, empaque, cliente, pueblo, ruta, resultado y mensaje, sin acentos ni mayúsculas', () => {
    const e = entry(
      1,
      { outcome: Outcomes.FoundAssigned, orderNumber: 'OT-7', packBatchNumber: 'PKG-9', tripCode: 'R-12', message: 'Asignado a la ruta R-12.' },
      { clientName: 'Advance Solutions', city: 'Bayamón' },
    )
    expect(entryMatchesQuery(e, 'bayamon', 'Asignado')).toBe(true)
    expect(entryMatchesQuery(e, 'pkg-9 advance', 'Asignado')).toBe(true)
    expect(entryMatchesQuery(e, 'r-12', 'Asignado')).toBe(true)
    expect(entryMatchesQuery(e, 'asignado', 'Asignado')).toBe(true)
    expect(entryMatchesQuery(e, 'caguas', 'Asignado')).toBe(false)
    expect(entryMatchesQuery(e, '', 'Asignado')).toBe(true)
    expect(pendingMatchesQuery({ orderNumber: 'OT-7', clientName: 'Advance', city: 'Bayamón' }, 'bayamon ot-7')).toBe(true)
    expect(pendingMatchesQuery({ orderNumber: 'OT-7' }, 'otro')).toBe(false)
  })
})

describe('filtros', () => {
  it('sin filtros entra todo; con filtro, solo lo que se sabe que cumple', () => {
    const e = entry(1, { outcome: Outcomes.FoundAssigned, orderPublicId: 'o-1' }, { zoneCode: 'R-01', postalCode: '00949', city: 'Toa Baja', clientName: 'Advance' })
    const unknown = entry(2, { outcome: Outcomes.FoundAssigned, orderPublicId: 'o-2' }, {})
    const notFound = entry(3, { outcome: Outcomes.NotFound }, {})
    expect(hasFilters(NO_FILTERS)).toBe(false)
    expect(entryMatchesFilters(unknown, NO_FILTERS)).toBe(true)
    expect(entryMatchesFilters(e, { ...NO_FILTERS, zone: '1', zoneCode: 'R-01' })).toBe(true)
    expect(entryMatchesFilters(e, { ...NO_FILTERS, zone: '2', zoneCode: 'R-02' })).toBe(false)
    expect(entryMatchesFilters(unknown, { ...NO_FILTERS, zone: '1', zoneCode: 'R-01' })).toBe(false)
    expect(entryMatchesFilters(unknown, { ...NO_FILTERS, zone: 'none' })).toBe(true)
    expect(entryMatchesFilters(e, { ...NO_FILTERS, zone: 'none' })).toBe(false)
    expect(entryMatchesFilters(e, { ...NO_FILTERS, postalCode: '009' })).toBe(true)
    expect(entryMatchesFilters(e, { ...NO_FILTERS, postalCode: '006' })).toBe(false)
    expect(entryMatchesFilters(e, { ...NO_FILTERS, city: 'toa baja' })).toBe(true)
    expect(entryMatchesFilters(e, { ...NO_FILTERS, clientName: 'ADVANCE' })).toBe(true)
    expect(entryMatchesFilters(e, { ...NO_FILTERS, clientName: 'Otro' })).toBe(false)
    // un código que no es de ninguna orden se ve siempre (un error de lectura no desaparece por un filtro)
    expect(entryMatchesFilters(notFound, { ...NO_FILTERS, zone: '1', zoneCode: 'R-01', clientName: 'Otro' })).toBe(true)
  })

  it('la consulta de pendientes manda solo lo elegido (zona, sin zona, CP, pueblo, cliente) con Take 500', () => {
    expect(buildPendingQuery(NO_FILTERS, null)).toEqual({ Skip: 0, Take: 500 })
    expect(buildPendingQuery({ ...NO_FILTERS, zone: '3', postalCode: ' 009 ', city: ' Toa ' }, 'c-1')).toEqual({
      DispatchZoneId: 3,
      PostalCode: '009',
      City: 'Toa',
      ClientPublicId: 'c-1',
      Skip: 0,
      Take: 500,
    })
    expect(buildPendingQuery({ ...NO_FILTERS, zone: 'none' }, null)).toEqual({ NoZone: true, Skip: 0, Take: 500 })
  })
})

describe('textos', () => {
  it('todas las claves scan.* existen en español e inglés y las de inglés no son el español', () => {
    const keys = [
      'scan.title', 'scan.sub', 'scan.voice.on', 'scan.voice.off', 'scan.mode.out', 'scan.input.go', 'scan.input.hint', 'scan.planDate.label',
      'scan.result.ready', 'scan.outcome.FOUND_ASSIGNED', 'scan.outcome.NOT_FOUND', 'scan.hint.NO_ZONE', 'scan.feed.title', 'scan.pending.title',
      'scan.meter.caption', 'scan.order.open',
    ]
    for (const key of keys) {
      expect(translate('es', key), key).not.toBe(key)
      expect(translate('en', key), key).not.toBe(key)
      expect(translate('en', key), key).not.toBe(translate('es', key))
    }
  })
})
