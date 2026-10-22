// Tema claro y oscuro de la app del cliente (decidido con Luis, 2026-10-17: todo debe tener los dos temas, con su opción para cambiarlo). Cada persona escoge Automático (sigue al teléfono o al navegador; por omisión),
// Claro u Oscuro; la elección se recuerda. `useColors()` da la paleta vigente: los componentes la leen de ahí (nunca colores fijos), así que cambiar el tema se ve al instante sin recargar ni perder la pantalla.
import { useColorScheme } from 'react-native'
import { useSyncExternalStore } from 'react'

import { StoreKeys, storeGet, storeSet } from '../storage'
import { brandColors, deriveAccent, deriveBar, deriveSecondary, deriveSurfaces, isDefaultBrand, onColor, type BrandSettings } from './brandTheme'

export type ThemePref = 'auto' | 'light' | 'dark'
export type Scheme = 'light' | 'dark'
export const THEME_PREFS: readonly ThemePref[] = ['auto', 'light', 'dark']

export interface Palette {
  /** fondo de la pantalla */
  bg: string
  /** tarjetas, campos y superficies */
  card: string
  line: string
  text: string
  /** texto secundario */
  muted: string
  /** barra superior (la marca) y su texto */
  brand: string
  onBrand: string
  /** relleno de los botones principales (naranja de Advance Logistics, Lote 138) con `onFlow` (azul marino oscuro: el blanco sobre naranja no llega a AA) encima */
  flow: string
  onFlow: string
  /** botón secundario (Cancelar, apoyo): azul marino con texto blanco */
  secondary: string
  secondaryLine: string
  onSecondary: string
  /** texto de los enlaces y de lo seleccionado */
  link: string
  /** lo ya recorrido en la barra de progreso */
  done: string
  segTodo: string
  segCancelled: string
  segAlert: string
  danger: string
  /** relleno del botón peligroso (con texto blanco encima) */
  dangerSolid: string
  dangerBg: string
  ok: string
  okBg: string
  warn: string
  warnBg: string
  info: string
  infoBg: string
  neutralBg: string
}

export const LIGHT: Palette = {
  bg: '#F1F3F8',
  card: '#FFFFFF',
  line: '#D9DDE3',
  text: '#111827',
  muted: '#566071',
  brand: '#03204B',
  onBrand: '#FFFFFF',
  flow: '#F37123',
  onFlow: '#0A1B33',
  secondary: '#03204B',
  secondaryLine: '#03204B',
  onSecondary: '#FFFFFF',
  link: '#B84602',
  done: '#03204B',
  segTodo: '#DCE0E8',
  segCancelled: '#C4C9D2',
  segAlert: '#C26A05',
  danger: '#B42318',
  dangerSolid: '#B42318',
  dangerBg: '#FDECEA',
  ok: '#067647',
  okBg: '#E7F6EC',
  warn: '#B54708',
  warnBg: '#FEF0C7',
  info: '#175CD3',
  infoBg: '#E5EEFE',
  neutralBg: '#EEF0F4',
}

export const DARK: Palette = {
  bg: '#0B1220',
  card: '#151D2E',
  line: '#2B3750',
  text: '#E6EAF2',
  muted: '#9AA6BA',
  brand: '#0B2C66',
  onBrand: '#FFFFFF',
  flow: '#F37123',
  onFlow: '#0A1B33',
  secondary: '#114188',
  secondaryLine: '#1961CC',
  onSecondary: '#FFFFFF',
  link: '#F4B188',
  done: '#F4B188',
  segTodo: '#2B3750',
  segCancelled: '#3A4558',
  segAlert: '#F59E0B',
  danger: '#FF8A80',
  dangerSolid: '#C62828',
  dangerBg: '#4A1B17',
  ok: '#5FD3A1',
  okBg: '#0F3D2A',
  warn: '#F5B35B',
  warnBg: '#4A3410',
  info: '#8DB8FF',
  infoBg: '#14305E',
  neutralBg: '#1E2840',
}

export const PALETTES: Record<Scheme, Palette> = { light: LIGHT, dark: DARK }

/**
 * Paleta con la marca de la compañía (2026-10-22, pedido de Luis: el portal se ve con los colores de la compañía de Ajustes → Marca). Con la marca de siempre (Enterprise) se queda la paleta de arriba;
 * con otra se derivan, con la misma lógica de la web del equipo, las superficies, la barra, el color de los botones, el secundario y los enlaces. Los colores de estado (ok, aviso, peligro, info) no cambian.
 */
