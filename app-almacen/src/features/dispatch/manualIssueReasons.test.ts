import { __resetAllForTests } from 'expo-sqlite'

import { api, ApiError } from '../../kernel/api/client'
import { __resetDbForTests } from '../../kernel/db/database'
import { __resetSessionForTests, saveUserSession } from '../../kernel/auth/session'
import { getKv, KvKeys, setKv } from '../../kernel/db/kv'
import { downloadManualIssueReasons, mapReason, readLastReason, readManualIssueReasons, saveLastReason } from './manualIssueReasons'

jest.mock('../../kernel/api/client', () => {
  const actual = jest.requireActual('../../kernel/api/client')
  return { ...actual, api: { GET: jest.fn() } }
})

const getMock = api.GET as jest.Mock

function ok(data: unknown) {
  return Promise.resolve({ data, response: new Response(null, { status: 200 }) })
}

const ROWS = [
  { id: 1, entity: 'ManualIssueReason', code: 'SAMPLE', label: 'Muestra gratis', labels: { es: 'Muestra gratis', en: 'Free sample' }, sortOrder: 1, isEnabled: true, isActive: true, isDefault: true },
  { id: 2, entity: 'ManualIssueReason', code: 'SALE', label: 'Venta', labels: { es: 'Venta', en: 'Sale' }, sortOrder: 4, isEnabled: false, isActive: true },
]

async function signIn(perms: string[]) {
  await saveUserSession({ accessToken: 'a', accessExpiresAtUtc: '', refreshToken: 'r', refreshExpiresAtUtc: '', tenantId: 1, userId: 7, fullName: 'Ana Ruiz' })
  setKv(KvKeys.myPermissions, JSON.stringify({ '7': { permissions: perms, fetchedAtUtc: '2026-10-11T00:00:00Z' } }))
}

beforeEach(async () => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSessionForTests()
  getMock.mockReset()
  await signIn(['warehouse.issue'])
})

