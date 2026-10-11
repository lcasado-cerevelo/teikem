// Despacho manual (decisión del dueño 2026-10-11): «Completar despacho» saca el inventario SIN entrega con un motivo obligatorio (catálogo
// ManualIssueReason) y una nota opcional, y genera el documento DMA-##### (POST /api/v1/manual-issues, permiso warehouse.issue). Va a la cola de
// salida (kind manualIssue, con Idempotency-Key) y resta del saldo local; el aviso muestra el número DMA si se envió en el momento o «en cola» si no.
// 2026-10-11 (b): abre directo la confirmación «Se despacha por: {motivo}» con el último motivo usado o el default de la compañía (2 toques, sin
// teclado); la lista solo si no hay ninguno o se toca «Cambiar»; la nota, detrás de «Agregar nota».
// Sin warehouse.issue solo queda «Empacar» con un aviso. En archivo propio: renderRouter() no aísla del todo su estado global de navegación (ver
// homeLock.test.tsx).
import { Alert } from 'react-native'
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { act, cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { addLocalPickLine, getOpenPick, startLocalPick } from '../features/dispatch/localPick'
import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests, getDb } from '../kernel/db/database'
import { getKv, KvKeys, setKv } from '../kernel/db/kv'
import { __resetSyncEngineForTests } from '../kernel/sync/engine'
import { listOutbox } from '../kernel/sync/outbox'
import { json, mockFetch, setupDevice, type Route } from './countKit'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSecureStoreForTests()
  __resetSessionForTests()
  __resetSyncEngineForTests()
})

afterEach(() => {
  cleanup()
  jest.restoreAllMocks()
})

const ISSUER = ['inventory.view', 'warehouse.pick', 'warehouse.issue']

function seedPick() {
  const db = getDb()
  db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_id, is_active) VALUES (55, 'A-01', 'wh-1', 1, 1)")
  db.runSync(
    `INSERT INTO stock_balance (id, warehouse_public_id, bin_id, product_id, product_public_id, qty_on_hand, qty_reserved, updated_at_utc)
     VALUES (1, 'wh-1', 55, 1, 'p1', 10, 0, '2026-10-11T10:00:00.000Z')`,
  )
  const id = startLocalPick('wh-1', null)
  addLocalPickLine(id, { productPublicId: 'p1', sku: 'SKU-1', productName: 'Tornillo', quantity: 3, fromBinCode: 'A-01' })
}

function withPermissions(perms: string[]): Route {
  setKv(KvKeys.myPermissions, JSON.stringify({ '7': { permissions: perms, fetchedAtUtc: '2026-10-11T00:00:00Z' } }))
  return (c) => (c.path === '/api/v1/me' ? json(200, { permissions: perms }) : null)
}

const onHand = () => getDb().getFirstSync<{ q: number }>('SELECT qty_on_hand AS q FROM stock_balance WHERE id = 1')?.q

/** 2026-10-11 (b): ya no hay un Alert de confirmación (la pantalla ES la confirmación); se espía para comprobar que no aparece. */
function spyAlert() {
  return jest.spyOn(Alert, 'alert').mockImplementation(() => {})
}

/** Copia de motivos bajada de la compañía (con isDefault, 2026-10-11 b). */
function companyReasons(defaultCode: string | null, disabled: string[] = []) {
  const all = [
    { code: 'SAMPLE', label: 'Muestra gratis', labels: { es: 'Muestra gratis', en: 'Free sample' }, sortOrder: 1 },
    { code: 'INTERNAL_USE', label: 'Uso interno', labels: { es: 'Uso interno', en: 'Internal use' }, sortOrder: 2 },
    { code: 'SALE', label: 'Venta', labels: { es: 'Venta', en: 'Sale' }, sortOrder: 4 },
    { code: 'OTHER', label: 'Otro', labels: { es: 'Otro', en: 'Other' }, sortOrder: 5 },
  ]
  setKv(
    KvKeys.manualIssueReasons,
    JSON.stringify({
      fetchedAtUtc: new Date().toISOString(),
      withDefault: true,
      reasons: all.filter((r) => !disabled.includes(r.code)).map((r) => ({ ...r, isDefault: r.code === defaultCode })),
    }),
  )
}

