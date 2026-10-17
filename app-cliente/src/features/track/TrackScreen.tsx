// «¿Cómo va mi orden?» — página PÚBLICA (sin sesión) en /track/<identificador de la compañía>: se escribe el número de conduce y se ve SOLO el avance del paquete (barra con las etapas de la compañía y el último movimiento);
// nunca nombres, direcciones, teléfonos ni montos. El enlace lo da la compañía (también puede traer ?n=<conduce> para abrir ya con la consulta hecha).
import { Link, useLocalSearchParams } from 'expo-router'
import { useCallback, useEffect, useRef, useState } from 'react'
import { Text } from 'react-native'

import { ApiError, apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { Button, Card, colors, Field, Muted, Notice, Screen, Title } from '../../kernel/ui/ui'
import { formatDateTime } from '../format'
import { type PublicTrackDto, trackOrder } from '../portalApi'
import { ProgressBar, ProgressSteps } from '../progress/ProgressBar'
import { toProgressInfo } from '../progress/progressInfo'

export function TrackScreen() {
  const { t, lang } = useT()
  const { company, n } = useLocalSearchParams<{ company?: string; n?: string }>()
  const [waybill, setWaybill] = useState(typeof n === 'string' ? n : '')
  const [result, setResult] = useState<PublicTrackDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const seq = useRef(0)

  const search = useCallback(
    async (value: string) => {
      const w = value.trim()
      if (w.length < 3) return
      const mine = ++seq.current
      setBusy(true)
      setError(null)
      try {
        const data = await trackOrder(String(company ?? ''), w, lang)
        if (mine === seq.current) setResult(data)
      } catch (e) {
        if (mine !== seq.current) return
        setResult(null)
        setError(e instanceof ApiError && e.status === 429 ? t('track.tooMany') : apiErrorMessage(e))
      } finally {
        if (mine === seq.current) setBusy(false)
      }
    },
    [company, lang, t],
  )

  // con ?n=… se abre ya con la consulta hecha
  useEffect(() => {
    if (typeof n === 'string' && n.trim().length >= 3) void search(n)
    // solo al abrir (o si cambia el enlace)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [n, company])

  const progress = result ? toProgressInfo({ step: result.step, total: result.total, state: result.state, statusLabel: result.statusLabel }) : null
  return (
    <Screen>
      <Title>{t('track.title')}</Title>
      <Muted>{t('track.help')}</Muted>
      <Field label={t('track.waybill')} value={waybill} onChangeText={setWaybill} placeholder={t('track.placeholder')} autoCapitalize="characters" autoCorrect={false} returnKeyType="search" onSubmitEditing={() => void search(waybill)} testID="track-waybill" />
      <Button title={t('track.submit')} busy={busy} disabled={waybill.trim().length < 3} onPress={() => void search(waybill)} testID="track-submit" />
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
      <Link href="/login" style={{ color: colors.flow, fontSize: 15, fontWeight: '600', paddingVertical: 8 }}>
        {t('track.portalLink')}
      </Link>
    </Screen>
  )
}
