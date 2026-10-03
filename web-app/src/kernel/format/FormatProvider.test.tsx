// Proveedor único de formatos: lee GET /tenant/settings, pinta Puerto Rico mientras carga y, al cambiar la consulta (guardar
// Ajustes o invalidarla), toda la app se vuelve a pintar con los formatos nuevos sin desmontar nada.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, waitFor } from '@testing-library/react'
import { useEffect, useRef } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { catalogKeys } from '../catalogs/api'
import { formatMoney } from '../i18n/numberFormat'
import { useLang, useT } from '../i18n/useT'
import { FormatProvider } from './FormatProvider'
import { resetFormatSettings } from './store'
import { useFormat } from './useFormat'

const mock = vi.hoisted(() => ({ settings: {} as Record<string, unknown> }))
vi.mock('../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/client')>()
  const fetch = async () =>
    new Response(JSON.stringify(mock.settings), { status: 200, headers: { 'Content-Type': 'application/json' } })
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

let mounts = 0

/** Pinta con el hook y con las firmas viejas (solo `useLang`), y cuenta los montajes. */
function Probe() {
  const f = useFormat()
  const lang = useLang()
  const t = useT()
  const mounted = useRef(false)
  useEffect(() => {
    if (!mounted.current) mounts++
    mounted.current = true
  }, [])
  return (
    <div>
      <span data-testid="date">{f.date('2026-10-02')}</span>
      <span data-testid="money">{formatMoney(1234.5, lang)}</span>
      <span data-testid="text">{t('ui.table.export.rows', { count: 1234 })}</span>
    </div>
  )
}

afterEach(() => {
  resetFormatSettings()
  mounts = 0
})

describe('FormatProvider', () => {
  it('Puerto Rico mientras carga; luego los ajustes de la compañía; al cambiar la consulta, sin desmontar', async () => {
    mock.settings = { regionCode: 'PR', dateOrder: 'DMY', dateSeparator: '-', thousandsSeparator: '.', decimalSeparator: ',' }
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
      <QueryClientProvider client={client}>
        <FormatProvider enabled>
          <Probe />
        </FormatProvider>
      </QueryClientProvider>,
    )
    expect(screen.getByTestId('date')).toHaveTextContent('10/02/2026')
    await waitFor(() => expect(screen.getByTestId('date')).toHaveTextContent('02-10-2026'))
    expect(screen.getByTestId('money')).toHaveTextContent('$1.234,50')
    expect(screen.getByTestId('text')).toHaveTextContent(/: 1\.234$/)

    // Ajustes guardó otra región: se pone en la caché y todo se vuelve a pintar
    act(() => {
      client.setQueryData(catalogKeys.tenantSettings, { regionCode: 'US', dateOrder: 'YMD', dateSeparator: '.', currencySymbolPosition: 'A' })
    })
    await waitFor(() => expect(screen.getByTestId('date')).toHaveTextContent('2026.10.02'))
    expect(screen.getByTestId('money').textContent).toBe('1,234.50 $')
    expect(mounts).toBe(1)
  })

  it('sin sesión no consulta y vale Puerto Rico', () => {
    mock.settings = { dateOrder: 'DMY' }
    const client = new QueryClient()
    render(
      <QueryClientProvider client={client}>
        <FormatProvider enabled={false}>
          <Probe />
        </FormatProvider>
      </QueryClientProvider>,
    )
    expect(screen.getByTestId('date')).toHaveTextContent('10/02/2026')
  })
})
