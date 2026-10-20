// Selector de secciones (botones en fila que se envuelven): la sección elegida se ve llena. Los colores salen de la paleta del tema.
import { Pressable, Text, View } from 'react-native'

import { useColors } from '../../kernel/ui/ui'

export function Segments({ value, options, onChange }: { value: string; options: { key: string; label: string }[]; onChange: (key: string) => void }) {
  const c = useColors()
  return (
    <View accessibilityRole="tablist" style={{ flexDirection: 'row', flexWrap: 'wrap', gap: 8 }}>
      {options.map((o) => {
        const on = o.key === value
        return (
          <Pressable
            key={o.key}
            accessibilityRole="tab"
            accessibilityState={{ selected: on }}
            testID={`segment-${o.key}`}
            onPress={() => onChange(o.key)}
            style={{ minHeight: 40, paddingHorizontal: 14, borderRadius: 999, alignItems: 'center', justifyContent: 'center', borderWidth: 1, borderColor: on ? c.flow : c.line, backgroundColor: on ? c.flow : c.card }}
          >
            <Text style={{ fontWeight: '600', color: on ? c.onFlow : c.text }}>{o.label}</Text>
          </Pressable>
        )
      })}
    </View>
  )
}
