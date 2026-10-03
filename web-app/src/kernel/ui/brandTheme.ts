// Marca de la compañía: colores (Ajustes → Marca), portado de la maqueta (`BRAND_PRESETS`, `deriveAccent`, `deriveSurfaces`,
// `brandChecks`, `applyTenantBrand`). Se guarda en `Tenant.BrandingJson` (`PUT /tenant/settings`) y se aplica sobrescribiendo
// las variables CSS de `tokens.css` en <html>. Qué se personaliza y qué no:
// - Marca: los dos acentos `--flow` (operación) y `--money` (dinero). Son semánticos ("dos corrientes"): se valida que sigan
//   siendo distinguibles (≥ 40° de matiz).
// - Estado (`--ok`, `--warn`, `--danger`, `--info`): NO se personaliza (verde = bien, rojo = mal).
// - Superficies: solo por preset o por un "tono base" acotado; se derivan de tono y saturación del neutro.
// Validación: contraste WCAG contra el panel en AMBOS modos (texto ≥ 7, atenuado y acentos ≥ 4.5) y separación de matiz.
// IMPORTANTE: la misma validación debe repetirse en el servidor (hoy `PUT /tenant/settings` solo exige JSON válido ≤ 200 KB).
// Lógica pura.

export type ThemeMode = 'dark' | 'light'

export interface BrandPreset {
  id: string
  nameEs: string
  nameEn: string
  flow: string
  money: string
  /** Tono (0-360) y saturación (0-1) del neutro: lo que hace que el tema se sienta frío, cálido o gris. */
  nh: number
  ns: number
}

/** Los 13 temas de la maqueta; cada uno pasa `brandChecks` (lo comprueba la prueba). */
export const BRAND_PRESETS: readonly BrandPreset[] = [
  { id: 'teikem', nameEs: 'Teikem (por defecto)', nameEn: 'Teikem (default)', flow: '#1F6FE5', money: '#FF6A1A', nh: 222, ns: 0.39 },
  { id: 'marino', nameEs: 'Marino', nameEn: 'Navy', flow: '#1F59A3', money: '#E08A17', nh: 215, ns: 0.42 },
  { id: 'acero', nameEs: 'Acero', nameEn: 'Steel', flow: '#3D6E9C', money: '#D2691E', nh: 210, ns: 0.26 },
  { id: 'carretera', nameEs: 'Carretera', nameEn: 'Highway', flow: '#D32F2F', money: '#0E8F8F', nh: 8, ns: 0.15 },
  { id: 'granate', nameEs: 'Granate', nameEn: 'Crimson', flow: '#B02A45', money: '#0E8F8F', nh: 345, ns: 0.17 },
  { id: 'vino', nameEs: 'Vino', nameEn: 'Burgundy', flow: '#9E2F4F', money: '#C69214', nh: 338, ns: 0.2 },
  { id: 'bosque', nameEs: 'Bosque', nameEn: 'Forest', flow: '#1E8E5A', money: '#D98324', nh: 160, ns: 0.19 },
  { id: 'selva', nameEs: 'Selva', nameEn: 'Jungle', flow: '#0F7A5A', money: '#C8452D', nh: 168, ns: 0.24 },
  { id: 'oliva', nameEs: 'Oliva', nameEn: 'Olive', flow: '#5B7A2E', money: '#C2571C', nh: 92, ns: 0.15 },
  { id: 'turquesa', nameEs: 'Turquesa', nameEn: 'Turquoise', flow: '#0E9AA7', money: '#E2622C', nh: 190, ns: 0.25 },
  { id: 'indigo', nameEs: 'Índigo', nameEn: 'Indigo', flow: '#4F46E5', money: '#EA8C00', nh: 250, ns: 0.3 },
  { id: 'violeta', nameEs: 'Violeta', nameEn: 'Violet', flow: '#7A4FD1', money: '#D98324', nh: 265, ns: 0.24 },
  { id: 'grafito', nameEs: 'Grafito', nameEn: 'Graphite', flow: '#4E5A6E', money: '#C2691C', nh: 220, ns: 0.06 },
]

