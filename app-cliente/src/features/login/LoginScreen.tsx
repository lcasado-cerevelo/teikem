// Login del portal: correo y contraseña, la casilla «No confiar en esta computadora» (que se RECUERDA: sale marcada la próxima vez), la pantalla de «Confirme esta computadora» (computadora nueva: se manda un correo)
// y el reto anti-robot cuando el servidor lo pide (tras varios fallos). Segundo paso (MFA) en /mfa.
import { useRouter } from 'expo-router'
import { useCallback, useEffect, useState } from 'react'
import { Linking, View } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { login, type BrowserConfirmationDto } from '../../kernel/auth/portalAuth'
import { getNoTrust, setNoTrust } from '../../kernel/auth/preferences'
import { useT } from '../../kernel/i18n/useT'
import { Button, Card, Check, Field, Muted, Notice, Screen, Title } from '../../kernel/ui/ui'
import { getAuthConfig } from '../portalApi'
import { Captcha } from './Captcha'
import { InstallPrompt } from './InstallPrompt'

export function LoginScreen() {
  const { t, lang, setLang } = useT()
  const router = useRouter()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [noTrust, setNoTrustState] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState<BrowserConfirmationDto | null>(null)
  const [captchaSiteKey, setCaptchaSiteKey] = useState<string | null>(null)
  const [captchaToken, setCaptchaToken] = useState<string | null>(null)

  // la casilla se recuerda: sale como se dejó la última vez
  useEffect(() => {
    void getNoTrust().then(setNoTrustState)
  }, [])

  const onNoTrust = (value: boolean) => {
    setNoTrustState(value)
    void setNoTrust(value)
  }

  const onToken = useCallback((token: string | null) => setCaptchaToken(token), [])

  async function submit() {
    setBusy(true)
    setError(null)
    try {
      const outcome = await login(email, password, captchaToken ?? undefined)
      if (outcome.status === 'ok') router.replace('/home')
      else if (outcome.status === 'mfa_required') router.push('/mfa')
      else if (outcome.status === 'browser_confirmation_required') setPending(outcome.browser)
      else {
        // el servidor pide el reto anti-robot: se lee con qué llave del sitio y se muestra
        const config = await getAuthConfig()
        setCaptchaToken(null)
        setCaptchaSiteKey(config.captchaSiteKey ?? null)
        setError(t('captcha.required'))
      }
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  if (pending) {
    return (
      <Screen>
        <Title>{t('browser.title')}</Title>
        {pending.sent ? (
          <>
            <Muted>{t('browser.sentTo', { email: pending.email ?? '' })}</Muted>
            <Card>
              <Muted>{t('browser.sentHelp')}</Muted>
            </Card>
          </>
        ) : (
          <Notice>{pending.message ?? t('browser.notSent')}</Notice>
        )}
        {!!pending.devLink && <Button title={t('browser.devLink')} variant="link" onPress={() => void Linking.openURL(pending.devLink as string)} />}
        <Muted>{t('browser.afterConfirm')}</Muted>
        <Button title={t('browser.back')} onPress={() => setPending(null)} />
      </Screen>
    )
  }

  const canSubmit = email.trim().length > 3 && password.length > 0 && (!captchaSiteKey || !!captchaToken)
  return (
    <Screen>
      <View style={{ height: 8 }} />
      <Title>{t('login.title')}</Title>
      <Muted>{t('login.subtitle')}</Muted>
      {!!error && <Notice>{error}</Notice>}
      <Field label={t('login.email')} value={email} onChangeText={setEmail} autoCapitalize="none" autoComplete="email" keyboardType="email-address" textContentType="username" testID="login-email" />
      <Field label={t('login.password')} value={password} onChangeText={setPassword} secureTextEntry={!showPassword} autoCapitalize="none" autoComplete="current-password" textContentType="password" testID="login-password" onSubmitEditing={() => canSubmit && void submit()} />
      <Button title={showPassword ? t('login.hide') : t('login.show')} variant="link" onPress={() => setShowPassword((v) => !v)} />
      <Check label={t('login.noTrust')} help={t('login.noTrustHelp')} value={noTrust} onChange={onNoTrust} testID="login-notrust" />
      {!!captchaSiteKey && (
        <Card>
          <Muted>{t('captcha.help')}</Muted>
          <Captcha siteKey={captchaSiteKey} onToken={onToken} />
        </Card>
      )}
      <Button title={t('login.submit')} onPress={() => void submit()} busy={busy} disabled={!canSubmit} testID="login-submit" />
      <InstallPrompt />
      <Button title={lang === 'es' ? 'English' : 'Español'} variant="link" onPress={() => setLang(lang === 'es' ? 'en' : 'es')} />
    </Screen>
  )
}
