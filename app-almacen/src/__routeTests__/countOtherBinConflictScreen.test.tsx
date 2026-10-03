// Lote A4 — "Otra posición" con 409: el servidor dice que ya existe una posición con ese código; la app lo muestra tal cual y,
// si la encuentra, ofrece usar la que ya existe (entra a la lista sin la marca "pendiente de revisión"). Una posición que ya
// está en la lista no se vuelve a agregar (el lote de captura entero se rechazaría por la línea repetida). En archivo propio:
// renderRouter() no aísla del todo su estado global de navegación entre dos llamadas del mismo archivo.
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { startLocalProductCount } from '../features/count/localCount'
import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'
import { json, mockFetch, setupDevice } from './countKit'

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

const PRODUCT = { publicId: 'p1', sku: 'SKU-1', name: 'Tornillo', trackingTypeCode: 'NONE' as const }

describe('Conteo por producto — "Otra posición" con 409', () => {
  it('muestra el 409 en claro, ofrece usar la posición existente y no repite una posición de la lista', async () => {
    await setupDevice()
    startLocalProductCount('wh-1', PRODUCT, { countId: 300, isBlind: false }, [
      { lineId: 1, productPublicId: 'p1', sku: 'SKU-1', productName: 'Tornillo', systemQty: 3, binId: 10, binCode: 'A-01', lotId: null, lotNumber: null, binIsProvisional: false },
    ])
    const calls = mockFetch([
      (c) =>
        c.method === 'GET' && c.path === '/api/v1/warehouses/wh-1/zones'
          ? json(200, [
              { id: 4, code: 'PCK', name: 'Picking', isActive: true },
              { id: 5, code: 'RES', name: 'Reserva', isActive: true },
            ])
          : null,
      (c) =>
        c.method === 'POST' && c.path === '/api/v1/cycle-counts/300/bins'
          ? json(409, { title: 'Ya existe una posición con ese código en el almacén.', status: 409, code: 'conflict' })
          : null,
      (c) => (c.method === 'GET' && c.path === '/api/v1/warehouses/wh-1/bins' ? json(200, { total: 1, skip: 0, take: 200, items: [{ id: 55, code: 'R-01-02' }] }) : null),
    ])

    await renderRouter('src/app', { initialUrl: '/count' })
    await waitFor(() => expect(screen.getByText('A-01')).toBeTruthy())
    await fireEvent.press(screen.getByRole('button', { name: 'Otra posición' }))
    await waitFor(() => expect(screen.getByRole('radio', { name: 'RES · Reserva' })).toBeTruthy())
    // varias zonas: hay que elegir una
    expect(screen.getByRole('button', { name: 'Agregar posición' }).props.accessibilityState).toMatchObject({ disabled: true })
    await fireEvent.press(screen.getByRole('radio', { name: 'RES · Reserva' }))

    // una posición que ya está en la lista: aviso, sin llamar al servidor
    await fireEvent.changeText(screen.getByLabelText('Código de la posición'), 'a-01')
    await fireEvent.press(screen.getByRole('button', { name: 'Agregar posición' }))
    await waitFor(() => expect(screen.getByText('La posición A-01 ya está en la lista: escribe la cantidad ahí.')).toBeTruthy())
    expect(calls.some((c) => c.path === '/api/v1/cycle-counts/300/bins')).toBe(false)

    // por partes: R-01-02 ya existe en el almacén (el aparato aún no la tenía) → 409
    await fireEvent.changeText(screen.getByLabelText('Código de la posición'), '')
    await fireEvent.changeText(screen.getByLabelText('Pasillo'), 'r')
    await fireEvent.changeText(screen.getByLabelText('Rack'), '01')
    await fireEvent.changeText(screen.getByLabelText('Nivel'), '02')
    await fireEvent.press(screen.getByRole('button', { name: 'Agregar posición' }))
    await waitFor(() => expect(screen.getByText('Ya existe una posición con ese código en el almacén.')).toBeTruthy())
    expect(calls.find((c) => c.path === '/api/v1/cycle-counts/300/bins')?.body).toEqual({ zoneId: 5, aisle: 'r', rack: '01', level: '02' })

    await waitFor(() => expect(screen.getByRole('button', { name: 'Usar R-01-02, que ya existe' })).toBeTruthy())
    await fireEvent.press(screen.getByRole('button', { name: 'Usar R-01-02, que ya existe' }))
    await waitFor(() => expect(screen.getByText('La posición R-01-02 ya existía; se agregó a la lista.')).toBeTruthy())
    expect(screen.getByText('R-01-02')).toBeTruthy()
    expect(screen.queryByText('Pendiente de revisión')).toBeNull()
    expect(screen.getByLabelText('Cantidad en R-01-02')).toBeTruthy()
  })
})