export function brandedPalette(scheme: Scheme, brand: BrandSettings | null): Palette {
  const base = PALETTES[scheme]
  if (!brand || isDefaultBrand(brand)) return base
  const c = brandColors(brand)
  const surf = deriveSurfaces(c.nh, c.ns, scheme)
  const bar = deriveBar(c.nh, c.ns, scheme)
  const sec = deriveSecondary(c.nh, c.ns, scheme)
  const accent = deriveAccent(c.action, scheme)
  return {
    ...base,
    bg: surf['--bg'],
    card: surf['--panel'],
    line: surf['--line'],
    text: surf['--text'],
    muted: surf['--muted'],
    brand: bar.bar,
    onBrand: bar.on,
    flow: c.flow,
    onFlow: onColor(c.flow),
    secondary: sec.base,
    secondaryLine: sec.line,
    onSecondary: sec.on,
    link: accent.v2,
    done: scheme === 'light' ? bar.bar : accent.v2,
    segTodo: surf['--line'],
    neutralBg: surf['--raise'],
  }
}

/** La elección → el tema que se pinta: «Automático» sigue al sistema (y si el sistema no dice, claro). */
export function resolveScheme(pref: ThemePref, system: string | null | undefined): Scheme {
  if (pref === 'light' || pref === 'dark') return pref
  return system === 'dark' ? 'dark' : 'light'
}

// ---------------------------------------------------------------- contraste (para que las pruebas lo vigilen)

function channel(v: number): number {
  const s = v / 255
  return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4
}

function luminance(hex: string): number {
  const n = Number.parseInt(hex.replace('#', ''), 16)
  return 0.2126 * channel((n >> 16) & 255) + 0.7152 * channel((n >> 8) & 255) + 0.0722 * channel(n & 255)
}

/** Relación de contraste WCAG entre dos colores `#RRGGBB` (de 1 a 21). */
export function contrastRatio(a: string, b: string): number {
  const [x, y] = [luminance(a), luminance(b)].sort((p, q) => q - p)
  return (x + 0.05) / (y + 0.05)
}

// ---------------------------------------------------------------- la elección guardada

let pref: ThemePref = 'auto'
let brand: BrandSettings | null = null
let brandVersion = 0
const listeners = new Set<() => void>()

/** La marca de la compañía de la sesión (null = la de siempre). Se pone al entrar y se quita al salir. */
export function setBrand(next: BrandSettings | null): void {
  brand = next
  brandVersion += 1
  listeners.forEach((l) => l())
}

export const getBrand = (): BrandSettings | null => brand
const getBrandVersion = (): number => brandVersion

// La paleta vigente se calcula una vez por (tema, marca): los componentes comparan la paleta por identidad (useMemo de los estilos), no debe ser un objeto nuevo en cada pintado.
const paletteCache = new Map<string, Palette>()
function currentPalette(scheme: Scheme): Palette {
  const key = `${scheme}:${brandVersion}`
  let p = paletteCache.get(key)
  if (!p) {
    if (paletteCache.size > 8) paletteCache.clear()
    p = brandedPalette(scheme, brand)
    paletteCache.set(key, p)
  }
  return p
}

export function getThemePref(): ThemePref {
  return pref
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

/** Carga la elección guardada (al arrancar). */
export async function initTheme(): Promise<void> {
  const stored = await storeGet(StoreKeys.theme)
  if ((stored === 'auto' || stored === 'light' || stored === 'dark') && stored !== pref) {
    pref = stored
    listeners.forEach((l) => l())
  }
}

export function setThemePref(next: ThemePref): void {
  if (next === pref) return
  pref = next
  void storeSet(StoreKeys.theme, next)
  listeners.forEach((l) => l())
}

/** El tema vigente: la elección, el tema resuelto y su paleta. Se vuelve a pintar al cambiar la elección o el tema del sistema. */
export function useTheme(): { pref: ThemePref; scheme: Scheme; colors: Palette; setPref: (p: ThemePref) => void } {
  const current = useSyncExternalStore(subscribe, getThemePref, getThemePref)
  useSyncExternalStore(subscribe, getBrandVersion, getBrandVersion)   // vuelve a pintar al cambiar la marca
  const system = useColorScheme()
  const scheme = resolveScheme(current, system)
  return { pref: current, scheme, colors: currentPalette(scheme), setPref: setThemePref }
}

export function useColors(): Palette {
  return useTheme().colors
}
