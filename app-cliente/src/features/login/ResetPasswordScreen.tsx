// /reset-password?email=…&token=… — el enlace del correo de «¿Olvidó su contraseña?» (Lote 219). La persona elige su contraseña nueva; quien tiene la verificación en dos pasos escribe además su código
// (de su app de autenticación o uno de recuperación): el enlace por sí solo NO basta, y si falta el código el servidor responde 400 `mfa_required` sin gastar el enlace.
import { Link, useLocalSearchParams } from 'expo-router'
import { useState } from 'react'

import { ApiError, apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { PasswordField } from '../../kernel/ui/PasswordField'
import { Button, Field, Muted, Notice, Screen, Title, useColors } from '../../kernel/ui/ui'
import { resetPassword } from '../portalApi'
import { INVITE_PASSWORD_MIN } from './AcceptInviteScreen'
import { LoginBrand } from './LoginBrand'

export function ResetPasswordScreen() {
  const colors = useColors()
  const { t } = useT()
  const { email, token } = useLocalSearchParams<{ email?: string; token?: string }>()
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [needMfa, setNeedMfa] = useState(false)
  const [mfaCode, setMfaCode] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState(false)

  const link = (to: '/login' | '/forgot-password', label: string) => (
    <Link href={to} style={{ color: colors.link, fontSize: 16, fontWeight: '600', paddingVertical: 12 }}>
      {label}
    </Link>
  )

  async function submit() {
    setError(null)
    if (password.length < INVITE_PASSWORD_MIN) return setError(t('invite.tooShort', { min: INVITE_PASSWORD_MIN }))
    if (password !== confirm) return setError(t('invite.mismatch'))
    setBusy(true)
    try {
      await resetPassword(String(email), String(token), password, needMfa ? mfaCode.trim() : undefined)
      setDone(true)
    } catch (e) {
      if (e instanceof ApiError && e.code === 'mfa_required') setNeedMfa(true)
      setError(apiErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  if (!email || !token) {
    return (
      <Screen>
        <LoginBrand />
        <Title>{t('reset.title')}</Title>
        <Notice>{t('reset.badLink')}</Notice>
        {link('/forgot-password', t('reset.askAgain'))}
      </Screen>
    )
  }
  if (done) {
    return (
      <Screen>
        <LoginBrand />
        <Title>{t('reset.doneTitle')}</Title>
        <Notice tone="ok">{t('reset.done')}</Notice>
        {link('/login', t('reset.goLogin'))}
      </Screen>
    )
  }
  return (
    <Screen>
      <LoginBrand />
      <Title>{t('reset.title')}</Title>
      <Muted>{t('reset.subtitle', { email: String(email) })}</Muted>
      {!!error && <Notice>{error}</Notice>}
      <PasswordField label={t('invite.password')} value={password} onChangeText={setPassword} autoComplete="new-password" testID="reset-password" />
      <PasswordField label={t('invite.confirm')} value={confirm} onChangeText={setConfirm} autoComplete="new-password" testID="reset-confirm" />
      <Muted>{t('invite.rule', { min: INVITE_PASSWORD_MIN })}</Muted>
      {needMfa && (
        <>
          <Field label={t('reset.mfaCode')} value={mfaCode} onChangeText={setMfaCode} autoCapitalize="none" autoComplete="one-time-code" keyboardType="number-pad" testID="reset-mfa" />
          <Muted>{t('reset.mfaHelp')}</Muted>
        </>
      )}
      <Button title={t('reset.submit')} onPress={() => void submit()} busy={busy} disabled={!password || !confirm || (needMfa && !mfaCode.trim())} testID="reset-submit" />
      {link('/forgot-password', t('reset.askAgain'))}
    </Screen>
  )
}
