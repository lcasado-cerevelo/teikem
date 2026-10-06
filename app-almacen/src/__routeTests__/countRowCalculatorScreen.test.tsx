// Lote A9 (pruebas del dueño en el Zebra, 2026-10-06): la calculadora de una fila del conteo por producto era una ventana (Modal) y, en
// Android, el teclado en pantalla tapaba «Sueltas» sin poder desplazar. Ahora ocupa la pantalla en el mismo lugar, dentro del
// KeyboardScreen del conteo (se desplaza hasta el campo enfocado), con el botón de volver por icono. En archivo propio por el estado
// global de renderRouter().
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor, within } from 'expo-router/testing-library'

import { getOpenCount, getProductCountRows, startLocalProductCount } from '../features/count/localCount'
import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'
import { mockFetch, setupDevice } from './countKit'

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

const LINE = { productPublicId: 'p-1', sku: 'SKU-1', productName: 'Tornillo', binIsProvisional: false, lotId: null, lotNumber: null }

describe('Conteo por producto — calculadora de una fila', () => {
  it('se abre en el mismo lugar (no en una ventana), dentro de la pantalla desplazable; «Usar 25» llena esa fila y volver la cierra', async () => {
    await setupDevice()
    startLocalProductCount('wh-1', { publicId: 'p-1', sku: 'SKU-1', name: 'Tornillo', trackingTypeCode: 'NONE' }, { countId: 310, isBlind: true }, [
      { ...LINE, lineId: 1, binId: 10, binCode: 'A-01', systemQty: null },
      { ...LINE, lineId: 2, binId: 11, binCode: 'B-02', systemQty: null },
    ])
    mockFetch([])

    await renderRouter('src/app', { initialUrl: '/count' })
    await waitFor(() => expect(screen.getByText('Tornillo')).toBeTruthy())

    await fireEvent.press(screen.getByRole('button', { name: 'Calculadora de A-01' }))
    const panel = screen.getByTestId('count-row-calculator')
    // dentro del KeyboardScreen del conteo: el campo que se escribe se puede llevar arriba del teclado
    expect(within(screen.getByTestId('keyboard-screen')).getByTestId('count-row-calculator')).toBe(panel)
    // la lista de filas ya no está debajo (no es una ventana encima)
    expect(screen.queryByLabelText('Cantidad en B-02')).toBeNull()
    expect(within(panel).getByText('Cantidad en A-01')).toBeTruthy()

    await fireEvent.changeText(screen.getByLabelText('Filas del bloque 1'), '5')
    await fireEvent.changeText(screen.getByLabelText('Columnas del bloque 1'), '3')
    await fireEvent.changeText(screen.getByLabelText('Sueltas'), '10')
    await fireEvent.press(screen.getByRole('button', { name: 'Usar 25' }))

    await waitFor(() => expect(screen.getByLabelText('Cantidad en A-01').props.value).toBe('25'))
    expect(getProductCountRows(getOpenCount()!.id).find((r) => r.binCode === 'A-01')?.countedQty).toBe(25)

    // volver con la flecha («Cantidad directa») cierra sin tocar la cantidad de la fila
    await fireEvent.press(screen.getByRole('button', { name: 'Calculadora de B-02' }))
    await fireEvent.changeText(screen.getByLabelText('Sueltas'), '4')
    await fireEvent.press(screen.getByRole('button', { name: 'Cantidad directa' }))
    await waitFor(() => expect(screen.getByLabelText('Cantidad en B-02').props.value).toBe(''))
    expect(screen.queryByTestId('count-row-calculator')).toBeNull()
  })
})
