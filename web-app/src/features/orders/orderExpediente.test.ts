// Lote F-C2 — Reglas puras del expediente: visibilidad de las acciones (permiso Y capacidad), derivación de «asignable», valores de
// edición, esquema y armado del PATCH solo con lo cambiado, y reconocimiento del 409 de edición concurrente.
import { describe, expect, it } from 'vitest'
import { ApiError } from '../../kernel/api/problem'
import es from '../../kernel/i18n/es.json'
import en from '../../kernel/i18n/en.json'
import type { OrderDetailDto } from './api'
import {
  buildOrderPatch,
  hasOrderChanges,
  isAssignableOrder,
  isConcurrencyConflict,
  orderActionVisibility,
  orderDay,
  orderEditSchema,
  orderEditValues,
  orderPatchChanges,
  type OrderAbilities,
} from './orderRules'

const ALL: OrderAbilities = { edit: true, cancel: true, reopen: true, creditOverride: true, dispatch: true, viewPrice: true, catalogOn: true }
const NONE: OrderAbilities = { edit: false, cancel: false, reopen: false, creditOverride: false, dispatch: false, viewPrice: false, catalogOn: false }

const DRAFT_CAPS = { canEditCargo: true, canCancel: true, canDelete: true, canConfirm: true, canReprice: false }
const CONFIRMED_CAPS = { canEditCargo: false, canCancel: true, canDelete: false, canConfirm: false, canReprice: true }

function order(extra: Partial<OrderDetailDto> = {}): OrderDetailDto {
  return {
    id: 7,
    publicId: 'o-7',
    orderNumber: 'ORD-00007',
    clientPublicId: 'c-1',
    clientName: 'Acme Corp',
    serviceType: 'STANDARD',
    serviceTypeLabel: 'Estándar',
    priority: 'NORMAL',
    status: 'DRAFT',
    isInitialStatus: true,
    isActive: true,
    codAmount: 50,
    notes: 'Llamar antes',
    requestedDate: '2026-10-12T00:00:00',
    promisedDate: '2026-10-14T00:00:00',
    rowVersion: 'AAA=',
    delivery: { locationPublicId: 'l-1', name: 'Farmacia Central', city: 'San Juan', postalCode: '00901' },
    packages: [
      { id: 1, packageType: 'BOX', packageTypeLabel: 'Caja', description: 'Ropa', pieces: 2 },
      { id: 2, packageType: 'ENV', packageTypeLabel: 'Sobre', description: '', pieces: 1 },
    ],
    capabilities: DRAFT_CAPS,
    ...extra,
  }
}

