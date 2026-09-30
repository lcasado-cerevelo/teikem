// Redirecciones de direcciones viejas (routes.tsx): Lote 13, la ficha del recibo pasa a ser el elegido de la lista.
import { render, screen } from '@testing-library/react'
import type { ComponentType } from 'react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { appRoutes, redirectWithParams } from './routes'

function Where() {
  const { pathname, search, hash } = useLocation()
  return <p data-testid="where">{`${pathname}${search}${hash}`}</p>
}

function renderAt(url: string, path: string, Element: ComponentType) {
  render(
    <MemoryRouter initialEntries={[url]}>
      <Routes>
        <Route path={path} element={<Element />} />
        <Route path="*" element={<Where />} />
      </Routes>
    </MemoryRouter>,
  )
  return screen.getByTestId('where').textContent
}

describe('redirecciones', () => {
  it('/warehouse/receipts/:publicId → /warehouse/receipts?receipt=<publicId> conservando los demás parámetros', () => {
    const route = appRoutes.find((r) => r.path === '/warehouse/receipts/:publicId')!
    const pid = '11111111-1111-1111-1111-111111111111'
    expect(renderAt(`/warehouse/receipts/${pid}?tab=asns&x=1`, route.path, route.element)).toBe(
      `/warehouse/receipts?receipt=${pid}&tab=asns&x=1`,
    )
  })

  it('/warehouse/receipts/:publicId sin consulta', () => {
    const route = appRoutes.find((r) => r.path === '/warehouse/receipts/:publicId')!
    expect(renderAt('/warehouse/receipts/ABC', route.path, route.element)).toBe('/warehouse/receipts?receipt=ABC')
  })

  it('redirectWithParams arma la consulta con los parámetros de la ruta y conserva el #hash', () => {
    const Redirect = redirectWithParams('/destino', (search, params) => {
      const next = new URLSearchParams(search)
      next.set('id', params.id ?? '')
      return next
    })
    expect(renderAt('/viejo/42?a=b#sec', '/viejo/:id', Redirect)).toBe('/destino?a=b&id=42#sec')
  })

  it('redirectWithParams sin consulta resultante: solo la ruta', () => {
    const Redirect = redirectWithParams('/destino', () => new URLSearchParams())
    expect(renderAt('/viejo/42?a=b', '/viejo/:id', Redirect)).toBe('/destino')
  })
})
