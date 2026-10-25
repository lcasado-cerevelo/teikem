// Mi perfil (Lote 120): quién soy (el nombre lo cambio yo; el correo es con el que entro y el rol lo da el administrador), los datos de mi empresa (solo lectura: los cambia la compañía) y el cambio de mi contraseña.
import { useEffect, useState } from 'react'
import { Text } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { Button, Card, Field, Muted, Notice, Screen, Spinner, Title, useColors } from '../../kernel/ui/ui'
import { PasswordField } from '../../kernel/ui/PasswordField'
import { changePassword, getProfile, saveProfile, type PortalProfileDto } from '../portalApi'

export function ProfileScreen() {
  const colors = useColors()
  const { t } = useT()
  const [profile, setProfile] = useState<PortalProfileDto | null>(null)
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)
  const [busy, setBusy] = useState(false)
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [again, setAgain] = useState('')
  const [pwError, setPwError] = useState<string | null>(null)
  const [pwDone, setPwDone] = useState(false)

  const load = () => {
    setError(null)
    getProfile().then((p) => { setProfile(p); setName(p.fullName ?? '') }).catch((e) => setError(apiErrorMessage(e)))
  }
  useEffect(load, [])

  async function onSaveName() {
    if (busy) return
    setBusy(true)
    setError(null)
    setSaved(false)
    try {
      const p = await saveProfile(name.trim())
      setProfile(p)
      setName(p.fullName ?? '')
      setSaved(true)
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  async function onChangePassword() {
    if (busy) return
    setPwDone(false)
    if (!current || !next) return setPwError(t('profile.passwordRequired'))
    if (next !== again) return setPwError(t('profile.passwordMismatch'))
    setPwError(null)
    setBusy(true)
    try {
      await changePassword(current, next)
      setPwDone(true)
      setCurrent('')
      setNext('')
      setAgain('')
    } catch (e) {
      setPwError(apiErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  const c = profile?.company
  return (
    <Screen>
      <Title>{t('profile.title')}</Title>
      {!!error && (
        <>
          <Notice>{error}</Notice>
          <Button title={t('common.retry')} variant="secondary" onPress={load} />
        </>
      )}
      {!profile && !error && <Spinner />}
      {!!profile && (
        <>
          <Card>
            <Text style={{ fontWeight: '700', color: colors.text }}>{t('profile.me')}</Text>
            <Field label={t('profile.name')} value={name} onChangeText={(v) => { setName(v); setSaved(false) }} maxLength={150} autoCapitalize="words" testID="profile-name" />
            <Muted>{t('profile.email', { email: profile.email ?? '' })}</Muted>
            <Muted>{t('profile.role', { role: profile.roleLabel ?? profile.roleCode ?? '' })}</Muted>
            <Muted>{t('profile.emailHelp')}</Muted>
            {saved && <Notice tone="ok">{t('profile.saved')}</Notice>}
            <Button title={t('profile.save')} onPress={() => void onSaveName()} disabled={busy || !name.trim() || name.trim() === (profile.fullName ?? '')} testID="profile-save" />
          </Card>
          {!!c && (
            <Card>
              <Text style={{ fontWeight: '700', color: colors.text }}>{t('profile.company')}</Text>
              <Text style={{ color: colors.text, fontWeight: '600' }}>{c.name}</Text>
              <Muted>{t('profile.companyCode', { code: c.code ?? '' })}</Muted>
              {!!c.legalName && <Muted>{t('profile.legalName', { name: c.legalName })}</Muted>}
              {!!c.taxId && <Muted>{t('profile.taxId', { id: c.taxId })}</Muted>}
              {!!c.line1 && <Muted>{[c.line1, c.line2, c.city, c.state, c.postalCode].filter(Boolean).join(', ')}</Muted>}
              <Muted>{t('profile.companyHelp')}</Muted>
            </Card>
          )}
          <Card>
            <Text style={{ fontWeight: '700', color: colors.text }}>{t('profile.password')}</Text>
            <Muted>{t('profile.passwordHelp')}</Muted>
            <PasswordField label={t('profile.passwordCurrent')} value={current} onChangeText={setCurrent} testID="profile-pw-current" />
            <PasswordField label={t('profile.passwordNew')} value={next} onChangeText={setNext} testID="profile-pw-new" />
            <PasswordField label={t('profile.passwordAgain')} value={again} onChangeText={setAgain} testID="profile-pw-again" />
            {!!pwError && <Notice>{pwError}</Notice>}
            {pwDone && <Notice tone="ok">{t('profile.passwordDone')}</Notice>}
            <Button title={t('profile.passwordSave')} variant="secondary" onPress={() => void onChangePassword()} disabled={busy} testID="profile-pw-save" />
          </Card>
        </>
      )}
    </Screen>
  )
}
