// Lote 243 — vista previa del correo: iframe sin scripts, adjuntos, texto en el idioma pedido y errores del API tal cual.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import { EmailPreviewModal } from './EmailPreviewModal'
import type { NotificationEventDto } from './notificationsApi'

const mock = vi.hoisted(() => ({ requests: [] as { body: Record<string, unknown> }[], mode: 'ok' as 'ok' | 'empty' | 'fail' }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const body = JSON.parse(await req.clone().text()) as Record<string, unknown>
    mock.requests.push({ body })
    if (mock.mode === 'fail') return new Response(JSON.stringify({ title: 'Este evento no admite vista previa con un documento real.', status: 400 }), { status: 400, headers: { 'Content-Type': 'application/problem+json' } })
    const attachments = mock.mode === 'empty' ? [] : [{ fileName: 'firma-OV-00012.png', contentType: 'image/png', sizeBytes: 1536 }, { fileName: 'grande.bin', contentType: 'application/octet-stream', sizeBytes: 3 * 1024 * 1024 }]
    return new Response(JSON.stringify({ channel: 'EMAIL', subject: `Asunto ${body.lang}`, bodyText: 't', bodyHtml: '<script>alert(1)</script><p>Hola</p>', attachments, usedSample: true, templateSource: 'REQUEST' }), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const EVENT: NotificationEventDto = { code: 'X', name: 'X', nameEn: 'X', variables: [], contextRecipients: [] }
const TEMPLATE = { subjectEs: 'Asunto es', subjectEn: 'Subject en', bodyEs: 'Cuerpo', bodyEn: 'Body' }

function open() {
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <EmailPreviewModal event={EVENT} template={TEMPLATE} onClose={() => {}} />
    </QueryClientProvider>,
  )
}

describe('EmailPreviewModal', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.mode = 'ok'
  })

  it('pinta el HTML en un iframe sin scripts y lista los adjuntos con su tamaño', async () => {
    open()
    const frame = await screen.findByTitle('Cómo llegará el correo')
    expect(frame).toHaveAttribute('sandbox', 'allow-same-origin')
    expect(frame.getAttribute('sandbox')).not.toContain('allow-scripts')
    expect(frame).toHaveAttribute('srcdoc', '<script>alert(1)</script><p>Hola</p>')
    expect(screen.getByText(/firma-OV-00012\.png/)).toHaveTextContent('(1.5 KB)')
    expect(screen.getByText(/grande\.bin/)).toHaveTextContent('(3.0 MB)')
    expect(screen.getByText('Asunto es')).toBeInTheDocument()
  })

  it('sin adjuntos lo dice, y cambiar el idioma pide el texto en inglés', async () => {
    mock.mode = 'empty'
    const user = userEvent.setup()
    open()
    expect(await screen.findByText('Sin adjuntos')).toBeInTheDocument()
    await user.selectOptions(screen.getByLabelText('Idioma'), 'en')
    await waitFor(() => expect(mock.requests.at(-1)!.body).toMatchObject({ lang: 'en', subject: 'Subject en', body: 'Body', channel: 'EMAIL', reference: null }))
    expect(screen.queryByLabelText('Para quién')).toBeNull()
  })

  it('el error del API se muestra tal cual y sin marco', async () => {
    mock.mode = 'fail'
    open()
    expect(await screen.findByRole('alert')).toHaveTextContent('Este evento no admite vista previa con un documento real.')
    expect(within(document.body).queryByTitle('Cómo llegará el correo')).toBeNull()
  })
})
