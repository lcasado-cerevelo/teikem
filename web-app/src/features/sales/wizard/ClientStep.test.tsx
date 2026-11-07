// Lote 223 (P3) — paso 1 del pedido guiado: buscador de clientes, Urgente + nota, forma de pago editable con su marca, almacén solo con varios y el alta mínima
// del cliente nuevo (POST /clients/basic + fotos), sobre un fetch simulado.
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../../kernel/i18n/i18n'
import { ClientStep } from './ClientStep'
import { json, recordingFormData, type FakeApi } from './wizardTestApi'
import { StepHarness, WizardProviders } from './wizardTestKit'
import type { WizardState } from './wizardRules'

const mock = vi.hoisted(() => ({ requests: [], route: null, forms: [] }) as FakeApi)

vi.mock('../../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../kernel/api/client')>()
  const { fakeFetch } = await import('./wizardTestApi')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: fakeFetch(mock) }) }
})

const CLIENTS = [
  { id: 1, publicId: 'c-1', code: 'C0001', name: 'Farmacia Central', status: 'ACTIVE', isActive: true },
  { id: 2, publicId: 'c-2', code: 'C0002', name: 'Farmacia Nueva', status: 'PROSPECT', isActive: true },
]

function Step({ initial, onPick, onCreated, needsWarehouse = false, changed = false, onState }: {
  initial?: Partial<WizardState>
  onPick?: (id: string | null, label: string) => void
  onCreated?: (c: unknown) => void
  needsWarehouse?: boolean
  changed?: boolean
  onState?: (s: WizardState) => void
}) {
  return (
    <StepHarness initial={initial} onState={onState}>
      {(state, change) => (
        <ClientStep
          state={state}
          onChange={change}
          onPickClient={(id, label) => onPick?.(id, label)}
          client={state.clientPublicId ? ({ publicId: state.clientPublicId, code: 'C0002', name: 'Farmacia Nueva', status: 'PROSPECT', physicalAddress: { city: 'Bayamón' } } as never) : undefined}
          clientLoading={false}
          paymentChanged={changed}
          needsWarehouse={needsWarehouse}
          onClientCreated={(c) => onCreated?.(c)}
        />
      )}
    </StepHarness>
  )
}

