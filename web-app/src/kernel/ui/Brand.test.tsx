// Marca Teikem (Lote F8a P7): el lockup elige su archivo por idioma y tema, y cambia sin desmontarse.
import { act, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { setLang } from '../i18n/i18n'
import { BrandLockup, BrandMark } from './Brand'
import { BRAND_SYMBOL_SRC, brandLockupSrc } from './brandAssets'
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