const lastReasons = () => JSON.parse(getKv(KvKeys.manualIssueLastReason) ?? '{}') as Record<string, string>

/** Ningún campo de texto en pantalla: completar no abre el teclado. */
function expectNoKeyboard() {
  expect(screen.queryByTestId('dispatch-issue-note')).toBeNull()
  expect(screen.queryByLabelText('Nota (opcional)')).toBeNull()
}

async function pressComplete() {
  await renderRouter('src/app', { initialUrl: '/dispatch' })
  await waitFor(() => expect(screen.getByRole('button', { name: 'Completar despacho' })).toBeTruthy())
  expect(screen.getByRole('button', { name: 'Empacar' })).toBeTruthy()
  await fireEvent.press(screen.getByRole('button', { name: 'Completar despacho' }))
}

/** Sin motivo puesto: se abre la lista para escogerlo. */
async function openReasonStep() {
  await pressComplete()
  await waitFor(() => expect(screen.getByText('¿Por qué sale sin entrega?')).toBeTruthy())
}

/** Con motivo puesto: se abre directo la confirmación. */
async function openConfirm(reasonLabel: string) {
  await pressComplete()
  await waitFor(() => expect(screen.getByTestId('dispatch-issue-reason').props.children).toBe(`Se despacha por: ${reasonLabel}`))
  expect(screen.getByText('¿Despachar sin entrega?')).toBeTruthy()
  expect(screen.queryByText('¿Por qué sale sin entrega?')).toBeNull()
}

/** El onPress del Pressable (el host no lo trae: está en el componente de arriba), para tocar dos veces sin esperar a que la pantalla cambie. */
function pressHandler(host: { unstable_fiber?: unknown }): () => void {
  type Fiber = { memoizedProps?: { onPress?: () => void } | null; return: Fiber | null }
  let fiber = host.unstable_fiber as Fiber | null | undefined
  while (fiber && !fiber.memoizedProps?.onPress) fiber = fiber.return
  const handler = fiber?.memoizedProps?.onPress
  if (!handler) throw new Error('sin onPress')
  return handler
}

const postOf = (calls: { method: string; path: string; body: unknown }[]) => calls.find((c) => c.method === 'POST' && c.path === '/api/v1/manual-issues')

