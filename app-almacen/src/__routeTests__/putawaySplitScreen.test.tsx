// Acomodo repartido (tarea 24b): con "Cantidad por posición" cada escaneo suma una posición al reparto, la que ya no cabe o la repetida se
// rechaza, y "Confirmar reparto" manda todas juntas a POST /warehouse-tasks/{id}/distribute. Sin cantidad por posición queda el acomodo de siempre.
// En archivo propio: renderRouter() no aísla del todo su estado global de navegación (ver homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'
import { json, mockFetch, setupDevice, type FetchCall } from './countKit'

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

const BINS = ['A-01', 'A-02', 'A-03', 'A-04']

function routes(taskQty: number) {
  return [
    (c: FetchCall) =>
      c.method === 'GET' && c.path === '/api/v1/warehouse-tasks'
        ? json(200, { total: 1, skip: 0, take: 100, items: [{ id: 5, sku: 'SKU-1', productName: 'Tornillo', quantity: taskQty, toBinCode: null, assignedToUserId: null }] })
        : null,
    (c: FetchCall) => (c.method === 'POST' && c.path === '/api/v1/warehouse-tasks/5/start' ? json(200, { id: 5 }) : null),
    (c: FetchCall) => (c.method === 'GET' && c.path === '/api/v1/warehouse-tasks/putaway-suggestions' ? json(200, []) : null),
    (c: FetchCall) => {
      if (c.method !== 'GET' || c.path !== '/api/v1/warehouses/wh-1/bins') return null
      const code = new URLSearchParams(c.search).get('search') ?? ''
      const i = BINS.indexOf(code.toUpperCase())
      // A-01 tiene cupo de 25 con 10 en existencia (15 libres); las demás no tienen cupo
      return json(200, { total: i < 0 ? 0 : 1, skip: 0, take: 200, items: i < 0 ? [] : [{ id: i + 1, code: BINS[i], ...(i === 0 ? { maxCapacityQty: 25, qtyOnHand: 10 } : {}) }] })
    },
    (c: FetchCall) => (c.method === 'POST' && c.path === '/api/v1/warehouse-tasks/5/distribute' ? json(200, { id: 5 }) : null),
  ]
}

async function openTask() {
  await renderRouter('src/app', { initialUrl: '/putaway' })
  await waitFor(() => expect(screen.getByText('SKU-1')).toBeTruthy())
  await fireEvent.press(screen.getByText('SKU-1'))
  await waitFor(() => expect(screen.getByLabelText('Cantidad por posición (opcional)')).toBeTruthy())
}

const scan = (label: string, code: string) => fireEvent(screen.getByLabelText(label), 'submitEditing', { nativeEvent: { text: code } })

