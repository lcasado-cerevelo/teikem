// Avisos al consignatario (Lote 116): EL CLIENTE los prende (y escoge el idioma de los avisos) y marca, por consignatario, si acepta correo y/o texto. Marcar muestra una advertencia (parte de la política de privacidad) que hay
// que aceptar; queda en la bitácora y se le avisa al consignatario. Solo administrador u operador; quien solo consulta lo ve sin poder cambiarlo.
// 2026-10-23 (Luis: «una manera de marcar más de uno fácil, sin ir uno a uno»): casilla por consignatario + «Seleccionar los que se ven» y una barra de acciones para los seleccionados (Avisar por correo / por texto,
// Quitar correo / texto) con UNA sola advertencia para todo el grupo; el servidor marca a todos en una llamada y dice cuáles no se pudieron y por qué. En la web ancha la lista es una tabla; en el celular, tarjetas.
import { useState } from 'react'
import { Text, View } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { CellText, DataTable, ExportButton, type Column } from '../../kernel/ui/desk'
import { fetchAll, filtersSentence, runExport, type ExportFormat } from '../../kernel/ui/tableExport'
import { useIsWide } from '../../kernel/ui/layout'
import { Button, Card, Check, Chip, Field, Muted, Notice, Screen, Spinner, Title, useColors } from '../../kernel/ui/ui'
import { useAsync } from '../../kernel/ui/useAsync'
import { ConsigneeAccessPanel } from './ConsigneeAccessPanel'
import { type ConsigneeNoticeConsigneeDto, getConsigneeNotices, listNoticeConsignees, markConsignee, markConsigneesBulk, setConsigneeNotices } from '../portalApi'

type Channel = 'EMAIL' | 'SMS'
/** Lo que espera la advertencia: un consignatario, o varios (bulk). */
type Pending = { consignee: ConsigneeNoticeConsigneeDto; channel: Channel } | { ids: string[]; channel: Channel }

