/// <reference types="node" />
// Marca de la compañía (portada de la maqueta): contraste WCAG, matiz, presets que pasan las validaciones, derivación del
// tema, BrandingJson y vista previa sobre <html>.
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
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
  DEFAULT_PRESET,
  defaultBrandVars,
  deriveAccent,
  deriveSecondary,
  deriveSurfaces,
  hexToRgb,
  hslToHex,
  hueDistance,
  isDefaultBrand,
  isValidHex,
  normalizeHex,
  ON_DARK,
  headMutedColor,
  HEAD_MUTED_TEXT_SHARE,
  panelHeadColor,
  PANEL_HEAD_MIX,
  onColor,
  parseBranding,
  rgbToHsl,
  serializeBranding,
  validateBrandingJson,
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
  it('los 14 temas (los 13 de la maqueta y el de Advance Logistics) pasan todas las validaciones (contraste en los dos modos y matiz ≥ 40°)', () => {
    expect(BRAND_PRESETS).toHaveLength(14)
    expect(BRAND_PRESETS[0].id).toBe(DEFAULT_PRESET)
    for (const p of BRAND_PRESETS) {
      const b: BrandSettings = { preset: p.id, useCustom: false, custom: DEFAULT_BRAND.custom }
      expect(brandChecksPass(b), p.id).toBe(true)
    }
  })

  it('Lote 138: la paleta por omisión de tokens.css es exactamente la del tema Advance calculado (oscuro y claro)', () => {
    const css = readFileSync(resolve(__dirname, '../../styles/tokens.css'), 'utf8')
    const block = (re: RegExp) => css.match(re)?.[0] ?? ''
    const vars = (b: string) => Object.fromEntries([...b.matchAll(/(--[a-z0-9-]+)\s*:\s*([^;]+);/g)].map((m) => [m[1], m[2].trim()]))
    // sin espacios, mayúsculas, y la opacidad como número (.14 = 0.14)
    const norm = (s: string) =>
      s
        .replace(/\s+/g, '')
        .replace(/rgba\((\d+),(\d+),(\d+),([\d.]+)\)/g, (_a, r, g, bl, a) => `rgba(${r},${g},${bl},${Number(a)})`)
        .toUpperCase()
    for (const [mode, re] of [['dark', /:root\{[^}]*--bg:[^}]*\}/], ['light', /\[data-theme="light"\]\{[^}]*\}/]] as const) {
      const fromCss = { ...vars(block(/:root{[^}]*--bg:[^}]*}/)), ...vars(block(re)) }   // el claro hereda lo que no redefine (--hexd, --hexl)
      for (const [k, v] of Object.entries(defaultBrandVars(mode))) {
        if (k === '--action' || k.startsWith('--action-')) continue   // en tokens.css son var(--flow…): mismo valor por omisión
        expect(norm(fromCss[k] ?? '<falta>'), `${mode} ${k}`).toBe(norm(v))
      }
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

  it('Lote 138b: el botón secundario sale del tono del tema, con letras blancas legibles (AA) y visible contra el panel en los dos modos', () => {
    for (const p of BRAND_PRESETS) {
      for (const mode of ['dark', 'light'] as const) {
        const s = deriveSecondary(p.nh, p.ns, mode)
        expect(contrastRatio(s.on, s.base), `${p.id} ${mode} letras`).toBeGreaterThanOrEqual(4.5)
        expect(contrastRatio(s.on, s.hover), `${p.id} ${mode} letras en hover`).toBeGreaterThanOrEqual(4.5)
        // se distingue del panel (en oscuro con borde más claro: el borde sí se ve)
        const panel = deriveSurfaces(p.nh, p.ns, mode)['--panel']
        expect(contrastRatio(mode === "dark" ? s.line : s.base, panel), `${p.id} ${mode} contra el panel`).toBeGreaterThanOrEqual(1.8)
      }
    }
    // con el tema Advance el secundario claro es el azul marino de su página (#03204B), a ±3 por canal
    const adv = BRAND_PRESETS.find((p) => p.id === 'advance')!
    const got = hexToRgb(deriveSecondary(adv.nh, adv.ns, 'light').base)
    const site = hexToRgb('#03204B')
    expect(Math.max(Math.abs(got.r - site.r), Math.abs(got.g - site.g), Math.abs(got.b - site.b))).toBeLessThanOrEqual(3)
  })

  it('onColor: blanco si llega a AA; si no, el azul marino oscuro (naranja) y blanco sobre el gris', () => {
    expect(onColor('#1F6FE5')).toBe('#FFFFFF')
    expect(onColor('#F37123')).toBe(ON_DARK)
    expect(onColor('#6C757D')).toBe('#FFFFFF')
    for (const c of ['#F37123', '#6C757D', '#1F6FE5', '#7A4FD1']) expect(contrastRatio(onColor(c), c), c).toBeGreaterThanOrEqual(4)
  })

  it('Lote 138: el color de acción es opcional, por omisión el primario, y solo se comprueba cuando es distinto', () => {
    const two: BrandSettings = { preset: 'teikem', useCustom: true, custom: { flow: '#7A4FD1', money: '#D98324', neutral: '#2B3A5C' } }
    expect(brandColors(two).action).toBe('#7A4FD1')
    expect(brandChecks(two)).toHaveLength(9)
    const three: BrandSettings = { ...two, custom: { ...two.custom, action: '#1F6FE5' } }
    expect(brandColors(three).action).toBe('#1F6FE5')
    const checks = brandChecks(three)
    expect(checks).toHaveLength(11)
    expect(checks.slice(0, 5).map((c) => (c.type === 'contrast' ? c.key : c.type))).toEqual(['text', 'muted', 'flow', 'money', 'action'])
    expect(brandChecksPass(three)).toBe(true)
    // la acción negra no se lee sobre el panel oscuro: mismo mensaje que el servidor
    const v = validateBrandingJson('{"useCustom":true,"custom":{"action":"#000000"}}')
    expect(v.code).toBe('contrast')
    expect(v.message).toBe('El contraste del color de acción en modo oscuro es 1.33:1; el mínimo es 4.5:1.')
    // las variables --action* salen del color de acción; sin él, del primario
    expect(brandCssVars(three, 'dark')['--action']).toBe('#1F6FE5')
    expect(brandCssVars(two, 'dark')['--action']).toBe('#7A4FD1')
    expect(BRAND_CSS_VARS).toContain('--action-bg')
    // se guarda solo cuando existe, y se lee de vuelta
    expect(JSON.parse(serializeBranding(two)).custom.action).toBeUndefined()
    expect(parseBranding(serializeBranding(three)).custom.action).toBe('#1F6FE5')
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
      preset: DEFAULT_PRESET,
      useCustom: true,
      custom: { flow: '#AABBCC', money: '#6C757D', neutral: '#03204B' },
    })
    const json = serializeBranding({ ...DEFAULT_BRAND, preset: 'acero' })
    expect(JSON.parse(json)).toEqual({ preset: 'acero', useCustom: false, custom: DEFAULT_BRAND.custom })
    expect(parseBranding(json).preset).toBe('acero')
  })
})

