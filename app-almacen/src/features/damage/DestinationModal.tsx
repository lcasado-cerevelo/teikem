// 2026-10-09 — «¿A dónde va?»: el destino final de lo dañado al darle salida (tirado, devuelto al proveedor, donado, vendido como saldo). Lista fija de los
// cuatro destinos de fábrica (el aparato trabaja sin señal; los destinos que la compañía agregue en el catálogo se escogen en la web). Se abre antes de
// confirmar la salida, tanto en Recibir (unidades dañadas) como en el tile Daño.
import { Modal, Pressable, StyleSheet, Text, View } from 'react-native'

import { useT } from '../../kernel/i18n/useT'
import { colors, fontSize, radius, spacing } from '../../kernel/ui/theme'
import { DAMAGE_DESTINATIONS } from './damageLogic'

export interface DestinationModalProps {
  visible: boolean
  onSelect: (code: string) => void
  onClose: () => void
}

export function DestinationModal({ visible, onSelect, onClose }: DestinationModalProps) {
  const { t } = useT()
  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onClose}>
      <Pressable style={styles.backdrop} onPress={onClose}>
        <View style={styles.sheet} testID="destination-modal">
          <Text style={styles.title}>{t('damage.destinationTitle')}</Text>
          <Text style={styles.help}>{t('damage.destinationHelp')}</Text>
          {DAMAGE_DESTINATIONS.map((d) => (
            <Pressable key={d.code} accessibilityRole="button" accessibilityLabel={t(d.key)} onPress={() => onSelect(d.code)} style={styles.option}>
              <Text style={styles.optionText}>{t(d.key)}</Text>
            </Pressable>
          ))}
          <Pressable accessibilityRole="button" accessibilityLabel={t('common.cancel')} onPress={onClose} style={[styles.option, styles.cancel]}>
            <Text style={styles.optionText}>{t('common.cancel')}</Text>
          </Pressable>
        </View>
      </Pressable>
    </Modal>
  )
}

const styles = StyleSheet.create({
  backdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.6)', justifyContent: 'center', padding: spacing.lg },
  sheet: { backgroundColor: colors.panel, borderRadius: radius.lg, padding: spacing.lg, gap: spacing.sm },
  title: { color: colors.text, fontSize: fontSize.title, fontWeight: '700' },
  help: { color: colors.muted, fontSize: fontSize.message },
  option: { minHeight: 56, justifyContent: 'center', paddingHorizontal: spacing.md, borderRadius: radius.md, borderWidth: 2, borderColor: colors.line, backgroundColor: colors.panelAlt },
  cancel: { marginTop: spacing.xs },
  optionText: { color: colors.text, fontSize: fontSize.label, fontWeight: '600' },
})
