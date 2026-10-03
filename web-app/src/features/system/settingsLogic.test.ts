// Lógica pura de Ajustes de la compañía: calendario laboral (máscara, feriados, próximo día hábil), módulos (dependencias y
// cascada), formulario de Región y formatos (vista previa, cuerpo del PUT, "Personalizada"), matriz de acciones por estatus y
// resumen de recepción por almacén.
import { describe, expect, it } from 'vitest'
import { PR_FORMAT, US_FORMAT } from '../../kernel/format'
import { capabilityAllowed, recvSummaryRows } from './settings/operations'
import { settingsTabFromParam } from './settings/settingsTabs'
import { allowedLists, formIsCustom, previewSettings, regionCodes, regionRequestBody, toRegionForm, withCurrent } from './regionForm'
import {
  addDays,
  holidayOnDate,
  isHoliday,
  isWorkDay,
  maskFromWorkDays,
  nextWorkDay,
  toggleWorkDay,
  weekDayOf,
  weekOrder,
  workDaysFromMask,
} from './tenantCalendar'
import { dependencyOff, enabledDependents, moduleName } from './tenantModules'

describe('calendario laboral', () => {
  const MON_FRI = 62 // lunes (2) … viernes (32)
  const holidays = [
    { date: '2026-01-01', isRecurring: true },
    { date: '2026-10-12', isRecurring: false },
  ]

  it('máscara ↔ días (domingo = bit 1, sábado = bit 64, como el servidor)', () => {
    expect(workDaysFromMask(MON_FRI)).toEqual([1, 2, 3, 4, 5])
    expect(maskFromWorkDays([1, 2, 3, 4, 5])).toBe(62)
    expect(maskFromWorkDays([0, 6])).toBe(65)
  })

  it('al menos un día laborable: apagar el último no se puede', () => {
    expect(toggleWorkDay(MON_FRI, 6)).toBe(126)
    expect(toggleWorkDay(MON_FRI, 1)).toBe(60)
    expect(toggleWorkDay(2, 1)).toBeNull()
  })

  it('orden de la semana según el primer día de la compañía', () => {
    expect(weekOrder(0)).toEqual([0, 1, 2, 3, 4, 5, 6])
    expect(weekOrder(1)).toEqual([1, 2, 3, 4, 5, 6, 0])
  })

  it('feriados exactos y "cada año"', () => {
    expect(isHoliday('2027-01-01', holidays)).toBe(true)
    expect(isHoliday('2026-10-12', holidays)).toBe(true)
    expect(isHoliday('2027-10-12', holidays)).toBe(false)
    expect(holidayOnDate('2026-10-12', holidays)).toBe(true)
    expect(holidayOnDate('2027-01-01', holidays)).toBe(false)
  })

  it('próximo día hábil: salta fin de semana y feriados', () => {
    // viernes 2 de octubre de 2026 → lunes 5
    expect(weekDayOf('2026-10-02')).toBe(5)
    expect(nextWorkDay('2026-10-02', MON_FRI, holidays)).toBe('2026-10-05')
    // viernes 9 → lunes 12 es feriado → martes 13
    expect(nextWorkDay('2026-10-09', MON_FRI, holidays)).toBe('2026-10-13')
    // fin de año: 31-dic (jueves) → 1-ene feriado (viernes) → lunes 4
    expect(nextWorkDay('2026-12-31', MON_FRI, holidays)).toBe('2027-01-04')
    expect(isWorkDay('2026-10-03', MON_FRI, holidays)).toBe(false)
    expect(nextWorkDay('2026-10-02', 0, holidays)).toBeNull()
    expect(addDays('2026-02-28', 1)).toBe('2026-03-01')
  })
})

describe('módulos', () => {
  const modules = [
    { key: 'CATALOG', name: 'Catálogo', isCore: true, isEnabled: true },
    { key: 'LTL_GROUND', name: 'Operación diaria', isEnabled: true },
    { key: 'COD', name: 'COD', dependsOn: 'LTL_GROUND', isEnabled: true },
    { key: 'WMS_LOTSERIAL', name: 'WMS', isEnabled: false },
    { key: 'CROSSDOCK', name: 'Cross-dock', dependsOn: 'WMS_LOTSERIAL', isEnabled: false },
    { key: 'X', name: 'Hijo de COD', dependsOn: 'COD', isEnabled: true },
  ]
  it('nombre, dependencia apagada y cascada de dependientes encendidos', () => {
    expect(moduleName('LTL_GROUND', modules)).toBe('Operación diaria')
    expect(moduleName('NOPE', modules)).toBe('NOPE')
    expect(dependencyOff(modules[4], modules)).toBe(true)
    expect(dependencyOff(modules[2], modules)).toBe(false)
    expect(enabledDependents('LTL_GROUND', modules).map((m) => m.key)).toEqual(['COD', 'X'])
    expect(enabledDependents('WMS_LOTSERIAL', modules)).toEqual([])
  })
})

