import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../access/AccessProvider'
import { setLang } from '../i18n/i18n'
import { useSaveCustomFields } from './api'
import { CustomFieldsForm } from './CustomFieldsForm'

const mock = vi.hoisted(() => ({
  put: null as unknown,
  calls: [] as string[],
  putResponse: (): Response => new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json' } }),
}))
vi.mock('../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/client')>()
  const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  const fetch = async (req: Request) => {
    const path = new URL(req.url).pathname
    mock.calls.push(`${req.method} ${path}`)
    if (req.method === 'PUT') {
      mock.put = await req.json()
      return mock.putResponse()
    }
    if (path === '/api/v1/custom-fields/definitions') return json(DEFINITIONS)
    if (path === '/api/v1/custom-fields/values/CLIENT/5') return json(VALUES)
    if (path === '/api/v1/catalogs/Region') return json([{ code: 'NORTE', label: 'Norte', sortOrder: 1, isEnabled: true }])
    return new Response(JSON.stringify({ title: 'No encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const DEFINITIONS = [
  { id: 1, fieldKey: 'cost_center', label: 'Centro de costo', dataType: 'TEXT', isRequired: true, validationJson: '{"regex":"^CC-\\\\d{3}$"}', sortOrder: 1, isActive: true },
  { id: 2, fieldKey: 'pallets', label: 'Paletas', dataType: 'NUMBER', validationJson: '{"min":0,"max":50}', sortOrder: 2, isActive: true },
  { id: 3, fieldKey: 'fragile', label: 'Frágil', dataType: 'BOOL', sortOrder: 3, isActive: true },
  { id: 4, fieldKey: 'tier', label: 'Nivel', dataType: 'SELECT', sortOrder: 4, isActive: true, options: [
    { value: 'GOLD', label: 'Oro', sortOrder: 1, isActive: true },
    { value: 'OLD', label: 'Viejo', sortOrder: 2, isActive: false },
  ] },
  { id: 5, fieldKey: 'docs', label: 'Documentos', dataType: 'MULTISELECT', sortOrder: 5, isActive: true, options: [
    { value: 'BOL', label: 'Conduce', sortOrder: 1, isActive: true },
    { value: 'INV', label: 'Factura', sortOrder: 2, isActive: true },
  ] },
  { id: 6, fieldKey: 'region', label: 'Región', dataType: 'LOOKUP_REF', refEntity: 'Region', sortOrder: 6, isActive: true },
]

const VALUES = [
  { fieldKey: 'cost_center', dataType: 'TEXT', value: 'CC-100' },
  { fieldKey: 'pallets', dataType: 'NUMBER', value: 4 },
  { fieldKey: 'fragile', dataType: 'BOOL', value: true },
  { fieldKey: 'tier', dataType: 'SELECT', value: 'GOLD' },
  { fieldKey: 'docs', dataType: 'MULTISELECT', value: ['BOL'] },
  { fieldKey: 'region', dataType: 'LOOKUP_REF', value: null },
]

function Host() {
  const form = useForm<{ name: string; customFields: Record<string, unknown> }>({ defaultValues: { name: 'X', customFields: {} } })
  const { save } = useSaveCustomFields('CLIENT')
  const [result, setResult] = useState('')
  return (
    <form
      onSubmit={form.handleSubmit(async () => {
        const problem = await save(5, form)
        setResult(problem ? `error: ${problem.title}` : 'ok')
      })}
    >
      <CustomFieldsForm entityType="CLIENT" entityId={5} form={form} />
      <button type="submit">guardar</button>
      <output>{result}</output>
    </form>
  )
}

beforeAll(() => setLang('es'))

const ALL_MODULES = ['CUSTOM_FIELDS']

function renderHost(modules: string[] = ALL_MODULES) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <AccessProvider permissions={[]} modules={modules}>
        <Host />
      </AccessProvider>
    </QueryClientProvider>,
  )
}

describe('CustomFieldsForm', () => {
  it('pinta cada tipo con su valor, valida en cliente y guarda con PUT tipado', async () => {
    const user = userEvent.setup()
    renderHost()
    const cost = await screen.findByLabelText('Centro de costo *')
    await waitFor(() => expect(cost).toHaveValue('CC-100'))
    expect(screen.getByLabelText('Paletas')).toHaveValue('4')
    expect(screen.getByLabelText('Sí')).toBeChecked()
    expect(screen.getByLabelText('Nivel')).toHaveValue('GOLD')
    expect(screen.queryByRole('option', { name: 'Viejo' })).toBeNull()
    // MULTISELECT: selección múltiple con buscador (no la lista cruda de casillas)
    const docs = screen.getByLabelText('Documentos')
    expect(docs).toHaveTextContent('Conduce')
    expect(await screen.findByRole('option', { name: 'Norte' })).toBeInTheDocument()

    // Validación en cliente con el DSL (regex) y min/max
    await user.clear(cost)
    await user.type(cost, 'XX')
    await user.clear(screen.getByLabelText('Paletas'))
    await user.type(screen.getByLabelText('Paletas'), '99')
    await user.click(screen.getByRole('button', { name: 'guardar' }))
    expect(await screen.findByText('No cumple el formato requerido.')).toBeInTheDocument()
    expect(screen.getByText('Valor máximo 50.')).toBeInTheDocument()
    expect(mock.put).toBeNull()

    await user.clear(cost)
    await user.type(cost, 'CC-200')
    await user.clear(screen.getByLabelText('Paletas'))
    await user.type(screen.getByLabelText('Paletas'), '12')
    await user.click(docs)
    await user.type(screen.getByRole('searchbox'), 'fact')
    expect(screen.queryByLabelText('Conduce')).toBeNull()
    await user.click(screen.getByLabelText('Factura'))
    await user.keyboard('{Escape}')
    expect(docs).toHaveTextContent('2 elegidos')
    await user.selectOptions(screen.getByLabelText('Región'), 'NORTE')
    await user.click(screen.getByRole('button', { name: 'guardar' }))
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('ok'))
    expect(mock.put).toEqual({
      values: { cost_center: 'CC-200', pallets: 12, fragile: true, tier: 'GOLD', docs: ['BOL', 'INV'], region: 'NORTE' },
    })
  })

  it('los errores del servidor quedan bajo su campo', async () => {
    mock.put = null
    mock.putResponse = () =>
      new Response(
        JSON.stringify({ title: 'Datos inválidos', status: 400, code: 'validation', errors: { cost_center: ['El valor ya existe en otro registro.'] } }),
        { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
      )
    const user = userEvent.setup()
    renderHost()
    await waitFor(async () => expect(await screen.findByLabelText('Centro de costo *')).toHaveValue('CC-100'))
    await user.click(screen.getByRole('button', { name: 'guardar' }))
    expect(await screen.findByText('El valor ya existe en otro registro.')).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('error: Datos inválidos')
  })

  it('con el módulo CUSTOM_FIELDS apagado no consulta, no pinta nada y save() devuelve null', async () => {
    mock.put = null
    mock.calls = []
    const user = userEvent.setup()
    const { container } = renderHost([])
    expect(container.querySelector('.r2, .note, .spin')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'guardar' }))
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('ok'))
    expect(container.querySelector('.r2, .note, .spin')).toBeNull()
    expect(mock.calls).toEqual([])
    expect(mock.put).toBeNull()
  })
})
