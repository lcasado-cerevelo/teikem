import { useState } from 'react'
import { StyleSheet, Text, View } from 'react-native'
import { useRouter } from 'expo-router'

import { ApiError, getApiBaseUrl, setApiBaseUrl } from '../kernel/api/client'
import { enrollDevice } from '../kernel/auth/deviceAuth'
import { useSession } from '../kernel/auth/useSession'
import { useT } from '../kernel/i18n/useT'
import { BigButton } from '../kernel/ui/BigButton'
import { BrandLockup } from '../kernel/ui/BrandLockup'
import { KeyboardScreen, KeyboardScreenInput } from '../kernel/ui/KeyboardScreen'
import { colors, spacing } from '../kernel/ui/theme'

/** Pantalla 1 (parte 1): registrar este aparato con el código de un solo uso del administrador (docs/mobile/
 *  app-almacen-plan.md §2). El servidor se configura aquí la primera vez; después se cambia desde Sincronización.
 *  Los dos campos llevan `testID` porque el texto de la etiqueta y el `accessibilityLabel` del campo son iguales
 *  (Maestro, en app-almacen/e2e-maestro, necesita distinguir el campo del texto que solo lo describe). */
/** Dirección del servidor que trae el APK de fábrica (EXPO_PUBLIC_API_URL al compilar con scripts/construir-apk.ps1): el aparato
 *  ya la trae escrita; se puede cambiar. Sin ella (desarrollo), el campo arranca vacío. */
const DEFAULT_SERVER_URL = process.env.EXPO_PUBLIC_API_URL ?? ''

export default function EnrollScreen() {
  const { t } = useT()
  const router = useRouter()
  const { devices } = useSession()
  const [serverUrl, setServerUrl] = useState(getApiBaseUrl() || DEFAULT_SERVER_URL)
  const [code, setCode] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const serverValid = /^https?:\/\/.+/i.test(serverUrl.trim())

  async function submit() {
    setError(null)
    if (!serverValid) {
      setError(t('server.urlInvalid'))
      return
    }
    setApiBaseUrl(serverUrl)
    if (!code.trim()) return
    setBusy(true)
    try {
      await enrollDevice(code)
      router.replace('/login')
    } catch (err) {
      setError(err instanceof ApiError ? err.title : t('errors.generic'))
    } finally {
      setBusy(false)
    }
  }

  return (
    // Lote A9: KeyboardScreen (antes KeyboardAvoidingView sin `behavior` en Android + ScrollView): con la app de borde a borde, Android ya no
    // encoge la ventana al salir el teclado y el campo del código y el botón quedaban debajo del teclado.
    <KeyboardScreen style={styles.fill} contentContainerStyle={styles.content} testID="enroll-scroll">
      <BrandLockup />
      <Text style={styles.title}>{t('enroll.title')}</Text>

      <View style={styles.field}>
        <Text style={styles.label}>{t('server.urlLabel')}</Text>
        <KeyboardScreenInput
          value={serverUrl}
          onChangeText={setServerUrl}
          autoCapitalize="none"
          autoCorrect={false}
          keyboardType="url"
          placeholder="http://192.168.1.20:5000/"
          placeholderTextColor={colors.muted}
          style={styles.input}
          accessibilityLabel={t('server.urlLabel')}
          testID="server-url-input"
        />
        <Text style={styles.help}>{t('server.urlHelp')}</Text>
      </View>

      <View style={styles.field}>
        <Text style={styles.label}>{t('enroll.codeLabel')}</Text>
        <KeyboardScreenInput
          value={code}
          onChangeText={(v) => setCode(v.toUpperCase())}
          autoCapitalize="characters"
          autoCorrect={false}
          maxLength={8}
          placeholder="ABCD1234"
          placeholderTextColor={colors.muted}
          style={styles.input}
          accessibilityLabel={t('enroll.codeLabel')}
          testID="enroll-code-input"
        />
        <Text style={styles.help}>{t('enroll.codeHelp')}</Text>
      </View>

      {error ? <Text style={styles.error}>{error}</Text> : null}

      <BigButton label={t('enroll.submit')} onPress={submit} loading={busy} disabled={!code.trim()} />
      {devices.length > 0 ? (
        <BigButton label={t('common.back')} variant="secondary" onPress={() => router.replace('/login')} />
      ) : null}
    </KeyboardScreen>
  )
}

const styles = StyleSheet.create({
  fill: { flex: 1, backgroundColor: colors.bg },
  content: { padding: spacing.lg, gap: spacing.lg },
  title: { color: colors.text, fontSize: 24, fontWeight: '700', marginBottom: spacing.md },
  field: { gap: spacing.xs },
  label: { color: colors.text, fontSize: 16, fontWeight: '600' },
  input: {
    minHeight: 56,
    borderWidth: 2,
    borderColor: colors.line,
    borderRadius: 12,
    paddingHorizontal: spacing.md,
    fontSize: 18,
    color: colors.text,
    backgroundColor: colors.panelAlt,
  },
  help: { color: colors.muted, fontSize: 16 },
  error: { color: colors.error, fontSize: 16 },
})
