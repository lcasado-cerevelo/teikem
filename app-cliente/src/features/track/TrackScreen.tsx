// «¿Cómo va mi orden?» — página PÚBLICA (sin sesión) en /track/<token>: el enlace del aviso trae el token propio de la orden y se ve SOLO el avance del paquete (barra con las etapas y el último movimiento);
// nunca nombres, direcciones, teléfonos ni montos. La página no recibe datos: ya no se busca por número de conduce.
import { Link, useLocalSearchParams } from 'expo-router'
import { useEffect, useState } from 'react'
import { Text } from 'react-native'

import { ApiError, apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { PreferencesBar } from '../../kernel/ui/PreferencesBar'
import { Card, Muted, Notice, Screen, Spinner, Title, useColors } from '../../kernel/ui/ui'
import { formatDateTime } from '../format'
import { type PublicTrackDto, trackByLink } from '../portalApi'
import { ProgressBar, ProgressSteps } from '../progress/ProgressBar'
import { toProgressInfo } from '../progress/progressInfo'

export function TrackScreen() {
  const colors = useColors()
  const { t, lang } = useT()
  const { token } = useLocalSearchParams<{ token?: string }>()
  const [result, setResult] = useState<PublicTrackDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(true)

  useEffect(() => {
    let alive = true
    setBusy(true)
    setError(null)
    trackByLink(String(token ?? ''), lang)
      .then((data) => alive && setResult(data))
      .catch((e: unknown) => {
        if (!alive) return
        setResult(null)
        setError(e instanceof ApiError && e.status === 429 ? t('track.tooMany') : apiErrorMessage(e))
      })
      .finally(() => alive && setBusy(false))
    return () => {
      alive = false
    }
  }, [token, lang, t])

  const progress = result ? toProgressInfo({ step: result.step, total: result.total, state: result.state, statusLabel: result.statusLabel }) : null
  return (
    <Screen>
      <Title>{t('track.title')}</Title>
      {busy && <Spinner />}
      {!!error && <Notice>{error}</Notice>}
      {!!result && !!progress && (
        <Card>
          <Muted>{result.companyName}</Muted>
          <Text style={{ fontSize: 20, fontWeight: '800', color: colors.text }} testID="track-result-waybill">{result.waybill}</Text>
          <ProgressBar progress={progress} />
          <ProgressSteps labels={(result.steps ?? []).map((s) => s.label ?? '')} progress={progress} />
          {!!result.lastUpdateUtc && <Muted>{t('track.lastUpdate', { date: formatDateTime(result.lastUpdateUtc, lang) })}</Muted>}
        </Card>
      )}
      <Muted>{t('track.privacy')}</Muted>
      <PreferencesBar />
      <Link href="/login" style={{ color: colors.link, fontSize: 15, fontWeight: '600', paddingVertical: 8 }}>
        {t('track.portalLink')}
      </Link>
    </Screen>
  )
}