export const DEFAULT_PRESET = 'teikem'
/** Techo de la saturación del neutro propio (0.45, apenas por encima del 0.39 original). */
export const NEUTRAL_SAT_MAX = 0.45
export const MIN_HUE_DISTANCE = 40

/** Lo que se guarda en `BrandingJson` (otras claves que hubiera se conservan al guardar). */
export interface BrandSettings {
  preset: string
  useCustom: boolean
  custom: { flow: string; money: string; neutral: string }
}

export const DEFAULT_BRAND: BrandSettings = {
  preset: DEFAULT_PRESET,
  useCustom: false,
  custom: { flow: '#1F6FE5', money: '#FF6A1A', neutral: '#2B3A5C' },
}

// ------------------------------------------------------------------ color

export function isValidHex(h: string | null | undefined): boolean {
  return /^#?([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$/.test(String(h ?? ''))
}

/** '#abc' / 'abc' / '#aabbcc' → '#AABBCC'. */
export function normalizeHex(h: string): string {
  let s = h.trim().replace('#', '')
  if (s.length === 3) s = s.split('').map((c) => c + c).join('')
  return `#${s.toUpperCase()}`
}

export function hexToRgb(hex: string): { r: number; g: number; b: number } {
  const n = parseInt(normalizeHex(hex).slice(1), 16)
  return { r: (n >> 16) & 255, g: (n >> 8) & 255, b: n & 255 }
}

export function rgbToHex(r: number, g: number, b: number): string {
  const c = (v: number) => Math.max(0, Math.min(255, Math.round(v))).toString(16).padStart(2, '0')
  return `#${c(r)}${c(g)}${c(b)}`.toUpperCase()
}

export function rgbToHsl(r: number, g: number, b: number): { h: number; s: number; l: number } {
  r /= 255
  g /= 255
  b /= 255
  const mx = Math.max(r, g, b)
  const mn = Math.min(r, g, b)
  let h = 0
  let s = 0
  const l = (mx + mn) / 2
  if (mx !== mn) {
    const d = mx - mn
    s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn)
    h = mx === r ? (g - b) / d + (g < b ? 6 : 0) : mx === g ? (b - r) / d + 2 : (r - g) / d + 4
    h *= 60
  }
  return { h, s, l }
}

export function hslToHex(h: number, s: number, l: number): string {
  h = ((h % 360) + 360) % 360
  s = Math.max(0, Math.min(1, s))
  l = Math.max(0, Math.min(1, l))
  const c = (1 - Math.abs(2 * l - 1)) * s
  const x = c * (1 - Math.abs(((h / 60) % 2) - 1))
  const m = l - c / 2
  let r = 0
  let g = 0
  let b = 0
  if (h < 60) [r, g] = [c, x]
  else if (h < 120) [r, g] = [x, c]
  else if (h < 180) [g, b] = [c, x]
  else if (h < 240) [g, b] = [x, c]
  else if (h < 300) [r, b] = [x, c]
  else [r, b] = [c, x]
  return rgbToHex((r + m) * 255, (g + m) * 255, (b + m) * 255)
}

const hslOf = (hex: string) => {
  const { r, g, b } = hexToRgb(hex)
  return rgbToHsl(r, g, b)
}

/** Luminancia relativa (WCAG 2.x). */
export function relLum(hex: string): number {
  const { r, g, b } = hexToRgb(hex)
  const f = (v: number) => {
    v /= 255
    return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4)
  }
  return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b)
}

/** Razón de contraste WCAG entre dos colores (1 a 21). */
export function contrastRatio(a: string, b: string): number {
  const l1 = relLum(a)
  const l2 = relLum(b)
  return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05)
}

/** Distancia de matiz (0-180°) entre dos colores. */
export function hueDistance(a: string, b: string): number {
  const d = Math.abs(hslOf(a).h - hslOf(b).h) % 360
  return d > 180 ? 360 - d : d
}

export function rgbaOf(hex: string, alpha: number): string {
  const { r, g, b } = hexToRgb(hex)
  return `rgba(${r},${g},${b},${alpha})`
}

// ------------------------------------------------------------------ derivación

