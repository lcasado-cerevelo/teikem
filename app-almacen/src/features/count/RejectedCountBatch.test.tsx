// Lote A6 — tarjeta del rechazo 409 "línea corregida por el supervisor": explicación, actualizar el conteo y sus estados de error.
import { fireEvent, render, waitFor } from '@testing-library/react-native'

import { CORRECTED_LINE_LOCKED, type CorrectedLineRejection } from './countRejection'
import type { CountRefreshResult } from './countRejectionApi'
import { MAX_REFRESHED_LINES_SHOWN, RejectedCountBatch } from './RejectedCountBatch'

const PARSED: CorrectedLineRejection = {
  countId: 300,
  rows: [
    { row: 1, sku: 'SKU-A', capturedQty: 4 },
    { row: 3, sku: 'SKU-B', capturedQty: null },
  ],
  message: `${CORRECTED_LINE_LOCKED} Renglón(es) del lote: 1 (SKU-A), 3 (SKU-B). No se guardó nada.`,
}

function flat(style: unknown): Record<string, unknown> {
  return Object.assign({}, ...[style].flat(3).filter(Boolean))
}

async function renderCard(rejection: CorrectedLineRejection, fetchCount: (id: number) => Promise<CountRefreshResult>, extra: { finishAlsoRejected?: boolean } = {}) {
  const onDiscard = jest.fn()
  const onRetry = jest.fn()
  const utils = await render(
    <RejectedCountBatch rowId={1} rejection={rejection} finishAlsoRejected={extra.finishAlsoRejected ?? false} onDiscard={onDiscard} onRetry={onRetry} fetchCount={fetchCount} />,
  )
  return { ...utils, onDiscard, onRetry }
}

