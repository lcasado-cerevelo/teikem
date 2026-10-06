// Quitar líneas en Recibir y en Despacho: cada línea trae su botón «Quitar», pide confirmar y solo con «Quitar» en el aviso se borra; «No» la deja.
// En archivo propio: renderRouter() no aísla del todo su estado global de navegación entre dos llamadas del mismo archivo (ver homeLock.test.tsx).
import { Alert } from 'react-native'
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { getOpenPick } from '../features/dispatch/localPick'
import { getOpenReceipt } from '../features/receive/localLookup'
import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests } from '../kernel/db/database'
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

const scan = (label: string, code: string) => fireEvent(screen.getByLabelText(label), 'submitEditing', { nativeEvent: { text: code } })

/** Responde el aviso: pulsa el botón con ese estilo ('destructive' = Quitar, 'cancel' = No). */
function answerAlert(style: 'destructive' | 'cancel') {
  return jest.spyOn(Alert, 'alert').mockImplementation((_title, _body, buttons) => {
    void buttons?.find((b) => b.style === style)?.onPress?.()
  })
}

describe('Quitar líneas', () => {
  it('Recibir: pide confirmar; «No» deja la línea y «Quitar» la borra sin tocar las demás', async () => {
    await setupDevice()
    insertProduct(1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE')
    insertProduct(2, 'p2', 'SKU-2', 'Tuerca', '7502', 'NONE')
    mockFetch([])
    await renderRouter('src/app', { initialUrl: '/receive' })
    await waitFor(() => expect(screen.getByText('Recibo ciego')).toBeTruthy())
    await fireEvent.press(screen.getByText('Recibo ciego'))
    for (const [code, qty] of [['7501', '3'], ['7502', '4']] as const) {
      await waitFor(() => expect(screen.getByLabelText('Escanea el producto')).toBeTruthy())
      await scan('Escanea el producto', code)
      await waitFor(() => expect(screen.getByLabelText('Cantidad')).toBeTruthy())
      await fireEvent.changeText(screen.getByLabelText('Cantidad'), qty)
      await fireEvent.press(screen.getByText('Agregar'))
    }
    await waitFor(() => expect(getOpenReceipt()?.lines).toHaveLength(2))

    const no = answerAlert('cancel')
    await fireEvent.press(screen.getByLabelText('Quitar 3 SKU-1'))
    expect(no).toHaveBeenCalledWith('¿Quitar esta línea?', 'Se quita 3 SKU-1 de este recibo. Las demás líneas no cambian.', expect.any(Array))
    expect(getOpenReceipt()?.lines).toHaveLength(2)

    no.mockRestore()
    answerAlert('destructive')
    await fireEvent.press(screen.getByLabelText('Quitar 3 SKU-1'))
    await waitFor(() => expect(getOpenReceipt()?.lines.map((l) => l.sku)).toEqual(['SKU-2']))
  })

  it('Despacho: pide confirmar; «No» deja la línea y «Quitar» la borra', async () => {
    await setupDevice()
    insertProduct(1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE')
    mockFetch([(c) => (c.method === 'GET' && c.path === '/api/v1/inventory/exit-options' ? json(200, { total: 0, skip: 0, take: 500, items: [] }) : null)])
    await renderRouter('src/app', { initialUrl: '/dispatch' })
    await waitFor(() => expect(screen.getByLabelText('Escanea el producto')).toBeTruthy())
    await scan('Escanea el producto', '7501')
    await waitFor(() => expect(screen.getByLabelText('Cantidad')).toBeTruthy())
    await fireEvent.changeText(screen.getByLabelText('Cantidad'), '2')
    jest.spyOn(Date, 'now').mockReturnValue(Date.now() + 5000)
    await scan('Escanea de qué posición sale', 'A-01')
    await waitFor(() => expect(getOpenPick()?.lineRows).toHaveLength(1))

    const no = answerAlert('cancel')
    await fireEvent.press(screen.getByLabelText('Quitar 2 SKU-1'))
    expect(no).toHaveBeenCalledWith('¿Quitar esta línea?', 'Se quita 2 SKU-1 (de A-01) de este despacho. Las demás líneas no cambian.', expect.any(Array))
    expect(getOpenPick()?.lineRows).toHaveLength(1)

    no.mockRestore()
    answerAlert('destructive')
    await fireEvent.press(screen.getByLabelText('Quitar 2 SKU-1'))
    await waitFor(() => expect(getOpenPick()?.lineRows).toHaveLength(0))
  })
})