describe('Despacho — Completar despacho = despacho manual', () => {
  it('sin default ni último usado pide escoger; tras escoger, confirmación con «Agregar nota»; manda POST /manual-issues y recuerda el motivo', async () => {
    await setupDevice()
    seedPick()
    const alert = spyAlert()
    const calls = mockFetch([
      withPermissions(ISSUER),
      (c) => (c.method === 'POST' && c.path === '/api/v1/manual-issues' ? json(200, { id: 9, number: 'DMA-00012', isManual: true }) : null),
    ])
    await openReasonStep()
    expect(screen.getByText('Despacho manual: el inventario sale sin orden ni empaque. Escoge el motivo.')).toBeTruthy()
    expect(screen.getByText('Inventario propio · líneas: 1 · unidades: 3')).toBeTruthy()
    // los cinco de fábrica (el aparato todavía no bajó los de la compañía), ninguno escogido y sin teclado
    for (const label of ['Muestra', 'Uso interno', 'Retiro del cliente', 'Venta', 'Otro']) {
      expect(screen.getByRole('radio', { name: label }).props.accessibilityState).toEqual({ selected: false })
    }
    expectNoKeyboard()
    expect(screen.queryByTestId('dispatch-issue-confirm')).toBeNull()

    await fireEvent.press(screen.getByRole('radio', { name: 'Muestra' }))
    await waitFor(() => expect(screen.getByTestId('dispatch-issue-reason').props.children).toBe('Se despacha por: Muestra'))
    // escogido a mano: sin la ayuda de «último» ni «default»
    expect(screen.queryByText(/último motivo|por default/)).toBeNull()
    expectNoKeyboard()
    // la nota está escondida: «Agregar nota» la abre
    await fireEvent.press(screen.getByRole('button', { name: 'Agregar nota' }))
    await fireEvent.changeText(screen.getByLabelText('Nota (opcional)'), 'Feria de salud')
    expect(screen.getByText('14/500 caracteres.')).toBeTruthy()
    await fireEvent.press(screen.getByRole('button', { name: 'Despachar' }))

    await waitFor(() => expect(screen.getByText('Listo: despacho manual DMA-00012.')).toBeTruthy())
    expect(alert).not.toHaveBeenCalled()
    const post = postOf(calls) as { body: unknown; idempotencyKey: string | null } | undefined
    // la posición se resolvió con la del aparato (no hizo falta pedirla al servidor)
    expect(post?.body).toEqual({ warehousePublicId: 'wh-1', lines: [{ productPublicId: 'p1', quantity: 3, binId: 55 }], reasonCode: 'SAMPLE', note: 'Feria de salud' })
    expect(post?.idempotencyKey).toMatch(/^app-/)
    expect(calls.some((c) => c.path.includes('/bins') || c.path.startsWith('/api/v1/pick-batches'))).toBe(false)
    expect(getOpenPick()).toBeNull()
    expect(listOutbox().map((r) => [r.kind, r.status])).toEqual([['manualIssue', 'sent']])
    expect(onHand()).toBe(7)
    expect(lastReasons()).toEqual({ '7': 'SAMPLE' })
  })

  it('con motivo por default de la compañía: Completar → Despachar (2 toques, sin teclado), nota null', async () => {
    await setupDevice()
    seedPick()
    companyReasons('SALE')
    const alert = spyAlert()
    const calls = mockFetch([
      withPermissions(ISSUER),
      (c) => (c.method === 'POST' && c.path === '/api/v1/manual-issues' ? json(200, { id: 9, number: 'DMA-00013', isManual: true }) : null),
    ])
    await openConfirm('Venta')
    expect(screen.getByText('Es el motivo por default de la compañía.')).toBeTruthy()
    expect(screen.queryByRole('radio', { name: 'Venta' })).toBeNull()
    expectNoKeyboard()
    await fireEvent.press(screen.getByRole('button', { name: 'Despachar' }))
    await waitFor(() => expect(screen.getByText('Listo: despacho manual DMA-00013.')).toBeTruthy())
    expect(alert).not.toHaveBeenCalled()
    expect(postOf(calls)?.body).toEqual({ warehousePublicId: 'wh-1', lines: [{ productPublicId: 'p1', quantity: 3, binId: 55 }], reasonCode: 'SALE', note: null })
    expect(lastReasons()).toEqual({ '7': 'SALE' })
  })

  it('un doble toque en «Despachar» encola un solo despacho (sin el Alert, el primer toque ya despacha)', async () => {
    await setupDevice()
    seedPick()
    companyReasons('SALE')
    mockFetch([withPermissions(ISSUER)])
    await openConfirm('Venta')
    // los dos toques en el mismo turno, antes de que la pantalla cambie a «Enviando…» (como un doble toque real)
    const onPress = pressHandler(screen.getByRole('button', { name: 'Despachar' }))
    await act(async () => {
      onPress()
      onPress()
    })
    await waitFor(() => expect(screen.getByText(/Despacho manual en cola/)).toBeTruthy(), { timeout: 10000 })
    expect(listOutbox().map((r) => r.kind)).toEqual(['manualIssue'])
    expect(onHand()).toBe(7)
  })

  it('el último motivo que el operario usó en el aparato gana al default', async () => {
    await setupDevice()
    seedPick()
    companyReasons('SALE')
    setKv(KvKeys.manualIssueLastReason, JSON.stringify({ '7': 'SAMPLE', '8': 'OTHER' }))
    const calls = mockFetch([withPermissions(ISSUER), (c) => (c.method === 'POST' && c.path === '/api/v1/manual-issues' ? json(200, { id: 9, number: 'DMA-00014' }) : null)])
    await openConfirm('Muestra gratis')
    expect(screen.getByText('Es el último motivo que usaste en este aparato.')).toBeTruthy()
    expectNoKeyboard()
    await fireEvent.press(screen.getByRole('button', { name: 'Despachar' }))
    await waitFor(() => expect(screen.getByText('Listo: despacho manual DMA-00014.')).toBeTruthy())
    expect(postOf(calls)?.body).toMatchObject({ reasonCode: 'SAMPLE' })
  })

  it('un último motivo que la compañía deshabilitó se ignora: queda el default', async () => {
    await setupDevice()
    seedPick()
    companyReasons('SALE', ['SAMPLE'])
    setKv(KvKeys.manualIssueLastReason, JSON.stringify({ '7': 'SAMPLE' }))
    mockFetch([withPermissions(ISSUER)])
    await openConfirm('Venta')
    expect(screen.getByText('Es el motivo por default de la compañía.')).toBeTruthy()
  })

  it('último motivo deshabilitado y sin default: hay que escoger, como antes', async () => {
    await setupDevice()
    seedPick()
    companyReasons(null, ['SAMPLE'])
    setKv(KvKeys.manualIssueLastReason, JSON.stringify({ '7': 'SAMPLE' }))
    mockFetch([withPermissions(ISSUER)])
    await openReasonStep()
    expect(screen.queryByRole('radio', { name: 'Muestra gratis' })).toBeNull()
    expect(screen.getByRole('radio', { name: 'Venta' }).props.accessibilityState).toEqual({ selected: false })
    // «Volver» sin motivo regresa al despacho
    await fireEvent.press(screen.getByRole('button', { name: 'Volver' }))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Completar despacho' })).toBeTruthy())
  })

  it('«Cambiar» abre la lista con el puesto marcado; «Volver» regresa sin cambiarlo; escoger otro vuelve a la confirmación con ese', async () => {
    await setupDevice()
    seedPick()
    companyReasons('SALE')
    const calls = mockFetch([withPermissions(ISSUER), (c) => (c.method === 'POST' && c.path === '/api/v1/manual-issues' ? json(200, { id: 9, number: 'DMA-00015' }) : null)])
    await openConfirm('Venta')
    await fireEvent.press(screen.getByRole('button', { name: 'Cambiar el motivo' }))
    await waitFor(() => expect(screen.getByText('¿Por qué sale sin entrega?')).toBeTruthy())
    expect(screen.getByRole('radio', { name: 'Venta' }).props.accessibilityState).toEqual({ selected: true })
    await fireEvent.press(screen.getByRole('button', { name: 'Volver' }))
    await waitFor(() => expect(screen.getByTestId('dispatch-issue-reason').props.children).toBe('Se despacha por: Venta'))

    await fireEvent.press(screen.getByRole('button', { name: 'Cambiar el motivo' }))
    await fireEvent.press(screen.getByRole('radio', { name: 'Otro' }))
    await waitFor(() => expect(screen.getByTestId('dispatch-issue-reason').props.children).toBe('Se despacha por: Otro'))
    expect(screen.queryByText(/por default/)).toBeNull()
    await fireEvent.press(screen.getByRole('button', { name: 'Despachar' }))
    await waitFor(() => expect(screen.getByText('Listo: despacho manual DMA-00015.')).toBeTruthy())
    expect(postOf(calls)?.body).toMatchObject({ reasonCode: 'OTHER' })
    expect(lastReasons()).toEqual({ '7': 'OTHER' })
  })

  it('sin señal queda en la cola (manualIssue): aviso «en cola», el despacho local se cierra y el saldo local ya refleja la salida', async () => {
    await setupDevice()
    seedPick()
    mockFetch([withPermissions(ISSUER)])
    await openReasonStep()
    await fireEvent.press(screen.getByRole('radio', { name: 'Uso interno' }))
    await fireEvent.press(screen.getByTestId('dispatch-issue-confirm'))
    await waitFor(() => expect(screen.getByText(/Despacho manual en cola/)).toBeTruthy(), { timeout: 10000 })
    expect(getOpenPick()).toBeNull()
    const row = listOutbox()[0]
    expect([row.kind, row.path, row.status]).toEqual(['manualIssue', '/api/v1/manual-issues', 'pending'])
    expect(JSON.parse(row.body)).toEqual({ warehousePublicId: 'wh-1', lines: [{ productPublicId: 'p1', quantity: 3, binId: 55 }], reasonCode: 'INTERNAL_USE', note: null })
    expect(onHand()).toBe(7)
    // en cola también cuenta como usado
    expect(lastReasons()).toEqual({ '7': 'INTERNAL_USE' })
  })

  it('el rechazo del servidor muestra su mensaje exacto, deja el despacho abierto como estaba, no queda en la cola y deshace el saldo local', async () => {
    await setupDevice()
    seedPick()
    const message = 'Inventario insuficiente de SKU-1 en A-01: disponible 2, solicitado 3.'
    mockFetch([
      withPermissions(ISSUER),
      (c) => (c.method === 'POST' && c.path === '/api/v1/manual-issues' ? json(409, { title: message, status: 409, code: 'insufficient_stock' }) : null),
    ])
    await openReasonStep()
    await fireEvent.press(screen.getByRole('radio', { name: 'Venta' }))
    await fireEvent.press(screen.getByRole('button', { name: 'Agregar nota' }))
    await fireEvent.changeText(screen.getByLabelText('Nota (opcional)'), 'Mostrador')
    await fireEvent.press(screen.getByTestId('dispatch-issue-confirm'))
    await waitFor(() => expect(screen.getByText(message)).toBeTruthy())
    expect(getOpenPick()?.lineRows.map((l) => [l.sku, l.quantity, l.fromBinCode])).toEqual([['SKU-1', 3, 'A-01']])
    expect(listOutbox()).toEqual([])
    expect(onHand()).toBe(10)
    // un rechazo no cuenta como «último usado»
    expect(getKv(KvKeys.manualIssueLastReason)).toBeNull()
    // el motivo y la nota se conservan para corregir y volver a intentar: directo a la confirmación, con la nota a la vista
    await fireEvent.press(screen.getByRole('button', { name: 'Completar despacho' }))
    await waitFor(() => expect(screen.getByTestId('dispatch-issue-reason').props.children).toBe('Se despacha por: Venta'))
    expect(screen.getByLabelText('Nota (opcional)').props.value).toBe('Mostrador')
  })

  it('ofrece los motivos que bajó la compañía (con su nombre y sin los deshabilitados)', async () => {
    await setupDevice()
    seedPick()
    setKv(
      KvKeys.manualIssueReasons,
      JSON.stringify({
        fetchedAtUtc: '2026-10-11T00:00:00Z',
        reasons: [
          { code: 'OTHER', label: 'Otro', labels: { es: 'Otro', en: 'Other' }, sortOrder: 5 },
          { code: 'SAMPLE', label: 'Muestra gratis', labels: { es: 'Muestra gratis', en: 'Free sample' }, sortOrder: 1 },
        ],
      }),
    )
    mockFetch([withPermissions(ISSUER)])
    await openReasonStep()
    expect(screen.getByRole('radio', { name: 'Muestra gratis' })).toBeTruthy()
    expect(screen.getByRole('radio', { name: 'Otro' })).toBeTruthy()
    expect(screen.queryByRole('radio', { name: 'Venta' })).toBeNull()
  })

  it('sin warehouse.issue no ofrece «Completar despacho»: queda «Empacar» y un aviso claro', async () => {
    await setupDevice()
    seedPick()
    mockFetch([withPermissions(['inventory.view', 'warehouse.pick'])])
    await renderRouter('src/app', { initialUrl: '/dispatch' })
    await waitFor(() => expect(screen.getByRole('button', { name: 'Empacar' })).toBeTruthy())
    expect(screen.queryByRole('button', { name: 'Completar despacho' })).toBeNull()
    expect(screen.getByTestId('dispatch-no-issue').props.children).toMatch(/necesita el permiso warehouse\.issue/)
  })

  it('una posición que no existe no completa nada y lo dice', async () => {
    await setupDevice()
    const id = startLocalPick('wh-1', null)
    addLocalPickLine(id, { productPublicId: 'p1', sku: 'SKU-1', productName: 'Tornillo', quantity: 1, fromBinCode: 'NO-EXISTE' })
    mockFetch([
      withPermissions(ISSUER),
      (c) => (c.method === 'GET' && c.path === '/api/v1/warehouses/wh-1/bins' ? json(200, { total: 0, skip: 0, take: 200, items: [] }) : null),
    ])
    await renderRouter('src/app', { initialUrl: '/dispatch' })
    await waitFor(() => expect(screen.getByRole('button', { name: 'Completar despacho' })).toBeTruthy())
    await fireEvent.press(screen.getByRole('button', { name: 'Completar despacho' }))
    await waitFor(() => expect(screen.getByText(/No hay una posición con ese código\. NO-EXISTE/)).toBeTruthy())
    expect(screen.queryByText('¿Por qué sale sin entrega?')).toBeNull()
    expect(getOpenPick()).not.toBeNull()
    expect(listOutbox()).toEqual([])
  })
})
