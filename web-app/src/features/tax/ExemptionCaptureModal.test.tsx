// Lote 248 — captura única de la exención: valida antes de enviar, con documento ya subido manda solo el alcance y muestra el título y los errores por campo del servidor.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import type { ClientDocumentDto } from '../clients/clientSalesApi'
import { ExemptionCaptureModal } from './ExemptionCaptureModal'

const CLIENT = '11111111-0000-0000-0000-000000000001'
const mock = vi.hoisted(() => ({ fields: [] as [string, string][], posts: 0, fail: false, key: '' }))

class RecordingFormData {
  append(field: string, value: string | File) {
    mock.fields.push([field, typeof value === 'string' ? value : value.name])
  }
}

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const p = new URL(req.url).pathname
    if (req.method === 'POST' && p === `/api/v1/clients/${CLIENT}/tax-exemptions`) {
      mock.posts++
      mock.key = req.headers.get('Idempotency-Key') ?? ''
      if (mock.fail) return new Response(JSON.stringify({ title: 'Ese documento ya respalda otra exención activa.', status: 409, errors: { clientDocumentPublicId: ['Ese documento ya respalda otra exención activa.'] } }), { status: 409, headers: { 'Content-Type': 'application/problem+json' } })
      return json({ exemption: { id: 1 }, documentPublicId: 'd1' })
    }
    if (p === '/api/v1/tax/rates') return json([{ code: 'IVU', status: 'ACTIVE', components: [{ code: 'STATE', name: 'Estatal', percent: 10.5 }, { code: 'CITY', name: 'Municipal', percent: 1 }] }])
    if (p === '/api/v1/tax/settings') return json({ defaultRateCode: 'IVU' })
    if (p === '/api/v1/catalogs/ClientDocumentType') return json([{ code: 'IVU_EXEMPTION_STATE', label: 'Exención de IVU estatal', sortOrder: 1, isEnabled: true, extraJson: '{"requiresValidTo":true,"isTax":true}' }, { code: 'OTHER', label: 'Otro', sortOrder: 9, isEnabled: true, extraJson: null }])
    return new Response('{}', { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})
function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

function wrap(document?: ClientDocumentDto) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const onClose = vi.fn()
  render(
    <QueryClientProvider client={client}>
      <AccessProvider permissions={new Set(['tax.manage'])} modules={new Set(['TAX'])}>
        <MemoryRouter>
          <ExemptionCaptureModal clientPublicId={CLIENT} clientName="Acme" document={document} onClose={onClose} />
        </MemoryRouter>
      </AccessProvider>
    </QueryClientProvider>,
  )
  return onClose
}

describe('Captura única de la exención', () => {
  beforeAll(() => {
    vi.stubGlobal('FormData', RecordingFormData)
    setLang('es')
  })
  afterAll(() => vi.unstubAllGlobals())
  beforeEach(() => {
    mock.fields.length = 0
    mock.posts = 0
    mock.fail = false
  })

  it('con archivo nuevo valida antes de enviar y solo ofrece tipos de impuestos', async () => {
    const user = userEvent.setup()
    wrap()
    const type = await screen.findByLabelText('Tipo de documento')
    expect(await screen.findByRole('option', { name: 'Exención de IVU estatal' })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: 'Otro' })).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Aplicar exención' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Escoja el tipo de certificado.')
    await user.selectOptions(type, 'IVU_EXEMPTION_STATE')
    await user.click(screen.getByRole('button', { name: 'Aplicar exención' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Adjunte el archivo del certificado.')
    expect(mock.posts).toBe(0)
  })

  it('con archivo nuevo manda el certificado, las fechas y el alcance por componentes en un solo envío con Idempotency-Key', async () => {
    const user = userEvent.setup()
    const onClose = wrap()
    await screen.findByRole('option', { name: 'Exención de IVU estatal' })
    await user.selectOptions(screen.getByLabelText('Tipo de documento'), 'IVU_EXEMPTION_STATE')
    await user.upload(screen.getByLabelText('Archivo'), new File(['x'], 'cert.pdf', { type: 'application/pdf' }))
    await user.type(screen.getByLabelText('Número de certificado'), 'A-9')
    await user.type(screen.getByLabelText('Vale desde'), '01/01/2026')
    await user.type(screen.getByLabelText('Vale hasta (obligatorio)'), '12/31/2026')
    await user.click(screen.getByRole('radio', { name: 'Solo estos componentes' }))
    await user.click(await screen.findByRole('checkbox', { name: /Municipal/ }))
    await user.click(screen.getByRole('button', { name: 'Aplicar exención' }))
    await waitFor(() => expect(onClose).toHaveBeenCalled())
    expect(mock.posts).toBe(1)
    expect(mock.key).not.toBe('')
    expect(mock.fields).toEqual(expect.arrayContaining([['file', 'cert.pdf'], ['documentType', 'IVU_EXEMPTION_STATE'], ['documentNumber', 'A-9'], ['validFrom', '2026-01-01'], ['validTo', '2026-12-31'], ['scope', 'COMPONENTS'], ['componentCodes', 'CITY']]))
  })

  it('con un documento ya subido no pide archivo ni fechas y manda su PublicId; el 409 se muestra', async () => {
    mock.fail = true
    const user = userEvent.setup()
    const onClose = wrap({ publicId: 'doc-1', documentType: 'IVU_EXEMPTION_STATE', documentTypeLabel: 'Exención de IVU estatal', documentNumber: 'A-9', validTo: '2026-12-31' })
    expect(screen.queryByLabelText('Archivo')).not.toBeInTheDocument()
    await user.click(await screen.findByRole('button', { name: 'Aplicar exención' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Ese documento ya respalda otra exención activa.')
    expect(mock.fields).toEqual(expect.arrayContaining([['clientDocumentPublicId', 'doc-1'], ['scope', 'ALL']]))
    expect(onClose).not.toHaveBeenCalled()
  })
})
