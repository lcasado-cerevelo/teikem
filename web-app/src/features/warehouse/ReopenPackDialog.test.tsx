// Lote 249 — «Reabrir el empaque»: exige el motivo y llama al endpoint de la recolección (o de la maestra).
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import { mock, sent, wrap } from '../sales/changes/testKit'
import { ReopenPackDialog } from './ReopenPackDialog'

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const { fetchFor } = await import('../sales/changes/testKit')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: fetchFor() }) }
})

describe('Reabrir el empaque', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.routes = {
      'POST /api/v1/pick-batches/b-1/reopen-pack': () => ({ json: {} }),
      'POST /api/v1/pick-batches/groups/g-1/reopen-pack': () => ({ json: {} }),
    }
  })

  it('sin motivo no envía; con motivo reabre la recolección', async () => {
    const user = userEvent.setup()
    const onClose = vi.fn()
    wrap(<ReopenPackDialog publicId="b-1" number="EMP-00001" rowVersion="rv" onClose={onClose} />)
    await user.click(screen.getByRole('button', { name: 'Reabrir el empaque' }))
    expect(screen.getByText('Indique el motivo para reabrir el empaque.')).toBeInTheDocument()
    expect(sent('POST', '/api/v1/pick-batches/b-1/reopen-pack')).toHaveLength(0)
    await user.type(screen.getByLabelText('Motivo'), 'El cliente cambió el pedido')
    await user.click(screen.getByRole('button', { name: 'Reabrir el empaque' }))
    await waitFor(() => expect(sent('POST', '/api/v1/pick-batches/b-1/reopen-pack')).toHaveLength(1))
    expect(sent('POST', '/api/v1/pick-batches/b-1/reopen-pack')[0].body).toEqual({ reason: 'El cliente cambió el pedido', rowVersion: 'rv' })
    await waitFor(() => expect(onClose).toHaveBeenCalled())
  })

  it('la maestra usa su propio endpoint', async () => {
    const user = userEvent.setup()
    wrap(<ReopenPackDialog publicId="g-1" number="ORD-1" group onClose={() => {}} />)
    await user.type(screen.getByLabelText('Motivo'), 'x')
    await user.click(screen.getByRole('button', { name: 'Reabrir el empaque' }))
    await waitFor(() => expect(sent('POST', '/api/v1/pick-batches/groups/g-1/reopen-pack')).toHaveLength(1))
  })
})
