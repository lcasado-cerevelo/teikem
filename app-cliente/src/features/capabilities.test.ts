// Qué pestañas ve el cliente según lo que ofrece la compañía de la sesión.
jest.mock('../kernel/auth/session', () => ({ useSessionState: () => ({ session: null }) }))
jest.mock('./portalApi', () => ({ getMe: jest.fn() }))

import { DEFAULT_OFFER, landingTab, offerOf, visibleTabs } from './capabilities'

const caps = (o: Partial<{ warehouse: boolean; transport: boolean; canSeeValue: boolean }>) =>
  offerOf({ companyName: 'Cía', warehouse: false, transport: false, canSeeValue: false, ...o })

describe('capabilities', () => {
  it('sin respuesta asume la app de siempre: transporte, sin almacén', () => {
    expect(offerOf(null)).toEqual(DEFAULT_OFFER)
    expect(visibleTabs(DEFAULT_OFFER, false)).toEqual({ home: true, orders: true, warehouse: false })
  })
  it('compañía solo de almacén: solo la pestaña Almacén y se entra por ahí', () => {
    const o = caps({ warehouse: true })
    expect(visibleTabs(o, false)).toEqual({ home: false, orders: false, warehouse: true })
    expect(landingTab(o)).toBe('warehouse')
  })
  it('compañía con ambos: todo y se entra por Inicio', () => {
    const o = caps({ warehouse: true, transport: true })
    expect(visibleTabs(o, false)).toEqual({ home: true, orders: true, warehouse: true })
    expect(landingTab(o)).toBe('home')
  })
  it('el consignatario solo ve Envíos', () => {
    expect(visibleTabs(caps({ warehouse: true, transport: true }), true)).toEqual({ home: false, orders: true, warehouse: false })
  })
  it('el valor depende del rol', () => {
    expect(caps({ warehouse: true, canSeeValue: true }).canSeeValue).toBe(true)
    expect(caps({ warehouse: true }).canSeeValue).toBe(false)
  })
})
