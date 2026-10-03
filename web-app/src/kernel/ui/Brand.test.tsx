// Marca Teikem (Lote F8a P7): el lockup elige su archivo por idioma y tema, y cambia sin desmontarse.
import { act, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { setLang } from '../i18n/i18n'
import { BrandLockup, BrandMark } from './Brand'
import { BRAND_SYMBOL_SRC, brandLockupSrc } from './brandAssets'
import { NO_COMPANY_LOGOS, setCompanyLogos } from './brandLogos'
import { setTheme, THEME_STORAGE_KEY } from './theme'

describe('brandLockupSrc (regla brandLogoFor de la maqueta)', () => {
  it('oscuro → variante -inv; claro → normal; el idioma elige el lema', () => {
    expect(brandLockupSrc('es', 'dark')).toBe('/brand/teikem-1b-horizontal-tagline-es-inv.svg')
    expect(brandLockupSrc('es', 'light')).toBe('/brand/teikem-1b-horizontal-tagline-es.svg')
    expect(brandLockupSrc('en', 'dark')).toBe('/brand/teikem-1a-horizontal-tagline-en-inv.svg')
    expect(brandLockupSrc('en', 'light')).toBe('/brand/teikem-1a-horizontal-tagline-en.svg')
  })

  it('sin lema es el mismo archivo en ambos idiomas', () => {
    expect(brandLockupSrc('es', 'dark', false)).toBe('/brand/teikem-2-horizontal-notagline-inv.svg')
    expect(brandLockupSrc('en', 'light', false)).toBe('/brand/teikem-2-horizontal-notagline.svg')
  })
})

describe('BrandLockup / BrandMark', () => {
  afterEach(() => {
    setLang('es')
    setTheme('dark')
    localStorage.removeItem(THEME_STORAGE_KEY)
  })

  it('el src cambia al cambiar idioma y tema, sobre la misma imagen (sin recargar ni desmontar)', () => {
    setLang('es')
    setTheme('dark')
    render(<BrandLockup />)
    const img = screen.getByRole('img', { name: 'Teikem' })
    expect(img).toHaveAttribute('src', '/brand/teikem-1b-horizontal-tagline-es-inv.svg')

    act(() => setLang('en'))
    expect(screen.getByRole('img', { name: 'Teikem' })).toBe(img)
    expect(img).toHaveAttribute('src', '/brand/teikem-1a-horizontal-tagline-en-inv.svg')

    act(() => setTheme('light'))
    expect(screen.getByRole('img', { name: 'Teikem' })).toBe(img)
    expect(img).toHaveAttribute('src', '/brand/teikem-1a-horizontal-tagline-en.svg')

    act(() => setLang('es'))
    expect(img).toHaveAttribute('src', '/brand/teikem-1b-horizontal-tagline-es.svg')
  })

  it('tagline={false} usa el lockup sin lema y conserva la clase extra', () => {
    setTheme('light')
    render(<BrandLockup tagline={false} className="x" />)
    const img = screen.getByTestId('brand-lockup')
    expect(img).toHaveAttribute('src', '/brand/teikem-2-horizontal-notagline.svg')
    expect(img).toHaveClass('brand-lockup', 'x')
  })

  it('BrandMark: el símbolo cuadrado del tamaño pedido (44 por defecto); alt vacío = decorativa', () => {
    const { unmount } = render(<BrandMark />)
    const mark = screen.getByRole('img', { name: 'Teikem' })
    expect(mark).toHaveAttribute('src', BRAND_SYMBOL_SRC)
    expect(BRAND_SYMBOL_SRC).toBe('/brand/teikem-symbol.svg')
    expect(mark).toHaveAttribute('width', '44')
    expect(mark).toHaveAttribute('height', '44')
    unmount()
    render(<BrandMark size={24} alt="" />)
    expect(screen.queryByRole('img', { name: 'Teikem' })).toBeNull()
    expect(screen.getByTestId('brand-mark')).toHaveAttribute('width', '24')
  })
})

describe('logos de la compañía en el lockup y la marca cuadrada', () => {
  afterEach(() => {
    act(() => setCompanyLogos(NO_COMPANY_LOGOS))
    setTheme('dark')
    localStorage.removeItem(THEME_STORAGE_KEY)
  })

  it('con logos propios los usa (invertido en oscuro) y al cambiar de tema cambia de variante sin desmontar', () => {
    setTheme('dark')
    render(<BrandLockup className="brand-full" />)
    expect(screen.getByTestId('brand-lockup')).toHaveAttribute('src', '/brand/teikem-1b-horizontal-tagline-es-inv.svg')

    act(() => setCompanyLogos({ urls: { lockup: 'blob:claro', 'lockup-inverted': 'blob:oscuro' }, name: 'Advance Logistics' }))
    const img = screen.getByTestId('brand-lockup')
    expect(img).toHaveAttribute('src', 'blob:oscuro')
    expect(img).toHaveAttribute('alt', 'Advance Logistics')
    expect(img).toHaveAttribute('data-company-logo', 'true')
    expect(img).toHaveClass('brand-lockup', 'own', 'brand-full')

    act(() => setTheme('light'))
    expect(screen.getByTestId('brand-lockup')).toBe(img)
    expect(img).toHaveAttribute('src', 'blob:claro')

    // quitar los logos: vuelve el de Teikem
    act(() => setCompanyLogos(NO_COMPANY_LOGOS))
    expect(screen.getByTestId('brand-lockup')).toHaveAttribute('src', '/brand/teikem-1b-horizontal-tagline-es.svg')
    expect(screen.getByTestId('brand-lockup')).not.toHaveAttribute('data-company-logo')
  })

  it('con una sola variante la reusa en ambos temas', () => {
    act(() => setCompanyLogos({ urls: { lockup: 'blob:unico' }, name: null }))
    setTheme('dark')
    render(<BrandLockup />)
    expect(screen.getByTestId('brand-lockup')).toHaveAttribute('src', 'blob:unico')
    expect(screen.getByTestId('brand-lockup')).toHaveAttribute('alt', 'Teikem')
    act(() => setTheme('light'))
    expect(screen.getByTestId('brand-lockup')).toHaveAttribute('src', 'blob:unico')
  })

  it('la marca cuadrada usa su propio logo; sin él, el símbolo de Teikem aunque haya lockup', () => {
    render(<BrandMark size={44} alt="" />)
    act(() => setCompanyLogos({ urls: { lockup: 'blob:l' }, name: 'Advance' }))
    expect(screen.getByTestId('brand-mark')).toHaveAttribute('src', BRAND_SYMBOL_SRC)
    act(() => setCompanyLogos({ urls: { lockup: 'blob:l', mark: 'blob:m', 'mark-inverted': 'blob:mi' }, name: 'Advance' }))
    const mark = screen.getByTestId('brand-mark')
    expect(mark).toHaveAttribute('src', 'blob:mi')   // tema oscuro
    expect(mark).toHaveAttribute('alt', '')           // decorativa sigue decorativa
    expect(mark).toHaveAttribute('width', '44')
  })
})
