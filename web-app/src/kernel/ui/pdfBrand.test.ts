// Colores de la compañía en los PDF: lectura de las variables de la marca, mezclas y el color del texto sobre la barra.
import { afterEach, describe, expect, it } from 'vitest'
import { DEFAULT_PDF_BRAND, mixRgb, onColor, parseCssColor, pdfBrandFrom, readPdfBrand } from './pdfBrand'

afterEach(() => {
  document.documentElement.removeAttribute('style')
})

describe('parseCssColor', () => {
  it('entiende hex corto y largo, rgb() y rgba(); lo demás es null', () => {
    expect(parseCssColor('#031F49')).toEqual([3, 31, 73])
    expect(parseCssColor(' #f60 ')).toEqual([255, 102, 0])
    expect(parseCssColor('rgb(10, 20, 30)')).toEqual([10, 20, 30])
    expect(parseCssColor('rgba(243,113,35,.14)')).toEqual([243, 113, 35])
    expect(parseCssColor('')).toBeNull()
    expect(parseCssColor('rojo')).toBeNull()
    expect(parseCssColor(undefined)).toBeNull()
  })
})

describe('colores derivados', () => {
  it('mezcla y elige el texto legible sobre la barra', () => {
    expect(mixRgb([0, 0, 0], [255, 255, 255], 0.5)).toEqual([128, 128, 128])
    expect(onColor([3, 31, 73])).toEqual([255, 255, 255])
    expect(onColor([250, 240, 200])[0]).toBeLessThan(100)
    const b = pdfBrandFrom([3, 31, 73], [243, 113, 35], [108, 117, 125])
    expect(b.tint[0]).toBeLessThan(b.zebra[0])   // la banda es más marcada (más oscura) que la cebra
    expect(b.zebra[0]).toBeGreaterThan(230)
    expect(b.moneyInk[0]).toBeLessThan(b.money[0])
  })
})

describe('readPdfBrand', () => {
  it('sin variables de marca (pruebas o marca de siempre sin sobrescribir) valen los colores de siempre', () => {
    expect(readPdfBrand()).toBe(DEFAULT_PDF_BRAND)
  })

  it('toma la barra, el acento y el tercer color de la marca de la compañía', () => {
    const root = document.documentElement
    root.style.setProperty('--navbar', '#7A1F2B')
    root.style.setProperty('--on-navbar', '#FFFFFF')
    root.style.setProperty('--flow', '#2E9E6B')
    root.style.setProperty('--money', '#C69214')
    const b = readPdfBrand()
    expect(b.navy).toEqual([122, 31, 43])
    expect(b.flow).toEqual([46, 158, 107])
    expect(b.money).toEqual([198, 146, 20])
    expect(b.onNavy).toEqual([255, 255, 255])
  })

  it('lo que la marca no define queda con el color de siempre', () => {
    document.documentElement.style.setProperty('--navbar', '#7A1F2B')
    const b = readPdfBrand()
    expect(b.navy).toEqual([122, 31, 43])
    expect(b.flow).toEqual(DEFAULT_PDF_BRAND.flow)
  })
})