describe('encabezado de tarjetas y paneles (menores, 2026-10-21)', () => {
  it('es el tercer color de la marca casi del todo aclarado: 8 % del gris en el fondo del panel', () => {
    expect([PANEL_HEAD_MIX, HEAD_MUTED_TEXT_SHARE]).toEqual([0.08, 0.25])
    expect(panelHeadColor('#6C757D', '#FFFFFF')).toBe('#F3F4F5')   // claro: casi blanco con un asomo de gris
    expect(panelHeadColor('#6C757D', '#000000')).toBe('#09090A')
  })

  it('el texto (principal y atenuado) conserva el contraste AA sobre el encabezado en TODOS los temas y en los dos modos', () => {
    for (const mode of ['light', 'dark'] as const) {
      for (const p of BRAND_PRESETS) {
        const c = brandColors({ preset: p.id, useCustom: false, custom: DEFAULT_BRAND.custom })
        const surf = deriveSurfaces(c.nh, c.ns, mode)
        const head = panelHeadColor(deriveAccent(c.money, mode).base, surf['--panel'])
        expect(contrastRatio(surf['--text'], head), `${p.id} ${mode} texto`).toBeGreaterThanOrEqual(7)
        expect(contrastRatio(headMutedColor(surf['--muted'], surf['--text']), head), `${p.id} ${mode} atenuado`).toBeGreaterThanOrEqual(4.5)
      }
    }
  })
})

describe('base.css usa las mismas proporciones que las constantes del encabezado', () => {
  it('8 % del tercer color en el fondo y 25 % del texto en lo atenuado', () => {
    const css = readFileSync(resolve(__dirname, '../../styles/base.css'), 'utf8')
    expect(css).toContain(`color-mix(in srgb,var(--money) ${PANEL_HEAD_MIX * 100}%,var(--panel))`)
    expect(css).toContain(`--head-muted:color-mix(in srgb,var(--text) ${HEAD_MUTED_TEXT_SHARE * 100}%,var(--muted))`)
  })
})
