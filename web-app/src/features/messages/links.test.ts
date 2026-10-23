// Lote 212 — enlaces pulsables en los mensajes: teléfonos, correos y direcciones web (mismas reglas que la app del chofer).
import { describe, expect, it } from 'vitest'
import { splitLinks, toDialable } from './links'

const kinds = (s: string) => splitLinks(s).map((p) => p.type)
const hrefs = (s: string) => splitLinks(s).flatMap((p) => (p.type === 'text' ? [] : [p.href]))

describe('enlaces en los mensajes', () => {
  it('un texto sin enlaces queda igual', () => {
    expect(kinds('hola')).toEqual(['text'])
    expect(kinds('orden 12345')).toEqual(['text'])
    expect(kinds('entrega 2026-0001 lista')).toEqual(['text'])
  })

  it('teléfonos en sus formas comunes, marcables con +1', () => {
    expect(hrefs('llámame al 787-555-1234 ahora')).toEqual(['tel:+17875551234'])
    expect(hrefs('(787) 555 1234')).toEqual(['tel:+17875551234'])
    expect(hrefs('7875551234')).toEqual(['tel:+17875551234'])
    expect(hrefs('+1 787 555 1234.')).toEqual(['tel:+17875551234'])
  })

  it('direcciones web y correos; la puntuación final no es del enlace', () => {
    expect(hrefs('mira https://teikem.com/ruta?id=3, gracias')).toEqual(['https://teikem.com/ruta?id=3'])
    expect(hrefs('www.teikem.com.')).toEqual(['https://www.teikem.com'])
    expect(hrefs('escribe a ana@empresa.com o llama 787-555-9999')).toEqual(['mailto:ana@empresa.com', 'tel:+17875559999'])
  })

  it('el texto se conserva completo', () => {
    expect(splitLinks('a 787-555-1234 b').map((p) => p.text).join('')).toBe('a 787-555-1234 b')
  })

  it('toDialable', () => {
    expect(toDialable('787.555.1234')).toBe('+17875551234')
    expect(toDialable('1-787-555-1234')).toBe('+17875551234')
    expect(toDialable('+44 20 7946 0958')).toBe('+442079460958')
  })
})

describe('telHref', () => {
  it('con código de país o como se escribió', async () => {
    const { telHref } = await import('./links')
    expect(telHref('7875551234', '1')).toBe('tel:+17875551234')
    expect(telHref('787-555-1234', '+1')).toBe('tel:+17875551234')
    expect(telHref('(787) 555-1234')).toBe('tel:+17875551234')
    expect(telHref('2079460958', '44')).toBe('tel:+442079460958')
  })
})