describe('formulario de Región y formatos', () => {
  it('ida y vuelta, vista previa con respaldo y cuerpo del PUT con el juego completo', () => {
    const v = toRegionForm(PR_FORMAT)
    expect(v.timeFormat).toBe('12')
    const draft = { ...v, dateOrder: 'DMY', currencyDecimals: '3', timeZoneId: 'Pacific/Honolulu', currencySymbol: ' ' }
    const preview = previewSettings(draft, PR_FORMAT)
    expect(preview.dateOrder).toBe('DMY')
    expect(preview.currencyDecimals).toBe(3)
    expect(preview.timeZoneId).toBe('Pacific/Honolulu')
    // un símbolo vacío no se puede pintar: la vista previa usa el vigente
    expect(preview.currencySymbol).toBe('$')
    const body = regionRequestBody({ ...draft, currencySymbol: '$', currencyCode: 'usd' })
    expect(body).toEqual({
      regionCode: 'PR',
      timeZoneId: 'Pacific/Honolulu',
      currencyCode: 'USD',
      currencySymbol: '$',
      currencySymbolPosition: 'B',
      currencyDecimals: 3,
      dateOrder: 'DMY',
      dateSeparator: '/',
      timeFormat: 12,
      weekStartDay: 0,
      thousandsSeparator: ',',
      decimalSeparator: '.',
      phoneCountryCode: '+1',
      phoneMask: '(###) ###-####',
    })
  })

  it('"Personalizada" frente a los valores de la región', () => {
    expect(formIsCustom(toRegionForm(PR_FORMAT), PR_FORMAT)).toBe(false)
    expect(formIsCustom({ ...toRegionForm(PR_FORMAT), timeFormat: '24' }, PR_FORMAT)).toBe(true)
    expect(formIsCustom(toRegionForm(US_FORMAT), US_FORMAT)).toBe(false)
  })

  it('listas permitidas del servidor (o las de respaldo) y valor actual agregado', () => {
    expect(allowedLists(null).dateOrders).toEqual(['MDY', 'DMY', 'YMD'])
    expect(allowedLists({ dateOrders: ['YMD'] }).dateOrders).toEqual(['YMD'])
    expect(regionCodes({ regions: [{ regionCode: 'us' }, { regionCode: 'PR' }] })).toEqual(['US', 'PR'])
    expect(regionCodes(undefined)).toEqual(['PR', 'US'])
    expect(withCurrent(['a', 'b'], 'c')).toEqual(['a', 'b', 'c'])
    expect(withCurrent([0, 2], '2')).toEqual([0, 2])
  })

  it('pestaña de ?tab=', () => {
    expect(settingsTabFromParam(null)).toBe('general')
    expect(settingsTabFromParam('region')).toBe('region')
    expect(settingsTabFromParam('nada')).toBe('general')
  })
})

describe('Operación', () => {
  it('acción por estatus: sin regla = permitido; la regla manda', () => {
    const rules = [
      { statusCode: 'DELIVERED', capability: 'CANCEL', isAllowed: false },
      { statusCode: 'CAPTURED', capability: 'EDIT_CARGO', isAllowed: true },
    ]
    expect(capabilityAllowed(rules, 'delivered', 'CANCEL')).toBe(false)
    expect(capabilityAllowed(rules, 'CAPTURED', 'EDIT_CARGO')).toBe(true)
    expect(capabilityAllowed(rules, 'CAPTURED', 'REPRICE')).toBe(true)
    expect(capabilityAllowed(undefined, 'X', 'CANCEL')).toBe(true)
  })

  it('recepción por almacén: con acomodo sin posición de recepción = aviso; directo nunca', () => {
    const rows = recvSummaryRows(
      [
        { publicId: 'a', code: 'ALM-01', receivingModeCode: 'PUTAWAY', defaultReceivingBinId: 7 },
        { publicId: 'b', code: 'ALM-02', receivingModeCode: 'PUTAWAY', defaultReceivingBinId: null },
        { publicId: 'c', code: 'ALM-03', receivingModeCode: 'DIRECT', defaultReceivingBinId: null },
      ],
      new Map([['a', { open: 3, pending: 1 }]]),
    )
    expect(rows.map((r) => [r.w.code, r.direct, r.noBin, r.open, r.pending])).toEqual([
      ['ALM-01', false, false, 3, 1],
      ['ALM-02', false, true, null, null],
      ['ALM-03', true, false, null, null],
    ])
  })
})
