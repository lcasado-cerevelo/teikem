// Margen inferior de toda la app (docs/mobile/mejoras-ux-zebra.md §1): la barra de navegación del aparato tapaba más de la
// mitad del último botón. Se aplica una sola vez en app/_layout.tsx. Lógica pura.
import { spacing } from './theme'

/** Mínimo de 32 dp: más de la mitad del alto de un botón (56 / 2 = 28), aunque el aparato reporte un margen de 0
 *  (insets.test.ts lo comprueba contra `touchTarget`). */
export const MIN_BOTTOM_INSET = 32

/** `max(inset del sistema, 32) + spacing.sm`: si el aparato reporta bien su barra se usa la real; si no, el mínimo. */
export function bottomPadding(insetBottom: number): number {
  return Math.max(Number.isFinite(insetBottom) ? insetBottom : 0, MIN_BOTTOM_INSET) + spacing.sm
}
