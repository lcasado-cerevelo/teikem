// Lote 24 — "Por producto" abre UN conteo con varios productos, de punta a punta en la pantalla real: el primer escaneo abre el
// conteo vacío en línea (sin producto ni posición), cada producto propone su única posición con existencia (GET
// .../product-bins, sin cantidades) o deja elegir si hay varias, la posición se puede cambiar, escanear otra vez el mismo producto
// en la misma posición abre su línea para corregir, y Terminar manda UN lote con todas las líneas y el cierre. En archivo propio:
// renderRouter() no aísla del todo su estado global de navegación entre dos llamadas del mismo archivo (ver homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { addOpenCountLine, getOpenCount, getProductCountRows, startLocalOpenCount } from '../features/count/localCount'
import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests, getDb } from '../kernel/db/database'
import { setKv, KvKeys } from '../kernel/db/kv'
import { listOutbox } from '../kernel/sync/outbox'
import { insertProduct, json, mockFetch, setupDevice } from './countKit'

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

const bins = (...rows: Array<{ binId: number; binCode: string; lotId?: number; lotNumber?: string }>) => ({
  productPublicId: 'x',
  sku: 'x',
  productName: 'x',
  trackingTypeCode: 'NONE',
  isActive: true,
  bins: rows.map((r) => ({ zoneCode: 'PCK', lotId: null, lotNumber: null, lineId: null, ...r })),
})

async function scan(label: string, code: string) {
  await fireEvent.changeText(screen.getByLabelText(label), code)
  await fireEvent.press(screen.getAllByLabelText('Aceptar')[0])
}

