import { useCallback, useState } from 'react'
import { Alert, Pressable, StyleSheet, Text, View } from 'react-native'
import { useFocusEffect, useRouter } from 'expo-router'

import { clearUserSession } from '../kernel/auth/session'
import { useSession } from '../kernel/auth/useSession'
import { getOpenReceipt } from '../features/receive/localLookup'
import { getOpenPick } from '../features/dispatch/localPick'
import { getOpenCount } from '../features/count/localCount'
import { useT } from '../kernel/i18n/useT'
import { runSync, useAutoSync, useLastSync, usePendingCount } from '../kernel/sync/engine'
import { syncStatusKey } from '../kernel/sync/syncStatus'
import { BigButton } from '../kernel/ui/BigButton'
import { colors, spacing } from '../kernel/ui/theme'

type OpenKind = 'receive' | 'dispatch' | 'count'

/** Pantalla 2 (docs/mobile/app-almacen-plan.md §2): 6 botones grandes + estado de sincronización.
 *  Un documento en curso (recibo, despacho o conteo) bloquea las demás acciones (decisión de Luis, 2026-09-28: uno a
 *  la vez por aparato): el botón del documento abierto lo retoma (cada pantalla nunca empieza uno nuevo mientras haya
 *  uno abierto, ver localLookup.ts/localPick.ts/localCount.ts) y los demás avisan en vez de navegar. */
export default function HomeScreen() {
  const { t } = useT()
  const router = useRouter()
  const { session } = useSession()
  const pending = usePendingCount()
  const lastSync = useLastSync()
  useAutoSync()

  const [openKind, setOpenKind] = useState<OpenKind | null>(null)
  useFocusEffect(
    useCallback(() => {
      if (getOpenReceipt() !== null) setOpenKind('receive')
      else if (getOpenPick() !== null) setOpenKind('dispatch')
      else if (getOpenCount() !== null) setOpenKind('count')
      else setOpenKind(null)
    }, []),
  )

  /** Navega a `path`, salvo que haya un documento distinto abierto (avisa cuál en vez de navegar). `ownKind` es el
   *  tipo de documento que esa pantalla retoma (undefined si no maneja ninguno, como Acomodar o Consultar). */
  function go(path: '/receive' | '/putaway' | '/dispatch' | '/count' | '/lookup', ownKind?: OpenKind) {
    if (openKind && openKind !== ownKind) {
      Alert.alert(t(`lock.${openKind}InProgress`))
      return
    }
    router.push(path)
  }

  const syncLabel = t(syncStatusKey(pending, Boolean(lastSync?.error)), { count: pending })

  return (
    <View style={styles.fill}>
      <View style={styles.header}>
        <Text style={styles.title}>{t('home.title')}</Text>
        {session ? <Text style={styles.userName}>{session.fullName}</Text> : null}
      </View>

      <View style={styles.grid}>
        <BigButton label={t('home.receive')} icon="📥" onPress={() => go('/receive', 'receive')} />
        <BigButton label={t('home.putaway')} icon="📦" variant="secondary" onPress={() => go('/putaway')} />
        <BigButton label={t('home.dispatch')} icon="🚚" variant="secondary" onPress={() => go('/dispatch', 'dispatch')} />
        <BigButton label={t('home.count')} icon="🔢" variant="secondary" onPress={() => go('/count', 'count')} />
        <BigButton label={t('home.lookup')} icon="🔎" variant="secondary" onPress={() => go('/lookup')} />
      </View>

      <View style={styles.syncBar}>
        <Pressable accessibilityRole="button" onPress={() => router.push('/sync')}>
          <Text style={[styles.syncText, lastSync?.error && styles.syncError]}>{syncLabel}</Text>
        </Pressable>
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