/** Rampa de un acento para el modo: en oscuro la variante `-2` aclara (lee sobre fondo oscuro); en claro oscurece. */
export function deriveAccent(baseHex: string, mode: ThemeMode): { base: string; v2: string; bg: string; line: string } {
  const { h, s, l } = hslOf(baseHex)
  const v2 = mode === 'light' ? hslToHex(h, Math.min(1, s + 0.08), Math.max(0.2, l - 0.18)) : hslToHex(h, Math.min(1, s * 0.92), Math.min(0.82, l + 0.2))
  return { base: normalizeHex(baseHex), v2, bg: rgbaOf(baseHex, mode === 'light' ? 0.1 : 0.14), line: rgbaOf(baseHex, mode === 'light' ? 0.3 : 0.4) }
}

/** Luminosidades de las superficies medidas sobre la paleta original de Teikem (tono 222, saturación 39 %). */
const SURF_L: Record<ThemeMode, Record<string, number>> = {
  dark: { bg2: 0.067, bg: 0.088, panel: 0.129, panel2: 0.153, line2: 0.18, raise: 0.194, line: 0.231, faint: 0.429, muted: 0.614, text: 0.935, appTop: 0.15 },
  light: { text: 0.153, muted: 0.429, faint: 0.614, line: 0.904, raise: 0.925, line2: 0.937, bg: 0.955, panel2: 0.965, bg2: 0.986, panel: 1, appTop: 1 },
}
const MUTED_SAT = 0.44

/** Superficies, líneas y texto del tema a partir del tono (`nh`) y la saturación (`ns`) del neutro. */
export function deriveSurfaces(nh: number, ns: number, mode: ThemeMode): Record<string, string> {
  const L = SURF_L[mode]
  const c = (l: number, sat: number = ns) => hslToHex(nh, sat, l)
  const o: Record<string, string> = {
    '--bg': c(L.bg),
    '--bg-2': c(L.bg2),
    '--panel': mode === 'light' ? '#FFFFFF' : c(L.panel),
    '--panel-2': c(L.panel2),
    '--raise': c(L.raise),
    '--line': c(L.line),
    '--line-2': c(L.line2),
    '--text': c(L.text),
    '--muted': c(L.muted, ns * MUTED_SAT),
    '--faint': c(L.faint, ns * MUTED_SAT),
    '--neutral-fg': c(L.muted, ns * MUTED_SAT * 0.8),
  }
  const top = mode === 'light' ? '#FFFFFF' : c(L.appTop)
  o['--app-bg'] =
    mode === 'light'
      ? `radial-gradient(1100px 560px at 82% -12%,${top} 0%,${o['--bg']} 60%)`
      : `radial-gradient(1200px 600px at 80% -10%,${top} 0%,${o['--bg']} 55%)`
  return o
}

export function presetById(id: string | null | undefined): BrandPreset | undefined {
  return BRAND_PRESETS.find((p) => p.id === id)
}

/** Colores efectivos: los del preset, o los propios con el neutro acotado. */
export function brandColors(b: BrandSettings): { flow: string; money: string; nh: number; ns: number } {
  if (!b.useCustom) {
    const p = presetById(b.preset) ?? BRAND_PRESETS[0]
    return { flow: p.flow, money: p.money, nh: p.nh, ns: p.ns }
  }
  const { h, s } = hslOf(isValidHex(b.custom.neutral) ? b.custom.neutral : DEFAULT_BRAND.custom.neutral)
  return { flow: normalizeHex(b.custom.flow), money: normalizeHex(b.custom.money), nh: h, ns: Math.min(NEUTRAL_SAT_MAX, s) }
}

export type BrandCheck =
  | { type: 'contrast'; key: 'text' | 'muted' | 'flow' | 'money'; mode: ThemeMode; ratio: number; min: number; pass: boolean }
  | { type: 'hue'; distance: number; min: number; pass: boolean }

