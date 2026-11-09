// Lote 229 — bloque «Foto» del editor de producto sobre un fetch simulado de `/api/v1/products/{id}/image`: vista de la foto (propia o la de la primera pieza del
// kit), subir (multipart `file`) y quitar con inventory.manage, tipo rechazado antes de enviar, el mensaje del servidor tal cual, sin permiso solo se ve y
// con un producto nuevo solo avisa.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { ProductPhotoBlock } from './ProductPhotoBlock'
import { isProductPhoto } from './offerRules'

interface Call {
  method: string
  path: string
  form: Record<string, unknown> | null
}
const mock = vi.hoisted(() => ({
  calls: [] as Call[],
  forms: [] as Record<string, unknown>[],
  image: { productPublicId: 'p1', sku: 'GUA-1', isKit: false, imagePublicId: null as string | null, effectiveImagePublicId: null as string | null },
  upload: null as null | (() => Response),
}))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const form = req.method === 'POST' ? mock.forms.shift() ?? null : null
    mock.calls.push({ method: req.method, path: url.pathname, form })
    const json = (b: unknown, status = 200) => new Response(JSON.stringify(b), { status, headers: { 'Content-Type': 'application/json' } })
    if (url.pathname === '/api/v1/products/p1/image') {
      if (req.method === 'POST') {
        if (mock.upload) return mock.upload()
        mock.image = { ...mock.image, imagePublicId: 'f-new', effectiveImagePublicId: 'f-new' }
      }
      if (req.method === 'DELETE') mock.image = { ...mock.image, imagePublicId: null, effectiveImagePublicId: null }
      return json(mock.image)
    }
    return new Response('', { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

/** jsdom no arma multipart: cada FormData guarda sus campos y el fetch simulado los toma. */
class RecordingFormData {
  readonly fields: Record<string, unknown> = {}
  constructor() {
    mock.forms.push(this.fields)
  }
  append(k: string, v: unknown) {
    this.fields[k] = v
  }
}

function wrap(permissions: string[], publicId: string | null = 'p1') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <AccessProvider permissions={permissions} modules={['WMS_LOTSERIAL']}>
        <ProductPhotoBlock publicId={publicId} />
      </AccessProvider>
    </QueryClientProvider>,
  )
}

const MANAGE = ['inventory.view', 'inventory.manage']
const png = () => new File(['x'], 'foto.png', { type: 'image/png' })

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.forms = []
  mock.upload = null
  mock.image = { productPublicId: 'p1', sku: 'GUA-1', isKit: false, imagePublicId: null, effectiveImagePublicId: null }
  vi.stubGlobal('FormData', RecordingFormData)
})
afterEach(() => vi.unstubAllGlobals())

describe('ProductPhotoBlock', () => {
  it('sin foto: el recuadro vacío y «Poner foto»; subir manda el archivo y pasa a «Cambiar» / «Quitar»', async () => {
    const user = userEvent.setup()
    wrap(MANAGE)
    expect(await screen.findByText('Sin foto')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Foto' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Quitar foto' })).toBeNull()
    const file = png()
    await user.upload(screen.getByLabelText('Archivo de la foto del producto'), file)
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'POST')).toBe(true))
    expect(mock.calls.find((c) => c.method === 'POST')?.form).toEqual({ file })
    expect(await screen.findByRole('button', { name: 'Cambiar foto' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Quitar foto' }))
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'DELETE')).toBe(true))
    expect(await screen.findByRole('button', { name: 'Poner foto' })).toBeInTheDocument()
  })

  it('un tipo que no es JPG, PNG o WEBP se rechaza antes de enviar', async () => {
    const user = userEvent.setup({ applyAccept: false })
    wrap(MANAGE)
    await screen.findByText('Sin foto')
    await user.upload(screen.getByLabelText('Archivo de la foto del producto'), new File(['x'], 'foto.gif', { type: 'image/gif' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('La foto debe ser una imagen JPG, PNG o WEBP.')
    expect(mock.calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('el mensaje del servidor se muestra tal cual', async () => {
    const user = userEvent.setup()
    mock.upload = () => new Response(JSON.stringify({ title: 'El archivo pasa de 10 MB.', status: 400, code: 'validation' }), { status: 400, headers: { 'Content-Type': 'application/problem+json' } })
    wrap(MANAGE)
    await screen.findByText('Sin foto')
    await user.upload(screen.getByLabelText('Archivo de la foto del producto'), png())
    expect(await screen.findByRole('alert')).toHaveTextContent('El archivo pasa de 10 MB.')
  })

  it('kit sin foto propia: dice que se ve la de su primera pieza y no ofrece quitarla', async () => {
    mock.image = { productPublicId: 'p1', sku: 'KIT-1', isKit: true, imagePublicId: null, effectiveImagePublicId: 'f-piece' }
    wrap(MANAGE)
    expect(await screen.findByText('Sin foto propia: se muestra la de la primera pieza del kit que tiene foto.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Poner foto' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Quitar foto' })).toBeNull()
  })

  it('sin inventory.manage solo se ve; con un producto nuevo avisa que la foto va después de guardar', async () => {
    mock.image = { ...mock.image, imagePublicId: 'f-1', effectiveImagePublicId: 'f-1' }
    const { unmount } = wrap(['inventory.view'])
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'GET')).toBe(true))
    expect(screen.queryByRole('button', { name: /foto/i })).toBeNull()
    expect(screen.queryByLabelText('Archivo de la foto del producto')).toBeNull()
    unmount()
    wrap(MANAGE, null)
    expect(screen.getByText('Guarde el producto para ponerle foto.')).toBeInTheDocument()
  })

  it('isProductPhoto: por tipo o, sin tipo, por extensión', () => {
    expect(isProductPhoto({ type: 'image/jpeg', name: 'a' })).toBe(true)
    expect(isProductPhoto({ type: 'image/webp', name: 'a' })).toBe(true)
    expect(isProductPhoto({ type: 'image/gif', name: 'a.png' })).toBe(false)
    expect(isProductPhoto({ type: '', name: 'FOTO.JPG' })).toBe(true)
    expect(isProductPhoto({ type: '', name: 'foto.bmp' })).toBe(false)
  })
})
