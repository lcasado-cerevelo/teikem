// /confirm-browser?token=… — el enlace del correo confirma la computadora desde la que se intentó entrar; después se vuelve al login y se entra otra vez. El enlace es de un solo uso.
import { Link, useLocalSearchParams } from 'expo-router'
import { useEffect, useRef, useState } from 'react'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { Muted, Notice, Screen, Spinner, Title } from '../../kernel/ui/ui'
import { confirmBrowser } from '../portalApi'

export function ConfirmBrowserScreen() {
  const { t } = useT()
  const { token } = useLocalSearchParams<{ token?: string }>()
  const [state, setState] = useState<'working' | 'done' | { error: string }>('working')
  const sent = useRef(false)

  useEffect(() => {
    if (sent.current) return
    sent.current = true
    if (!token) {
      setState({ error: t('browser.confirmInvalid') })
      return
    }
    confirmBrowser(String(token)).then(() => setState('done')).catch((e) => setState({ error: apiErrorMessage(e) }))
  }, [token, t])

  return (
    <Screen>
      <Title>{t('browser.confirmTitle')}</Title>
      {state === 'working' && <Spinner />}
      {state === 'done' && (
        <>
          <Notice tone="ok">{t('browser.confirmDone')}</Notice>
          <Muted>{t('browser.confirmNext')}</Muted>
        </>
      )}
      {typeof state === 'object' && <Notice>{state.error}</Notice>}
      {state !== 'working' && (
        <Link href="/login" style={{ color: '#1D6FE8', fontSize: 16, fontWeight: '600', paddingVertical: 12 }}>
          {t('browser.goLogin')}
        </Link>
      )}
    </Screen>
  )
}
