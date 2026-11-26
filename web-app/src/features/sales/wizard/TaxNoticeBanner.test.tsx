// Lote 248 — aviso de la exención de IVU al vender: el texto sale de t() con los datos del servidor (en español y en inglés) y no pinta nada sin aviso.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../../kernel/i18n/i18n'
import { TaxNoticeBanner } from './TaxNoticeBanner'

const mock = vi.hoisted(() => ({ notice: { level: 'NONE' } as Record<string, unknown> }))

vi.mock('../../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../kernel/api/client')>()
  const fetch = async () => new Response(JSON.stringify(mock.notice), { status: 200, headers: { 'Content-Type': 'application/json' } })
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function wrap(client: string | null = 'c1') {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={qc}>
      <TaxNoticeBanner clientPublicId={client} />
    </QueryClientProvider>,
  )
}

describe('Aviso de exención al vender', () => {
  beforeAll(() => setLang('es'))
  afterAll(() => setLang('es'))

  it('sin aviso (NONE) no pinta nada', async () => {
    mock.notice = { level: 'NONE' }
    const { container } = wrap()
    await new Promise((r) => setTimeout(r, 50))
    expect(container).toBeEmptyDOMElement()
  })

  it('por vencer: fecha de la compañía y días, en español', async () => {
    mock.notice = { level: 'EXPIRING', validTo: '2026-12-31', days: 12, certificateNumber: 'A-77' }
    wrap()
    const note = await screen.findByTestId('tax-notice')
    expect(note).toHaveTextContent('vence el 12/31/2026 (en 12 días): después se cobrará el IVU.')
    expect(note).toHaveTextContent('certificado A-77')
  })

  it('vencido, en inglés: el texto no depende del idioma del servidor', async () => {
    setLang('en')
    mock.notice = { level: 'EXPIRED', validTo: '2026-11-01', message: 'texto en español del servidor' }
    wrap()
    const note = await screen.findByTestId('tax-notice')
    expect(note).toHaveTextContent('expired on 11/01/2026: IVU will be charged.')
    expect(note).not.toHaveTextContent('servidor')
  })

  it('sin cliente no consulta ni pinta', () => {
    const { container } = wrap(null)
    expect(container).toBeEmptyDOMElement()
  })
})
