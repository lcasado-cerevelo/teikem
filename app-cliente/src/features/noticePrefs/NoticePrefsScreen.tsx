// Mis avisos (Lote 119): la persona decide qué avisos quiere recibir. La compañía manda: lo que ella no encendió para el cliente se ve «No disponible»; yo solo apago lo que no quiero (correo y bandeja). Se guarda al tocar.
import { useEffect, useState } from 'react'
import { Text } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { Button, Card, Check, Muted, Notice, Screen, Spinner, Title, useColors } from '../../kernel/ui/ui'
import { getNoticePrefs, saveNoticePrefs, type PortalNoticePrefDto, type PortalNoticePrefsDto } from '../portalApi'

export function NoticePrefsScreen() {
  const colors = useColors()
  const { t } = useT()
  const [data, setData] = useState<PortalNoticePrefsDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const load = () => {
    setError(null)
    getNoticePrefs().then(setData).catch((e) => setError(apiErrorMessage(e)))
  }
  useEffect(load, [])

  async function change(pref: PortalNoticePrefDto, patch: Partial<PortalNoticePrefDto>) {
    if (saving || !data) return
    const next = { ...pref, ...patch }
    setSaving(true)
    setError(null)
    try {
      setData(await saveNoticePrefs([next]))
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Screen>
      <Title>{t('noticePrefs.title')}</Title>
      <Muted>{t('noticePrefs.help')}</Muted>
      {!!error && (
        <>
          <Notice>{error}</Notice>
          <Button title={t('common.retry')} variant="secondary" onPress={load} />
        </>
      )}
      {!data && !error && <Spinner />}
      {data && !data.emailConfigured && <Notice tone="warn">{t('noticePrefs.emailNotConfigured')}</Notice>}
      {(data?.prefs ?? []).map((p) => {
        const none = !p.emailAvailable && !p.appAvailable
        return (
          <Card key={p.kind}>
            <Text style={{ fontWeight: '700', color: colors.text }}>{t(`noticePrefs.kind.${p.kind}`)}</Text>
            <Muted>{t(`noticePrefs.kindHelp.${p.kind}`)}</Muted>
            {none ? (
              <Muted>{t('noticePrefs.unavailable')}</Muted>
            ) : (
              <>
                <Check label={t('noticePrefs.email')} help={p.emailAvailable ? undefined : t('noticePrefs.channelOff')} value={!!p.emailAvailable && !!p.emailEnabled} disabled={!p.emailAvailable || saving} onChange={(v) => void change(p, { emailEnabled: v })} testID={`prefs-email-${p.kind}`} />
                <Check label={t('noticePrefs.inbox')} help={p.appAvailable ? undefined : t('noticePrefs.channelOff')} value={!!p.appAvailable && !!p.appEnabled} disabled={!p.appAvailable || saving} onChange={(v) => void change(p, { appEnabled: v })} testID={`prefs-app-${p.kind}`} />
              </>
            )}
          </Card>
        )
      })}
    </Screen>
  )
}
