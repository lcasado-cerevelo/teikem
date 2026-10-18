// Acceso del consignatario (Lote 117): dentro de la tarjeta de cada consignatario, el cliente ve quién tiene acceso, invita a un correo (si lo deja vacío se usa el principal del consignatario), reenvía la invitación o quita el
// acceso. Solo el administrador o el operador cambian algo; quien solo consulta ve la lista.
import { useState } from 'react'
import { Text, View } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { Button, Chip, Field, Muted, Notice, Spinner, type Tone, useColors } from '../../kernel/ui/ui'
import { useAsync } from '../../kernel/ui/useAsync'
import { formatDateTime } from '../format'
import { inviteConsigneeAccess, listConsigneeAccess, removeConsigneeAccess, resendConsigneeAccess } from '../portalApi'

const TONE: Record<string, Tone> = { ACTIVE: 'ok', INVITED: 'info', SUSPENDED: 'warn', DISABLED: 'neutral' }

export function ConsigneeAccessPanel({ locationId, canEdit, defaultEmail }: { locationId: string; canEdit: boolean; defaultEmail?: string | null }) {
  const colors = useColors()
  const { t, lang } = useT()
  const access = useAsync(() => listConsigneeAccess(locationId), [locationId])
  const [email, setEmail] = useState('')
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [info, setInfo] = useState<string | null>(null)

  async function run(action: () => Promise<string>) {
    setBusy(true)
    setError(null)
    setInfo(null)
    try {
      setInfo(await action())
      access.reload()
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  const invite = () =>
    run(async () => {
      const r = await inviteConsigneeAccess(locationId, email.trim(), name.trim())
      setEmail('')
      setName('')
      return t('consigneeAccess.sent', { email: r.access?.email ?? '' })
    })

  const list = access.data ?? []
  return (
    <View style={{ gap: 8, paddingTop: 8, borderTopWidth: 1, borderTopColor: colors.line }}>
      <Text style={{ fontWeight: '700', color: colors.text }}>{t('consigneeAccess.title')}</Text>
      <Muted>{t('consigneeAccess.help')}</Muted>
      {!!error && <Notice>{error}</Notice>}
      {!!info && <Notice tone="ok">{info}</Notice>}
      {access.loading && !access.data && <Spinner />}
      {!!access.data && list.length === 0 && <Muted>{t('consigneeAccess.none')}</Muted>}
      {list.map((a) => (
        <View key={a.id} style={{ gap: 4 }}>
          <Text style={{ fontWeight: '600', color: colors.text }}>{a.fullName ? `${a.fullName} · ${a.email}` : a.email}</Text>
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
            <Chip label={t(`consigneeAccess.states.${a.status}`)} tone={TONE[a.status ?? ''] ?? 'neutral'} />
            {!!a.lastLoginUtc && <Muted>{formatDateTime(a.lastLoginUtc, lang)}</Muted>}
          </View>
          {canEdit && (
            <View style={{ flexDirection: 'row', gap: 8, flexWrap: 'wrap' }}>
              {a.status === 'INVITED' && (
                <Button title={t('consigneeAccess.resend')} variant="secondary" disabled={busy} onPress={() => void run(async () => { await resendConsigneeAccess(locationId, a.id as number); return t('consigneeAccess.resent') })} testID={`access-resend-${a.id}`} />
              )}
              <Button title={t('consigneeAccess.remove')} variant="secondary" disabled={busy} onPress={() => void run(async () => { await removeConsigneeAccess(locationId, a.id as number); return t('consigneeAccess.removed') })} testID={`access-remove-${a.id}`} />
            </View>
          )}
        </View>
      ))}
      {canEdit && (
        <View style={{ gap: 8 }}>
          <Field label={t('consigneeAccess.emailLabel')} value={email} onChangeText={setEmail} placeholder={defaultEmail ?? t('consigneeAccess.emailPlaceholder')} autoCapitalize="none" autoCorrect={false} keyboardType="email-address" testID="access-email" />
          <Field label={t('consigneeAccess.nameLabel')} value={name} onChangeText={setName} testID="access-name" />
          <Button title={t('consigneeAccess.invite')} busy={busy} onPress={() => void invite()} testID="access-invite" />
        </View>
      )}
    </View>
  )
}
