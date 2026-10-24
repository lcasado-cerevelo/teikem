// Desplegable de opciones del kit (2026-10-23). Esta es la versión del TELÉFONO: un campo que al tocarlo abre una hoja con las opciones. En la web (Select.web.tsx) es un
// <select> nativo del navegador, que se maneja completo con el teclado (Tab, flechas, Enter). Las dos versiones tienen las mismas propiedades.
import { useState } from 'react'
import { Modal, Pressable, ScrollView, Text, View } from 'react-native'

import { useT } from '../i18n/useT'
import { useColors } from './theme'

export interface SelectOption {
  value: string
  label: string
}

export interface SelectProps {
  value: string
  options: readonly SelectOption[]
  onChange: (value: string) => void
  /** Texto cuando no hay valor (en la web es la opción vacía). */
  placeholder?: string
  /** Etiqueta accesible (obligatoria si no hay un rótulo visible al lado). */
  accessibilityLabel: string
  disabled?: boolean
  /** Alto de 40 px (rejillas y filtros) en lugar de 48. */
  compact?: boolean
  /** Si está, Enter en la web llama esto (la rejilla de captura guarda la fila) en lugar de abrir la lista. */
  onEnter?: () => void
  /** Para llevarle el foco desde afuera (en la web). */
  inputRef?: (el: { focus: () => void } | null) => void
  testID?: string
  /** Marca de error (borde rojo). */
  invalid?: boolean
}

export function Select({ value, options, onChange, placeholder, accessibilityLabel, disabled, compact, inputRef, testID, invalid }: SelectProps) {
  const c = useColors()
  const { t } = useT()
  const [open, setOpen] = useState(false)
  const current = options.find((o) => o.value === value)
  return (
    <>
      <Pressable
        ref={(el) => inputRef?.(el as unknown as { focus: () => void } | null)}
        accessibilityRole="button"
        accessibilityLabel={accessibilityLabel}
        accessibilityState={{ disabled: !!disabled, expanded: open }}
        accessibilityValue={{ text: current?.label ?? '' }}
        testID={testID}
        disabled={disabled}
        onPress={() => setOpen(true)}
        style={{ minHeight: compact ? 40 : 48, borderRadius: compact ? 8 : 10, borderWidth: 1, borderColor: invalid ? c.danger : c.line, backgroundColor: c.card, paddingHorizontal: compact ? 8 : 12, flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 6, opacity: disabled ? 0.6 : 1 }}
      >
        <Text numberOfLines={1} style={{ fontSize: compact ? 14 : 16, color: current ? c.text : c.muted, flexShrink: 1 }}>{current?.label ?? placeholder ?? t('select.choose')}</Text>
        <Text style={{ color: c.muted, fontSize: 12 }}>▾</Text>
      </Pressable>
      <Modal visible={open} transparent animationType="fade" onRequestClose={() => setOpen(false)}>
        <Pressable accessibilityRole="button" accessibilityLabel={t('common.cancel')} onPress={() => setOpen(false)} style={{ flex: 1, backgroundColor: 'rgba(0,0,0,0.45)', justifyContent: 'flex-end' }}>
          <View style={{ backgroundColor: c.card, borderTopLeftRadius: 16, borderTopRightRadius: 16, maxHeight: '70%', paddingBottom: 24 }}>
            <Text style={{ fontWeight: '700', color: c.text, padding: 16, fontSize: 16 }}>{accessibilityLabel}</Text>
            <ScrollView>
              {options.map((o) => {
                const selected = o.value === value
                return (
                  <Pressable key={o.value} accessibilityRole="radio" accessibilityState={{ selected }} testID={testID ? `${testID}-opt-${o.value}` : undefined} onPress={() => { onChange(o.value); setOpen(false) }}
                    style={{ paddingVertical: 14, paddingHorizontal: 16, backgroundColor: selected ? c.neutralBg : c.card, borderTopWidth: 1, borderTopColor: c.line }}>
                    <Text style={{ fontSize: 16, color: c.text, fontWeight: selected ? '700' : '400' }}>{(selected ? '● ' : '○ ') + o.label}</Text>
                  </Pressable>
                )
              })}
            </ScrollView>
          </View>
        </Pressable>
      </Modal>
    </>
  )
}
