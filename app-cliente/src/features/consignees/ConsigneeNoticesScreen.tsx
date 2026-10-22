// Avisos al consignatario (Lote 116): EL CLIENTE los prende (y escoge el idioma de los avisos) y marca, por consignatario, si acepta correo y/o texto. Marcar muestra una advertencia (parte de la política de privacidad) que hay
// que aceptar; queda en la bitácora y se le avisa al consignatario. Solo administrador u operador; quien solo consulta lo ve sin poder cambiarlo.
import { useState } from 'react'
import { Text, View } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { Button, Card, Check, Chip, Field, Muted, Notice, Screen, Spinner, Title, useColors } from '../../kernel/ui/ui'
import { useAsync } from '../../kernel/ui/useAsync'
import { ConsigneeAccessPanel } from './ConsigneeAccessPanel'
import { type ConsigneeNoticeConsigneeDto, getConsigneeNotices, listNoticeConsignees, markConsignee, setConsigneeNotices } from '../portalApi'

type Pending = { consignee: ConsigneeNoticeConsigneeDto; channel: 'EMAIL' | 'SMS' }

export function ConsigneeNoticesScreen() {
  const colors = useColors()
  const { t } = useT()
  const settings = useAsync(getConsigneeNotices, [])
  const [search, setSearch] = useState('')
  const list = useAsync(() => listNoticeConsignees(search.trim()), [search])
  const [pending, setPending] = useState<Pending | null>(null)
  const [openAccess, setOpenAccess] = useState<string | null>(null)   // Lote 117: el consignatario cuyo acceso se está viendo
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const s = settings.data
  const canEdit = !!s?.canEdit

  async function change(body: { enabled?: boolean; lang?: string }) {
    setBusy(true)
    setError(null)
    try {
      await setConsigneeNotices(body)
      settings.reload()
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  async function toggle(c: ConsigneeNoticeConsigneeDto, channel: 'EMAIL' | 'SMS') {
    const accepted = channel === 'EMAIL' ? !!c.acceptsEmail : !!c.acceptsSms
    if (accepted) return apply(c, channel, false, false)
    setPending({ consignee: c, channel })   // marcar pide aceptar la advertencia
  }

  async function apply(c: ConsigneeNoticeConsigneeDto, channel: 'EMAIL' | 'SMS', accepted: boolean, warningAccepted: boolean) {
    setBusy(true)
    setError(null)
    try {
      await markConsignee(c.publicId as string, channel, accepted, warningAccepted)
      setPending(null)
      list.reload()
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Screen wide>
      <Title>{t('consigneeNotices.title')}</Title>
      <Muted>{t('consigneeNotices.help')}</Muted>
      {!!error && <Notice>{error}</Notice>}
      {settings.loading && !s && <Spinner />}
      {!!s && !canEdit && <Notice tone="warn">{t('consigneeNotices.readOnly')}</Notice>}
      {!!s && !s.emailConfigured && <Notice tone="warn">{t('consigneeNotices.noEmail')}</Notice>}

      {!!s && (
        <Card>
          <Check label={t('consigneeNotices.enable')} help={t('consigneeNotices.enableHelp')} value={!!s.enabled} disabled={!canEdit || busy} onChange={(v) => void change({ enabled: v })} testID="notices-enable" />
          <Text style={{ fontWeight: '700', color: colors.text }}>{t('consigneeNotices.lang')}</Text>
          <View style={{ flexDirection: 'row', gap: 8 }}>
            <Button title="Español" variant={s.lang === 'es' ? 'primary' : 'secondary'} disabled={!canEdit || busy} onPress={() => void change({ lang: 'es' })} testID="notices-lang-es" />
            <Button title="English" variant={s.lang === 'en' ? 'primary' : 'secondary'} disabled={!canEdit || busy} onPress={() => void change({ lang: 'en' })} testID="notices-lang-en" />
          </View>
        </Card>
      )}

      {!!pending && (
        <Card>
          <Text style={{ fontWeight: '700', color: colors.text }}>{t('consigneeNotices.warningTitle')}</Text>
          <Muted>{t('consigneeNotices.warning')}</Muted>
          <Muted>{pending.consignee.name}{' · '}{pending.channel === 'EMAIL' ? pending.consignee.email : pending.consignee.phone}</Muted>
          <View style={{ flexDirection: 'row', gap: 8 }}>
            <Button title={t('consigneeNotices.continue')} busy={busy} onPress={() => void apply(pending.consignee, pending.channel, true, true)} testID="notices-warning-continue" />
            <Button title={t('common.cancel')} variant="secondary" disabled={busy} onPress={() => setPending(null)} testID="notices-warning-cancel" />
          </View>
        </Card>
      )}

      <Field label={t('consigneeNotices.search')} value={search} onChangeText={setSearch} placeholder={t('consigneeNotices.searchPlaceholder')} autoCorrect={false} testID="notices-search" />
      {list.loading && !list.data && <Spinner />}
      {!!list.data && list.data.items?.length === 0 && <Muted>{t('consigneeNotices.empty')}</Muted>}
      {(list.data?.items ?? []).map((c) => (
        <Card key={c.publicId}>
          <Text style={{ fontWeight: '700', color: colors.text }}>{c.name}</Text>
          {!!c.city && <Muted>{c.city}</Muted>}
          <Check
            label={t('consigneeNotices.byEmail', { value: c.email ?? t('consigneeNotices.none') })}
            value={!!c.acceptsEmail}
            disabled={!canEdit || !s?.enabled || busy || (!c.email && !c.acceptsEmail)}
            onChange={() => void toggle(c, 'EMAIL')}
            testID={`notices-email-${c.publicId}`}
          />
          <Check
            label={t('consigneeNotices.bySms', { value: c.phone ?? t('consigneeNotices.none') })}
            help={t('consigneeNotices.smsHelp')}
            value={!!c.acceptsSms}
            disabled={!canEdit || !s?.enabled || busy || (!c.phone && !c.acceptsSms)}
            onChange={() => void toggle(c, 'SMS')}
            testID={`notices-sms-${c.publicId}`}
          />
          {!!c.acceptsEmail && <Chip label={t('consigneeNotices.on')} tone="ok" />}
          <Button
            title={openAccess === c.publicId ? t('consigneeAccess.close') : t('consigneeAccess.open')}
            variant="secondary"
            onPress={() => setOpenAccess(openAccess === c.publicId ? null : (c.publicId as string))}
            testID={`access-open-${c.publicId}`}
          />
          {openAccess === c.publicId && <ConsigneeAccessPanel locationId={c.publicId as string} canEdit={canEdit} defaultEmail={c.email} />}
        </Card>
      ))}
    </Screen>
  )
}
