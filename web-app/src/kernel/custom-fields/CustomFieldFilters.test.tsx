// Lote 130: filtros por campo personalizado (solo los marcados «Mostrar en la búsqueda»), del tipo del campo, con las claves de la dirección del API.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../access/AccessProvider'
import { setLang } from '../i18n/i18n'
import { Filters } from '../ui'
import { CustomFieldFilterBar, CustomFieldFilters, customFieldQuery, withCustomFields, type CustomFieldFilterValues } from './CustomFieldFilters'

vi.mock('../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/client')>()
  const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  const fetch = async (req: Request) => {
    const path = new URL(req.url).pathname
    if (path === '/api/v1/custom-fields/definitions') return json(new URL(req.url).searchParams.get('entityType') === 'EMPTY' ? [] : DEFINITIONS)
    return new Response(JSON.stringify({ title: 'No encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const DEFINITIONS = [
  { id: 1, fieldKey: 'cost_center', label: 'Centro de costo', dataType: 'TEXT', showInList: true, isActive: true },
  { id: 2, fieldKey: 'pallets', label: 'Paletas', dataType: 'NUMBER', showInList: true, isActive: true },
  { id: 3, fieldKey: 'fragile', label: 'Frágil', dataType: 'BOOL', showInList: true, isActive: true },
  { id: 4, fieldKey: 'tier', label: 'Nivel', dataType: 'SELECT', showInList: true, isActive: true, options: [
    { value: 'GOLD', label: 'Oro', sortOrder: 1, isActive: true },
    { value: 'OLD', label: 'Viejo', sortOrder: 2, isActive: false },
  ] },
  { id: 5, fieldKey: 'delivered', label: 'Entrega', dataType: 'DATE', showInList: true, isActive: true },
  { id: 6, fieldKey: 'secret', label: 'Secreto', dataType: 'TEXT', showInList: false, isActive: true },
]

beforeAll(() => setLang('es'))

function Host({ entityType = 'TRANSPORT_ORDER', onValue }: { entityType?: string; onValue: (v: CustomFieldFilterValues) => void }) {
  const [value, setValue] = useState<CustomFieldFilterValues>({})
  const change = (v: CustomFieldFilterValues) => {
    setValue(v)
    onValue(v)
  }
  return (
    <Filters>
      <CustomFieldFilters entityType={entityType} value={value} onChange={change} />
    </Filters>
  )
}

function renderIt(ui: React.ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <AccessProvider permissions={[]} modules={['CUSTOM_FIELDS']}>
        {ui}
      </AccessProvider>
    </QueryClientProvider>,
  )
}

describe('CustomFieldFilters', () => {
  it('ofrece un filtro por cada campo con «Mostrar en la búsqueda», del tipo del campo, y manda las claves cf.*', async () => {
    const user = userEvent.setup()
    let last: CustomFieldFilterValues = {}
    renderIt(<Host onValue={(v) => (last = v)} />)

    await user.type(await screen.findByLabelText('Centro de costo'), 'CC-1')
    expect(last['cf.cost_center']).toBe('CC-1')

    await user.type(screen.getByLabelText('Paletas (desde)'), '5')
    await user.type(screen.getByLabelText('Paletas (hasta)'), '10')
    expect(last).toMatchObject({ 'cf.pallets.from': '5', 'cf.pallets.to': '10' })

    await user.selectOptions(screen.getByLabelText('Frágil'), 'true')
    expect(last['cf.fragile']).toBe('true')

    // lista: solo las opciones activas
    expect(screen.queryByRole('option', { name: 'Viejo' })).toBeNull()
    await user.selectOptions(screen.getByLabelText('Nivel'), 'GOLD')
    expect(last['cf.tier']).toBe('GOLD')

    // fecha: filtro de rango de fechas (desplegable de rangos)
    expect(screen.getAllByLabelText('Entrega').length).toBeGreaterThan(0)
    // el campo sin «Mostrar en la búsqueda» no se ofrece
    expect(screen.queryByLabelText('Secreto')).toBeNull()
  })

  it('la barra no pinta nada si la entidad no tiene campos habilitados para la búsqueda', async () => {
    const { container } = renderIt(<CustomFieldFilterBar entityType="EMPTY" value={{}} onChange={() => {}} />)
    await new Promise((r) => setTimeout(r, 50))
    expect(container.querySelector('.filters')).toBeNull()
  })

  it('customFieldQuery deja solo los filtros con valor y withCustomFields los mezcla en la consulta', () => {
    expect(customFieldQuery({ 'cf.a': 'x', 'cf.b': ' ', 'otro': 'y' })).toEqual({ 'cf.a': 'x' })
    expect(withCustomFields({ search: 'q' }, { 'cf.a': 'x', 'cf.b': '' })).toEqual({ search: 'q', 'cf.a': 'x' })
  })
})
