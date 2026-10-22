// Barra superior de escritorio: qué sección queda activa según la ruta, y dónde sale «← Atrás».
jest.mock('expo-router', () => ({ usePathname: () => '/home', useRouter: () => ({ push: jest.fn(), back: jest.fn(), canGoBack: () => true }) }))

import { isActive } from './TopBar'
import { isSectionPath } from './WideBack'

describe('TopBar', () => {
  it('la sección activa es la de la ruta y la de sus detalles', () => {
    expect(isActive({ route: '/orders', also: ['/order'] }, '/orders')).toBe(true)
    expect(isActive({ route: '/orders', also: ['/order'] }, '/order/abc')).toBe(true)
    expect(isActive({ route: '/orders', also: ['/order'] }, '/order/new')).toBe(true)
    expect(isActive({ route: '/home', also: ['/invoices', '/invoice'] }, '/invoice/x')).toBe(true)
    expect(isActive({ route: '/home', also: ['/invoices'] }, '/orders')).toBe(false)
    expect(isActive({ route: '/order' as never }, '/orders')).toBe(false)   // «/orders» no es un detalle de «/order»
  })
})

describe('WideBack', () => {
  it('las secciones y las pantallas de entrada no llevan «Atrás»; los detalles sí', () => {
    for (const p of ['/', '/home', '/orders', '/reports', '/login', '/accept-invite']) expect(isSectionPath(p)).toBe(true)
    for (const p of ['/order/abc', '/order/new', '/invoice/x', '/profile']) expect(isSectionPath(p)).toBe(false)
  })
})