describe('Conteo abierto — varios productos', () => {
  it('abre el conteo vacío al primer escaneo; propone la única posición, deja elegir con varias, corrige un repetido y termina con un solo lote', async () => {
    await setupDevice()
    insertProduct(1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE')
    insertProduct(2, 'p2', 'SKU-2', 'Tuerca', '7502', 'NONE')
    setKv(KvKeys.countEntryMode, 'PRODUCT')
    const calls = mockFetch([
      (c) => (c.method === 'POST' && c.path === '/api/v1/cycle-counts' ? json(200, { count: { id: 700, originCode: 'PRODUCT' }, isBlind: true, lines: [] }) : null),
      (c) => (c.method === 'GET' && c.path === '/api/v1/cycle-counts/700/product-bins' && c.search.includes('p1') ? json(200, bins({ binId: 10, binCode: 'A-01' })) : null),
      (c) =>
        c.method === 'GET' && c.path === '/api/v1/cycle-counts/700/product-bins' && c.search.includes('p2')
          ? json(200, bins({ binId: 11, binCode: 'B-02' }, { binId: 12, binCode: 'C-03' }))
          : null,
    ])

    await renderRouter('src/app', { initialUrl: '/count' })
    await waitFor(() => expect(screen.getByLabelText('Escanea el producto a contar')).toBeTruthy())
    await scan('Escanea el producto a contar', '7501')

    // el conteo se abrió vacío (sin producto ni posición) y el producto ya trae su única posición
    await waitFor(() => expect(screen.getByTestId('open-count-bin')).toBeTruthy())
    expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/cycle-counts')?.body).toEqual({ warehousePublicId: 'wh-1', allowEmpty: true, assignToMe: true })
    expect(getOpenCount()).toMatchObject({ countId: 700, mode: 'OPEN', binId: null, product: null })
    expect(screen.getByText('A-01')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Cambiar posición' })).toBeTruthy()
    await fireEvent.changeText(screen.getByLabelText('Cantidad encontrada'), '5')
    await fireEvent.press(screen.getByRole('button', { name: 'Agregar' }))

    // vuelve a escanear: la línea quedó en la lista
    await waitFor(() => expect(screen.getByText('SKU-1 agregado en A-01.')).toBeTruthy())
    expect(screen.getByText('Tornillo · 5')).toBeTruthy()

    // segundo producto en dos posiciones: no se propone ninguna, se elige
    await scan('Escanea el producto contado', '7502')
    await waitFor(() => expect(screen.getByText('El producto está en varias posiciones: elige en cuál lo contaste.')).toBeTruthy())
    expect(screen.queryByTestId('open-count-bin')).toBeNull()
    await fireEvent.press(screen.getByRole('button', { name: 'C-03 · PCK' }))
    expect(screen.getByText('C-03')).toBeTruthy()
    await fireEvent.changeText(screen.getByLabelText('Cantidad encontrada'), '2')
    await fireEvent.press(screen.getByRole('button', { name: 'Agregar' }))
    await waitFor(() => expect(screen.getByText('SKU-2 agregado en C-03.')).toBeTruthy())

    // el primer producto otra vez en la misma posición: abre su línea para corregir (no duplica ni suma)
    await scan('Escanea el producto contado', '7501')
    await waitFor(() => expect(screen.getByText('SKU-1 ya estaba contado en A-01: corrige la cantidad.')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Cantidad encontrada'), '6')
    await fireEvent.press(screen.getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(screen.getByText('Tornillo · 6')).toBeTruthy())
    expect(getProductCountRows(getOpenCount()!.id)).toHaveLength(2)

    // Terminar: UN lote con las dos líneas (posición + producto + cantidad) y el cierre
    await fireEvent.press(screen.getByRole('button', { name: 'Terminar conteo' }))
    await waitFor(() => expect(getOpenCount()).toBeNull())
    const outbox = listOutbox()
    expect(outbox.map((r) => [r.kind, r.path])).toEqual([
      ['countBatch', '/api/v1/cycle-counts/700/lines/batch'],
      ['countFinish', '/api/v1/cycle-counts/700/finish'],
    ])
    expect(JSON.parse(outbox[0].body)).toEqual({
      lines: [
        { binId: 10, productPublicId: 'p1', countedQty: 6 },
        { binId: 12, productPublicId: 'p2', countedQty: 2 },
      ],
    })
  })

  it('sin existencia en ninguna posición pide escanear la posición; una posición del aparato se acepta sin señal', async () => {
    await setupDevice()
    insertProduct(1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE')
    getDb().runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_id, zone_code, zone_name, is_active) VALUES (55, 'Z-05', 'wh-1', 3, 'RES', 'Reserva', 1)")
    setKv(KvKeys.countEntryMode, 'PRODUCT')
    mockFetch([
      (c) => (c.method === 'POST' && c.path === '/api/v1/cycle-counts' ? json(200, { count: { id: 701, originCode: 'PRODUCT' }, isBlind: true, lines: [] }) : null),
      (c) => (c.method === 'GET' && c.path === '/api/v1/cycle-counts/701/product-bins' ? json(200, bins()) : null),
    ])

    await renderRouter('src/app', { initialUrl: '/count' })
    await waitFor(() => expect(screen.getByLabelText('Escanea el producto a contar')).toBeTruthy())
    await scan('Escanea el producto a contar', '7501')
    await waitFor(() => expect(screen.getByText('El sistema no tiene este producto en ninguna posición. Escanea la posición donde lo encontraste.')).toBeTruthy())
    expect(screen.getByRole('button', { name: 'Agregar' }).props.accessibilityState).toMatchObject({ disabled: true })

    await scan('Escanea la posición', 'z-05')
    await waitFor(() => expect(screen.getByTestId('open-count-bin')).toBeTruthy())
    expect(screen.getByText('Z-05')).toBeTruthy()
    await fireEvent.changeText(screen.getByLabelText('Cantidad encontrada'), '1')
    await fireEvent.press(screen.getByRole('button', { name: 'Agregar' }))
    await waitFor(() => expect(screen.getByText('SKU-1 agregado en Z-05.')).toBeTruthy())
    expect(getProductCountRows(getOpenCount()!.id).map((r) => [r.binId, r.countedQty])).toEqual([[55, 1]])
  })

  it('el conteo abierto se retoma sin señal con lo ya contado y se puede quitar una línea', async () => {
    await setupDevice()
    const id = startLocalOpenCount('wh-1', { countId: 702, isBlind: true })
    addOpenCountLine(id, { publicId: 'p1', sku: 'SKU-1', name: 'Tornillo' }, { id: 10, code: 'A-01', isProvisional: false }, null, 3)
    mockFetch([])

    await renderRouter('src/app', { initialUrl: '/count' })
    await waitFor(() => expect(screen.getByText('Tornillo · 3')).toBeTruthy())
    expect(screen.getByText('SKU-1 · A-01')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Terminar conteo' })).toBeTruthy()
    await fireEvent.press(screen.getByRole('button', { name: 'Quitar Tornillo · 3' }))
    await waitFor(() => expect(screen.getByText('Todavía no has contado nada. Escanea un producto.')).toBeTruthy())
    expect(screen.getByRole('button', { name: 'Terminar conteo' }).props.accessibilityState).toMatchObject({ disabled: true })
  })

  it('la calculadora de la cantidad: 5 filas × 3 columnas + 10 sueltas agrega la línea con 25', async () => {
    await setupDevice()
    insertProduct(1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE')
    setKv(KvKeys.countEntryMode, 'PRODUCT')
    mockFetch([
      (c) => (c.method === 'POST' && c.path === '/api/v1/cycle-counts' ? json(200, { count: { id: 703, originCode: 'PRODUCT' }, isBlind: true, lines: [] }) : null),
      (c) => (c.method === 'GET' && c.path === '/api/v1/cycle-counts/703/product-bins' ? json(200, bins({ binId: 10, binCode: 'A-01' })) : null),
    ])
    await renderRouter('src/app', { initialUrl: '/count' })
    await waitFor(() => expect(screen.getByLabelText('Escanea el producto a contar')).toBeTruthy())
    await scan('Escanea el producto a contar', '7501')
    await waitFor(() => expect(screen.getByTestId('open-count-bin')).toBeTruthy())

    await fireEvent.press(screen.getByLabelText('Calculadora'))
    await fireEvent.changeText(screen.getByLabelText('Filas del bloque 1'), '5')
    await fireEvent.changeText(screen.getByLabelText('Columnas del bloque 1'), '3')
    await fireEvent.changeText(screen.getByLabelText('Sueltas'), '10')
    expect(screen.getByText('(5 × 3) + 10')).toBeTruthy()
    // la línea entra con el total (25) aunque se agregue sin volver a la cantidad directa
    await fireEvent.press(screen.getByRole('button', { name: 'Agregar' }))
    await waitFor(() => expect(screen.getByText('Tornillo · 25')).toBeTruthy())
    expect(getProductCountRows(getOpenCount()!.id).map((r) => [r.binId, r.countedQty])).toEqual([[10, 25]])
  })
})