describe('orderActionVisibility — permiso Y capacidad', () => {
  it('en Borrador con todos los permisos: confirmar, cancelar, eliminar y editar; no re-cotizar', () => {
    const v = orderActionVisibility(order(), ALL, false)
    expect(v).toMatchObject({ confirm: 'normal', reprice: false, cancel: true, delete: true, edit: true, assignDriver: false, quote: true, lateral: true })
  })

  it('confirmada: re-cotizar y cancelar; no confirmar, eliminar ni editar', () => {
    const v = orderActionVisibility(order({ status: 'CONFIRMED', capabilities: CONFIRMED_CAPS }), ALL, false)
    expect(v).toMatchObject({ confirm: null, reprice: true, cancel: true, delete: false, edit: false })
  })

  it('sin permisos no se ofrece ninguna acción aunque la capacidad lo permita', () => {
    const v = orderActionVisibility(order(), NONE, true)
    expect(v).toEqual({ confirm: null, reprice: false, cancel: false, reopen: false, delete: false, assignDriver: false, edit: false, quote: false, lateral: false })
  })

  it('quien solo tiene orders.credit_override ve «Confirmar» en modo de autorización (Facturación)', () => {
    expect(orderActionVisibility(order(), { ...NONE, creditOverride: true }, false).confirm).toBe('overrideOnly')
    expect(orderActionVisibility(order(), { ...NONE, edit: true }, false).confirm).toBe('normal')
  })

  it('Reabrir exige la capacidad canReopen Y el permiso orders.reopen', () => {
    const final = order({ capabilities: { canReopen: true } })
    expect(orderActionVisibility(final, { ...NONE, reopen: true }, false).reopen).toBe(true)
    expect(orderActionVisibility(final, NONE, false).reopen).toBe(false)
    expect(orderActionVisibility(order({ capabilities: { canReopen: false } }), { ...NONE, reopen: true }, false).reopen).toBe(false)
    expect(orderActionVisibility(order({ capabilities: { canReopen: true }, isActive: false }), { ...NONE, reopen: true }, false).reopen).toBe(false)
  })

  it('cancelar y eliminar usan orders.cancel; re-cotizar y laterales, orders.edit', () => {
    const v = orderActionVisibility(order({ capabilities: { ...DRAFT_CAPS, canReprice: true } }), { ...NONE, cancel: true }, false)
    expect(v).toMatchObject({ cancel: true, delete: true, reprice: false, lateral: false, edit: false })
  })

  it('asignar chofer exige derivación «asignable», trips.dispatch y el módulo CATALOG', () => {
    expect(orderActionVisibility(order(), ALL, true).assignDriver).toBe(true)
    expect(orderActionVisibility(order(), { ...ALL, dispatch: false }, true).assignDriver).toBe(false)
    expect(orderActionVisibility(order(), { ...ALL, catalogOn: false }, true).assignDriver).toBe(false)
  })

  it('una orden dada de baja no ofrece nada', () => {
    const v = orderActionVisibility(order({ isActive: false }), ALL, true)
    expect(Object.values(v).some(Boolean)).toBe(false)
  })

  it('el precio (Cotizar) es solo de orders.view_price', () => {
    expect(orderActionVisibility(order(), { ...ALL, viewPrice: false }, false).quote).toBe(false)
  })
})

describe('isAssignableOrder — derivación en el cliente', () => {
  const statuses = [
    { code: 'DRAFT', stageKind: 'PIPELINE', sortOrder: 10 },
    { code: 'CONFIRMED', stageKind: 'PIPELINE', sortOrder: 20 },
    { code: 'PLANNED', stageKind: 'PIPELINE', sortOrder: 40 },
    { code: 'IN_TRANSIT', stageKind: 'PIPELINE', sortOrder: 50 },
    { code: 'ARRIVED', stageKind: 'PIPELINE', sortOrder: 60 },
    { code: 'DELIVERED', stageKind: 'TERMINAL', sortOrder: 70 },
    { code: 'ON_HOLD', stageKind: 'LATERAL', sortOrder: 80 },
    { code: 'CANCELLED', stageKind: 'TERMINAL', sortOrder: 90 },
  ]
  const special = (status: string, extra: Partial<OrderDetailDto> = {}) => ({ isSpecialDelivery: true, isActive: true, status, ...extra })

  it('una entrega especial hasta IN_TRANSIT inclusive es asignable (en tránsito se reasigna)', () => {
    for (const s of ['DRAFT', 'CONFIRMED', 'PLANNED', 'IN_TRANSIT']) expect(isAssignableOrder(special(s), statuses)).toBe(true)
  })

  it('después de IN_TRANSIT, en un lateral o en un terminal ya no', () => {
    for (const s of ['ARRIVED', 'DELIVERED', 'ON_HOLD', 'CANCELLED']) expect(isAssignableOrder(special(s), statuses)).toBe(false)
  })

  it('una orden normal, dada de baja o con un estatus desconocido no', () => {
    expect(isAssignableOrder(special('DRAFT', { isSpecialDelivery: false }), statuses)).toBe(false)
    expect(isAssignableOrder(special('DRAFT', { isActive: false }), statuses)).toBe(false)
    expect(isAssignableOrder(special('NOPE'), statuses)).toBe(false)
    expect(isAssignableOrder(special('DRAFT'), [])).toBe(false)
  })

  it('sin IN_TRANSIT en la lista basta con estar en el pipeline', () => {
    expect(isAssignableOrder(special('CONFIRMED'), statuses.filter((s) => s.code !== 'IN_TRANSIT'))).toBe(true)
  })
})

