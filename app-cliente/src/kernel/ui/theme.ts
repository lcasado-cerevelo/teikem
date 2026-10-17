// Tema claro y oscuro de la app del cliente (decidido con Luis, 2026-10-17: todo debe tener los dos temas, con su opción para cambiarlo). Cada persona escoge Automático (sigue al teléfono o al navegador; por omisión),
// Claro u Oscuro; la elección se recuerda. `useColors()` da la paleta vigente: los componentes la leen de ahí (nunca colores fijos), así que cambiar el tema se ve al instante sin recargar ni perder la pantalla.
import { useColorScheme } from 'react-native'
import { useSyncExternalStore } from 'react'

import { StoreKeys, storeGet, storeSet } from '../storage'

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
  /** relleno de los botones principales y del tramo actual (con texto blanco encima) */
  flow: string
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
  brand: '#0B2C66',
  onBrand: '#FFFFFF',
  flow: '#1D6FE8',
  link: '#1558C0',
  done: '#0B2C66',
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
  flow: '#2563EB',
  link: '#7FB0FF',
  done: '#5B9BFF',
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
const listeners = new Set<() => void>()

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
  const system = useColorScheme()
  const scheme = resolveScheme(current, system)
  return { pref: current, scheme, colors: PALETTES[scheme], setPref: setThemePref }
}

export function useColors(): Palette {
  return useTheme().colors
}
