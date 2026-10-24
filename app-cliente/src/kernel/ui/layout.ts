// Vista ancha (computadora en el navegador) o vista móvil (app instalada, tabletas y celulares): decide si el portal usa la barra lateral y la columna ancha, o las pestañas de abajo y la columna estrecha.
// Regla de Luis (2026-10-24): la forma depende del TIPO DE APARATO, no solo del ancho de la ventana: la app instalada y las tabletas y celulares (también en el navegador) ven la forma móvil
// (tarjetas, «Nuevo envío» solo detallado); la web en una computadora ve la forma ancha (tablas, entrada rápida con teclado). Una tableta en el navegador mide 1024 px o más, pero NO es una computadora.
import { Platform, useWindowDimensions } from 'react-native'

/** Ancho desde el que la web de una computadora se arma como escritorio. */
export const WIDE_MIN_WIDTH = 900

/** Ancho máximo del contenido en escritorio (listas y detalles) y en formularios. */
export const WIDE_COLUMN = 1280
export const NARROW_COLUMN = 560

/**
 * ¿El aparato es táctil (tableta o celular) y no una computadora? Se mira el puntero PRINCIPAL del navegador (`pointer: coarse` = dedo), que en una computadora, aun con pantalla táctil, es el ratón.
 * Fuera de la web (app instalada) siempre es un aparato móvil.
 */
export function isTouchDevice(os: string = Platform.OS): boolean {
  if (os !== 'web') return true
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return false
  try {
    return window.matchMedia('(pointer: coarse)').matches
  } catch {
    return false
  }
}

/** Vista ancha: solo la web, en una computadora (puntero fino) y con ventana de 900 px o más. `touch` se deduce del aparato si no se da. */
export const isWideWidth = (width: number, os: string = Platform.OS, touch: boolean = isTouchDevice(os)): boolean => os === 'web' && !touch && width >= WIDE_MIN_WIDTH

export function useIsWide(): boolean {
  const { width } = useWindowDimensions()
  return isWideWidth(width)
}