describe('orderEditValues y orderPatchChanges — solo lo cambiado', () => {
  it('los valores salen de la ficha (día de las fechas sin correrlo de zona, COD vacío si no hay)', () => {
    const v = orderEditValues(order())
    expect(v).toMatchObject({ serviceType: 'STANDARD', priority: 'NORMAL', readyDate: '2026-10-12', dueDate: '2026-10-14', consigneeLocationPublicId: 'l-1', codAmount: 50, notes: 'Llamar antes' })
    expect(v.packages).toEqual([
      { packageType: 'BOX', description: 'Ropa', pieces: 2 },
      { packageType: 'ENV', description: '', pieces: 1 },
    ])
    expect(orderEditValues(order({ codAmount: null })).codAmount).toBeNull()
    expect(orderDay(null)).toBe('')
  })

  it('sin cambios no hay nada que mandar', () => {
    const o = order()
    expect(orderPatchChanges(o, orderEditValues(o))).toEqual({})
    expect(hasOrderChanges(buildOrderPatch(o, orderEditValues(o)))).toBe(false)
  })

  it('manda solo los campos cambiados, con el rowVersion de la ficha', () => {
    const o = order()
    const v = { ...orderEditValues(o), serviceType: 'EXPRESS', notes: 'Dejar con el portero', dueDate: '2026-10-20', consigneeLocationPublicId: 'l-2' }
    expect(buildOrderPatch(o, v)).toEqual({
      serviceType: 'EXPRESS',
      notes: 'Dejar con el portero',
      promisedDate: '2026-10-20T00:00:00',
      consigneeLocationPublicId: 'l-2',
      rowVersion: 'AAA=',
    })
  })

  it('el texto va tal cual: «» borra el comentario y la prioridad', () => {
    const o = order()
    const patch = orderPatchChanges(o, { ...orderEditValues(o), notes: '', priority: '' })
    expect(patch.notes).toBe('')
    expect(patch.priority).toBe('')
    expect(patch).not.toHaveProperty('serviceType')
  })

  it('los paquetes van como reemplazo completo solo si cambiaron (tipo, descripción o piezas, añadir o quitar)', () => {
    const o = order()
    const base = orderEditValues(o)
    // mismo contenido con otro formato de mayúsculas o espacios: no es un cambio
    expect(orderPatchChanges(o, { ...base, packages: base.packages.map((p) => ({ ...p, packageType: p.packageType.toLowerCase(), description: `${p.description} ` })) })).toEqual({})
    const edited = orderPatchChanges(o, { ...base, packages: [{ packageType: 'BOX', description: 'Ropa', pieces: 5 }, base.packages[1]] })
    expect(edited.packages).toEqual([
      { packageType: 'BOX', description: 'Ropa', pieces: 5 },
      { packageType: 'ENV', description: null, pieces: 1 },
    ])
    expect(orderPatchChanges(o, { ...base, packages: [base.packages[0]] }).packages).toHaveLength(1)
    expect(orderPatchChanges(o, { ...base, packages: [...base.packages, { packageType: 'BOX', description: '', pieces: 1 }] }).packages).toHaveLength(3)
  })

  it('COD: monto nuevo o distinto → codAmount; quitarlo → clearCod (nunca los dos)', () => {
    const o = order()
    const base = orderEditValues(o)
    expect(orderPatchChanges(o, { ...base, codAmount: 80 })).toEqual({ codAmount: 80 })
    expect(orderPatchChanges(o, { ...base, codAmount: null })).toEqual({ clearCod: true })
    expect(orderPatchChanges(o, { ...base, codAmount: 0 })).toEqual({ clearCod: true })
    const noCod = order({ codAmount: null })
    expect(orderPatchChanges(noCod, orderEditValues(noCod))).toEqual({})
    expect(orderPatchChanges(noCod, { ...orderEditValues(noCod), codAmount: 25 })).toEqual({ codAmount: 25 })
  })

  it('una entrega especial no manda paquetes ni COD', () => {
    const o = order({ isSpecialDelivery: true, packages: [], codAmount: null })
    const patch = orderPatchChanges(o, { ...orderEditValues(o), packages: [{ packageType: 'BOX', description: '', pieces: 1 }], codAmount: 30, notes: 'x' })
    expect(patch).toEqual({ notes: 'x' })
  })

  it('«factura repetida: guardar de todos modos» agrega confirmDuplicateInvoice', () => {
    const o = order()
    const body = buildOrderPatch(o, { ...orderEditValues(o), consigneeLocationPublicId: 'l-2' }, { confirmDuplicateInvoice: true })
    expect(body.confirmDuplicateInvoice).toBe(true)
    expect(hasOrderChanges({ rowVersion: 'x', confirmDuplicateInvoice: true })).toBe(false)
  })
})

