// «Poner en mi escritorio» (web): el navegador (Chrome, Edge, Android) deja instalar el portal como app, con ícono en el escritorio o la pantalla de inicio; este botón dispara esa instalación. En iPhone y iPad el navegador
// no lo permite con un botón: se muestra la instrucción «Compartir → Añadir a pantalla de inicio». Si ya está instalada (se abre como app), no se muestra nada.
import { useEffect, useState } from 'react'
import { Text } from 'react-native'

import { useT } from '../../kernel/i18n/useT'
import { Button, Card, Muted } from '../../kernel/ui/ui'

interface InstallEvent extends Event {
  prompt: () => Promise<void>
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>
}

function isStandalone(): boolean {
  return window.matchMedia?.('(display-mode: standalone)').matches || (navigator as unknown as { standalone?: boolean }).standalone === true
}

function isIos(): boolean {
  return /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1)
}

export function InstallPrompt() {
  const { t } = useT()
  const [event, setEvent] = useState<InstallEvent | null>(null)
  const [installed, setInstalled] = useState(false)
  const ios = typeof navigator !== 'undefined' && isIos()

  useEffect(() => {
    const onPrompt = (e: Event) => {
      e.preventDefault()
      setEvent(e as InstallEvent)
    }
    const onInstalled = () => setInstalled(true)
    window.addEventListener('beforeinstallprompt', onPrompt)
    window.addEventListener('appinstalled', onInstalled)
    return () => {
      window.removeEventListener('beforeinstallprompt', onPrompt)
      window.removeEventListener('appinstalled', onInstalled)
    }
  }, [])

  if (typeof window === 'undefined' || isStandalone() || installed) return null
  if (!event && !ios) return null
  return (
    <Card>
      <Text style={{ fontWeight: '700' }}>{t('install.title')}</Text>
      {event ? (
        <>
          <Muted>{t('install.help')}</Muted>
          <Button
            title={t('install.button')}
            variant="secondary"
            onPress={() => {
              void event.prompt().then(() => event.userChoice).then((c) => {
                if (c.outcome === 'accepted') setInstalled(true)
                setEvent(null)
              })
            }}
          />
        </>
      ) : (
        <Muted>{t('install.ios')}</Muted>
      )}
    </Card>
  )
}
