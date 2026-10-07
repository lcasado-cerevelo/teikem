import { useState } from 'react'
import { ScrollView, StyleSheet, Text, useWindowDimensions, View } from 'react-native'
import { useRouter } from 'expo-router'

import { ApiError } from '../kernel/api/client'
import { clearUserSession } from '../kernel/auth/session'
import { loginWithPin } from '../kernel/auth/deviceAuth'
import { PIN_MAX_LENGTH, PIN_MIN_LENGTH } from '../kernel/auth/pinRules'
import { useSession } from '../kernel/auth/useSession'
import { useT } from '../kernel/i18n/useT'
import { BigButton } from '../kernel/ui/BigButton'
import { NumericKeypad, PIN_COMPACT_HEIGHT, PinDots } from '../kernel/ui/NumericKeypad'
import { colors, spacing } from '../kernel/ui/theme'

/** Sesión bloqueada (botón Bloquear de Inicio, o app recién abierta con la sesión guardada): pide el PIN del mismo usuario.
 *  No se pierde nada de lo capturado. «Cerrar sesión» sale por completo (otro usuario). */
export default function LockScreen() {
  const { t } = useT()
  const router = useRouter()
  const { device, session } = useSession()
  const [pin, setPin] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const { height } = useWindowDimensions()
  const compact = height < PIN_COMPACT_HEIGHT

  async function submit() {
    if (!device || !session) return
    setError(null)
    setBusy(true)
    try {
      await loginWithPin(device.devicePublicId, device.deviceSecret, session.userId, pin, session.fullName)
      router.replace('/home')
    } catch (err) {
      setError(err instanceof ApiError ? err.title : t('errors.generic'))
      setPin('')
    } finally {
      setBusy(false)
    }
  }

  return (
    <ScrollView style={styles.scroll} contentContainerStyle={[styles.area, compact && styles.areaCompact]} keyboardShouldPersistTaps="handled">
      <Text style={styles.title}>{t('login.lockedTitle')}</Text>
      <Text style={styles.name}>{session?.fullName ?? ''}</Text>
      <Text style={styles.subtitle}>{t('login.lockedHelp')}</Text>
      <PinDots length={PIN_MAX_LENGTH} filled={pin.length} />
      <NumericKeypad value={pin} onChange={setPin} maxLength={PIN_MAX_LENGTH} keySize={compact ? 60 : undefined} />
      {error ? <Text style={styles.error}>{error}</Text> : null}
      <View style={styles.actions}>
        <View style={styles.action}>
          <BigButton label={t('home.signOut')} variant="secondary" onPress={() => void clearUserSession().then(() => router.replace('/login'))} />
        </View>
        <View style={styles.action}>
          <BigButton label={t('login.unlock')} onPress={submit} loading={busy} disabled={pin.length < PIN_MIN_LENGTH} />
        </View>
      </View>
    </ScrollView>
  )
}

const styles = StyleSheet.create({
  scroll: { flex: 1, backgroundColor: colors.bg },
  area: { flexGrow: 1, alignItems: 'center', justifyContent: 'center', gap: spacing.lg, padding: spacing.lg, paddingBottom: spacing.xl },
  areaCompact: { gap: spacing.md, paddingTop: spacing.md, paddingBottom: spacing.md },
  title: { color: colors.text, fontSize: 24, fontWeight: '700' },
  name: { color: colors.text, fontSize: 18, fontWeight: '600' },
  subtitle: { color: colors.muted, fontSize: 16, textAlign: 'center' },
  actions: { flexDirection: 'row', gap: spacing.md, alignSelf: 'stretch', maxWidth: 360, width: '100%' },
  action: { flex: 1 },
  error: { color: colors.error, fontSize: 16, textAlign: 'center' },
})
