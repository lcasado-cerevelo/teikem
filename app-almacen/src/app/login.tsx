import { useCallback, useEffect, useState } from 'react'
import { ActivityIndicator, FlatList, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native'
import { useRouter } from 'expo-router'

import { ApiError } from '../kernel/api/client'
import { selectDevice } from '../kernel/auth/session'
import { type DeviceUser, fetchDeviceUsers, loginWithPin } from '../kernel/auth/deviceAuth'
import { PIN_MAX_LENGTH, PIN_MIN_LENGTH } from '../kernel/auth/pinRules'
import { useSession } from '../kernel/auth/useSession'
import { useT } from '../kernel/i18n/useT'
import { BigButton } from '../kernel/ui/BigButton'
import { BrandLockup } from '../kernel/ui/BrandLockup'
import { NumericKeypad, PinDots } from '../kernel/ui/NumericKeypad'
import { colors, spacing } from '../kernel/ui/theme'

/** Pantalla 1 (parte 2): elegir usuario del aparato y teclear su PIN (docs/mobile/app-almacen-plan.md §2). Sin
 *  contraseña ni MFA en el aparato (decisión ratificada). 2026-09-30: si el teléfono está registrado en varias compañías,
 *  primero se elige la compañía. */
export default function LoginScreen() {
  const { t } = useT()
  const router = useRouter()
  const { device, devices } = useSession()
  const [users, setUsers] = useState<DeviceUser[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [selected, setSelected] = useState<DeviceUser | null>(null)
  const [pin, setPin] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const fetchUsers = useCallback(() => {
    if (!device) return
    fetchDeviceUsers(device.devicePublicId, device.deviceSecret)
      .then(setUsers)
      .catch((err: unknown) => setLoadError(err instanceof ApiError ? err.title : t('errors.generic')))
  }, [device, t])

  useEffect(() => {
    fetchUsers()
  }, [fetchUsers])

  function retry() {
    setLoadError(null)
    setUsers(null)
    fetchUsers()
  }

  async function submit() {
    if (!device || !selected) return
    setError(null)
    setBusy(true)
    try {
      await loginWithPin(device.devicePublicId, device.deviceSecret, selected.userId, pin, selected.fullName)
      router.replace('/home')
    } catch (err) {
      setError(err instanceof ApiError ? err.title : t('errors.generic'))
      setPin('')
    } finally {
      setBusy(false)
    }
  }

  if (!device) {
    return (
      <View style={styles.fill}>
        <BrandLockup />
        <Text style={styles.title}>{t('login.chooseCompany')}</Text>
        <ScrollView contentContainerStyle={styles.list}>
          {devices.map((d) => (
            <Pressable
              key={d.devicePublicId}
              accessibilityRole="button"
              onPress={() => {
                setUsers(null)
                setLoadError(null)
                void selectDevice(d.devicePublicId)
              }}
              style={styles.userRow}
            >
              <Text style={styles.userName}>{d.tenantName}</Text>
            </Pressable>
          ))}
        </ScrollView>
        <BigButton label={t('login.addCompany')} variant="secondary" onPress={() => router.push('/enroll')} />
      </View>
    )
  }

  if (selected) {
    return (
      <View style={styles.fill}>
        <View style={styles.pinArea}>
          <Text style={styles.title}>{selected.fullName}</Text>
          <Text style={styles.subtitle}>{t('login.pinLabel')}</Text>
          <PinDots length={PIN_MAX_LENGTH} filled={pin.length} />
          <NumericKeypad value={pin} onChange={setPin} maxLength={PIN_MAX_LENGTH} />
          {error ? <Text style={styles.error}>{error}</Text> : null}
          <View style={styles.pinActions}>
            <BigButton label={t('common.back')} variant="secondary" onPress={() => { setSelected(null); setPin(''); setError(null) }} />
            <BigButton label={t('login.submit')} onPress={submit} loading={busy} disabled={pin.length < PIN_MIN_LENGTH} />
          </View>
        </View>
      </View>
    )
  }

  return (
    <View style={styles.fill}>
      <BrandLockup />
      {devices.length > 1 ? <Text style={styles.subtitle}>{device.tenantName}</Text> : null}
      <Text style={styles.title}>{t('login.chooseUser')}</Text>
      {users === null && !loadError ? (
        <View style={styles.center}>
          <ActivityIndicator size="large" color={colors.brand} />
          <Text style={styles.subtitle}>{t('login.loadingUsers')}</Text>
        </View>
      ) : loadError ? (
        <View style={styles.center}>
          <Text style={styles.error}>{loadError}</Text>
          <BigButton label={t('common.retry')} onPress={retry} />
        </View>
      ) : users?.length === 0 ? (
        <Text style={styles.subtitle}>{t('login.noUsers')}</Text>
      ) : (
        <FlatList
          data={users ?? []}
          keyExtractor={(u) => String(u.userId)}
          contentContainerStyle={styles.list}
          renderItem={({ item }) => (
            <Pressable accessibilityRole="button" onPress={() => setSelected(item)} style={styles.userRow}>
              <View style={styles.avatar}>
                <Text style={styles.avatarLabel}>{item.initials}</Text>
              </View>
              <Text style={styles.userName}>{item.fullName}</Text>
            </Pressable>
          )}
        />
      )}
      {devices.length > 1 ? (
        <BigButton label={t('login.changeCompany')} variant="secondary" onPress={() => void selectDevice(null)} />
      ) : null}
      <BigButton label={t('login.addCompany')} variant="secondary" onPress={() => router.push('/enroll')} />
    </View>
  )
}

const styles = StyleSheet.create({
  fill: { flex: 1, backgroundColor: colors.bg, padding: spacing.lg, gap: spacing.lg },
  title: { color: colors.text, fontSize: 24, fontWeight: '700' },
  subtitle: { color: colors.muted, fontSize: 16, textAlign: 'center' },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: spacing.md },
  list: { gap: spacing.sm },
  userRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    minHeight: 64,
    paddingHorizontal: spacing.md,
    backgroundColor: colors.panelAlt,
    borderRadius: 12,
  },
  avatar: { width: 44, height: 44, borderRadius: 22, backgroundColor: colors.brand, alignItems: 'center', justifyContent: 'center' },
  avatarLabel: { color: colors.text, fontWeight: '700' },
  userName: { color: colors.text, fontSize: 18, fontWeight: '600' },
  pinArea: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: spacing.lg },
  pinActions: { flexDirection: 'row', gap: spacing.md },
  error: { color: colors.error, fontSize: 16, textAlign: 'center' },
})
