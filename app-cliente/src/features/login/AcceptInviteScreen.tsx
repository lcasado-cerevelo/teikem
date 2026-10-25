// /accept-invite?email=…&token=… — el enlace del correo de la invitación al portal (la del usuario del cliente y la de su consignatario). La persona elige su contraseña y pasa a
// entrar. El enlace es de un solo uso y vence; el servidor responde lo mismo exista o no la invitación, y valida la contraseña (mínimo 12 caracteres, sin claves filtradas).
import { Link, useLocalSearchParams } from 'expo-router'
import { useState } from 'react'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { Button, Field, Muted, Notice, Screen, Title, useColors } from '../../kernel/ui/ui'
import { PasswordField } from '../../kernel/ui/PasswordField'
import { acceptInvite } from '../portalApi'

/** Mínimo que pide el servidor (NIST: ≥ 12, sin reglas de composición); se avisa antes de ir al servidor. */
export const INVITE_PASSWORD_MIN = 12

export function AcceptInviteScreen() {
  const colors = useColors()
  const { t } = useT()
  const { email, token } = useLocalSearchParams<{ email?: string; token?: string }>()
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState(false)

  const linkOk = !!email && !!token
  const login = (
    <Link href="/login" style={{ color: colors.link, fontSize: 16, fontWeight: '600', paddingVertical: 12 }}>
      {t('invite.goLogin')}
    </Link>
  )

  async function submit() {
    setError(null)
    if (password.length < INVITE_PASSWORD_MIN) return setError(t('invite.tooShort', { min: INVITE_PASSWORD_MIN }))
    if (password !== confirm) return setError(t('invite.mismatch'))
    setBusy(true)
    try {
      await acceptInvite(String(email), String(token), password)
      setDone(true)
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  if (!linkOk) {
    return (
      <Screen>
        <Title>{t('invite.title')}</Title>
        <Notice>{t('invite.invalid')}</Notice>
        {login}
      </Screen>
    )
  }
  if (done) {
    return (
      <Screen>
        <Title>{t('invite.title')}</Title>
        <Notice tone="ok">{t('invite.done')}</Notice>
        <Muted>{t('invite.doneNext')}</Muted>
        {login}
      </Screen>
    )
  }
  return (
    <Screen>
      <Title>{t('invite.title')}</Title>
      <Muted>{t('invite.subtitle', { email: String(email) })}</Muted>
      {!!error && <Notice>{error}</Notice>}
      <PasswordField label={t('invite.password')} value={password} onChangeText={setPassword} autoComplete="new-password" />
      <PasswordField label={t('invite.confirm')} value={confirm} onChangeText={setConfirm} autoComplete="new-password" />
      <Muted>{t('invite.rule', { min: INVITE_PASSWORD_MIN })}</Muted>
      <Button title={t('invite.submit')} busy={busy} onPress={() => void submit()} />
      {login}
    </Screen>
  )
}
