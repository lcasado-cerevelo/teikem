// Tarea 25 — conteo informado al capturar en la pantalla de Conteo por posición: al ACEPTAR la cantidad de una línea esperada la app la verifica con el servidor
// (sin haber mostrado lo esperado antes): fuera del margen pide recontar sin decir el esperado y no captura; tras recontar (o al coincidir) captura, muestra el
// resultado y deja la línea cerrada (no se edita ni se quita). Un 403 deja capturar como siempre y no vuelve a preguntar. En archivo propio: renderRouter()
// no aísla del todo su estado global de navegación entre dos llamadas del mismo archivo (ver homeLock.test.tsx).
import { __resetAllForTests } from 'expo-sqlite'
import { __resetSecureStoreForTests } from 'expo-secure-store'
import { cleanup, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library'

import { forgetChecks } from '../features/count/lineCheck'
import { getCapturedLines, startLocalCount } from '../features/count/localCount'
import { __resetSessionForTests } from '../kernel/auth/session'
import { __resetDbForTests, getDb } from '../kernel/db/database'
import { json, mockFetch, setupDevice, type FetchCall } from './countKit'

jest.mock('../features/count/countApi', () => {
  const actual = jest.requireActual('../features/count/countApi')
  return {
    ...actual,
    fetchExpectedLines: jest.fn(async () => [
      { lineId: 11, productPublicId: 'p1', sku: 'SKU-1', productName: 'Tornillo', systemQty: null },
      { lineId: 12, productPublicId: 'p2', sku: 'SKU-2', productName: 'Tuerca', systemQty: null },
    ]),
  }
})

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSecureStoreForTests()
  __resetSessionForTests()
  forgetChecks()
})

afterEach(() => {
  cleanup()
  jest.restoreAllMocks()
})

async function open() {
  await setupDevice()
  const db = getDb()
  db.runSync("INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, is_active) VALUES (1, 'p1', 'SKU-1', 'Tornillo', '7501', 'NONE', 1)")
  db.runSync("INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, is_active) VALUES (2, 'p2', 'SKU-2', 'Tuerca', '7502', 'NONE', 1)")
  startLocalCount('wh-1', { id: 5, code: 'A-01-01' }, { countId: 99, isBlind: true })
  await renderRouter('src/app', { initialUrl: '/count' })
  await waitFor(() => expect(screen.getByText('Lo que se espera aquí')).toBeTruthy())
}

async function scanAndQty(code: string, qty: string) {
  await fireEvent(screen.getByLabelText('Escanea el producto contado'), 'submitEditing', { nativeEvent: { text: code } })
  await waitFor(() => expect(screen.getByLabelText('Cantidad encontrada')).toBeTruthy())
  await fireEvent.changeText(screen.getByLabelText('Cantidad encontrada'), qty)
}

const check = (...results: Array<Record<string, unknown> | number>) => {
  let i = 0
  return (c: FetchCall) => {
    if (c.method !== 'POST' || !/\/cycle-counts\/99\/lines\/\d+\/check$/.test(c.path)) return null
    const r = results[Math.min(i++, results.length - 1)]
    return typeof r === 'number' ? json(r, { title: 'No está habilitado ver lo esperado al contar.', status: r }) : json(200, r)
  }
}

describe('Conteo por posición — informado al capturar', () => {
  it('fuera del margen pide recontar sin decir el esperado; el reconteo captura la línea, la deja cerrada y avisa el resultado', async () => {
    const calls = mockFetch([check({ lineId: 11, state: 'RECOUNT', matches: false, countedQty: 18 }, { lineId: 11, state: 'FINAL', matches: false, countedQty: 19, expectedQty: 20 })])
    await open()
    await scanAndQty('7501', '18')
    await fireEvent.press(screen.getByText('Agregar'))
    await waitFor(() => expect(screen.getByText('No coincide con lo esperado. Vuelve a contar y acepta de nuevo.')).toBeTruthy())
    // no se capturó, sigue en la misma pantalla con la cantidad vacía y nunca se dijo el esperado
    expect(getCapturedLines(1)).toHaveLength(0)
    expect(screen.getByLabelText('Cantidad encontrada').props.value).toBe('')
    expect(screen.queryByText(/se esperaba/)).toBeNull()
    expect(calls.find((c) => c.path.endsWith('/lines/11/check'))?.body).toEqual({ countedQty: 18 })

    await fireEvent.changeText(screen.getByLabelText('Cantidad encontrada'), '19')
    await fireEvent.press(screen.getByText('Agregar'))
    await waitFor(() => expect(screen.getByText('Contaste 19 y se esperaba 20. Queda para revisión.')).toBeTruthy())
    expect(getCapturedLines(1).map((r) => [r.sku, r.countedQty])).toEqual([['SKU-1', 19]])

    // la línea está cerrada: no se edita ni se quita
    await fireEvent.press(screen.getByLabelText(/^Editar /))
    await waitFor(() => expect(screen.getByText('Esa línea ya se verificó; no se puede cambiar ni quitar.')).toBeTruthy())
    await fireEvent.press(screen.getByLabelText(/^Quitar /))
    expect(getCapturedLines(1)).toHaveLength(1)
  })

  it('una coincidencia captura de una vez y avisa "Coincide"', async () => {
    mockFetch([check({ lineId: 12, state: 'MATCH', matches: true, countedQty: 5, expectedQty: 5 })])
    await open()
    await scanAndQty('7502', '5')
    await fireEvent.press(screen.getByText('Agregar'))
    await waitFor(() => expect(screen.getByText('Coincide: contaste 5 y se esperaba 5.')).toBeTruthy())
    expect(getCapturedLines(1).map((r) => [r.sku, r.countedQty])).toEqual([['SKU-2', 5]])
  })

  it('403 (no habilitado): captura como siempre y no vuelve a preguntar en ese conteo', async () => {
    const calls = mockFetch([check(403)])
    await open()
    await scanAndQty('7501', '3')
    await fireEvent.press(screen.getByText('Agregar'))
    await waitFor(() => expect(getCapturedLines(1)).toHaveLength(1))
    await scanAndQty('7502', '4')
    await fireEvent.press(screen.getByText('Agregar'))
    await waitFor(() => expect(getCapturedLines(1)).toHaveLength(2))
    expect(calls.filter((c) => c.path.includes('/check'))).toHaveLength(1)
  })

  it('sin señal captura sin verificar', async () => {
    mockFetch([])
    await open()
    await scanAndQty('7501', '3')
    await fireEvent.press(screen.getByText('Agregar'))
    await waitFor(() => expect(getCapturedLines(1)).toHaveLength(1))
  })
})
