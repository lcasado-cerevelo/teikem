// Lote 99 — documento legal único: pantalla de aceptación (botón solo al llegar al final), compuerta y lista de aceptaciones, sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import { LegalAcceptancesTab } from './LegalAcceptancesTab'
import { LegalGate } from './LegalGate'
import { acceptanceStatusParam, reachedEnd } from './legalRules'

const mock = vi.hoisted(() => ({ requests: [] as { method: string; path: string; body: unknown; query: string }[], required: true, logout: vi.fn() }))

vi.mock('../../app/session', () => ({
  useSession: () => ({ lang: 'es', setLang: () => undefined, logout: mock.logout }),
}))
vi.mock('../../app/LangSelect', () => ({ LangSelect: () => null }))
vi.mock('../../kernel/ui/Brand', () => ({ BrandLockup: () => null }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, body, query: url.search })
    const p = url.pathname
    if (req.method === 'GET' && p === '/api/v1/legal/status') return json(mock.required ? { currentVersion: '2026-10-17', required: true, accepted: false } : { currentVersion: '2026-10-17', required: false, accepted: true })
    if (req.method === 'GET' && p === '/api/v1/legal/current') return json(DOC)
    if (req.method === 'POST' && p === '/api/v1/legal/accept') {
      mock.required = false
      return json({ currentVersion: '2026-10-17', required: false, accepted: true, acceptedVersion: '2026-10-17' })
    }
    if (req.method === 'GET' && p === '/api/v1/legal/acceptances') return json({ items: ROWS, total: 3, skip: 0, take: 25 })
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}
const DOC = {
  version: '2026-10-17', lang: 'es', title: 'Política de privacidad y términos de uso', intro: 'Teikem es una plataforma de logística.', partPrivacy: 'Parte 1 — Política de privacidad', partTerms: 'Parte 2 — Términos de uso', required: true,
  sections: [
    { id: 'p1', part: 'privacy', heading: '1. Quiénes somos', paragraphs: ['Texto uno.'] },
    { id: 't1', part: 'terms', heading: '13. Aceptación', paragraphs: ['Texto trece.'] },
  ],
}
const ROWS = [
  { fullName: 'Beto', email: 'beto@x.co', accepted: false, lastVersion: '2020-01-01' },
  { fullName: 'Carla', email: 'carla@x.co', accepted: false },
  { fullName: 'Ana', email: 'ana@x.co', accepted: true, acceptedAtUtc: '2026-10-17T15:00:00Z' },
]

function wrap(ui: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>)
}

/** jsdom no mide: se simula un texto largo (alto 1000, ventana 200) para probar el desplazamiento. */
function longText() {
  vi.spyOn(HTMLElement.prototype, 'scrollHeight', 'get').mockReturnValue(1000)
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(200)
}

describe('reglas del documento legal', () => {
  it('llegar al final: con margen, y un texto que cabe cuenta como leído', () => {
    expect(reachedEnd({ scrollTop: 0, clientHeight: 200, scrollHeight: 1000 })).toBe(false)
    expect(reachedEnd({ scrollTop: 795, clientHeight: 200, scrollHeight: 1000 })).toBe(true)
    expect(reachedEnd({ scrollTop: 0, clientHeight: 300, scrollHeight: 300 })).toBe(true)
    expect(acceptanceStatusParam('')).toBeUndefined()
    expect(acceptanceStatusParam('pending')).toBe('pending')
  })
})

describe('compuerta y pantalla de aceptación', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.required = true
    mock.logout.mockReset()
  })
  afterEach(() => vi.restoreAllMocks())

  it('sin aceptar sale el documento y «Acepto» se habilita solo al llegar al final; aceptar deja pasar a la aplicación', async () => {
    longText()
    const user = userEvent.setup()
    wrap(<LegalGate><p>La aplicación</p></LegalGate>)
    expect(await screen.findByText('Parte 1 — Política de privacidad')).toBeInTheDocument()
    expect(screen.getByText('Parte 2 — Términos de uso')).toBeInTheDocument()
    expect(screen.queryByText('La aplicación')).not.toBeInTheDocument()
    const accept = screen.getByRole('button', { name: 'Acepto la política de privacidad y los términos de uso' })
    expect(accept).toBeDisabled()
    expect(screen.getByText('Desplácese hasta el final del texto para poder aceptar.')).toBeInTheDocument()

    const box = screen.getByTestId('legal-scroll')
    fireEvent.scroll(box, { target: { scrollTop: 300 } })
    expect(accept).toBeDisabled()
    fireEvent.scroll(box, { target: { scrollTop: 800 } })
    await waitFor(() => expect(accept).toBeEnabled())

    await user.click(accept)
    await waitFor(() => expect(screen.getByText('La aplicación')).toBeInTheDocument())
    expect(mock.requests.find((r) => r.method === 'POST')?.body).toEqual({ version: '2026-10-17', lang: 'es' })
  })

  it('«No acepto» cierra la sesión sin mandar nada', async () => {
    longText()
    const user = userEvent.setup()
    wrap(<LegalGate><p>La aplicación</p></LegalGate>)
    await user.click(await screen.findByRole('button', { name: 'No acepto, salir' }))
    expect(mock.logout).toHaveBeenCalledTimes(1)
    expect(mock.requests.some((r) => r.method === 'POST')).toBe(false)
  })

  it('quien ya aceptó entra directo a la aplicación', async () => {
    mock.required = false
    wrap(<LegalGate><p>La aplicación</p></LegalGate>)
    expect(await screen.findByText('La aplicación')).toBeInTheDocument()
    expect(mock.requests.some((r) => r.path === '/api/v1/legal/current')).toBe(false)
  })
})

describe('lista de aceptaciones', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
  })

  it('muestra quién aceptó y quién no, y filtra por estado en el servidor', async () => {
    const user = userEvent.setup()
    wrap(<LegalAcceptancesTab />)
    expect(await screen.findByText('Carla')).toBeInTheDocument()
    expect(screen.getByText('Versión anterior (2020-01-01)')).toBeInTheDocument()
    expect(screen.getAllByText('Pendiente').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Aceptó').length).toBeGreaterThan(0)
    await user.selectOptions(screen.getByLabelText('Estado'), 'pending')
    await waitFor(() => expect(mock.requests.some((r) => r.path === '/api/v1/legal/acceptances' && r.query.includes('status=pending'))).toBe(true))
  })
})
