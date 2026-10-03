// Lote A7 — bloque de Sincronización para un lote de conteo parcial.
import { fireEvent, render, waitFor } from '@testing-library/react-native'

import { skippedLineText as lineText, type SkippedNotice } from './countSkipped'
import type { CountRefreshResult } from './countRejectionApi'
import { SkippedLinesNotice } from './SkippedLinesNotice'
import { translate } from '../../kernel/i18n/i18n'

const NOTICE: SkippedNotice = {
  id: 'skip-3-x',
  outboxId: 3,
  countId: 300,
  createdAtUtc: '2026-10-03T15:00:00.000Z',
  lines: [
    { lineId: 9, sku: 'SKU-B', binCode: 'A-01', lotNumber: null, sentQty: 6, currentQty: 5 },
    { lineId: 10, sku: 'SKU-C', binCode: 'A-02', lotNumber: 'L-7', sentQty: 2, currentQty: 4 },
    { lineId: 11, sku: 'SKU-D', binCode: 'A-03', lotNumber: null, sentQty: 1, currentQty: null },
  ],
}

function flat(style: unknown): Record<string, unknown> {
  return Object.assign({}, ...[style].flat(3).filter(Boolean))
}

describe('SkippedLinesNotice', () => {
  it('dice que lo demás se guardó y lista las no guardadas con lo mandado y lo que dejó el supervisor', async () => {
    const { getByText, getByRole } = await render(<SkippedLinesNotice notice={NOTICE} onDismiss={jest.fn()} fetchCount={jest.fn()} />)
    const head = getByText('Se guardaron las demás líneas de tu conteo.')
    expect(flat(head.props.style).fontSize).toBeGreaterThanOrEqual(16)
    expect(getByText('Estas no se guardaron porque el supervisor ya las corrigió:')).toBeTruthy()
    expect(getByText('• SKU-B · A-01 (mandaste 6 → el supervisor dejó 5)')).toBeTruthy()
    expect(getByText('• SKU-C · lote L-7 · A-02 (mandaste 2 → el supervisor dejó 4)')).toBeTruthy()
    expect(getByText('• SKU-D · A-03 (mandaste 1; el supervisor ya la corrigió)')).toBeTruthy()
    expect(flat(getByText('• SKU-B · A-01 (mandaste 6 → el supervisor dejó 5)').props.style).fontSize).toBeGreaterThanOrEqual(16)
    expect(getByRole('button', { name: 'Actualizar el conteo' })).toBeTruthy()
    expect(getByRole('button', { name: 'Descartar este aviso' })).toBeTruthy()
  })

  it('no pide el conteo por su cuenta; al tocar Actualizar lo pide y no muestra cantidades esperadas', async () => {
    const result: CountRefreshResult = {
      kind: 'ok',
      state: 'open',
      number: 'CC-0300',
      lines: [{ lineId: 9, sku: 'SKU-B', productName: 'Tuerca', binCode: 'A-01', lotNumber: null, countedQty: 5, wasCorrected: true, correctedByName: 'Beto' }],
    }
    const fetchCount = jest.fn().mockResolvedValue(result)
    const { getByRole, getByText, queryByText } = await render(<SkippedLinesNotice notice={NOTICE} onDismiss={jest.fn()} fetchCount={fetchCount} />)
    expect(fetchCount).not.toHaveBeenCalled()
    await fireEvent.press(getByRole('button', { name: 'Actualizar el conteo' }))
    await waitFor(() => expect(getByText('El conteo CC-0300 sigue abierto (Pendiente).')).toBeTruthy())
    expect(fetchCount).toHaveBeenCalledWith(300)
    expect(getByText('Corregida por el supervisor (Beto) · Contado: 5')).toBeTruthy()
    expect(queryByText(/Esperado/)).toBeNull()
  })

  it('sin id de conteo no ofrece actualizar; Descartar avisa al padre', async () => {
    const onDismiss = jest.fn()
    const { queryByRole, getByRole } = await render(<SkippedLinesNotice notice={{ ...NOTICE, countId: null }} onDismiss={onDismiss} fetchCount={jest.fn()} />)
    expect(queryByRole('button', { name: 'Actualizar el conteo' })).toBeNull()
    await fireEvent.press(getByRole('button', { name: 'Descartar este aviso' }))
    expect(onDismiss).toHaveBeenCalledTimes(1)
  })

  it('un fallo inesperado al actualizar se muestra como error genérico', async () => {
    const { getByRole, getByText } = await render(<SkippedLinesNotice notice={NOTICE} onDismiss={jest.fn()} fetchCount={jest.fn().mockRejectedValue(new Error('x'))} />)
    await fireEvent.press(getByRole('button', { name: 'Actualizar el conteo' }))
    await waitFor(() => expect(getByText(translate('es', 'errors.generic'))).toBeTruthy())
  })
})

describe('textos del aviso (es/en)', () => {
  const t = (lang: 'es' | 'en') => (key: string, params?: Record<string, string | number>) => translate(lang, key, params)
  const line = NOTICE.lines[0]

  it('español exacto', () => {
    expect(translate('es', 'countSkipped.headline')).toBe('Se guardaron las demás líneas de tu conteo.')
    expect(translate('es', 'countSkipped.intro')).toBe('Estas no se guardaron porque el supervisor ya las corrigió:')
    expect(lineText(line, t('es'))).toBe('• SKU-B · A-01 (mandaste 6 → el supervisor dejó 5)')
  })

  it('inglés', () => {
    expect(translate('en', 'countSkipped.headline')).toBe('The rest of the lines of your count were saved.')
    expect(lineText(NOTICE.lines[1], t('en'))).toBe('• SKU-C · lot L-7 · A-02 (you sent 2 → the supervisor left 4)')
    expect(lineText(NOTICE.lines[2], t('en'))).toContain('the supervisor already corrected it')
    expect(translate('en', 'countSkipped.dismiss')).toBe('Dismiss this notice')
  })

  it('el residual del 409 dice que no se guardó ninguna línea y que se cree un conteo nuevo', () => {
    expect(translate('es', 'countRejection.nothingSaved')).toBe('No se guardó ninguna línea de este envío porque todas ya las corrigió el supervisor.')
    expect(translate('es', 'countRejection.whatToDo')).toContain('crea un conteo nuevo')
    expect(translate('es', 'countRejection.noReopenInApp')).not.toContain('avisa al supervisor')
  })
})
