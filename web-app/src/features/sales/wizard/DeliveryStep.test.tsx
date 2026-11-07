// Lote 223 (P3) — paso 3 del pedido guiado: Entrega (por omisión la dirección física del cliente; otra con permiso de consignatarios) o Recogido, el resumen que
// calcula el servidor (subtotal, IVU, cargo de envío con su porqué y total) y la casilla «En consignación» solo con esa forma de pago.
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../../kernel/i18n/i18n'
import type { SalesOrderDto } from '../salesApi'
import { DeliveryStep } from './DeliveryStep'
import type { FakeApi } from './wizardTestApi'
import { StepHarness, WizardProviders } from './wizardTestKit'
import type { WizardState } from './wizardRules'

const mock = vi.hoisted(() => ({ requests: [], route: null }) as FakeApi)

vi.mock('../../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../kernel/api/client')>()
  const { fakeFetch } = await import('./wizardTestApi')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: fakeFetch(mock) }) }
})

const CLIENT = { publicId: 'c-1', code: 'C0001', name: 'Farmacia Central', physicalAddress: { publicId: 'l-phys', line1: 'Calle Sol 12', line2: null, city: 'Bayamón', postalCode: '00961', isActive: true } }
const SAVED = {
  publicId: 'o-1',
  number: 'OV-00042',
  subtotal: 100,
  taxAmount: 11.5,
  total: 125.88,
  terms: { shippingCharge: 12, shippingTaxAmount: 1.38, shippingReason: 'COMPANY_RULE', shippingReasonLabel: 'regla de envío de la compañía: $12.00 por pedido' },
} as unknown as SalesOrderDto

function Step({ initial, onRecalc = () => {}, saved = SAVED, onState }: { initial?: Partial<WizardState>; onRecalc?: (p: Partial<WizardState>) => void; saved?: SalesOrderDto | null; onState?: (s: WizardState) => void }) {
  return (
    <StepHarness initial={{ clientPublicId: 'c-1', orderPublicId: 'o-1', consigneePublicId: 'l-phys', ...initial }} onState={onState}>
      {(state, change) => <DeliveryStep state={state} onChange={change} onRecalc={onRecalc} client={CLIENT as never} saved={saved} recalculating={false} />}
    </StepHarness>
  )
}

describe('DeliveryStep — paso 3 del pedido guiado', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.route = null
  })

  it('con entrega, la dirección física del cliente queda escogida y el resumen trae el envío con su porqué', () => {
    render(<WizardProviders permissions={['sales.view', 'sales.create']}><Step /></WizardProviders>)
    expect(screen.getByRole('radio', { name: /^Entrega/ })).toBeChecked()
    expect(screen.getByRole('radio', { name: /Dirección física del cliente/ })).toBeChecked()
    expect(screen.getByText('Calle Sol 12, Bayamón 00961')).toBeInTheDocument()
    // sin permiso de consignatarios no se ofrece otra dirección
    expect(screen.queryByText('Otra dirección (consignatario)')).not.toBeInTheDocument()
    const summary = screen.getByRole('region', { name: 'Resumen' })
    expect(within(summary).getByText('Subtotal').nextSibling).toHaveTextContent('$100.00')
    expect(within(summary).getByText('IVU').nextSibling).toHaveTextContent('$12.88')
    expect(within(summary).getByText('Cargo de envío').nextSibling).toHaveTextContent('$12.00')
    expect(within(summary).getByText('Por qué: regla de envío de la compañía: $12.00 por pedido')).toBeInTheDocument()
    expect(within(summary).getByText('Total').nextSibling).toHaveTextContent('$125.88')
  })

  it('Recogido vuelve a calcular el envío (guarda el borrador); sin cargo lo dice', async () => {
    const user = userEvent.setup()
    const onRecalc = vi.fn()
    const pickup = { ...SAVED, total: 111.5, terms: { shippingCharge: 0, shippingTaxAmount: 0, shippingReason: 'PICKUP', shippingReasonLabel: 'el cliente lo recoge' } } as unknown as SalesOrderDto
    const { unmount } = render(<WizardProviders permissions={['sales.view', 'sales.create']}><Step onRecalc={onRecalc} /></WizardProviders>)
    await user.click(screen.getByRole('radio', { name: /^Recogido/ }))
    expect(onRecalc).toHaveBeenCalledWith({ deliveryMode: 'PICKUP', consigneePublicId: 'l-phys' })
    unmount()
    render(<WizardProviders permissions={['sales.view', 'sales.create']}><Step initial={{ deliveryMode: 'PICKUP' }} saved={pickup} /></WizardProviders>)
    expect(screen.queryByRole('radio', { name: /Dirección física/ })).not.toBeInTheDocument()
    expect(screen.getByText('Sin cargo')).toBeInTheDocument()
    expect(screen.getByText('Por qué: el cliente lo recoge')).toBeInTheDocument()
  })

  it('con permiso de consignatarios se puede escoger otra dirección', () => {
    render(<WizardProviders><Step /></WizardProviders>)
    expect(screen.getByText('Otra dirección (consignatario)')).toBeInTheDocument()
    expect(screen.getByText('Escoja otra dirección del cliente o cree una nueva.')).toBeInTheDocument()
  })

  it('«En consignación» solo con esa forma de pago; la fecha requerida es opcional', async () => {
    const user = userEvent.setup()
    let last: WizardState | null = null
    const { unmount } = render(<WizardProviders permissions={['sales.view', 'sales.create']}><Step initial={{ paymentMethod: 'CREDIT', paymentTerm: 'NET30' }} /></WizardProviders>)
    expect(screen.queryByRole('switch', { name: 'En consignación' })).not.toBeInTheDocument()
    expect(screen.getByText('Fecha requerida (opcional)')).toBeInTheDocument()
    unmount()
    render(<WizardProviders permissions={['sales.view', 'sales.create']}><Step initial={{ paymentMethod: 'CONSIGNMENT' }} onState={(s) => (last = s)} /></WizardProviders>)
    await user.click(screen.getByRole('switch', { name: 'En consignación' }))
    expect(last!.isConsignment).toBe(true)
  })
})
