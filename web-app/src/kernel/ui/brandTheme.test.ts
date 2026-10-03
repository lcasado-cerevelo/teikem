// Marca de la compañía (portada de la maqueta): contraste WCAG, matiz, presets que pasan las validaciones, derivación del
// tema, BrandingJson y vista previa sobre <html>.
import { afterEach, describe, expect, it } from 'vitest'
import { applyBrandVars } from './brandPreview'
import {
  BRAND_CSS_VARS,
  BRAND_PRESETS,
  brandChecks,
  brandChecksPass,
  brandColors,
  brandCssVars,
  contrastRatio,
  DEFAULT_BRAND,
  deriveAccent,
  deriveSurfaces,
  hexToRgb,
  hslToHex,
  hueDistance,
  isDefaultBrand,
  isValidHex,
  normalizeHex,
  parseBranding,
  rgbToHsl,
  serializeBranding,
  type BrandSettings,
} from './brandTheme'

afterEach(() => {
  document.documentElement.removeAttribute('style')
})

describe('colores', () => {
  it('hex, rgb y hsl de ida y vuelta', () => {
    expect(isValidHex('#1F6FE5')).toBe(true)
    expect(isValidHex('abc')).toBe(true)
    expect(isValidHex('#12')).toBe(false)
    expect(normalizeHex('abc')).toBe('#AABBCC')
    expect(hexToRgb('#1F6FE5')).toEqual({ r: 31, g: 111, b: 229 })
    const { h, s, l } = rgbToHsl(31, 111, 229)
    expect(hslToHex(h, s, l)).toBe('#1F6FE5')
  })

  it('contraste WCAG y distancia de matiz', () => {
    expect(contrastRatio('#000000', '#FFFFFF')).toBeCloseTo(21, 5)
    expect(contrastRatio('#777777', '#777777')).toBeCloseTo(1, 5)
    expect(Math.round(hueDistance('#FF0000', '#00FF00'))).toBe(120)
    expect(Math.round(hueDistance('#FF0000', '#FF0080'))).toBe(30)
  })

  it('la variante -2 aclara en oscuro y oscurece en claro', () => {
    const base = rgbToHsl(31, 111, 229).l
    const dark = deriveAccent('#1F6FE5', 'dark').v2
    const light = deriveAccent('#1F6FE5', 'light').v2
    const lum = (hex: string) => {
      const { r, g, b } = hexToRgb(hex)
      return rgbToHsl(r, g, b).l
    }
    expect(lum(dark)).toBeGreaterThan(base)
    expect(lum(light)).toBeLessThan(base)
  })
})

describe('temas', () => {
  it('los 13 temas de la maqueta pasan todas las validaciones (contraste en los dos modos y matiz ≥ 40°)', () => {
    expect(BRAND_PRESETS).toHaveLength(13)
    for (const p of BRAND_PRESETS) {
      const b: BrandSettings = { preset: p.id, useCustom: false, custom: DEFAULT_BRAND.custom }
      expect(brandChecksPass(b), p.id).toBe(true)
    }
  })

  it('el tema Teikem derivado reproduce (±2 por canal) la paleta original de tokens.css', () => {
    const close = (a: string, b: string) => {
      const x = hexToRgb(a)
      const y = hexToRgb(b)
      return Math.max(Math.abs(x.r - y.r), Math.abs(x.g - y.g), Math.abs(x.b - y.b))
    }
    const dark = deriveSurfaces(222, 0.39, 'dark')
    expect(close(dark['--panel'], '#141B2E')).toBeLessThanOrEqual(2)
    expect(close(dark['--text'], '#E8ECF5')).toBeLessThanOrEqual(2)
    const light = deriveSurfaces(222, 0.39, 'light')
    expect(light['--panel']).toBe('#FFFFFF')
    expect(close(light['--text'], '#182136')).toBeLessThanOrEqual(2)
  })

  it('colores propios que fallan: acentos del mismo matiz y texto atenuado ilegible', () => {
    const b: BrandSettings = { preset: 'teikem', useCustom: true, custom: { flow: '#1F6FE5', money: '#2B7BF0', neutral: '#2B3A5C' } }
    const checks = brandChecks(b)
    const hue = checks.find((c) => c.type === 'hue')
    expect(hue?.pass).toBe(false)
    expect(brandChecksPass(b)).toBe(false)
    // el neutro propio se acota a 0.45 de saturación
    expect(brandColors({ ...b, custom: { ...b.custom, neutral: '#FF0000' } }).ns).toBe(0.45)
    // los contrastes llevan modo, razón redondeada a 2 decimales y su mínimo
    const text = checks.find((c) => c.type === 'contrast' && c.key === 'text' && c.mode === 'dark')
    expect(text).toMatchObject({ min: 7, pass: true })
  })

  it('la marca de siempre no sobrescribe nada; otro tema sí (sin los colores de estado)', () => {
    expect(isDefaultBrand(DEFAULT_BRAND)).toBe(true)
    expect(brandCssVars(DEFAULT_BRAND, 'dark')).toEqual({})
    const vars = brandCssVars({ ...DEFAULT_BRAND, preset: 'bosque' }, 'light')
    expect(vars['--flow']).toBe('#1E8E5A')
    expect(vars['--panel']).toBe('#FFFFFF')
    expect(Object.keys(vars)).not.toContain('--ok')
    expect(Object.keys(vars)).not.toContain('--danger')
    expect(BRAND_CSS_VARS).toContain('--money-2')
  })

  it('applyBrandVars escribe en <html> y las quita al volver a la marca de siempre', () => {
    const el = document.documentElement
    applyBrandVars(el, { ...DEFAULT_BRAND, preset: 'vino' }, 'dark')
    expect(el.style.getPropertyValue('--flow')).toBe('#9E2F4F')
    applyBrandVars(el, DEFAULT_BRAND, 'dark')
    expect(el.style.getPropertyValue('--flow')).toBe('')
  })
})

describe('BrandingJson', () => {
  it('lee con valores por defecto lo que no entiende y al guardar solo escribe las claves conocidas', () => {
    expect(parseBranding(null)).toEqual(DEFAULT_BRAND)
    expect(parseBranding('no es json')).toEqual(DEFAULT_BRAND)
    expect(parseBranding('{"preset":"nada","useCustom":true,"custom":{"flow":"#abc","money":"zz"}}')).toEqual({
      preset: 'teikem',
      useCustom: true,
      custom: { flow: '#AABBCC', money: '#FF6A1A', neutral: '#2B3A5C' },
    })
    const json = serializeBranding({ ...DEFAULT_BRAND, preset: 'acero' })
    expect(JSON.parse(json)).toEqual({ preset: 'acero', useCustom: false, custom: DEFAULT_BRAND.custom })
    expect(parseBranding(json).preset).toBe('acero')
  })
})
