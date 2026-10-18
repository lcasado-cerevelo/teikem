// Baja de los avisos (pública, sin sesión) en /unsubscribe/<token>: el enlace de cada aviso al consignatario. Pide confirmar con un botón (abrir el enlace no da de baja: así un filtro de correo no lo hace por usted).
import { useLocalSearchParams } from 'expo-router'
import { useState } from 'react'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { PreferencesBar } from '../../kernel/ui/PreferencesBar'
import { Button, Card, Muted, Notice, Screen, Title } from '../../kernel/ui/ui'
import { unsubscribeConsignee } from '../portalApi'

export function UnsubscribeScreen() {
  const { t } = useT()
  const { token } = useLocalSearchParams<{ token?: string }>()
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function confirm() {
    setBusy(true)
    setError(null)
    try {
      await unsubscribeConsignee(String(token ?? ''))
      setDone(true)
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Screen>
      <Title>{t('unsubscribe.title')}</Title>
      {done ? (
        <Notice tone="ok">{t('unsubscribe.done')}</Notice>
      ) : (
        <Card>
          <Muted>{t('unsubscribe.help')}</Muted>
          <Button title={t('unsubscribe.confirm')} variant="danger" busy={busy} onPress={() => void confirm()} testID="unsubscribe-confirm" />
        </Card>
      )}
      {!!error && <Notice>{error}</Notice>}
      <PreferencesBar />
    </Screen>
  )
}
