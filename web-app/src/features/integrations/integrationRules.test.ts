// Lote 132 — reglas puras de Integraciones: esquema del formulario con sus mensajes, cuerpos del API, horario y edición de celdas de la bandeja.
import { describe, expect, it } from 'vitest'
import { emptyEmail } from './emailRules'
import { emptyFormat } from './formatRules'
import { emptyOutput } from './outputRules'
import { emptyRemote } from './remoteRules'
import { OUTBOX_TONE, WEBHOOK_EVENTS, buildCreateRequest, buildPatchRequest, eventKey, integrationSchema, integrationValuesOf, scheduleText, webhookState, withCell, type IntegrationValues } from './integrationRules'

const M = {
  nameRequired: 'nombre', nameMax: 'max', clientRequired: 'cliente', templateRequired: 'plantilla', folderRequired: 'carpeta', folderInvalid: 'carpeta inválida', interval: 'intervalo', retention: 'retención', scopes: 'alcances',
}
const t = (key: string, p?: Record<string, string | number>) => (p ? `${key}:${Object.values(p).join(',')}` : key)
const base: IntegrationValues = {
  name: ' AxisCare ', description: ' ', clientPublicId: 'c1', templatePublicId: 't1', connectorKind: 'FOLDER', folder: ' axis_1 ', scheduleKind: 'INTERVAL', intervalMinutes: '5', scopes: ['files.orders_import'], notifyUserIds: [3], includeMe: true, retentionDays: '30', email: emptyEmail(), remote: emptyRemote('SFTP'), format: emptyFormat(), direction: 'INBOUND', output: emptyOutput(),
}

describe('integrationSchema', () => {
  const issues = (v: Partial<IntegrationValues>, editing = false) => {
    const r = integrationSchema(M, editing).safeParse({ ...base, ...v })
    return r.success ? [] : r.error.issues.map((i) => `${i.path.join('.')}:${i.message}`)
  }

  it('acepta un alta completa y exige nombre, cliente, plantilla y al menos un alcance', () => {
    expect(issues({})).toEqual([])
    expect(issues({ name: ' ', clientPublicId: '', templatePublicId: '', scopes: [] })).toEqual(['name:nombre', 'scopes:alcances', 'templatePublicId:plantilla', 'clientPublicId:cliente'])
  })

  it('en la edición no se exige el cliente (no se cambia)', () => {
    expect(issues({ clientPublicId: '' }, true)).toEqual([])
  })

  it('la carpeta solo admite letras, números, guion y guion bajo y es obligatoria en el conector de carpeta', () => {
    expect(issues({ folder: '' })).toEqual(['folder:carpeta'])
    expect(issues({ folder: '../fuera' })).toEqual(['folder:carpeta inválida'])
    expect(issues({ folder: 'a b' })).toEqual(['folder:carpeta inválida'])
    expect(issues({ connectorKind: 'MANUAL', folder: '' })).toEqual([])
  })

  it('el intervalo va de 1 a 10080 minutos y la retención de 1 a 365 días', () => {
    expect(issues({ intervalMinutes: '0' })).toEqual(['intervalMinutes:intervalo'])
    expect(issues({ intervalMinutes: '10081' })).toEqual(['intervalMinutes:intervalo'])
    expect(issues({ scheduleKind: 'MANUAL', intervalMinutes: 'x' })).toEqual([])
    expect(issues({ retentionDays: '0' })).toEqual(['retentionDays:retención'])
    expect(issues({ retentionDays: '366' })).toEqual(['retentionDays:retención'])
    expect(issues({ retentionDays: '1.5' })).toEqual(['retentionDays:retención'])
  })
})

describe('cuerpos del API', () => {
  it('el alta manda el intervalo en segundos, la carpeta recortada y los avisos con «incluirme»', () => {
    expect(buildCreateRequest(base)).toEqual({
      name: 'AxisCare', description: null, clientPublicId: 'c1', connectorKind: 'FOLDER', folder: 'axis_1', templatePublicId: 't1', scheduleKind: 'INTERVAL', intervalSeconds: 300, scopes: ['files.orders_import'], notifyUserIds: [3], includeMe: true, retentionDays: 30,
    })
  })

  it('una subida manual no lleva carpeta ni horario', () => {
    expect(buildCreateRequest({ ...base, connectorKind: 'MANUAL', scheduleKind: 'INTERVAL' })).toMatchObject({ connectorKind: 'MANUAL', folder: null, scheduleKind: 'MANUAL' })
    expect(buildCreateRequest({ ...base, connectorKind: 'MANUAL', scheduleKind: 'INTERVAL' })).not.toHaveProperty('intervalSeconds')
  })

  it('la edición manda lo que cambia sin el cliente ni el conector y «incluirme» solo si se marca', () => {
    const patch = buildPatchRequest({ ...base, includeMe: false }, { connectorKind: 'FOLDER' })
    expect(patch).toMatchObject({ name: 'AxisCare', description: '', templatePublicId: 't1', folder: 'axis_1', scheduleKind: 'INTERVAL', intervalSeconds: 300 })
    expect(patch).not.toHaveProperty('clientPublicId')
    expect(patch.includeMe).toBeUndefined()
    expect(buildPatchRequest(base, { connectorKind: 'MANUAL' })).not.toHaveProperty('folder')
  })

  it('el formulario de edición parte de la integración guardada (minutos, no segundos)', () => {
    const v = integrationValuesOf({ id: 1, name: 'X', connectorKind: 'FOLDER', folder: 'f', scheduleKind: 'INTERVAL', intervalSeconds: 3600, scopes: ['a'], notifyUsers: [{ id: 4 }], retentionDays: 10, clientPublicId: 'c', templatePublicId: 't' } as never)
    expect(v).toMatchObject({ intervalMinutes: '60', scopes: ['a'], notifyUserIds: [4], retentionDays: '10', includeMe: false })
    expect(integrationValuesOf(null)).toMatchObject({ connectorKind: 'FOLDER', scheduleKind: 'MANUAL', includeMe: true, retentionDays: '30' })
  })
})