describe('orderEditSchema', () => {
  const m = {
    clientRequired: 'c',
    serviceRequired: 'El tipo de servicio es obligatorio.',
    consigneeRequired: 'El consignatario es obligatorio.',
    packagesRequired: 'Indique al menos una línea de paquete.',
    packageTypeRequired: 'El tipo de paquete es obligatorio.',
    pieces: 'La cantidad de piezas debe ser al menos 1.',
    codNegative: 'El monto COD no puede ser negativo.',
    specialRequired: 's',
    numberMax: 'n',
    descriptionMax: 'Máximo 200 caracteres.',
    dueBeforeReady: 'La fecha límite no puede ser anterior a la fecha lista.',
    dateRequired: 'La fecha no se puede quitar; elija otra.',
  }
  const schema = (isSpecial = false) => orderEditSchema(m, { hadReady: true, hadDue: true, isSpecial })
  const messages = (v: unknown, isSpecial = false) => {
    const r = schema(isSpecial).safeParse(v)
    return r.success ? [] : r.error.issues.map((i) => i.message)
  }

  it('los valores de una ficha válida pasan', () => {
    expect(messages(orderEditValues(order()))).toEqual([])
  })

  it('usa los mensajes exactos del servidor', () => {
    const v = { ...orderEditValues(order()), serviceType: '', consigneeLocationPublicId: '', packages: [{ packageType: '', description: '', pieces: 0 }], codAmount: -1 }
    expect(messages(v)).toEqual([m.serviceRequired, m.consigneeRequired, m.packageTypeRequired, m.pieces, m.codNegative])
    expect(messages({ ...orderEditValues(order()), packages: [] })).toEqual([m.packagesRequired])
  })

  it('no deja quitar una fecha ya capturada (el servidor la leería como «no cambiar») ni invertir las fechas', () => {
    expect(messages({ ...orderEditValues(order()), readyDate: '' })).toEqual([m.dateRequired])
    expect(messages({ ...orderEditValues(order()), dueDate: '2026-10-01' })).toEqual([m.dueBeforeReady])
    expect(orderEditSchema(m, { hadReady: false, hadDue: false, isSpecial: false }).safeParse({ ...orderEditValues(order()), readyDate: '', dueDate: '' }).success).toBe(true)
  })

  it('una entrega especial no valida paquetes ni COD', () => {
    expect(messages({ ...orderEditValues(order()), packages: [], codAmount: -5 }, true)).toEqual([])
  })
})

describe('isConcurrencyConflict', () => {
  it('solo el 409 «conflict»; la factura repetida (también 409) no lo es', () => {
    expect(isConcurrencyConflict(new ApiError(409, { title: 'El registro fue modificado por otro usuario; recargue e intente de nuevo.', code: 'conflict' }))).toBe(true)
    expect(isConcurrencyConflict(new ApiError(409, { title: 'x', code: 'duplicate_invoice' }))).toBe(false)
    expect(isConcurrencyConflict(new ApiError(422, { title: 'x', code: 'conflict' }))).toBe(false)
    expect(isConcurrencyConflict(new Error('x'))).toBe(false)
  })
})

describe('textos del expediente', () => {
  const keys = (o: unknown, prefix = ''): string[] =>
    Object.entries(o as Record<string, unknown>).flatMap(([k, v]) => (typeof v === 'object' && v ? keys(v, `${prefix}${k}.`) : [`${prefix}${k}`]))

  it('orders.detail y orders.actions existen igual en español e inglés', () => {
    expect(keys(en.orders.detail).sort()).toEqual(keys(es.orders.detail).sort())
    expect(keys(en.orders.actions).sort()).toEqual(keys(es.orders.actions).sort())
  })
})
