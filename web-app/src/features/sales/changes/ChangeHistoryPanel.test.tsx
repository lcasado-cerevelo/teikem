// Lote 249 — historial de modificaciones: quién registró, quién autorizó en el cliente, cuánto cambió el total y el detalle con su «antes y después».
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../../kernel/i18n/i18n'
import { ChangeHistoryPanel } from './ChangeHistoryPanel'
import { mock, wrap } from './testKit'

vi.mock('../../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../kernel/api/client')>()
  const { fetchFor } = await import('./testKit')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: fetchFor() }) }
})

const ITEM = {
  publicId: 'ch-1', orderPublicId: 'o-1', orderNumber: 'OV-00001', changeNo: 1, kind: 'MODIFY', status: 'APPLIED', statusLabel: 'Aplicada', isPending: false, registeredBy: 5, registeredByName: 'Ana',
  registeredAtUtc: '2026-11-26T14:00:00', authorizedByName: 'Marta Ríos', authorizedByTitle: 'Compras', channel: 'PHONE', channelLabel: 'Por teléfono', reason: 'Llamó',
  totalBefore: 100, totalAfter: 110, totalDelta: 10, requiresApproval: false,
}

describe('Historial de modificaciones', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.routes = {
      'GET /api/v1/sales-orders/o-1/changes': () => ({ json: [ITEM] }),
      'GET /api/v1/sales-orders/o-1/changes/ch-1': () => ({
        json: {
          item: ITEM, authorizedAtUtc: '2026-11-26T13:50:00', attachmentFileName: 'correo.png', linesAdded: 0, linesRemoved: 0, linesChanged: 1, reversedBatchCount: 1,
          diff: { added: [], removed: [], changed: [{ lineId: 1, lineNo: 1, sku: 'A1', name: 'Guantes', change: 'CHANGED', fields: [{ field: 'qtyOrdered', before: '8', after: '9' }] }], terms: [], totalBefore: 100, totalAfter: 110, delta: 10 },
          effects: { reversedBatches: ['EMP-00003'], reopenedPacks: [], warehouseImpact: '' },
        },
      }),
    }
  })

  it('lista la modificación con quién la autorizó y abre su detalle con la diferencia y los efectos', async () => {
    const user = userEvent.setup()
    wrap(<ChangeHistoryPanel orderPublicId="o-1" />)
    expect(await screen.findByText('Marta Ríos')).toBeInTheDocument()
    expect(screen.getByText('Aplicada')).toBeInTheDocument()
    await user.click(screen.getByText('Marta Ríos'))
    const dialog = await screen.findByRole('dialog', { name: /Modificación n\.º 1 de OV-00001/ })
    expect(await within(dialog).findByText('Cantidad')).toBeInTheDocument()
    expect(within(dialog).getByText(/Se deshace la recolección EMP-00003/)).toBeInTheDocument()
    expect(within(dialog).getByText('correo.png')).toBeInTheDocument()
  })

  it('sin modificaciones lo dice', async () => {
    mock.routes['GET /api/v1/sales-orders/o-1/changes'] = () => ({ json: [] })
    wrap(<ChangeHistoryPanel orderPublicId="o-1" />)
    expect(await screen.findByText('Esta orden no tiene modificaciones.')).toBeInTheDocument()
  })
})
