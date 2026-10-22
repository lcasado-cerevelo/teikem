// Segundo paso del acceso: el código de la app de autenticación (o uno de recuperación). La primera vez la cuenta inscribe su MFA (secreto + enlace para la app, confirmar con un código y guardar los códigos de recuperación).
// El código se recuerda 30 días en esta computadora de confianza (salvo «No confiar en esta computadora»).
import { useRouter } from 'expo-router'
import { useEffect, useState } from 'react'
import { Linking, Text, View } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { cancelMfa, confirmMfa, enrollMfa, getChallenge, verifyMfa, type MfaEnrollResultDto } from '../../kernel/auth/portalAuth'
import { useT } from '../../kernel/i18n/useT'
import { QrCode } from '../../kernel/ui/QrCode'
import { Button, Card, Field, Muted, Notice, Screen, Title } from '../../kernel/ui/ui'

type Step = 'verify' | 'enroll-start' | 'enroll-confirm' | 'recovery'

export function MfaScreen() {
  const { t } = useT()
  const router = useRouter()
  const challenge = getChallenge()
  const [step, setStep] = useState<Step>(challenge?.enrollmentRequired ? 'enroll-start' : 'verify')
  const [code, setCode] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [enroll, setEnroll] = useState<MfaEnrollResultDto | null>(null)
  const [recovery, setRecovery] = useState<string[]>([])

  // sin paso pendiente (por ejemplo, al recargar la página) se vuelve al login
  useEffect(() => {
    if (!challenge && step !== 'recovery') router.replace('/login')
  }, [challenge, step, router])

  async function run(action: () => Promise<void>) {
    setBusy(true)
    setError(null)
    try {
      await action()
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  const cancel = () => {
    cancelMfa()
    router.replace('/login')
  }

  const onVerify = () =>
    run(async () => {
      const outcome = await verifyMfa(code.trim())
      if (outcome.status === 'ok') router.replace('/home')
    })

  if (step === 'enroll-start') {
    return (
      <Screen>
        <Title>{t('mfa.enrollTitle')}</Title>
        <Muted>{t('mfa.enrollSubtitle')}</Muted>
        {!!error && <Notice>{error}</Notice>}
        <Button title={t('mfa.enrollStart')} busy={busy} onPress={() => void run(async () => { setEnroll(await enrollMfa()); setStep('enroll-confirm') })} />
        <Button title={t('common.cancel')} variant="link" onPress={cancel} />
      </Screen>
    )
  }

  if (step === 'enroll-confirm' && enroll) {
    return (
      <Screen>
        <Title>{t('mfa.enrollTitle')}</Title>
        <Muted>{t('mfa.enrollScan')}</Muted>
        {!!error && <Notice>{error}</Notice>}
        {!!enroll.otpAuthUri && <QrCode value={enroll.otpAuthUri} label={t('mfa.qrLabel')} />}
        <Card>
          <Muted>{t('mfa.secretLabel')}</Muted>
          <Text selectable testID="mfa-secret" style={{ fontSize: 18, fontWeight: '700', letterSpacing: 1 }}>{enroll.secret}</Text>
        </Card>
        {!!enroll.otpAuthUri && <Button title={t('mfa.openInApp')} variant="secondary" onPress={() => void Linking.openURL(enroll.otpAuthUri as string)} />}
        <Field label={t('mfa.code')} value={code} onChangeText={setCode} keyboardType="number-pad" autoComplete="one-time-code" maxLength={8} testID="mfa-code" />
        <Button title={t('mfa.confirm')} busy={busy} disabled={code.trim().length < 6} onPress={() => void run(async () => { setRecovery(await confirmMfa(code.trim())); setCode(''); setStep('recovery') })} />
        <Button title={t('common.cancel')} variant="link" onPress={cancel} />
      </Screen>
    )
  }

  if (step === 'recovery') {
    return (
      <Screen>
        <Title>{t('mfa.recoveryTitle')}</Title>
        <Notice tone="warn">{t('mfa.recoveryHelp')}</Notice>
        <Card>
          {recovery.map((c) => (
            <Text key={c} selectable style={{ fontSize: 16, fontFamily: 'monospace' }}>{c}</Text>
          ))}
        </Card>
        <Button title={t('mfa.recoverySaved')} onPress={() => { setStep('verify'); setError(null) }} />
      </Screen>
    )
  }

  return (
    <Screen>
      <View style={{ height: 8 }} />
      <Title>{t('mfa.verifyTitle')}</Title>
      <Muted>{t('mfa.verifySubtitle')}</Muted>
      {!!error && <Notice>{error}</Notice>}
      <Field label={t('mfa.code')} value={code} onChangeText={setCode} keyboardType="number-pad" autoComplete="one-time-code" maxLength={32} testID="mfa-code" onSubmitEditing={() => code.trim() && void onVerify()} />
      <Button title={t('mfa.verify')} busy={busy} disabled={code.trim().length < 6} onPress={() => void onVerify()} testID="mfa-verify" />
      <Button title={t('mfa.back')} variant="link" onPress={cancel} />
    </Screen>
  )
}
