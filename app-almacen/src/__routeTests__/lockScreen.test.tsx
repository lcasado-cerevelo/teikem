// 2026-10-07 — bloqueo: al abrir la app con una sesión guardada se pide el PIN; el candado de Inicio bloquea sin perder nada;
// quitar una compañía borra su registro y su base. Archivo propio por el estado global de renderRouter().
import { __resetAllForTests, openDatabaseSync } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { pendingForCompany, removeCompany } from '../kernel/auth/companies'
import { addDeviceIdentity, dbNameFor, getSessionState, hydrateSession, lockSession, saveUserSession, __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'

const SESSION = { accessToken: 'a', accessExpiresAtUtc: '', refreshToken: 'r', refreshExpiresAtUtc: '', tenantId: 1, userId: 7, fullName: 'Ana Ruiz' }
const device = (id: string, name: string) => ({ devicePublicId: id, deviceSecret: `s-${id}`, tenantName: name, defaultWarehousePublicId: null, theme: null, dbName: dbNameFor(id) })

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSecureStoreForTests()
  __resetSessionForTests()
})

afterEach(() => {
  cleanup()
  jest.restoreAllMocks()
})

describe('bloqueo de la sesión', () => {
  it('al abrir la app con la sesión guardada pide el PIN (pantalla de bloqueo)', async () => {
    await addDeviceIdentity(device('dev-1', 'Teikem Demo'))
    await saveUserSession(SESSION)
    __resetSessionForTests() // como una app recién abierta: se vuelve a leer lo guardado

    renderRouter('src/app', { initialUrl: '/' })
    await waitFor(() => expect(screen.getByText('Sesión bloqueada')).toBeTruthy())
    expect(screen.getByText('Ana Ruiz')).toBeTruthy()
    expect(getSessionState().locked).toBe(true)
  })

  it('lockSession conserva la sesión y marca el bloqueo', async () => {
    await addDeviceIdentity(device('dev-1', 'Teikem Demo'))
    await saveUserSession(SESSION)
    lockSession()
    expect(getSessionState().locked).toBe(true)
    expect(getSessionState().session?.fullName).toBe('Ana Ruiz')
    await hydrateSession()
    expect(getSessionState().locked).toBe(true)
  })
})

describe('quitar una compañía', () => {
  it('cuenta lo pendiente y al quitarla borra su registro y su base; las demás siguen', async () => {
    const a = device('dev-1', 'Depot')
    const b = device('dev-2', 'Solutions')
    await addDeviceIdentity(a)
    await addDeviceIdentity(b)
    const db = openDatabaseSync(a.dbName)
    db.execSync("CREATE TABLE outbox (id INTEGER PRIMARY KEY, status TEXT)")
    db.execSync("INSERT INTO outbox (status) VALUES ('pending'), ('pending'), ('sent')")

    expect(pendingForCompany(a)).toBe(2)
    expect(pendingForCompany(b)).toBe(0)

    await removeCompany(a)
    expect(getSessionState().devices.map((d) => d.devicePublicId)).toEqual(['dev-2'])
    // la base de Depot ya no existe: abrirla de nuevo la trae vacía
    expect(() => openDatabaseSync(a.dbName).getFirstSync('SELECT * FROM outbox')).toThrow()
  })
})
