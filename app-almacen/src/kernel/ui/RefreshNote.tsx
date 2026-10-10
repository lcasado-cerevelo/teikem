// Señal débil (2026-10-10) — indicador de que la pantalla se está poniendo al día con el servidor mientras ya muestra lo que el aparato
// tiene. Mientras trabaja: rueda y «Actualizando…»; al terminar: «Al día»; si no alcanzó el servidor: aviso de que son datos del aparato.
// Lo usan todas las pantallas que leen de la base local (Consultar, Conteo, Acomodar, Transferir, Ajustar, Daño, Despacho).
import { ActivityIndicator, StyleSheet, Text, View } from 'react-native'

import { useT } from '../i18n/useT'
import { colors, fontSize, spacing } from './theme'

export type RefreshState = 'idle' | 'syncing' | 'done' | 'offline'

export function RefreshNote({ state, offlineText }: { state: RefreshState; offlineText?: string }) {
  const { t } = useT()
  if (state === 'idle') return null
  if (state === 'syncing') {
    return (
      <View style={styles.row} accessibilityRole="progressbar" accessibilityLabel={t('lookup.updating')}>
        <ActivityIndicator size="small" color={colors.brand} />
        <Text style={styles.text}>{t('lookup.updating')}</Text>
      </View>
    )
  }
  if (state === 'done') {
    return (
      <View style={styles.row}>
        <Text style={styles.ok}>✓ {t('lookup.updated')}</Text>
      </View>
    )
  }
  return (
    <View style={styles.row}>
      <Text style={styles.warn}>{offlineText ?? t('lookup.offlineNote')}</Text>
    </View>
  )
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  text: { color: colors.muted, fontSize: fontSize.message },
  ok: { color: colors.muted, fontSize: fontSize.message },
  warn: { color: colors.muted, fontSize: fontSize.message },
})
