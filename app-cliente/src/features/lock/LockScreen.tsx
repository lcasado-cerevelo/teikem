// Pantalla de bloqueo: «Abrir con huella». Al entrar pide la huella (o el PIN del teléfono) solo; si falla puede reintentar. Tras 5 fallos seguidos, o con «Usar mi contraseña», se cierra la sesión del aparato y se vuelve al login.
import { useRouter } from 'expo-router'
import { useCallback, useEffect, useRef, useState } from 'react'

import { promptBiometric } from '../../kernel/auth/biometric'
import { MAX_BIOMETRIC_FAILURES } from '../../kernel/auth/lockRules'
import { logout } from '../../kernel/auth/portalAuth'
import { useT } from '../../kernel/i18n/useT'
import { Button, Muted, Notice, Screen, Title } from '../../kernel/ui/ui'

export function LockScreen({ onUnlock }: { onUnlock: () => void }) {
  const { t } = useT()
  const router = useRouter()
  const [failures, setFailures] = useState(0)
  const [busy, setBusy] = useState(false)
  const asked = useRef(false)
  const failuresRef = useRef(0)

  const goPassword = useCallback(async () => {
    await logout()
    router.replace('/login')
  }, [router])

  const tryBiometric = useCallback(async () => {
    setBusy(true)
    const ok = await promptBiometric()
    setBusy(false)
    if (ok) {
      onUnlock()
      return
    }
    const next = failuresRef.current + 1
    failuresRef.current = next
    setFailures(next)
    if (next >= MAX_BIOMETRIC_FAILURES) void goPassword()
  }, [onUnlock, goPassword])

  // al llegar a la pantalla se pide la huella sin que haya que tocar nada
  useEffect(() => {
    if (asked.current) return
    asked.current = true
    void tryBiometric()
  }, [tryBiometric])

  return (
    <Screen>
      <Title>{t('lock.title')}</Title>
      <Muted>{t('lock.help')}</Muted>
      {failures > 0 && <Notice tone="warn">{t('lock.failed', { left: MAX_BIOMETRIC_FAILURES - failures })}</Notice>}
      <Button title={t('lock.unlock')} busy={busy} onPress={() => void tryBiometric()} testID="lock-unlock" />
      <Button title={t('lock.goPassword')} variant="secondary" onPress={() => void goPassword()} testID="lock-password" />
    </Screen>
  )
}
