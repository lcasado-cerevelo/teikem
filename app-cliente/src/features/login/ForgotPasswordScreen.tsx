// «¿Olvidó su contraseña?» del portal (Lote 219): se escribe el correo y el servidor manda un enlace de un solo uso que abre /reset-password. La respuesta es SIEMPRE la misma exista o no el correo.
import { Link, useRouter } from 'expo-router'
import { useState } from 'react'
import { View } from 'react-native'

import { loginErrorMessage } from '../../kernel/api/loginErrors'
import { useT } from '../../kernel/i18n/useT'
import { PreferencesBar } from '../../kernel/ui/PreferencesBar'
import { Button, Field, Muted, Notice, Screen, Title, useColors } from '../../kernel/ui/ui'
import { forgotPassword } from '../portalApi'
import { LoginBrand } from './LoginBrand'

export function ForgotPasswordScreen() {
  const { t } = useT()
  const colors = useColors()
  const router = useRouter()
  const [email, setEmail] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [sent, setSent] = useState<{ message: string; link: string | null } | null>(null)

  async function submit() {
    setBusy(true)
    setError(null)
    try {
      setSent(await forgotPassword(email.trim()))
    } catch (e) {
      setError(loginErrorMessage(e, t))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Screen>
      <LoginBrand />
      <Title>{t('forgot.title')}</Title>
      {sent ? (
        <>
          <Notice tone="ok">{sent.message || t('forgot.sent')}</Notice>
          <Muted>{t('forgot.sentHelp')}</Muted>
          {!!sent.link && (
            <Link href={sent.link.replace(/^https?:\/\/[^/]+/, '') as never} style={{ color: colors.link, fontSize: 14 }} testID="forgot-devlink">
              {t('forgot.devLink')}
            </Link>
          )}
        </>
      ) : (
        <>
          <Muted>{t('forgot.subtitle')}</Muted>
          {!!error && <Notice>{error}</Notice>}
          <Field label={t('login.email')} value={email} onChangeText={setEmail} autoCapitalize="none" autoComplete="email" keyboardType="email-address" textContentType="username" testID="forgot-email" onSubmitEditing={() => email.trim().length > 3 && void submit()} />
          <Button title={t('forgot.submit')} onPress={() => void submit()} busy={busy} disabled={email.trim().length < 4} testID="forgot-submit" />
        </>
      )}
      <View style={{ height: 4 }} />
      <Button title={t('forgot.back')} variant="link" onPress={() => (router.canGoBack() ? router.back() : router.replace('/login'))} testID="forgot-back" />
      <PreferencesBar />
    </Screen>
  )
}
