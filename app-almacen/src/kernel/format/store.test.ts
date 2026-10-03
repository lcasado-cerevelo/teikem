// Persistencia local de los formatos de la compañía (contra SQL real: better-sqlite3 detrás del mock de expo-sqlite) y su
// llegada por sincronización: un cambio de región en la web llega a la app en la siguiente pasada, y queda guardado para
// trabajar sin señal.
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'

import { api } from '../api/client'
import { __resetSessionForTests, addDeviceIdentity, dbNameFor, saveUserSession, selectDevice } from '../auth/session'
import { __resetDbForTests } from '../db/database'
import { getKv, KvKeys } from '../db/kv'
import { translate } from '../i18n/i18n'
import { __resetSyncEngineForTests, runSync } from '../sync/engine'
import { formatDate, formatQuantity, formatTime } from './format'
import { PR_FORMAT } from './settings'
import { __resetFormatForTests, getFormatSettings, setFormatSettings, subscribeFormat } from './store'
import { refreshTenantFormat } from './tenantFormatApi'

jest.mock('../api/client', () => {
  const actual = jest.requireActual('../api/client')
  return { ...actual, api: { GET: jest.fn(), POST: jest.fn() } }
})

const getMock = api.GET as jest.Mock
const postMock = api.POST as jest.Mock
const ok = (data: unknown) => Promise.resolve({ data, response: new Response(null, { status: 200 }) })

const DMY_24 = {
  regionCode: 'PR',
  timeZoneId: 'America/Puerto_Rico',
  currencyCode: 'USD',
  currencySymbol: '$',
  currencySymbolPosition: 'B',
  currencyDecimals: 2,
  dateOrder: 'DMY',
  dateSeparator: '-',
  timeFormat: 24,
  weekStartDay: 1,
  thousandsSeparator: '.',
  decimalSeparator: ',',
  phoneCountryCode: '+1',
  phoneMask: '(###) ###-####',
  // campos de la compañía que NO se guardan en el aparato
  legalName: 'Compañía S.A.',
  taxId: '66-1234567',
}

async function enrollAndLogin(id: string): Promise<void> {
  await addDeviceIdentity({ devicePublicId: id, deviceSecret: 's', tenantName: id, defaultWarehousePublicId: null, theme: null, dbName: dbNameFor(id) })
  await saveUserSession({ accessToken: 'a', accessExpiresAtUtc: '', refreshToken: 'r', refreshExpiresAtUtc: '', tenantId: 1, userId: 7, fullName: 'Ana' })
}

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSecureStoreForTests()
  __resetSessionForTests()
  __resetSyncEngineForTests()
  __resetFormatForTests()
  getMock.mockReset()
  postMock.mockReset()
})

describe('formatos de la compañía en el aparato', () => {
  it('sin compañía ni ajustes guardados: Puerto Rico', async () => {
    expect(getFormatSettings()).toBe(PR_FORMAT)
    await enrollAndLogin('dev-1')
    expect(getFormatSettings()).toEqual(PR_FORMAT)
  })

  it('refreshTenantFormat los trae del servidor, los guarda en la base de la compañía (sin datos fiscales) y avisa', async () => {
    await enrollAndLogin('dev-1')
    getMock.mockImplementation((path: string) => (path === '/api/v1/tenant/settings' ? ok(DMY_24) : ok({})))
    const listener = jest.fn()
    const off = subscribeFormat(listener)

    expect(await refreshTenantFormat()).toBe(true)
    off()

    expect(listener).toHaveBeenCalled()
    expect(getFormatSettings().dateOrder).toBe('DMY')
    const stored = JSON.parse(getKv(KvKeys.tenantFormat) ?? '{}') as Record<string, unknown>
    expect(stored).toMatchObject({ dateOrder: 'DMY', dateSeparator: '-', timeFormat: 24, thousandsSeparator: '.' })
    expect(stored).not.toHaveProperty('legalName')
    expect(stored).not.toHaveProperty('taxId')

    // las funciones sin ajustes explícitos usan los vigentes (y los textos traducidos también)
    expect(formatDate('2026-10-03T01:30:00Z')).toBe('02-10-2026')
    expect(formatTime('2026-10-03T01:30:00Z', 'es')).toBe('21:30')
    expect(formatQuantity(1250.5)).toBe('1.250,5')
    expect(translate('es', 'sync.pendingWithCount', { count: 1250 })).toBe('Pendientes (1.250)')
  })

  it('sin señal (o sin sesión) no cambia nada y no lanza', async () => {
    expect(await refreshTenantFormat()).toBe(false) // sin aparato ni sesión
    await enrollAndLogin('dev-1')
    getMock.mockImplementation(() => Promise.resolve({ response: Response.error() }))
    expect(await refreshTenantFormat()).toBe(false)
    expect(getFormatSettings()).toEqual(PR_FORMAT)
  })

  it('quedan guardados: al volver a abrir la app (memoria limpia) se leen de la base local, sin señal', async () => {
    await enrollAndLogin('dev-1')
    setFormatSettings({ ...PR_FORMAT, timeFormat: 24, dateOrder: 'YMD' })
    __resetFormatForTests() // como si se cerrara y abriera la app
    expect(getFormatSettings()).toMatchObject({ timeFormat: 24, dateOrder: 'YMD' })
  })

  it('cada compañía del teléfono tiene los suyos', async () => {
    await enrollAndLogin('dev-1')
    setFormatSettings({ ...PR_FORMAT, dateOrder: 'DMY' })
    await enrollAndLogin('dev-2')
    expect(getFormatSettings().dateOrder).toBe('MDY')
    await selectDevice('dev-1')
    expect(getFormatSettings().dateOrder).toBe('DMY')
  })

  it('un cambio de región en la web llega en la siguiente sincronización', async () => {
    await enrollAndLogin('dev-1')
    let serverSettings: Record<string, unknown> = { ...PR_FORMAT }
    getMock.mockImplementation((path: string) =>
      path === '/api/v1/tenant/settings' ? ok(serverSettings) : ok({ items: [], nextCursor: null, serverTimeUtc: new Date().toISOString() }),
    )
    postMock.mockImplementation(() => ok({ isActive: true }))

    await runSync()
    expect(getFormatSettings().regionCode).toBe('PR')

    serverSettings = { ...PR_FORMAT, regionCode: 'US', timeZoneId: 'America/New_York' }
    await runSync()
    expect(getFormatSettings()).toMatchObject({ regionCode: 'US', timeZoneId: 'America/New_York' })
    expect(getMock).toHaveBeenCalledWith('/api/v1/tenant/settings')
  })
})
