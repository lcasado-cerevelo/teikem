import { useCallback, useEffect, useState } from 'react'
import { ActivityIndicator, Alert, FlatList, Pressable, ScrollView, StyleSheet, Text, useWindowDimensions, View } from 'react-native'
import { useRouter } from 'expo-router'
import { Swipeable } from 'react-native-gesture-handler'

import { ApiError } from '../kernel/api/client'
import { pendingForCompany, removeCompany } from '../kernel/auth/companies'
import { type DeviceIdentity, selectDevice } from '../kernel/auth/session'
import { type DeviceUser, fetchDeviceUsers, loginWithPin } from '../kernel/auth/deviceAuth'
import { PIN_MAX_LENGTH, PIN_MIN_LENGTH } from '../kernel/auth/pinRules'
import { useSession } from '../kernel/auth/useSession'
import { useT } from '../kernel/i18n/useT'
import { BigButton } from '../kernel/ui/BigButton'
import { BrandLockup } from '../kernel/ui/BrandLockup'
import { NumericKeypad, PIN_COMPACT_HEIGHT, PinDots } from '../kernel/ui/NumericKeypad'
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
  const { height } = useWindowDimensions()

  const fetchUsers = useCallback(() => {
    if (!device) return
    fetchDeviceUsers(device.devicePublicId, device.deviceSecret)
      .then(setUsers)
      .catch((err: unknown) => {
        // 401 = el servidor ya no conoce este aparato (base reiniciada o aparato desactivado): se quita la compañía sin preguntar.
        if (err instanceof ApiError && err.status === 401) void removeCompany(device)
        else setLoadError(err instanceof ApiError ? err.title : t('errors.generic'))
      })
  }, [device, t])

  // Sin ninguna compañía registrada (se quitaron todas) → a registrar una.
  useEffect(() => {
    if (devices.length === 0) router.replace('/enroll')
  }, [devices.length, router])

  /** Quitar una compañía a mano (deslizando su fila a la izquierda): pide confirmación y avisa si hay capturas sin enviar. */
  function askRemove(d: DeviceIdentity) {
    const pending = pendingForCompany(d)
    const body = [t('login.removeCompanyBody'), pending > 0 ? t('login.removeCompanyPending', { count: pending }) : null].filter(Boolean).join('\n')
    Alert.alert(t('login.removeCompanyTitle', { name: d.tenantName }), body, [
      { text: t('common.cancel'), style: 'cancel' },
      { text: t('login.removeCompany'), style: 'destructive', onPress: () => void removeCompany(d) },
    ])
  }

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
            <Swipeable
              key={d.devicePublicId}
              overshootRight={false}
              renderRightActions={() => (
                <View style={styles.removeAction}>
                  <Text style={styles.removeLabel}>{t('login.removeCompany')}</Text>
                </View>
              )}
              onSwipeableOpen={(_direction, swipeable) => {
                swipeable.close()
                askRemove(d)
              }}
            >
              <Pressable
                accessibilityRole="button"
                onPress={() => {
                  setUsers(null)
                  setLoadError(null)
                  void selectDevice(d.devicePublicId)
                }}
                onLongPress={() => askRemove(d)}
                style={styles.userRow}
              >
                <Text style={styles.userName}>{d.tenantName}</Text>
              </Pressable>
            </Swipeable>
          ))}
          <Text style={styles.hint}>{t('login.swipeHint')}</Text>
        </ScrollView>
        <BigButton label={t('login.addCompany')} variant="secondary" onPress={() => router.push('/enroll')} />
      </View>
    )
  }

  // Lote A9 (pruebas en el Zebra, 2026-10-06): poner el PIN. Antes era un View fijo centrado (`justifyContent: center`) con el teclado de
  // 4 × 72 dp: en la pantalla chica del Zebra el contenido medía más que la pantalla, se cortaba abajo (Volver/Entrar a la mitad) y no
  // había desplazamiento. El margen inferior del aparato sí se aplicaba (esta pantalla está dentro de app/_layout.tsx); faltaba poder
  // desplazar. Ahora es un ScrollView que centra cuando cabe y se desplaza cuando no, y en pantallas bajas el teclado del PIN se
  // compacta (teclas de 60 dp, siguen siendo más grandes que el mínimo de 56) para que en lo posible no haga falta desplazar.
  if (selected) {
    const compact = height < PIN_COMPACT_HEIGHT
    return (
      <ScrollView
        style={styles.pinScroll}
        contentContainerStyle={[styles.pinArea, compact && styles.pinAreaCompact]}
        keyboardShouldPersistTaps="handled"
        testID="pin-scroll"
      >
        <Text style={styles.title}>{selected.fullName}</Text>
        <Text style={styles.subtitle}>{t('login.pinLabel')}</Text>
        <PinDots length={PIN_MAX_LENGTH} filled={pin.length} />
        <NumericKeypad value={pin} onChange={setPin} maxLength={PIN_MAX_LENGTH} keySize={compact ? 60 : undefined} />
        {error ? <Text style={styles.error}>{error}</Text> : null}
        <View style={styles.pinActions}>
          <View style={styles.pinAction}>
            <BigButton label={t('common.back')} variant="secondary" onPress={() => { setSelected(null); setPin(''); setError(null) }} />
          </View>
          <View style={styles.pinAction}>
            <BigButton label={t('login.submit')} onPress={submit} loading={busy} disabled={pin.length < PIN_MIN_LENGTH} />
          </View>
        </View>
      </ScrollView>
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
  removeAction: { justifyContent: 'center', alignItems: 'center', minWidth: 96, paddingHorizontal: spacing.md, backgroundColor: colors.error, borderRadius: 12 },
  removeLabel: { color: colors.text, fontSize: 16, fontWeight: '700' },
  hint: { color: colors.muted, fontSize: 14, textAlign: 'center' },
  pinScroll: { flex: 1, backgroundColor: colors.bg },
  // flexGrow (no flex): centrado si cabe, y si no cabe el contenido crece y se desplaza; el padding de abajo deja el último botón completo
  pinArea: { flexGrow: 1, alignItems: 'center', justifyContent: 'center', gap: spacing.lg, padding: spacing.lg, paddingBottom: spacing.xl },
  pinAreaCompact: { gap: spacing.md, paddingTop: spacing.md, paddingBottom: spacing.md },
  // Volver y Entrar del mismo ancho, juntos ocupan el ancho del teclado (caben en 320 dp)
  pinActions: { flexDirection: 'row', gap: spacing.md, alignSelf: 'stretch', maxWidth: 360, width: '100%' },
  pinAction: { flex: 1 },
  error: { color: colors.error, fontSize: 16, textAlign: 'center' },
})
