// 2026-10-08 — Desplegable de la razón del daño (catálogo DamageCause): muestra la escogida o «Escoge la razón» y, al tocarlo, una lista para elegir.
// Se usa en la captura de Recibir (junto a la cantidad dañada); el reporte suelto de Daño muestra las cuatro opciones a la vista.
import { useState } from 'react'
import { Modal, Pressable, StyleSheet, Text, View } from 'react-native'

import { useT } from '../../kernel/i18n/useT'
import { colors, fontSize, radius, spacing } from '../../kernel/ui/theme'
import { DAMAGE_CAUSES } from './damageLogic'

export interface CauseSelectProps {
  value: string
  onChange: (code: string) => void
  placeholder: string
  label: string
  testID?: string
}

export function CauseSelect({ value, onChange, placeholder, label, testID }: CauseSelectProps) {
  const { t } = useT()
  const [open, setOpen] = useState(false)
  const current = DAMAGE_CAUSES.find((c) => c.code === value)
  return (
    <>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={label}
        testID={testID}
        onPress={() => setOpen(true)}
        style={styles.field}
      >
        <Text style={[styles.value, !current && styles.placeholder]} numberOfLines={1}>
          {current ? t(current.key) : placeholder}
        </Text>
        <Text style={styles.caret}>▾</Text>
      </Pressable>
      <Modal visible={open} transparent animationType="fade" onRequestClose={() => setOpen(false)}>
        <Pressable style={styles.backdrop} onPress={() => setOpen(false)}>
          <View style={styles.sheet}>
            <Text style={styles.title}>{label}</Text>
            {DAMAGE_CAUSES.map((c) => (
              <Pressable
                key={c.code}
                accessibilityRole="button"
                accessibilityState={{ selected: value === c.code }}
                onPress={() => {
                  onChange(c.code)
                  setOpen(false)
                }}
                style={[styles.option, value === c.code && styles.optionOn]}
              >
                <Text style={styles.optionText}>{t(c.key)}</Text>
              </Pressable>
            ))}
          </View>
        </Pressable>
      </Modal>
    </>
  )
}

const styles = StyleSheet.create({
  field: {
    minHeight: 56,
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    paddingHorizontal: spacing.md,
    borderWidth: 2,
    borderColor: colors.line,
    borderRadius: radius.md,
    backgroundColor: colors.panelAlt,
  },
  value: { flex: 1, color: colors.text, fontSize: 20 },
  placeholder: { color: colors.muted },
  caret: { color: colors.muted, fontSize: 20 },
  backdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.6)', justifyContent: 'center', padding: spacing.lg },
  sheet: { backgroundColor: colors.panel, borderRadius: radius.lg, padding: spacing.lg, gap: spacing.sm },
  title: { color: colors.text, fontSize: fontSize.title, fontWeight: '700', marginBottom: spacing.xs },
  option: { minHeight: 56, justifyContent: 'center', paddingHorizontal: spacing.md, borderRadius: radius.md, borderWidth: 2, borderColor: colors.line, backgroundColor: colors.panelAlt },
  optionOn: { borderColor: colors.brand, backgroundColor: colors.brandDark },
  optionText: { color: colors.text, fontSize: fontSize.label, fontWeight: '600' },
})