describe('Acomodar — reparto por posición', () => {
  it('acumula posiciones, rechaza la repetida y la que no cabe, y confirma todo junto', async () => {
    await setupDevice()
    const calls = mockFetch(routes(45)) // 45 de 20 → 20 + 20 y una tercera con los 5 que quedaban
    await openTask()
    expect(screen.getByText('Pendiente de acomodar: 45')).toBeTruthy()

    await fireEvent.changeText(screen.getByLabelText('Cantidad por posición (opcional)'), '20')
    await scan('Escanea la siguiente posición', 'A-01')
    await waitFor(() => expect(screen.getByText('A-01 · 20')).toBeTruthy())
    expect(screen.getByText('Repartido: 20 · quedan 25 sin acomodar')).toBeTruthy()

    // el mismo código dentro de la ventana anti-doble-lectura de ScanField se ignora; pasada la ventana, se avisa que ya está
    const realNow = Date.now()
    jest.spyOn(Date, 'now').mockReturnValue(realNow + 5000)
    await scan('Escanea la siguiente posición', 'A-01')
    await waitFor(() => expect(screen.getByText('Esa posición ya está en el reparto.')).toBeTruthy())

    await scan('Escanea la siguiente posición', 'A-02')
    await waitFor(() => expect(screen.getByText('A-02 · 20')).toBeTruthy())
    expect(screen.getByText('Repartido: 40 · quedan 5 sin acomodar')).toBeTruthy()
    expect(screen.queryByTestId('sticky-alert')).toBeNull()

    // A-01 tiene cupo para 15 y recibirá 20: avisa (sin bloquear); A-02 no tiene cupo configurado y no avisa
    expect(screen.getByText('Cupo para 15: recibirá 20. Se puede confirmar igual.')).toBeTruthy()
    expect(screen.getAllByText(/^Cupo para/)).toHaveLength(1)

    // la tercera recibe lo que quedaba (5) y sale la alerta fija, que se puede cerrar
    await scan('Escanea la siguiente posición', 'A-03')
    await waitFor(() => expect(screen.getByText('A-03 · 5')).toBeTruthy())
    expect(screen.getByText('Repartido: 45 · quedan 0 sin acomodar')).toBeTruthy()
    expect(screen.getByText('A-03 recibe solo 5 (lo que quedaba), no 20.')).toBeTruthy()
    await fireEvent.press(screen.getByLabelText('Cerrar aviso'))
    await waitFor(() => expect(screen.queryByTestId('sticky-alert')).toBeNull())

    // una cuarta ya no cabe
    await scan('Escanea la siguiente posición', 'A-04')
    await waitFor(() => expect(screen.getByText(/Ya no hay unidades por acomodar/)).toBeTruthy())
    expect(screen.queryByText('A-04 · 20')).toBeNull()

    // nada se mandó hasta confirmar
    expect(calls.some((c) => c.path.endsWith('/distribute'))).toBe(false)
    await fireEvent.press(screen.getByText('Confirmar reparto'))
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/warehouse-tasks/5/distribute')).toBe(true))
    expect(calls.find((c) => c.path.endsWith('/distribute'))?.body).toEqual({ quantityPerBin: 20, toBinIds: [1, 2, 3] })
    await waitFor(() => expect(screen.getByText('Listo: SKU-1, 45 en 3 posición(es); quedan 0 pendientes')).toBeTruthy())
  })

  it('cada posición del reparto se puede quitar y las cantidades se recalculan (la que queda de resto cambia)', async () => {
    await setupDevice()
    mockFetch(routes(45))
    await openTask()
    await fireEvent.changeText(screen.getByLabelText('Cantidad por posición (opcional)'), '20')
    for (const code of ['A-01', 'A-02', 'A-03']) {
      await scan('Escanea la siguiente posición', code)
      await waitFor(() => expect(screen.getByLabelText(`Quitar ${code}`)).toBeTruthy())
    }
    expect(screen.getByText('A-03 · 5')).toBeTruthy()
    expect(screen.getByText('A-03 recibe solo 5 (lo que quedaba), no 20.')).toBeTruthy()

    // quitar la primera: A-02 y A-03 pasan a llevar 20 y 20, ya no hay posición de resto ni alerta
    await fireEvent.press(screen.getByLabelText('Quitar A-01'))
    await waitFor(() => expect(screen.queryByLabelText('Quitar A-01')).toBeNull())
    expect(screen.getByText('A-02 · 20')).toBeTruthy()
    expect(screen.getByText('A-03 · 20')).toBeTruthy()
    expect(screen.getByText('Repartido: 40 · quedan 5 sin acomodar')).toBeTruthy()
    expect(screen.queryByTestId('sticky-alert')).toBeNull()

    // la siguiente que se escanee es la del resto
    await scan('Escanea la siguiente posición', 'A-04')
    await waitFor(() => expect(screen.getByText('A-04 · 5')).toBeTruthy())
    expect(screen.getByText('A-04 recibe solo 5 (lo que quedaba), no 20.')).toBeTruthy()
  })

  it('sin cantidad por posición acomoda todo en la posición escaneada, como siempre', async () => {
    await setupDevice()
    const calls = mockFetch([
      ...routes(10),
      (c) => (c.method === 'POST' && c.path === '/api/v1/warehouse-tasks/5/complete' ? json(200, { id: 5 }) : null),
    ])
    await openTask()
    await scan('Escanea la posición destino', 'A-03')
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/warehouse-tasks/5/complete')).toBe(true))
    expect(calls.find((c) => c.path.endsWith('/complete'))?.body).toEqual({ toBinId: 3, quantity: 10 })
    expect(calls.some((c) => c.path.endsWith('/distribute'))).toBe(false)
  })
})
