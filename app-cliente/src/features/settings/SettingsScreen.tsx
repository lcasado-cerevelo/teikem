// Ajustes: la cuenta, cambiar de cliente (si tiene acceso a varios), idioma, «Abrir con huella» (solo en el teléfono, con lector y una huella registrada), «No confiar en esta computadora» (la misma preferencia del login),
// mis computadoras de confianza (con «Quitar») y cerrar sesión.
import { useRouter } from 'expo-router'
import { useEffect, useState } from 'react'
import { Text, View } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { useAppLock } from '../../kernel/auth/AppLock'
import { getBiometricSupport, promptBiometric } from '../../kernel/auth/biometric'
import type { BiometricSupport } from '../../kernel/auth/lockRules'
import { logout, switchClient } from '../../kernel/auth/portalAuth'
import { getNoTrust, setNoTrust } from '../../kernel/auth/preferences'
import { useT } from '../../kernel/i18n/useT'
import { Button, Card, Check, Chip, colors, Muted, Notice, Screen, Spinner, Title } from '../../kernel/ui/ui'
import { useAsync } from '../../kernel/ui/useAsync'
import { formatDateTime } from '../format'
import { getMe, listTrustedBrowsers, revokeTrustedBrowser } from '../portalApi'

export function SettingsScreen() {
  const { t, lang, setLang } = useT()
  const router = useRouter()
  const lock = useAppLock()
  const me = useAsync(getMe, [])
  const computers = useAsync(listTrustedBrowsers, [])
  const [support, setSupport] = useState<BiometricSupport>('unavailable')
  const [noTrust, setNoTrustState] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    void getBiometricSupport().then(setSupport)
    void getNoTrust().then(setNoTrustState)
  }, [])

  async function toggleBiometric(value: boolean) {
    setError(null)
    // para activarla hay que identificarse una vez: así se sabe que el lector funciona y que es el dueño del aparato
    if (value && !(await promptBiometric())) return
    await lock.setEnabled(value)
  }

  async function onSwitch(clientPublicId: string) {
    setBusy(true)
    setError(null)
    try {
      await switchClient(clientPublicId)
      me.reload()
      router.replace('/home')
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  async function onRevoke(id: string) {
    setError(null)
    try {
      await revokeTrustedBrowser(id)
      computers.reload()
    } catch (e) {
      setError(apiErrorMessage(e))
    }
  }

  const clients = me.data?.clients ?? []
  return (
    <Screen>
      <Title>{t('settings.title')}</Title>
      {!!error && <Notice>{error}</Notice>}

      <Card>
        <Text style={{ fontWeight: '700', color: colors.text }}>{me.data?.fullName ?? '—'}</Text>
        <Muted>{me.data?.email ?? ''}</Muted>
        {!!me.data && <Chip label={me.data.currentClient?.name ?? ''} tone="info" />}
      </Card>

      {clients.length > 1 && (
        <Card>
          <Text style={{ fontWeight: '700', color: colors.text }}>{t('settings.switchClient')}</Text>
          {clients.map((c) => (
            <Button key={c.clientPublicId} title={c.name ?? ''} variant={c.isCurrent ? 'primary' : 'secondary'} disabled={!!c.isCurrent || busy} onPress={() => void onSwitch(c.clientPublicId as string)} />
          ))}
        </Card>
      )}

      <Card>
        <Text style={{ fontWeight: '700', color: colors.text }}>{t('settings.language')}</Text>
        <View style={{ flexDirection: 'row', gap: 8 }}>
          <View style={{ flex: 1 }}><Button title="Español" variant={lang === 'es' ? 'primary' : 'secondary'} onPress={() => setLang('es')} /></View>
          <View style={{ flex: 1 }}><Button title="English" variant={lang === 'en' ? 'primary' : 'secondary'} onPress={() => setLang('en')} /></View>
        </View>
      </Card>

      <Card>
        <Text style={{ fontWeight: '700', color: colors.text }}>{t('settings.security')}</Text>
        {support === 'ready' ? (
          <Check label={t('settings.biometric')} help={t('settings.biometricHelp')} value={lock.enabled} onChange={(v) => void toggleBiometric(v)} testID="settings-biometric" />
        ) : (
          <Muted>{support === 'not_enrolled' ? t('settings.biometricNotEnrolled') : t('settings.biometricUnavailable')}</Muted>
        )}
        <Check
          label={t('login.noTrust')}
          help={t('login.noTrustHelp')}
          value={noTrust}
          onChange={(v) => {
            setNoTrustState(v)
            void setNoTrust(v)
          }}
          testID="settings-notrust"
        />
      </Card>

      <Card>
        <Text style={{ fontWeight: '700', color: colors.text }}>{t('settings.computers')}</Text>
        <Muted>{t('settings.computersHelp')}</Muted>
        {computers.loading && !computers.data && <Spinner />}
        {!!computers.data && computers.data.length === 0 && <Muted>{t('settings.computersEmpty')}</Muted>}
        {(computers.data ?? []).map((c) => (
          <View key={c.publicId} style={{ gap: 4, paddingVertical: 6, borderTopWidth: 1, borderTopColor: colors.line }}>
            <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
              <Text style={{ fontWeight: '600', color: colors.text }}>{c.label}</Text>
              {!!c.isCurrent && <Chip label={t('settings.thisComputer')} tone="info" />}
            </View>
            <Muted>{t('settings.computerExpires', { date: formatDateTime(c.expiresAtUtc, lang) })}</Muted>
            <Button title={t('settings.remove')} variant="secondary" onPress={() => void onRevoke(c.publicId as string)} />
          </View>
        ))}
      </Card>

      <Button title={t('settings.logout')} variant="danger" onPress={() => void logout().then(() => router.replace('/login'))} testID="settings-logout" />
    </Screen>
  )
}
