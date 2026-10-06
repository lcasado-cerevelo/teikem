import { Pressable, StyleSheet, Text, View } from 'react-native'

import { colors, radius, spacing, touchTarget } from './theme'

export interface NumericKeypadProps {
  value: string
  onChange: (value: string) => void
  maxLength: number
  /** Lado de cada tecla en dp (por defecto 72; en pantallas bajas la pantalla del PIN pide 60, nunca menos de 56). */
  keySize?: number
}

/** Lado normal de una tecla (dp). */
const KEY_SIZE = 72
/** Alto de ventana (dp) por debajo del cual la pantalla del PIN compacta el teclado (Lote A9: un Zebra de 4" mide ~533 dp de alto). */
export const PIN_COMPACT_HEIGHT = 640

const KEYS = ['1', '2', '3', '4', '5', '6', '7', '8', '9', '', '0', '⌫']

/** Teclado numérico grande para el PIN (docs/mobile/app-almacen-plan.md §1: "PIN de 4 a 6 dígitos"). Sin teclado del
 *  sistema: así el mismo gesto sirve con o sin pantalla táctil grande y no se confunde con DataWedge. */
export function NumericKeypad({ value, onChange, maxLength, keySize = KEY_SIZE }: NumericKeypadProps) {
  const size = Math.max(keySize, touchTarget)
  // teclas más chicas, también menos espacio entre ellas
  const gap = size < KEY_SIZE ? spacing.sm : spacing.md
  function press(key: string) {
    if (key === '') return
    if (key === '⌫') {
      onChange(value.slice(0, -1))
      return
    }
    if (value.length < maxLength) onChange(value + key)
  }

  return (
    <View style={[styles.grid, { gap, maxWidth: size * 3 + gap * 2 + 4 }]}>
      {KEYS.map((key, i) => (
        <Pressable
          key={key || `blank-${i}`}
          accessibilityRole={key ? 'button' : undefined}
          accessibilityLabel={key === '⌫' ? 'Borrar' : key || undefined}
          disabled={key === ''}
          onPress={() => press(key)}
          style={({ pressed }) => [styles.key, { width: size, height: size }, key === '' && styles.blank, pressed && styles.keyPressed]}
        >
          <Text style={styles.keyLabel}>{key}</Text>
        </Pressable>
      ))}
    </View>
  )
}

export interface PinDotsProps {
  length: number
  filled: number
}

/** Puntos de progreso del PIN (nunca muestra los dígitos, ni siquiera en claro). */
export function PinDots({ length, filled }: PinDotsProps) {
  return (
    <View style={styles.dotsRow} accessibilityLabel={`${filled} de ${length} dígitos`}>
      {Array.from({ length }).map((_, i) => (
        <View key={i} style={[styles.dot, i < filled && styles.dotFilled]} />
      ))}
    </View>
  )
}


const styles = StyleSheet.create({
  // gap, maxWidth y el lado de las teclas dependen de `keySize` (estilo en línea arriba)
  grid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    justifyContent: 'center',
  },
  key: {
    borderRadius: radius.lg,
    backgroundColor: colors.panelAlt,
    borderWidth: 1,
    borderColor: colors.line,
    alignItems: 'center',
    justifyContent: 'center',
  },
  blank: { backgroundColor: 'transparent', borderWidth: 0 },
  keyPressed: { backgroundColor: colors.line },
  keyLabel: { color: colors.text, fontSize: 26, fontWeight: '600' },
  dotsRow: { flexDirection: 'row', gap: spacing.md, justifyContent: 'center' },
  dot: { width: 18, height: 18, borderRadius: 9, borderWidth: 2, borderColor: colors.muted },
  dotFilled: { backgroundColor: colors.brand, borderColor: colors.brand },
})