describe('horario y celdas', () => {
  it('resume el horario en días, horas o minutos', () => {
    expect(scheduleText({ scheduleKind: 'MANUAL', intervalSeconds: null }, t)).toBe('integrations.schedule.manual')
    expect(scheduleText({ scheduleKind: 'INTERVAL', intervalSeconds: 300 }, t)).toBe('integrations.schedule.everyMinutes:5')
    expect(scheduleText({ scheduleKind: 'INTERVAL', intervalSeconds: 7200 }, t)).toBe('integrations.schedule.everyHours:2')
    expect(scheduleText({ scheduleKind: 'INTERVAL', intervalSeconds: 172800 }, t)).toBe('integrations.schedule.everyDays:2')
  })

  it('cambia una celda de una fila sin tocar las demás ni mutar', () => {
    const rows = [{ row: 2, cells: ['a', 'b'] }, { row: 3, cells: ['c', 'd'] }]
    expect(withCell(rows, 3, 1, 'X')).toEqual([{ row: 2, cells: ['a', 'b'] }, { row: 3, cells: ['c', 'X'] }])
    expect(rows[1].cells[1]).toBe('d')
  })
})

describe('conector API (Lote 135)', () => {
  const api = { needsClient: (codes: string[]) => codes.includes('orders:create'), message: 'cliente por alcance' }
  const issues = (v: Partial<IntegrationValues>) => {
    const r = integrationSchema(M, false, undefined, undefined, api).safeParse({ ...base, connectorKind: 'API', folder: '', templatePublicId: '', clientPublicId: '', scopes: ['warehouse:read'], ...v })
    return r.success ? [] : r.error.issues.map((i) => `${i.path.join('.')}:${i.message}`)
  }

  it('no pide plantilla ni carpeta, y el cliente solo si algún alcance es de transporte', () => {
    expect(issues({})).toEqual([])
    expect(issues({ scopes: ['orders:create'] })).toEqual(['clientPublicId:cliente por alcance'])
    expect(issues({ scopes: ['orders:create'], clientPublicId: 'c1' })).toEqual([])
  })

  it('el alta no manda plantilla, carpeta ni horario y el cliente puede ir vacío', () => {
    const body = buildCreateRequest({ ...base, connectorKind: 'API', clientPublicId: '', templatePublicId: '', scopes: ['warehouse:read'] })
    expect(body.templatePublicId).toBeNull()
    expect(body.clientPublicId).toBeNull()
    expect(body.folder).toBeNull()
    expect(body.scheduleKind).toBe('MANUAL')
  })
})

describe('webhooks (Lote 136)', () => {
  it('el estado de un webhook: suspendido manda sobre apagado y activo', () => {
    expect(webhookState({ isActive: true, suspendedAtUtc: null })).toBe('ACTIVE')
    expect(webhookState({ isActive: true, suspendedAtUtc: '2026-10-19T10:00:00Z' })).toBe('SUSPENDED')
    expect(webhookState({ isActive: false })).toBe('OFF')
  })

  it('los eventos son los del API y su clave de traducción no lleva punto', () => {
    expect([...WEBHOOK_EVENTS]).toEqual(['order.status_changed', 'order.delivered', 'invoice.issued'])
    expect(WEBHOOK_EVENTS.map(eventKey)).toEqual(['order_status_changed', 'order_delivered', 'invoice_issued'])
    expect(WEBHOOK_EVENTS.map(eventKey).every((k) => !k.includes('.'))).toBe(true)
  })

  it('cada estatus de una salida tiene su tono', () => {
    expect(Object.keys(OUTBOX_TONE).sort()).toEqual(['DELIVERED', 'DISCARDED', 'PENDING', 'UNDELIVERED'])
    expect(OUTBOX_TONE.UNDELIVERED).toBe('fail')
  })
})
