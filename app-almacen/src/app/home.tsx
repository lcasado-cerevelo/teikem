import { useCallback, useState } from 'react'
import { Alert, StyleSheet, Text, View } from 'react-native'
import { useFocusEffect, useRouter } from 'expo-router'

import { clearUserSession } from '../kernel/auth/session'
import { useSession } from '../kernel/auth/useSession'
import { getOpenReceipt } from '../features/receive/localLookup'
import { useT } from '../kernel/i18n/useT'
import { runSync, useAutoSync, useLastSync, usePendingCount } from '../kernel/sync/engine'
import { BigButton } from '../kernel/ui/BigButton'
import { colors, spacing } from '../kernel/ui/theme'

/** Pantalla 2 (docs/mobile/app-almacen-plan.md §2): 5 botones grandes + estado de sincronización. Solo Recibir está
 *  conectado en esta entrega; las demás quedan con "Disponible en la próxima entrega" (A2 a A5).
 *  Un recibo en curso bloquea las demás acciones (decisión de Luis, 2026-09-28: un recibo a la vez, y lo mismo debe
 *  aplicar a Despacho cuando se construya, docs/lote8A-app-decisiones.md): mientras haya uno abierto, "Recibir" lo
 *  retoma (nunca empieza otro, localLookup.ts lo impide) y los demás botones avisan en vez de "próximamente". */
export default function HomeScreen() {
  const { t } = useT()
  const router = useRouter()
  const { session } = useSession()
  const pending = usePendingCount()
  const lastSync = useLastSync()
  useAutoSync()

  const [receiveOpen, setReceiveOpen] = useState(false)
  useFocusEffect(
    useCallback(() => {
      setReceiveOpen(getOpenReceipt() !== null)
    }, []),
  )

  function comingSoon() {
    Alert.alert(t('home.comingSoon'))
  }

  function blocked() {
    Alert.alert(t('lock.receiveInProgress'))
  }

  const syncLabel = lastSync?.error
    ? t('home.syncStatusError', { count: pending })
    : pending > 0
      ? t('home.syncStatusPending', { count: pending })
      : t('home.syncStatusOk')

  return (
    <View style={styles.fill}>
      <View style={styles.header}>
        <Text style={styles.title}>{t('home.title')}</Text>
        {session ? <Text style={styles.userName}>{session.fullName}</Text> : null}
      </View>

      <View style={styles.grid}>
        <BigButton label={t('home.receive')} icon="📥" onPress={() => router.push('/receive')} />
        <BigButton label={t('home.putaway')} icon="📦" variant="secondary" onPress={receiveOpen ? blocked : comingSoon} />
        <BigButton label={t('home.dispatch')} icon="🚚" variant="secondary" onPress={receiveOpen ? blocked : comingSoon} />
        <BigButton label={t('home.count')} icon="🔢" variant="secondary" onPress={receiveOpen ? blocked : comingSoon} />
        <BigButton label={t('home.lookup')} icon="🔎" variant="secondary" onPress={receiveOpen ? blocked : comingSoon} />
      </View>

      <View style={styles.syncBar}>
        <Text style={[styles.syncText, lastSync?.error && styles.syncError]}>{syncLabel}</Text>
        <BigButton label={t('home.syncNow')} variant="secondary" fullWidth={false} onPress={() => void runSync()} />
      </View>

      <BigButton label={t('home.signOut')} variant="secondary" onPress={() => void clearUserSession().then(() => router.replace('/login'))} />
    </View>
  )
}

const styles = StyleSheet.create({
  fill: { flex: 1, backgroundColor: colors.bg, padding: spacing.lg, gap: spacing.lg },
  header: { gap: spacing.xs },
  title: { color: colors.text, fontSize: 24, fontWeight: '700' },
  userName: { color: colors.muted, fontSize: 15 },
  grid: { gap: spacing.md, flex: 1 },
  syncBar: { gap: spacing.sm },
  syncText: { color: colors.muted, fontSize: 14, textAlign: 'center' },
  syncError: { color: colors.error },
})