describe('ClientStep — paso 1 del pedido guiado', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    vi.stubGlobal('FormData', recordingFormData(mock))
    mock.requests.length = 0
    mock.forms = []
    mock.route = (r) => {
      if (r.method === 'GET' && r.path === '/api/v1/clients') return CLIENTS.filter((c) => c.name.toLowerCase().includes((r.url.searchParams.get('search') ?? '').toLowerCase()))
      if (r.method === 'POST' && r.path === '/api/v1/clients/basic') return { id: 9, publicId: 'c-9', code: 'C0009', name: (r.body as { name: string }).name, status: 'PROSPECT', paymentTerm: 'NET30' }
      if (r.method === 'GET' && r.path === '/api/v1/postal-localities') return [{ id: 7, city: 'BAYAMON', postalCode: '00961', state: 'PR', countryCode: 'PR', country: 'Puerto Rico', municipality: 'Bayamón' }]
      if (r.method === 'POST' && r.path === '/api/v1/clients/c-9/documents') return json({ publicId: 'd-1' })
      return undefined
    }
  })

  afterEach(() => vi.unstubAllGlobals())

  it('busca por número o nombre y escoge el cliente con «Código · Nombre»; marca al prospecto', async () => {
    const user = userEvent.setup()
    const onPick = vi.fn()
    render(<WizardProviders><Step onPick={onPick} /></WizardProviders>)
    await user.type(screen.getByRole('searchbox', { name: 'Buscar cliente' }), 'farm')
    const list = await screen.findByRole('list', { name: 'Clientes encontrados' })
    expect(within(list).getAllByRole('button')).toHaveLength(2)
    expect(within(list).getByRole('button', { name: /Farmacia Nueva/ })).toHaveTextContent('Prospecto: espera aprobación')
    await user.click(within(list).getByRole('button', { name: /Farmacia Central/ }))
    expect(onPick).toHaveBeenCalledWith('c-1', 'C0001 · Farmacia Central')
    expect(mock.requests.find((r) => r.path === '/api/v1/clients')?.url.searchParams.get('search')).toBe('farm')
  })

  it('sin coincidencias lo dice; sin clients.create_basic no ofrece «Cliente nuevo»', async () => {
    const user = userEvent.setup()
    render(<WizardProviders permissions={['sales.view', 'sales.create', 'clients.read']}><Step /></WizardProviders>)
    await user.type(screen.getByRole('searchbox', { name: 'Buscar cliente' }), 'zzz')
    expect(await screen.findByText('No hay clientes con ese texto.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Cliente nuevo' })).not.toBeInTheDocument()
  })

  it('con el cliente escogido: Urgente, nota con su contador, forma de pago editable (Crédito pide el término) y la marca de cambio', async () => {
    const user = userEvent.setup()
    let last: WizardState | null = null
    render(<WizardProviders><Step initial={{ clientPublicId: 'c-2', clientLabel: 'C0002 · Farmacia Nueva', paymentMethod: 'COD_CASH' }} changed onState={(s) => (last = s)} /></WizardProviders>)
    expect(screen.getByRole('status')).toHaveTextContent('Farmacia Nueva')
    expect(screen.getByRole('status')).toHaveTextContent('Núm. C0002 · Bayamón')
    await user.click(screen.getByRole('switch', { name: 'Pedido urgente' }))
    await user.type(screen.getByRole('textbox', { name: 'Nota del pedido' }), 'Antes de las 10')
    expect(screen.getByText('15 de 500 caracteres')).toBeInTheDocument()
    expect(screen.getByText('Cambió la forma de pago')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('option', { name: 'Crédito' })).toBeInTheDocument())
    await user.selectOptions(screen.getByRole('combobox', { name: 'Forma de pago' }), 'CREDIT')
    await waitFor(() => expect(screen.getByRole('option', { name: 'Neto 30' })).toBeInTheDocument())
    await user.selectOptions(screen.getByRole('combobox', { name: 'Término del crédito' }), 'NET30')
    expect(last).toMatchObject({ isUrgent: true, notes: 'Antes de las 10', paymentMethod: 'CREDIT', paymentTerm: 'NET30' })
    expect(screen.getByRole('textbox', { name: 'Nota del pedido' })).toHaveAttribute('maxLength', '500')
  })

  it('el almacén solo se pide si la compañía tiene más de uno', () => {
    const { unmount } = render(<WizardProviders><Step initial={{ clientPublicId: 'c-2' }} /></WizardProviders>)
    expect(screen.queryByText('Almacén')).not.toBeInTheDocument()
    unmount()
    render(<WizardProviders><Step initial={{ clientPublicId: 'c-2' }} needsWarehouse /></WizardProviders>)
    expect(screen.getByText('De qué almacén sale el pedido.')).toBeInTheDocument()
  })

  it('«Cliente nuevo»: valida el alta mínima y la crea con los teléfonos en dígitos y las fotos después', async () => {
    const user = userEvent.setup()
    const onCreated = vi.fn()
    render(<WizardProviders><Step onCreated={onCreated} /></WizardProviders>)
    await user.click(screen.getByRole('button', { name: 'Cliente nuevo' }))
    const dialog = await screen.findByRole('dialog', { name: 'Cliente nuevo' })
    await user.click(within(dialog).getByRole('button', { name: 'Crear cliente' }))
    expect(await within(dialog).findByText('El nombre del negocio es obligatorio.')).toBeInTheDocument()
    expect(within(dialog).getByText('El teléfono es obligatorio.')).toBeInTheDocument()
    expect(within(dialog).getByText('La dirección física es obligatoria.')).toBeInTheDocument()

    await user.type(within(dialog).getByRole('textbox', { name: /^Nombre del negocio/ }), 'Colmado Luis')
    await user.type(within(dialog).getByRole('textbox', { name: /^Persona de contacto/ }), 'Luis Rivera')
    await user.type(within(dialog).getByRole('textbox', { name: /^Teléfono\b(?! 2)/ }), '7875551234')
    await user.type(within(dialog).getByRole('textbox', { name: /^Línea 1/ }), 'Calle 1')
    await user.type(within(dialog).getByRole('combobox', { name: /^Pueblo/ }), 'Bay')
    await user.click(await screen.findByRole('option', { name: /00961/ }))
    const photo = new File(['x'], 'registro.jpg', { type: 'image/jpeg' })
    await user.upload(within(dialog).getByLabelText('Foto del registro de comerciante'), photo)
    await user.click(within(dialog).getByRole('button', { name: 'Crear cliente' }))

    await waitFor(() => expect(onCreated).toHaveBeenCalled())
    const post = mock.requests.find((r) => r.method === 'POST' && r.path === '/api/v1/clients/basic')
    expect(post?.body).toMatchObject({ name: 'Colmado Luis', contactName: 'Luis Rivera', phone: '7875551234', physicalAddress: { line1: 'Calle 1', city: 'Bayamón', postalCode: '00961' } })
    const doc = mock.requests.find((r) => r.path === '/api/v1/clients/c-9/documents')
    expect(doc?.form).toMatchObject({ documentType: 'MERCHANT_REGISTRATION', file: photo })
  })
})
