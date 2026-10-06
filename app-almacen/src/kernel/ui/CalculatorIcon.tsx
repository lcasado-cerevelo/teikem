// Lote A9 (pruebas del dueño en el Zebra, 2026-10-06): el botón de la calculadora usaba el emoji 🧮 (un ábaco marrón y negro) sobre
// el fondo oscuro de la app y no se notaba. El icono se dibuja aquí con trazos de un solo color claro (por defecto `colors.onStrong`,
// blanco), así no depende de la fuente de emojis del aparato y siempre contrasta con el fondo del botón (`colors.brandDark`).
import { StyleSheet, View } from 'react-native'

import { colors } from './theme'

export interface CalculatorIconProps {
  /** Color de los trazos (claro: el botón es oscuro). */
  color?: string
  /** Alto aproximado del icono en dp (el ancho es 4/5; el alto exacto sale de las teclas). */
  size?: number
}

/** Calculadora dibujada: marco, pantalla y 3 × 3 teclas. Decorativo: el nombre lo pone el botón que lo contiene. */
export function CalculatorIcon({ color = colors.onStrong, size = 28 }: CalculatorIconProps) {
  const width = Math.round((size * 4) / 5)
  const stroke = Math.max(2, Math.round(size / 14))
  const pad = Math.max(2, Math.round(size / 10))
  const inner = width - stroke * 2 - pad * 2
  const gap = Math.max(1, Math.round(inner / 10))
  const key = Math.floor((inner - gap * 2) / 3)
  return (
    <View
      testID="calculator-icon"
      accessible={false}
      importantForAccessibility="no-hide-descendants"
      style={[styles.frame, { width, borderColor: color, borderWidth: stroke, padding: pad, borderRadius: Math.round(size / 7), gap }]}
    >
      <View style={{ height: Math.max(3, Math.round(size / 6)), backgroundColor: color, borderRadius: 1 }} />
      {[0, 1, 2].map((r) => (
        <View key={r} style={[styles.keys, { gap }]}>
          {[0, 1, 2].map((c) => (
            <View key={c} style={{ width: key, height: key, backgroundColor: color, borderRadius: 1 }} />
          ))}
        </View>
      ))}
    </View>
  )
}

const styles = StyleSheet.create({
  frame: { alignItems: 'stretch', justifyContent: 'flex-start' },
  keys: { flexDirection: 'row', justifyContent: 'center' },
})