/** Validaciones: contraste de texto, texto atenuado y los dos acentos contra el panel en los DOS modos, y matiz entre acentos. */
export function brandChecks(b: BrandSettings): BrandCheck[] {
  const c = brandColors(b)
  const out: BrandCheck[] = []
  for (const mode of ['dark', 'light'] as const) {
    const surf = deriveSurfaces(c.nh, c.ns, mode)
    const panel = surf['--panel']
    const rows: ['text' | 'muted' | 'flow' | 'money', string, number][] = [
      ['text', surf['--text'], 7],
      ['muted', surf['--muted'], 4.5],
      ['flow', deriveAccent(c.flow, mode).v2, 4.5],
      ['money', deriveAccent(c.money, mode).v2, 4.5],
    ]
    for (const [key, color, min] of rows) {
      const ratio = Math.round(contrastRatio(color, panel) * 100) / 100
      out.push({ type: 'contrast', key, mode, ratio, min, pass: ratio >= min })
    }
  }
  const distance = Math.round(hueDistance(c.flow, c.money))
  out.push({ type: 'hue', distance, min: MIN_HUE_DISTANCE, pass: distance >= MIN_HUE_DISTANCE })
  return out
}

export function brandChecksPass(b: BrandSettings): boolean {
  return brandChecks(b).every((x) => x.pass)
}

/** true = la marca de siempre (preset Teikem sin colores propios): no se sobrescribe nada de `tokens.css`. */
export function isDefaultBrand(b: BrandSettings): boolean {
  return !b.useCustom && (presetById(b.preset)?.id ?? DEFAULT_PRESET) === DEFAULT_PRESET
}

/** Variables CSS que el tema sobrescribe en <html> para el modo (vacío con la marca de siempre). */
export function brandCssVars(b: BrandSettings, mode: ThemeMode): Record<string, string> {
  if (isDefaultBrand(b)) return {}
  const c = brandColors(b)
  const vars = deriveSurfaces(c.nh, c.ns, mode)
  const f = deriveAccent(c.flow, mode)
  const m = deriveAccent(c.money, mode)
  Object.assign(vars, {
    '--flow': f.base,
    '--flow-2': f.v2,
    '--flow-bg': f.bg,
    '--flow-line': f.line,
    '--money': m.base,
    '--money-2': m.v2,
    '--money-bg': m.bg,
    '--money-line': m.line,
    '--brand-wash': rgbaOf(c.flow, mode === 'light' ? 0.1 : 0.14),
    '--brand-glow': rgbaOf(c.flow, mode === 'light' ? 0.22 : 0.4),
    '--hexl': f.base,
  })
  const fl = hslOf(c.flow)
  vars['--hexd'] = hslToHex(fl.h, fl.s, Math.max(0.12, fl.l - 0.22))
  return vars
}

/** Todas las variables que el tema puede tocar (para limpiarlas al volver a la marca de siempre). */
export const BRAND_CSS_VARS = Object.keys(brandCssVars({ ...DEFAULT_BRAND, preset: 'marino' }, 'dark'))

/** `BrandingJson` → `BrandSettings` (lo que no se entienda, por defecto). */
export function parseBranding(json: string | null | undefined): BrandSettings {
  if (!json) return DEFAULT_BRAND
  try {
    const o = JSON.parse(json) as Partial<BrandSettings> & { custom?: Partial<BrandSettings['custom']> }
    const custom = { ...DEFAULT_BRAND.custom }
    for (const k of ['flow', 'money', 'neutral'] as const) {
      const v = o.custom?.[k]
      if (typeof v === 'string' && isValidHex(v)) custom[k] = normalizeHex(v)
    }
    return {
      preset: typeof o.preset === 'string' && presetById(o.preset) ? o.preset : DEFAULT_PRESET,
      useCustom: o.useCustom === true,
      custom,
    }
  } catch {
    return DEFAULT_BRAND
  }
}

/** `BrandSettings` → `BrandingJson`, conservando otras claves que ya tuviera el JSON guardado. */
export function serializeBranding(b: BrandSettings, previousJson?: string | null): string {
  let base: Record<string, unknown> = {}
  try {
    const prev = previousJson ? (JSON.parse(previousJson) as unknown) : null
    if (prev && typeof prev === 'object' && !Array.isArray(prev)) base = prev as Record<string, unknown>
  } catch {
    base = {}
  }
  return JSON.stringify({ ...base, preset: b.preset, useCustom: b.useCustom, custom: b.custom })
}
