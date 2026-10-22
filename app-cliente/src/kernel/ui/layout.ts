// Pantalla ancha (web de escritorio) o angosta (celular y web angosta): decide si el portal usa la barra lateral y la columna ancha, o las pestañas de abajo y la columna estrecha.
import { Platform, useWindowDimensions } from 'react-native'

/** Ancho desde el que la web se arma como escritorio. */
export const WIDE_MIN_WIDTH = 900

/** Ancho máximo del contenido en escritorio (listas y detalles) y en formularios. */
export const WIDE_COLUMN = 1040
export const NARROW_COLUMN = 560

export const isWideWidth = (width: number, os: string = Platform.OS): boolean => os === 'web' && width >= WIDE_MIN_WIDTH

export function useIsWide(): boolean {
  const { width } = useWindowDimensions()
  return isWideWidth(width)
}