describe('motivos del despacho manual en el aparato (2026-10-11)', () => {
  it('nunca bajados → null (la pantalla usa los de fábrica)', () => {
    expect(readManualIssueReasons()).toBeNull()
  })

  it('baja GET /manual-issues/reasons y guarda los habilitados con sus etiquetas por idioma', async () => {
    getMock.mockReturnValueOnce(ok(ROWS))
    expect(await downloadManualIssueReasons()).toEqual({ resource: 'manualIssueReasons', pages: 1, items: 1 })
    expect(getMock.mock.calls[0][0]).toBe('/api/v1/manual-issues/reasons')
    expect(readManualIssueReasons()).toEqual([{ code: 'SAMPLE', label: 'Muestra gratis', labels: { es: 'Muestra gratis', en: 'Free sample' }, sortOrder: 1, isDefault: true }])
  })

  it('no vuelve a preguntar antes de 30 minutos, salvo que se fuerce', async () => {
    getMock.mockImplementation(() => ok(ROWS))
    await downloadManualIssueReasons()
    expect(await downloadManualIssueReasons()).toEqual({ resource: 'manualIssueReasons', pages: 0, items: 0 })
    expect(getMock).toHaveBeenCalledTimes(1)
    await downloadManualIssueReasons(true)
    expect(getMock).toHaveBeenCalledTimes(2)
  })

  it('403 (sin warehouse.issue) o 404 (servidor anterior) se saltan y conservan la copia; otros errores se propagan', async () => {
    setKv(KvKeys.manualIssueReasons, JSON.stringify({ reasons: [{ code: 'OTHER', label: 'Otro', labels: {}, sortOrder: 5 }], fetchedAtUtc: '2020-01-01T00:00:00Z' }))
    getMock.mockImplementationOnce(() => Promise.reject(new ApiError(403, null)))
    expect(await downloadManualIssueReasons()).toEqual({ resource: 'manualIssueReasons', pages: 0, items: 0 })
    getMock.mockImplementationOnce(() => Promise.reject(new ApiError(404, null)))
    await downloadManualIssueReasons()
    expect(readManualIssueReasons()?.map((r) => r.code)).toEqual(['OTHER'])
    getMock.mockImplementationOnce(() => Promise.reject(new ApiError(500, null)))
    await expect(downloadManualIssueReasons()).rejects.toMatchObject({ status: 500 })
  })

  it('sin warehouse.issue en los permisos guardados (o sin sesión) no pregunta: el servidor dejaría un PERMISSION_DENIED en cada pasada', async () => {
    await signIn(['warehouse.pick'])
    expect(await downloadManualIssueReasons(true)).toEqual({ resource: 'manualIssueReasons', pages: 0, items: 0 })
    __resetSessionForTests()
    expect(await downloadManualIssueReasons(true)).toEqual({ resource: 'manualIssueReasons', pages: 0, items: 0 })
    expect(getMock).not.toHaveBeenCalled()
  })

  it('una copia dañada se trata como «nunca bajados»', () => {
    setKv(KvKeys.manualIssueReasons, '{no es json')
    expect(readManualIssueReasons()).toBeNull()
    expect(getKv(KvKeys.manualIssueReasons)).toBe('{no es json')
  })

  it('mapReason descarta los deshabilitados, inactivos o sin código', () => {
    expect(mapReason({ code: ' SAMPLE ', label: 'Muestra', sortOrder: 2 })).toEqual({ code: 'SAMPLE', label: 'Muestra', labels: {}, sortOrder: 2, isDefault: false })
    expect(mapReason({ code: 'SALE', isDefault: true })?.isDefault).toBe(true)
    expect(mapReason({ code: 'X', isEnabled: false })).toBeNull()
    expect(mapReason({ code: 'X', isActive: false })).toBeNull()
    expect(mapReason({ code: '' })).toBeNull()
  })

  it('una copia anterior al motivo por default (sin isDefault) se vuelve a bajar sin esperar los 30 minutos', async () => {
    setKv(KvKeys.manualIssueReasons, JSON.stringify({ reasons: [{ code: 'OTHER', label: 'Otro', labels: {}, sortOrder: 5 }], fetchedAtUtc: new Date().toISOString() }))
    getMock.mockImplementation(() => ok(ROWS))
    expect(await downloadManualIssueReasons()).toEqual({ resource: 'manualIssueReasons', pages: 1, items: 1 })
    expect(readManualIssueReasons()?.find((r) => r.isDefault)?.code).toBe('SAMPLE')
    // la nueva ya trae la marca: vuelve a respetar los 30 minutos
    expect(await downloadManualIssueReasons()).toEqual({ resource: 'manualIssueReasons', pages: 0, items: 0 })
    expect(getMock).toHaveBeenCalledTimes(1)
  })

  it('recuerda el último motivo por operario; sin sesión no lee ni guarda; un valor dañado se ignora', async () => {
    expect(readLastReason()).toBeNull()
    saveLastReason('SALE')
    expect(readLastReason()).toBe('SALE')
    await saveUserSession({ accessToken: 'a', accessExpiresAtUtc: '', refreshToken: 'r', refreshExpiresAtUtc: '', tenantId: 1, userId: 8, fullName: 'Luis' })
    expect(readLastReason()).toBeNull()
    saveLastReason('OTHER')
    expect(JSON.parse(getKv(KvKeys.manualIssueLastReason) ?? '{}')).toEqual({ '7': 'SALE', '8': 'OTHER' })
    __resetSessionForTests()
    expect(readLastReason()).toBeNull()
    saveLastReason('SAMPLE')
    expect(JSON.parse(getKv(KvKeys.manualIssueLastReason) ?? '{}')).toEqual({ '7': 'SALE', '8': 'OTHER' })
    setKv(KvKeys.manualIssueLastReason, '[roto')
    await signIn(['warehouse.issue'])
    expect(readLastReason()).toBeNull()
    saveLastReason('SALE')
    expect(readLastReason()).toBe('SALE')
  })
})
