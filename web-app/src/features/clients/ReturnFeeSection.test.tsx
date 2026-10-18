// Lote 115 — Contrato → «Cargo por devolución»: reglas del formulario y la sección contra un fetch simulado (modos, montos, tipos de paquete, solo lectura).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import type { ContractDetail } from './contractRules'
import { ReturnFeeSection } from './ReturnFeeSection'
import { parseFeeAmount, returnFeeError, returnFeeFormOf, toReturnFeeRequest } from './returnFeeRules'

const mock = vi.hoisted(() => ({ requests: [] as { method: string; path: string; body: unknown }[] }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, body })
    if (url.pathname === '/api/v1/catalogs/PackageType') {
      return json([
        { id: 1, entity: 'PackageType', code: 'BOX', label: 'Caja', labels: { es: 'Caja' }, sortOrder: 1, isSystem: true, isEnabled: true, isOverridden: false, isActive: true },
        { id: 2, entity: 'PackageType', code: 'ENV', label: 'Sobre', labels: { es: 'Sobre' }, sortOrder: 2, isSystem: true, isEnabled: true, isOverridden: false, isActive: true },
      ])
    }
    if (req.method === 'PATCH' && url.pathname === '/api/v1/contracts/k-1/return-fee') return json({ ...CONTRACT, returnFee: null })
    return new Response('{}', { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const CONTRACT = { publicId: 'k-1', id: 1, canEdit: true, returnFee: null } as unknown as ContractDetail

function wrap(contract: ContractDetail = CONTRACT, canWrite = true) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <ReturnFeeSection contract={contract} clientPublicId="c-1" canWrite={canWrite} />
    </QueryClientProvider>,
  )
}

describe('returnFeeRules', () => {
  it('lee montos con coma o punto y rechaza negativos', () => {
    expect(parseFeeAmount('4,5')).toBe(4.5)
    expect(parseFeeAmount('')).toBeNull()
    expect(parseFeeAmount('-1')).toBeNaN()
  })
  it('valida según el modo', () => {
    expect(returnFeeError({ mode: '', amount: '', rows: [] })).toBeNull()
    expect(returnFeeError({ mode: 'ORDER', amount: '', rows: [] })).toBe('amountRequired')
    expect(returnFeeError({ mode: 'PIECE', amount: 'x', rows: [] })).toBe('badAmount')
    expect(returnFeeError({ mode: 'PIECE', amount: '3', rows: [] })).toBeNull()
    expect(returnFeeError({ mode: 'PACKAGE_TYPE', amount: '', rows: [] })).toBe('byTypeRequired')
    expect(returnFeeError({ mode: 'PACKAGE_TYPE', amount: '', rows: [{ packageType: '', amount: '5' }] })).toBe('typeRequired')
    expect(returnFeeError({ mode: 'PACKAGE_TYPE', amount: '', rows: [{ packageType: 'BOX', amount: '5' }, { packageType: 'BOX', amount: '6' }] })).toBe('duplicateType')
    expect(returnFeeError({ mode: 'PACKAGE_TYPE', amount: '', rows: [{ packageType: 'BOX', amount: '5' }] })).toBeNull()
  })
  it('arma la solicitud por modo y lee la que viene del servidor', () => {
    expect(toReturnFeeRequest({ mode: '', amount: '5', rows: [] })).toEqual({ mode: null, amount: null, byType: null })
    expect(toReturnFeeRequest({ mode: 'ORDER', amount: '12,5', rows: [] })).toEqual({ mode: 'ORDER', amount: 12.5, byType: null })
    expect(toReturnFeeRequest({ mode: 'PACKAGE_TYPE', amount: '', rows: [{ packageType: 'BOX', amount: '5' }] })).toEqual({ mode: 'PACKAGE_TYPE', amount: null, byType: [{ packageType: 'BOX', amount: 5 }] })
    expect(returnFeeFormOf({ mode: 'PIECE', amount: 3, byType: [] })).toEqual({ mode: 'PIECE', amount: '3', rows: [] })
    expect(returnFeeFormOf(null).mode).toBe('')
  })
})

describe('Cargo por devolución (contrato)', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
  })

  it('por pieza: pide el monto y manda el modo y el monto', async () => {
    const user = userEvent.setup()
    wrap()
    await user.selectOptions(screen.getByLabelText('Cargo por devolución'), 'PIECE')
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    expect(await screen.findByText('Indique el monto del cargo por devolución.')).toBeInTheDocument()
    expect(mock.requests.some((r) => r.method === 'PATCH')).toBe(false)
    await user.type(screen.getByLabelText('Monto por cada pieza devuelta'), '4')
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'PATCH')).toBe(true))
    expect(mock.requests.find((r) => r.method === 'PATCH')?.body).toEqual({ mode: 'PIECE', amount: 4, byType: null })
  })

  it('por tipo de paquete: agrega filas con su monto', async () => {
    const user = userEvent.setup()
    wrap()
    await user.selectOptions(screen.getByLabelText('Cargo por devolución'), 'PACKAGE_TYPE')
    await user.click(screen.getByRole('button', { name: 'Agregar tipo de paquete' }))
    await screen.findByRole('option', { name: 'Caja' })
    await user.selectOptions(screen.getByLabelText('Tipo de paquete'), 'BOX')
    await user.type(screen.getByLabelText('Monto'), '7')
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'PATCH')).toBe(true))
    expect(mock.requests.find((r) => r.method === 'PATCH')?.body).toEqual({ mode: 'PACKAGE_TYPE', amount: null, byType: [{ packageType: 'BOX', amount: 7 }] })
  })

  it('muestra lo configurado y sin permiso de escritura es solo lectura', async () => {
    wrap({ ...CONTRACT, returnFee: { mode: 'ORDER', amount: 12, byType: [] } } as unknown as ContractDetail, false)
    expect(screen.getByLabelText('Cargo por devolución')).toHaveValue('ORDER')
    expect(screen.getByLabelText('Monto por orden devuelta')).toHaveValue('12')
    expect(screen.getByLabelText('Cargo por devolución')).toBeDisabled()
    expect(screen.queryByRole('button', { name: 'Guardar' })).toBeNull()
  })
})
