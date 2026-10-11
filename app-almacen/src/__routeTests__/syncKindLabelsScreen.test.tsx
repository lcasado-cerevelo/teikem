// 2026-10-11 — Sincronización muestra el nombre traducido de cada tipo de operación de la cola (antes, el `kind` técnico:
// «manualIssue», «transfer»…), en Pendientes, en Con error y en «Reintentar: …»; un tipo desconocido se muestra tal cual. En
// archivo propio: renderRouter() no aísla del todo su estado global de navegación entre dos llamadas del mismo archivo (ver
// homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { act, cleanup, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { saveDeviceIdentity, saveUserSession, __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests, getDb } from '../kernel/db/database'
import { __resetLangForTests, setLang } from '../kernel/i18n/i18n'
import { enqueue } from '../kernel/sync/outbox'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSecureStoreForTests()
  __resetSessionForTests()
  __resetLangForTests()
})

afterEach(cleanup)

function insertRejected(kind: string, error: string) {
  getDb().runSync(
    `INSERT INTO outbox (idempotency_key, kind, method, path, body, created_at_utc, status, attempts, last_error)
     VALUES (?, ?, 'POST', '/api/v1/x', '{}', ?, 'rejected', 1, ?)`,
    [`k-${kind}`, kind, new Date().toISOString(), error],
  )
}

describe('navegación — Sincronización: nombre de cada tipo de operación', () => {
  it('pendientes y con error con su nombre (es y en); el desconocido, tal cual', async () => {
    await saveDeviceIdentity({ devicePublicId: 'dev-1', deviceSecret: 'secret-1', tenantName: 'Teikem Demo', defaultWarehousePublicId: 'wh-1', theme: null })
    await saveUserSession({ accessToken: 'a', accessExpiresAtUtc: '', refreshToken: 'r', refreshExpiresAtUtc: '', tenantId: 1, userId: 7, fullName: 'Ana Ruiz' })
    enqueue({ kind: 'manualIssue', body: {}, projection: [] })
    enqueue({ kind: 'transfer', body: {} })
    enqueue({ kind: 'damage', body: {} })
    insertRejected('adjust', 'Inventario insuficiente.')
    insertRejected('legacyKind', 'Algo viejo.')

    renderRouter('src/app', { initialUrl: '/sync' })

    await waitFor(() => expect(screen.getByText('Sincronización')).toBeTruthy())
    expect(screen.getByText('Pendientes (3)')).toBeTruthy()
    expect(screen.getByText('Despacho manual')).toBeTruthy()
    expect(screen.getByText('Transferencia')).toBeTruthy()
    expect(screen.getByText('Reporte de daño')).toBeTruthy()
    expect(screen.getByText('Ajuste de inventario')).toBeTruthy()
    expect(screen.getByText('Reintentar: Ajuste de inventario')).toBeTruthy()
    expect(screen.getByText('legacyKind')).toBeTruthy()
    expect(screen.getByText('Reintentar: legacyKind')).toBeTruthy()
    // ningún kind técnico conocido a la vista
    for (const raw of ['manualIssue', 'transfer', 'damage', 'adjust']) expect(screen.queryByText(raw)).toBeNull()

    // idioma sin reiniciar
    await act(async () => setLang('en'))
    await waitFor(() => expect(screen.getByText('Manual issue')).toBeTruthy())
    expect(screen.getByText('Retry: Inventory adjustment')).toBeTruthy()
  })
})