describe('RejectedCountBatch', () => {
  it('explica en grande qué pasó, qué renglones y qué hacer, sin llamar al servidor por su cuenta', async () => {
    const fetchCount = jest.fn()
    const { getByText, getByRole, queryByText } = await renderCard(PARSED, fetchCount)

    const headline = getByText('El supervisor ya corrigió todas las líneas de este envío.')
    expect(flat(headline.props.style).fontSize).toBeGreaterThanOrEqual(16)
    expect(getByText('No se guardó ninguna línea de este envío porque todas ya las corrigió el supervisor.')).toBeTruthy()
    expect(getByText('• Renglón 1: SKU-A (mandaste 4)')).toBeTruthy()
    expect(getByText('• Renglón 3: SKU-B')).toBeTruthy()
    const todo = getByText(
      'Si falta contar algo, crea un conteo nuevo (escanea la posición otra vez) o pide al supervisor que lo revise.',
    )
    expect(flat(todo.props.style).fontSize).toBeGreaterThanOrEqual(16)
    expect(getByRole('button', { name: 'Actualizar el conteo' })).toBeTruthy()
    expect(getByRole('button', { name: 'Descartar este envío' })).toBeTruthy()
    // el mensaje crudo no se repite cuando se pudo leer
    expect(queryByText(PARSED.message)).toBeNull()
    expect(fetchCount).not.toHaveBeenCalled()
  })

  it('mensaje sin lista legible: lo muestra tal cual', async () => {
    const raw: CorrectedLineRejection = { countId: 300, rows: null, message: CORRECTED_LINE_LOCKED }
    const { getByText } = await renderCard(raw, jest.fn())
    expect(getByText('Lo que dijo el servidor:')).toBeTruthy()
    expect(getByText(CORRECTED_LINE_LOCKED)).toBeTruthy()
  })

  it('sin id de conteo: no ofrece actualizar y lo dice', async () => {
    const { queryByRole, getByText } = await renderCard({ ...PARSED, countId: null }, jest.fn())
    expect(queryByRole('button', { name: 'Actualizar el conteo' })).toBeNull()
    expect(getByText('No se pudo saber de qué conteo es este envío; pide al supervisor que lo revise.')).toBeTruthy()
  })

  it('avisa si el cierre del mismo conteo también quedó con error', async () => {
    const { getByText } = await renderCard(PARSED, jest.fn(), { finishAlsoRejected: true })
    expect(getByText('El cierre de este mismo conteo también quedó con error: el conteo no se terminó desde este aparato.')).toBeTruthy()
  })

  it('Actualizar el conteo abierto: estado, resumen y líneas vigentes (corregida por quién), sin cantidades esperadas', async () => {
    const fetchCount = jest.fn().mockResolvedValue({
      kind: 'ok',
      state: 'open',
      number: 'CC-0300',
      lines: [
        { lineId: 1, sku: 'SKU-A', productName: 'Tornillo', binCode: 'A-01', lotNumber: null, countedQty: 5, wasCorrected: true, correctedByName: 'Beto' },
        { lineId: 2, sku: 'SKU-B', productName: 'Tuerca', binCode: 'A-02', lotNumber: 'L1', countedQty: null, wasCorrected: false, correctedByName: null },
        { lineId: 3, sku: 'SKU-C', productName: 'Arandela', binCode: 'A-03', lotNumber: null, countedQty: 2, wasCorrected: true, correctedByName: null },
      ],
    } satisfies CountRefreshResult)
    const { getByRole, getByText, queryByText } = await renderCard(PARSED, fetchCount)

    await fireEvent.press(getByRole('button', { name: 'Actualizar el conteo' }))
    await waitFor(() => expect(getByText('El conteo CC-0300 sigue abierto (Pendiente).')).toBeTruthy())
    expect(fetchCount).toHaveBeenCalledWith(300)
    expect(getByText('3 líneas: 2 corregidas por el supervisor, 1 sin contar.')).toBeTruthy()
    expect(getByText('SKU-A · A-01')).toBeTruthy()
    expect(getByText('Corregida por el supervisor (Beto) · Contado: 5')).toBeTruthy()
    expect(getByText('SKU-B · A-02 · L1')).toBeTruthy()
    expect(getByText('Sin contar')).toBeTruthy()
    expect(getByText('Corregida por el supervisor · Contado: 2')).toBeTruthy()
    expect(queryByText(/Esperado/)).toBeNull()
  })

  it('muchas líneas: pinta las primeras y resume el resto', async () => {
    const lines = Array.from({ length: MAX_REFRESHED_LINES_SHOWN + 5 }, (_, i) => ({
      lineId: i + 1,
      sku: `S-${i + 1}`,
      productName: 'P',
      binCode: 'B',
      lotNumber: null,
      countedQty: null,
      wasCorrected: false,
      correctedByName: null,
    }))
    const { getByRole, getByText, queryByText } = await renderCard(PARSED, jest.fn().mockResolvedValue({ kind: 'ok', state: 'counted', number: 'CC-1', lines }))
    await fireEvent.press(getByRole('button', { name: 'Actualizar el conteo' }))
    await waitFor(() => expect(getByText('El conteo CC-1 ya se terminó de contar (Contado): pide al supervisor que lo revise.')).toBeTruthy())
    expect(getByText(`S-${MAX_REFRESHED_LINES_SHOWN} · B`)).toBeTruthy()
    expect(queryByText(`S-${MAX_REFRESHED_LINES_SHOWN + 1} · B`)).toBeNull()
    expect(getByText('… y 5 líneas más (míralas en la web).')).toBeTruthy()
  })

  it.each([
    [{ kind: 'notFound' } as CountRefreshResult, 'El conteo ya no existe (se eliminó). Puedes descartar este envío.'],
    [{ kind: 'offline' } as CountRefreshResult, 'Sin señal: no se pudo actualizar el conteo. Inténtalo de nuevo cuando haya conexión.'],
    [{ kind: 'error', message: 'No tiene permiso para esta acción.' } as CountRefreshResult, 'No tiene permiso para esta acción.'],
    [
      { kind: 'ok', state: 'closed', number: 'CC-9', lines: [] } as CountRefreshResult,
      'El conteo CC-9 ya fue reconciliado: no admite más capturas. Puedes descartar este envío.',
    ],
  ])('estado tras actualizar: %j', async (result, text) => {
    const { getByRole, getByText } = await renderCard(PARSED, jest.fn().mockResolvedValue(result))
    await fireEvent.press(getByRole('button', { name: 'Actualizar el conteo' }))
    await waitFor(() => expect(getByText(text)).toBeTruthy())
    // se puede volver a intentar
    expect(getByRole('button', { name: 'Actualizar el conteo' })).toBeTruthy()
  })

  it('un fallo inesperado de la consulta muestra el error genérico', async () => {
    const { getByRole, getByText } = await renderCard(PARSED, jest.fn().mockRejectedValue(new Error('boom')))
    await fireEvent.press(getByRole('button', { name: 'Actualizar el conteo' }))
    await waitFor(() => expect(getByText('Ocurrió un error. Intente de nuevo.')).toBeTruthy())
  })

  it('Descartar y Reintentar llaman a la cola', async () => {
    const { getByRole, onDiscard, onRetry } = await renderCard(PARSED, jest.fn())
    await fireEvent.press(getByRole('button', { name: 'Descartar este envío' }))
    expect(onDiscard).toHaveBeenCalledTimes(1)
    await fireEvent.press(getByRole('button', { name: 'Reintentar: Conteo (líneas capturadas)' }))
    expect(onRetry).toHaveBeenCalledTimes(1)
  })
})
