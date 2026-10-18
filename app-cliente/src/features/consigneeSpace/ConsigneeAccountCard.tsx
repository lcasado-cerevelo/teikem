// La cuenta del consignatario con acceso propio en Ajustes (Lote 117): quién es, de qué cliente, y cómo recibe los avisos. El cliente decide si le avisa y por qué medio; el consignatario puede APAGARLOS cuando quiera y volver
// a prender solo el que apagó él mismo (si el cliente nunca lo prendió, o se dio de baja con el enlace de un aviso, no puede).
import { useState } from 'react'
import { Text } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { Card, Check, Chip, Muted, Notice, Spinner, useColors } from '../../kernel/ui/ui'
import { useAsync } from '../../kernel/ui/useAsync'
import { type ConsigneeChannelDto, getConsigneeMe, setConsigneeChannel } from '../portalApi'

export function ConsigneeAccountCard() {
  const colors = useColors()
  const { t } = useT()
  const me = useAsync(getConsigneeMe, [])
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function change(channel: 'EMAIL' | 'SMS', enabled: boolean) {
    setBusy(true)
    setError(null)
    try {
      await setConsigneeChannel(channel, enabled)
      me.reload()
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  const d = me.data
  function row(channel: 'EMAIL' | 'SMS', c: ConsigneeChannelDto | undefined) {
    if (!c) return null
    const label = t(channel === 'EMAIL' ? 'consigneeSpace.profile.email' : 'consigneeSpace.profile.sms', { value: c.contact ?? t('consigneeSpace.profile.none') })
    const help = c.enabled ? undefined : c.unsubscribed ? t('consigneeSpace.profile.unsubscribed') : c.canTurnOn ? t('consigneeSpace.profile.canTurnOn') : t('consigneeSpace.profile.clientOnly')
    return (
      <Check
        key={channel}
        label={label}
        help={help}
        value={!!c.enabled}
        disabled={busy || (!c.enabled && !c.canTurnOn)}
        onChange={(v) => void change(channel, v)}
        testID={`consignee-${channel.toLowerCase()}`}
      />
    )
  }

  return (
    <>
      <Card>
        <Text style={{ fontWeight: '700', color: colors.text }}>{d?.fullName ?? d?.consigneeName ?? '—'}</Text>
        <Muted>{d?.email ?? ''}</Muted>
        {!!d && <Muted>{d.consigneeName}{d.city ? ` · ${d.city}` : ''}</Muted>}
        {!!d && <Chip label={t('consigneeSpace.profile.client', { name: d.clientName ?? '' })} tone="info" />}
      </Card>
      <Card>
        <Text style={{ fontWeight: '700', color: colors.text }}>{t('consigneeSpace.profile.title')}</Text>
        <Muted>{t('consigneeSpace.profile.help')}</Muted>
        {!!error && <Notice>{error}</Notice>}
        {me.loading && !d && <Spinner />}
        {!!d && !d.noticesEnabled && <Notice tone="warn">{t('consigneeSpace.profile.disabled')}</Notice>}
        {!!d && row('EMAIL', d.emailChannel)}
        {!!d && row('SMS', d.smsChannel)}
      </Card>
    </>
  )
}
