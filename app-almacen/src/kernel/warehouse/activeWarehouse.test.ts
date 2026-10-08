import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'

import { setApiBaseUrl } from '../api/client'
import { sendHeartbeat } from '../auth/deviceAuth'
import { __resetSessionForTests, getSessionState, saveDeviceIdentity } from '../auth/session'
import { __resetDbForTests } from '../db/database'
import { getActiveWarehousePublicId, getCachedWarehouseOptions, refreshWarehouseOptions, resolveActiveWarehouse, selectActiveWarehouse } from './activeWarehouse'

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

const WAREHOUSES = [
  { publicId: 'wh-1', code: 'DEP', name: 'Depot', isActive: true, receivingModeCode: 'PUTAWAY' },
  { publicId: 'wh-2', code: 'SUR', name: 'Almacén Sur', isActive: true, receivingModeCode: 'DIRECT' },
  { publicId: 'wh-3', code: 'OLD', name: 'Viejo', isActive: false, receivingModeCode: 'PUTAWAY' },
]

let handlers: Record<string, (req: Request) => Response>

beforeEach(async () => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSecureStoreForTests()
  __resetSessionForTests()
  setApiBaseUrl('http://api.test')
  handlers = { '/api/v1/warehouses': () => jsonResponse(200, WAREHOUSES) }
  jest.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const req = input as Request
    const handler = handlers[new URL(req.url).pathname]
    if (!handler) throw new Error('sin red')
    return handler(req)
  })
  await saveDeviceIdentity({
    devicePublicId: 'dev-1', deviceSecret: 's', tenantName: 'T', defaultWarehousePublicId: 'wh-1', theme: null, defaultWarehouseReceivingMode: 'PUTAWAY',
  })
})

afterEach(() => jest.restoreAllMocks())

describe('almacén activo del aparato (varios almacenes por compañía)', () => {
  it('sin elección se trabaja en el por defecto, con su modo de recepción', () => {
    const active = resolveActiveWarehouse(getSessionState().device)
    expect(active).toMatchObject({ publicId: 'wh-1', receivingMode: 'PUTAWAY', isSelected: false })
    expect(getActiveWarehousePublicId()).toBe('wh-1')
  })

  it('refreshWarehouseOptions guarda solo los almacenes activos y los puede leer sin señal', async () => {
    const options = await refreshWarehouseOptions()
    expect(options.map((o) => o.name)).toEqual(['Almacén Sur', 'Depot'])
    expect(options).toHaveLength(2)
    handlers = {}
    expect(getCachedWarehouseOptions()).toHaveLength(2)
    expect(await refreshWarehouseOptions()).toHaveLength(2)
  })

  it('elegir otro almacén cambia el activo y su modo; elegir el por defecto quita la elección', async () => {
    const options = await refreshWarehouseOptions()
    const sur = options.find((o) => o.publicId === 'wh-2')!
    await selectActiveWarehouse(sur)
    expect(getActiveWarehousePublicId()).toBe('wh-2')
    expect(resolveActiveWarehouse(getSessionState().device)).toMatchObject({ name: 'Almacén Sur', receivingMode: 'DIRECT', isSelected: true })

    await selectActiveWarehouse(options.find((o) => o.publicId === 'wh-1')!)
    expect(getSessionState().device?.selectedWarehouse).toBeNull()
    expect(getActiveWarehousePublicId()).toBe('wh-1')
  })

  it('si el almacén elegido se desactiva, se vuelve al por defecto', async () => {
    const options = await refreshWarehouseOptions()
    await selectActiveWarehouse(options.find((o) => o.publicId === 'wh-2')!)
    handlers['/api/v1/warehouses'] = () => jsonResponse(200, [WAREHOUSES[0], { ...WAREHOUSES[1], isActive: false }])
    await refreshWarehouseOptions()
    expect(getActiveWarehousePublicId()).toBe('wh-1')
  })

  it('si la web cambia el almacén por defecto, se descarta el elegido en el aparato', async () => {
    const options = await refreshWarehouseOptions()
    await selectActiveWarehouse(options.find((o) => o.publicId === 'wh-2')!)
    handlers['/api/v1/devices/heartbeat'] = () =>
      jsonResponse(200, { isActive: true, defaultWarehousePublicId: 'wh-9', defaultWarehouseReceivingMode: 'DIRECT', serverTimeUtc: '2026-01-01T00:00:00Z' })
    await sendHeartbeat()
    expect(getSessionState().device?.selectedWarehouse).toBeNull()
    expect(getActiveWarehousePublicId()).toBe('wh-9')
  })

  it('un heartbeat sin cambio de por defecto conserva el almacén elegido', async () => {
    const options = await refreshWarehouseOptions()
    await selectActiveWarehouse(options.find((o) => o.publicId === 'wh-2')!)
    handlers['/api/v1/devices/heartbeat'] = () =>
      jsonResponse(200, { isActive: true, defaultWarehousePublicId: 'wh-1', defaultWarehouseReceivingMode: 'PUTAWAY', serverTimeUtc: '2026-01-01T00:00:00Z' })
    await sendHeartbeat()
    expect(getActiveWarehousePublicId()).toBe('wh-2')
  })
})
