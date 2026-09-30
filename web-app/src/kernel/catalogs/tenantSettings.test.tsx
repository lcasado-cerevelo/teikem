// Configuración de la compañía (`useTenantSettings`) y la etiqueta de un código de catálogo (`lookupLabelOrCode`), que usa
// el empaque para decir "Predeterminado de la compañía (Estándar)". Sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { renderHook, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useTenantSettings } from './api'
import { lookupLabelOrCode, type LookupOption } from './types'

const mock = vi.hoisted(() => ({ urls: [] as string[] }))
vi.mock('../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/client')>()
  const fetch = async (req: Request) => {
    mock.urls.push(new URL(req.url).pathname)
    return new Response(JSON.stringify({ id: 1, name: 'Advance Logistics', defaultServiceType: 'STANDARD', defaultPackageType: null }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const opt = (code: string, label: string): LookupOption => ({ code, label, description: null, sortOrder: 0, isEnabled: true })

beforeEach(() => {
  mock.urls = []
})

describe('useTenantSettings', () => {
  it('lee GET /api/v1/tenant/settings con los predeterminados (código o null)', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>
    const { result } = renderHook(() => useTenantSettings(), { wrapper })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(result.current.data?.defaultServiceType).toBe('STANDARD')
    expect(result.current.data?.defaultPackageType).toBeNull()
    expect(mock.urls).toEqual(['/api/v1/tenant/settings'])
  })

  it('enabled = false no consulta', () => {
    const client = new QueryClient()
    const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>
    renderHook(() => useTenantSettings(false), { wrapper })
    expect(mock.urls).toEqual([])
  })
})

describe('lookupLabelOrCode', () => {
  const options = [opt('STANDARD', 'Estándar'), opt('EXPRESS', 'Expreso')]
  it('etiqueta del código (sin distinguir mayúsculas); desconocido = el código; sin código = null', () => {
    expect(lookupLabelOrCode('STANDARD', options)).toBe('Estándar')
    expect(lookupLabelOrCode('express', options)).toBe('Expreso')
    expect(lookupLabelOrCode('OLD', options)).toBe('OLD')
    expect(lookupLabelOrCode('STANDARD', [])).toBe('STANDARD')
    expect(lookupLabelOrCode(null, options)).toBeNull()
    expect(lookupLabelOrCode('', options)).toBeNull()
  })
})