export function ConsigneeNoticesScreen() {
  const colors = useColors()
  const { t } = useT()
  const wide = useIsWide()
  const settings = useAsync(getConsigneeNotices, [])
  const [search, setSearch] = useState('')
  const list = useAsync(() => listNoticeConsignees(search.trim(), 0, 100), [search])
  const [pending, setPending] = useState<Pending | null>(null)
  const [openAccess, setOpenAccess] = useState<string | null>(null)   // Lote 117: el consignatario cuyo acceso se está viendo
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [error, setError] = useState<string | null>(null)
  const [info, setInfo] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const s = settings.data
  const canEdit = !!s?.canEdit
  const items = list.data?.items ?? []
  const total = list.data?.total ?? 0
  const canMark = canEdit && !!s?.enabled && !busy
  const visibleIds = items.map((c) => c.publicId as string)
  const allVisibleSelected = visibleIds.length > 0 && visibleIds.every((id) => selected.has(id))

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

  async function toggle(c: ConsigneeNoticeConsigneeDto, channel: Channel) {
    const accepted = channel === 'EMAIL' ? !!c.acceptsEmail : !!c.acceptsSms
    if (accepted) return apply(c, channel, false, false)
    setPending({ consignee: c, channel })   // marcar pide aceptar la advertencia
  }

  async function apply(c: ConsigneeNoticeConsigneeDto, channel: Channel, accepted: boolean, warningAccepted: boolean) {
    setBusy(true)
    setError(null)
    setInfo(null)
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

  /** Varios a la vez: marcar pide la advertencia una sola vez; quitar va directo. */
  function bulk(channel: Channel, accepted: boolean) {
    const ids = [...selected]
    if (ids.length === 0) return
    if (accepted) setPending({ ids, channel })
    else void applyBulk(ids, channel, false, false)
  }

  async function applyBulk(ids: string[], channel: Channel, accepted: boolean, warningAccepted: boolean) {
    setBusy(true)
    setError(null)
    setInfo(null)
    try {
      const r = await markConsigneesBulk(ids, channel, accepted, warningAccepted)
      setPending(null)
      const failed = (r.items ?? []).filter((i) => !i.ok)
      const summary = t('consigneeNotices.bulkDone', { done: r.done ?? 0, failed: r.failed ?? 0 })
      if (failed.length > 0) setError(`${summary} ${failed.map((i) => `${i.name || i.publicId}: ${i.message ?? ''}`).join(' · ')}`)
      else setInfo(summary)
      setSelected(new Set())
      list.reload()
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  const toggleSelected = (id: string, on: boolean) => setSelected((prev) => {
    const next = new Set(prev)
    if (on) next.add(id)
    else next.delete(id)
    return next
  })
  const selectVisible = (on: boolean) => setSelected((prev) => {
    const next = new Set(prev)
    visibleIds.forEach((id) => (on ? next.add(id) : next.delete(id)))
    return next
  })

  const emailCheck = (c: ConsigneeNoticeConsigneeDto, compact: boolean) => (
    <Check compact={compact} label={compact ? (c.email ?? t('consigneeNotices.none')) : t('consigneeNotices.byEmail', { value: c.email ?? t('consigneeNotices.none') })} value={!!c.acceptsEmail} disabled={!canMark || (!c.email && !c.acceptsEmail)} onChange={() => void toggle(c, 'EMAIL')} testID={`notices-email-${c.publicId}`} />
  )
  const smsCheck = (c: ConsigneeNoticeConsigneeDto, compact: boolean) => (
    <Check compact={compact} label={compact ? (c.phone ?? t('consigneeNotices.none')) : t('consigneeNotices.bySms', { value: c.phone ?? t('consigneeNotices.none') })} help={compact ? undefined : t('consigneeNotices.smsHelp')} value={!!c.acceptsSms} disabled={!canMark || (!c.phone && !c.acceptsSms)} onChange={() => void toggle(c, 'SMS')} testID={`notices-sms-${c.publicId}`} />
  )
  const accessButton = (c: ConsigneeNoticeConsigneeDto) => (
    <Button title={openAccess === c.publicId ? t('consigneeAccess.close') : t('consigneeAccess.open')} variant="secondary" onPress={() => setOpenAccess(openAccess === c.publicId ? null : (c.publicId as string))} testID={`access-open-${c.publicId}`} />
  )

  const columns: Column<ConsigneeNoticeConsigneeDto>[] = [
    { key: 'sel', title: '', width: 44, render: (c) => <Check compact label="" value={selected.has(c.publicId as string)} disabled={!canEdit} onChange={(v) => toggleSelected(c.publicId as string, v)} testID={`notices-select-${c.publicId}`} /> },
    { key: 'name', title: t('consigneeNotices.col.consignee'), flex: 2, render: (c) => <CellText bold>{c.name}</CellText>, csv: (c) => c.name },
    { key: 'city', title: t('consigneeNotices.col.town'), flex: 1, render: (c) => <CellText muted>{c.city ?? '—'}</CellText>, csv: (c) => c.city ?? '' },
    { key: 'email', title: t('consigneeNotices.col.email'), flex: 2, render: (c) => emailCheck(c, true), csv: (c) => c.email ?? '' },
    { key: 'emailOn', title: t('consigneeNotices.col.emailOn'), width: 0, render: () => null, csv: (c) => !!c.acceptsEmail },
    { key: 'sms', title: t('consigneeNotices.col.sms'), flex: 1.5, render: (c) => smsCheck(c, true), csv: (c) => c.phone ?? '' },
    { key: 'smsOn', title: t('consigneeNotices.col.smsOn'), width: 0, render: () => null, csv: (c) => !!c.acceptsSms },
    { key: 'access', title: '', width: 120, render: (c) => accessButton(c) },
  ]
  const visibleColumns = columns.filter((c) => c.width !== 0)   // las de «¿acepta?» solo existen para exportar
  const exportAll = async (format: ExportFormat) => {
    const all = await fetchAll((skip, take) => listNoticeConsignees(search.trim(), skip, take))
    await runExport({ format, title: t('consigneeNotices.title'), columns, rows: all.items, filters: filtersSentence([!!search.trim() && t('table.fSearch', { q: search.trim() })]) })
  }
  const openOne = openAccess ? items.find((c) => c.publicId === openAccess) : null

  return (
    <Screen wide>
      <Title>{t('consigneeNotices.title')}</Title>
      <Muted>{t('consigneeNotices.help')}</Muted>
      {!!error && <Notice>{error}</Notice>}
      {!!info && <Notice tone="ok">{info}</Notice>}
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
          {'ids' in pending ? (
            <Muted>{t('consigneeNotices.bulkWarning', { n: pending.ids.length, channel: pending.channel === 'EMAIL' ? t('consigneeNotices.channelEmail') : t('consigneeNotices.channelSms') })}</Muted>
          ) : (
            <Muted>{pending.consignee.name}{' · '}{pending.channel === 'EMAIL' ? pending.consignee.email : pending.consignee.phone}</Muted>
          )}
          <View style={{ flexDirection: 'row', gap: 8 }}>
            <Button title={t('consigneeNotices.continue')} busy={busy} onPress={() => void ('ids' in pending ? applyBulk(pending.ids, pending.channel, true, true) : apply(pending.consignee, pending.channel, true, true))} testID="notices-warning-continue" />
            <Button title={t('common.cancel')} variant="secondary" disabled={busy} onPress={() => setPending(null)} testID="notices-warning-cancel" />
          </View>
        </Card>
      )}

      <Field label={t('consigneeNotices.search')} value={search} onChangeText={setSearch} placeholder={t('consigneeNotices.searchPlaceholder')} autoCorrect={false} testID="notices-search" />

      {canEdit && items.length > 0 && (
        <Card style={{ gap: 8 }}>
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
            <Check compact label={t('consigneeNotices.selectVisible', { n: visibleIds.length })} value={allVisibleSelected} onChange={selectVisible} testID="notices-select-visible" />
            {selected.size > 0 && <Chip label={t('consigneeNotices.selected', { n: selected.size })} tone="info" />}
            {selected.size > 0 && <Button title={t('consigneeNotices.clearSelection')} variant="link" onPress={() => setSelected(new Set())} testID="notices-clear-selection" />}
          </View>
          {selected.size > 0 && (
            <View style={{ flexDirection: 'row', gap: 8, flexWrap: 'wrap' }}>
              <Button title={t('consigneeNotices.bulkEmailOn')} disabled={!canMark} onPress={() => bulk('EMAIL', true)} testID="notices-bulk-email-on" />
              <Button title={t('consigneeNotices.bulkSmsOn')} disabled={!canMark} onPress={() => bulk('SMS', true)} testID="notices-bulk-sms-on" />
              <Button title={t('consigneeNotices.bulkEmailOff')} variant="secondary" disabled={!canEdit || busy} onPress={() => bulk('EMAIL', false)} testID="notices-bulk-email-off" />
              <Button title={t('consigneeNotices.bulkSmsOff')} variant="secondary" disabled={!canEdit || busy} onPress={() => bulk('SMS', false)} testID="notices-bulk-sms-off" />
            </View>
          )}
          <Muted>{t('consigneeNotices.bulkHelp')}</Muted>
        </Card>
      )}

      {list.loading && !list.data && <Spinner />}
      {!!list.data && items.length === 0 && <Muted>{t('consigneeNotices.empty')}</Muted>}

      {wide && items.length > 0 && (
        <>
          <DataTable columns={visibleColumns} rows={items} keyOf={(c) => String(c.publicId)} total={total} onExport={exportAll} testID="notices-table" />
          {!!openOne && (
            <Card>
              <Text style={{ fontWeight: '700', color: colors.text }}>{openOne.name}</Text>
              <ConsigneeAccessPanel locationId={openOne.publicId as string} canEdit={canEdit} defaultEmail={openOne.email} />
            </Card>
          )}
        </>
      )}

      {!wide && items.map((c) => (
        <Card key={c.publicId}>
          <View style={{ flexDirection: 'row', alignItems: 'flex-start', justifyContent: 'space-between', gap: 8 }}>
            <View style={{ flexShrink: 1 }}>
              <Text style={{ fontWeight: '700', color: colors.text }}>{c.name}</Text>
              {!!c.city && <Muted>{c.city}</Muted>}
            </View>
            {canEdit && <Check compact label={t('consigneeNotices.select')} value={selected.has(c.publicId as string)} onChange={(v) => toggleSelected(c.publicId as string, v)} testID={`notices-select-${c.publicId}`} />}
          </View>
          {emailCheck(c, false)}
          {smsCheck(c, false)}
          {!!c.acceptsEmail && <Chip label={t('consigneeNotices.on')} tone="ok" />}
          {accessButton(c)}
          {openAccess === c.publicId && <ConsigneeAccessPanel locationId={c.publicId as string} canEdit={canEdit} defaultEmail={c.email} />}
        </Card>
      ))}
      {!wide && !list.loading && total > 0 && (
        <View style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
          <Muted>{t('table.count', { shown: items.length, total })}</Muted>
          <ExportButton onExport={exportAll} testID="notices-export" />
        </View>
      )}
    </Screen>
  )
}
