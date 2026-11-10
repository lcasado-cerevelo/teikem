// Lote 234 — `useStoredFileUrl`: la URL de objeto de una foto vive lo que viva su entrada en la caché de consultas, no lo que viva un componente. Antes cada
// componente la soltaba al desmontarse aunque otro (la tarjeta de la oferta, otro renglón de la tabla, el pedido) siguiera mostrándola desde la misma caché:
// la imagen salía rota al cambiar la foto de la oferta, al reabrir la ventana o al volver a la pantalla.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useStoredFileUrl } from './api'

const mock = vi.hoisted(() => ({ gets: 0 }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async () => {
    mock.gets++
    return new Response(new Blob(['png'], { type: 'image/png' }), { status: 200, headers: { 'Content-Type': 'image/png' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const FILE = 'aaaaaaaa-0000-0000-0000-000000000001'

function Photo({ id, label }: { id: string; label: string }) {
  const url = useStoredFileUrl(id).data
  return url ? <img src={url} alt={label} /> : null
}

describe('useStoredFileUrl (lote 234)', () => {
  let created: string[]
  let revoked: string[]
  beforeEach(() => {
    mock.gets = 0
    created = []
    revoked = []
    vi.spyOn(URL, 'createObjectURL').mockImplementation(() => {
      const url = `blob:test/${created.length + 1}`
      created.push(url)
      return url
    })
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation((url: string) => void revoked.push(url))
  })
  afterEach(() => vi.restoreAllMocks())

  it('dos componentes comparten la foto: al desmontarse uno, el otro la sigue viendo (la URL no se suelta)', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const view = (both: boolean) => (
      <QueryClientProvider client={client}>
        <Photo id={FILE} label="tarjeta" />
        {both && <Photo id={FILE} label="renglón" />}
      </QueryClientProvider>
    )
    const { rerender, unmount } = render(view(true))
    await waitFor(() => expect(screen.getByAltText('renglón')).toHaveAttribute('src', 'blob:test/1'))
    expect(screen.getByAltText('tarjeta')).toHaveAttribute('src', 'blob:test/1')
    expect(mock.gets).toBe(1) // un solo pedido al servidor para los dos

    rerender(view(false)) // se cierra la ventana del renglón
    expect(screen.getByAltText('tarjeta')).toHaveAttribute('src', 'blob:test/1')
    expect(revoked).toEqual([])

    rerender(view(true)) // se reabre: la misma URL, sin volver a pedirla y todavía viva
    expect(screen.getByAltText('renglón')).toHaveAttribute('src', 'blob:test/1')
    expect(mock.gets).toBe(1)
    expect(revoked).toEqual([])

    // sin nadie mirándola tampoco se suelta mientras siga en la caché (quien vuelva a montarse la recibe de ahí)
    unmount()
    expect(revoked).toEqual([])
    // solo cuando la consulta sale de la caché se libera
    client.removeQueries({ queryKey: ['files', 'blob', FILE] })
    expect(revoked).toEqual(['blob:test/1'])
  })

  it('al volver a pedir el archivo la URL anterior no se suelta de inmediato (las imágenes aún la usan)', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
      <QueryClientProvider client={client}>
        <Photo id={FILE} label="tarjeta" />
      </QueryClientProvider>,
    )
    await waitFor(() => expect(screen.getByAltText('tarjeta')).toHaveAttribute('src', 'blob:test/1'))
    await client.refetchQueries({ queryKey: ['files', 'blob', FILE] })
    await waitFor(() => expect(screen.getByAltText('tarjeta')).toHaveAttribute('src', 'blob:test/2'))
    expect(revoked).toEqual([]) // la anterior se libera 30 s después, no ahora
  })
})
