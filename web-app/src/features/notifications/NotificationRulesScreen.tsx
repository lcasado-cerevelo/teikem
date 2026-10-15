// Sistema → Reglas de aviso (Lote 52): las reglas de la compañía (evento + canales con plantilla + destinatarios) con su interruptor, el
// modal para crear y editar, y el registro de envíos con filtros. Permiso notifications.manage, módulo SYSTEM (la ruta lo exige).
import { useMemo, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { useFormat } from '../../kernel/format'
import { useLang, useT } from '../../kernel/i18n'
import { Chip, DataTable, FilterScope, IconEdit, IconPlus, Panel, QBox, Spinner, Tabs, matchesQ, toast, type ChipTone, type DataColumn } from '../../kernel/ui'
import { IconClock } from '../../kernel/ui/screenIcons'
import { CHANNELS } from './ruleForm'
import {
  logPageReader,
  useNotificationEvents,
  useNotificationLog,
  useNotificationRules,
  useRuleMutations,
  type NotificationLogDto,
  type NotificationRuleDto,
} from './notificationsApi'
import { RuleModal } from './RuleModal'
import './notifications.css'

const NO_RULES: NotificationRuleDto[] = []
const STATUS_TONE: Record<string, ChipTone> = { SENT: 'deliv', SKIPPED: 'neutral', FAILED: 'fail', PENDING: 'warn' }
const STATUSES = ['SENT', 'SKIPPED', 'FAILED', 'PENDING'] as const

function RulesTab() {
  const t = useT()
  const lang = useLang()
  const rules = useNotificationRules()
  const events = useNotificationEvents()
  const { update } = useRuleMutations()
  const [q, setQ] = useState('')
  const [editing, setEditing] = useState<{ rule: NotificationRuleDto | null } | null>(null)
  const eventName = (code: string | undefined) => {
    const e = events.data?.find((x) => x.code === code)
    return (lang === 'en' ? e?.nameEn : e?.name) ?? code ?? ''
  }
  const rows = useMemo(() => (rules.data ?? NO_RULES).filter((r) => matchesQ(q, r.name, eventName(r.eventCode))), [rules.data, q, events.data, lang]) // eslint-disable-line react-hooks/exhaustive-deps

  const toggle = async (r: NotificationRuleDto, next: boolean) => {
    try {
      await update.mutateAsync({ publicId: r.publicId!, body: { isActive: next, rowVersion: r.rowVersion } })
      toast.success(t(next ? 'notifications.rules.on' : 'notifications.rules.off', { name: r.name ?? '' }))
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }

  const columns = useMemo<DataColumn<NotificationRuleDto>[]>(
    () => [
      { id: 'name', header: t('notifications.rules.col.name'), cell: (r) => <b>{r.name}</b>, sortValue: (r) => r.name ?? '', card: 'title' },
      { id: 'event', header: t('notifications.rules.col.event'), cell: (r) => eventName(r.eventCode), sortValue: (r) => eventName(r.eventCode) },
      {
        id: 'channels',
        header: t('notifications.rules.col.channels'),
        cell: (r) => {
          const on = CHANNELS.filter((c) => r.channels?.some((x) => x.channel === c && x.isEnabled))
          return on.length === 0 ? '—' : on.map((c) => <Chip key={c} tone="route">{t(`notifications.rules.channel.${c}`)}</Chip>)
        },
        exportValue: (r) => CHANNELS.filter((c) => r.channels?.some((x) => x.channel === c && x.isEnabled)).map((c) => t(`notifications.rules.channel.${c}`)).join(', '),
        sortValue: (r) => r.channels?.filter((x) => x.isEnabled).length ?? 0,
      },
      {
        id: 'recipients',
        header: t('notifications.rules.col.recipients'),
        cell: (r) => (r.recipients?.length === 1 ? t('notifications.rules.recipientsCount1') : t('notifications.rules.recipientsCount', { n: r.recipients?.length ?? 0 })),
        exportValue: (r) => (r.recipients ?? []).map((x) => x.label).join('; '),
        sortValue: (r) => r.recipients?.length ?? 0,
      },
      {
        id: 'active',
        header: t('notifications.rules.col.active'),
        cell: (r) => (
          <label className="sw">
            <input
              type="checkbox"
              role="switch"
              aria-label={t('notifications.rules.col.activeFor', { name: r.name ?? '' })}
              checked={!!r.isActive}
              disabled={update.isPending}
              onChange={(e) => void toggle(r, e.target.checked)}
            />
            <span className="tk" aria-hidden="true" />
          </label>
        ),
        exportValue: (r) => (r.isActive ? t('printing.yes') : t('printing.no')),
        sortValue: (r) => (r.isActive ? 1 : 0),
      },
    ],
    [t, lang, events.data, update.isPending], // eslint-disable-line react-hooks/exhaustive-deps
  )

  return (
    <FilterScope>
      <Panel
        flush
        icon={<IconClock />}
        title={t('notifications.rules.tabRules')}
        badge={rules.isPending ? undefined : rows.length}
        actions={
          <button type="button" className="btn sm flow" onClick={() => setEditing({ rule: null })}>
            <IconPlus /> {t('notifications.rules.new')}
          </button>
        }
      >
        <div className="pb" style={{ paddingBottom: 0 }}>
          <QBox value={q} onChange={setQ} placeholder={t('ui.qbox.placeholder')} />
        </div>
        <div className="pb" style={{ paddingTop: 10 }}>
          {rules.isError ? (
            <p className="ferr" role="alert">
              {applyProblemDetails(rules.error).title}
            </p>
          ) : rules.isPending ? (
            <Spinner block />
          ) : (
            <DataTable
              label={t('notifications.rules.tabRules')}
              columns={columns}
              rows={rows}
              rowKey={(r) => r.publicId ?? ''}
              defaultSort={{ id: 'name', desc: false }}
              rowClassName={(r) => (r.isActive ? undefined : 'dim')}
              empty={t('notifications.rules.empty')}
              rowActions={[{ key: 'edit', label: t('notifications.rules.edit'), icon: <IconEdit />, onClick: (r) => setEditing({ rule: r }) }]}
            />
          )}
        </div>
        {editing && <RuleModal key={editing.rule?.publicId ?? 'new'} rule={editing.rule} onClose={() => setEditing(null)} />}
      </Panel>
    </FilterScope>
  )
}

function LogTab() {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const events = useNotificationEvents()
  const { testEvent } = useRuleMutations()
  const [eventCode, setEventCode] = useState('')
  const [channel, setChannel] = useState('')
  const [status, setStatus] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const query = { eventCode, channel, status }
  const log = useNotificationLog({ ...query, skip: (page - 1) * pageSize, take: pageSize })
  const reset = () => setPage(1)
  const eventName = (code: string | undefined) => {
    const e = events.data?.find((x) => x.code === code)
    return (lang === 'en' ? e?.nameEn : e?.name) ?? code ?? ''
  }

  const columns = useMemo<DataColumn<NotificationLogDto>[]>(
    () => [
      { id: 'when', header: t('notifications.rules.log.col.when'), cell: (l) => f.dateTime(l.createdAtUtc), sortValue: (l) => l.createdAtUtc ?? '', card: 'title' },
      { id: 'rule', header: t('notifications.rules.log.col.rule'), cell: (l) => l.ruleName, sortValue: (l) => l.ruleName ?? '' },
      { id: 'event', header: t('notifications.rules.log.col.event'), cell: (l) => eventName(l.eventCode), sortValue: (l) => eventName(l.eventCode) },
      { id: 'channel', header: t('notifications.rules.log.col.channel'), cell: (l) => t(`notifications.rules.channel.${l.channel}`), sortValue: (l) => l.channel ?? '' },
      { id: 'recipient', header: t('notifications.rules.log.col.recipient'), cell: (l) => l.recipient, sortValue: (l) => l.recipient ?? '' },
      {
        id: 'status',
        header: t('notifications.rules.log.col.status'),
        cell: (l) => <Chip tone={STATUS_TONE[l.status ?? 'PENDING']}>{t(`notifications.rules.log.status_.${l.status}`)}</Chip>,
        exportValue: (l) => t(`notifications.rules.log.status_.${l.status}`),
        sortValue: (l) => l.status ?? '',
      },
      { id: 'detail', header: t('notifications.rules.log.col.detail'), cell: (l) => l.error || '—', sortValue: (l) => l.error ?? '' },
      { id: 'attempts', header: t('notifications.rules.log.col.attempts'), cell: (l) => l.attempts, sortValue: (l) => l.attempts ?? 0 },
    ],
    [t, f, lang, events.data], // eslint-disable-line react-hooks/exhaustive-deps
  )

  return (
    <FilterScope>
      <Panel
        flush
        icon={<IconClock />}
        title={t('notifications.rules.log.title')}
        badge={log.isPending ? undefined : log.data?.total}
        actions={
          <button
            type="button"
            className="btn sm"
            disabled={testEvent.isPending}
            onClick={async () => {
              try {
                await testEvent.mutateAsync(t('notifications.rules.log.testMessage'))
                toast.success(t('notifications.rules.log.testEventDone'))
              } catch (e) {
                toast.error(applyProblemDetails(e).title)
              }
            }}
          >
            {t('notifications.rules.log.testEvent')}
          </button>
        }
      >
        <div className="pb" style={{ paddingBottom: 0 }}>
          <div className="nr-filters">
            <div className="f">
              <label htmlFor="nl-event">{t('notifications.rules.log.event')}</label>
              <select id="nl-event" value={eventCode} onChange={(e) => { setEventCode(e.target.value); reset() }}>
                <option value="">{t('notifications.rules.log.all')}</option>
                {(events.data ?? []).map((e) => (
                  <option key={e.code} value={e.code}>
                    {lang === 'en' ? e.nameEn : e.name}
                  </option>
                ))}
              </select>
            </div>
            <div className="f">
              <label htmlFor="nl-channel">{t('notifications.rules.log.channel')}</label>
              <select id="nl-channel" value={channel} onChange={(e) => { setChannel(e.target.value); reset() }}>
                <option value="">{t('notifications.rules.log.all')}</option>
                {CHANNELS.map((c) => (
                  <option key={c} value={c}>
                    {t(`notifications.rules.channel.${c}`)}
                  </option>
                ))}
              </select>
            </div>
            <div className="f">
              <label htmlFor="nl-status">{t('notifications.rules.log.status')}</label>
              <select id="nl-status" value={status} onChange={(e) => { setStatus(e.target.value); reset() }}>
                <option value="">{t('notifications.rules.log.all')}</option>
                {STATUSES.map((s) => (
                  <option key={s} value={s}>
                    {t(`notifications.rules.log.status_.${s}`)}
                  </option>
                ))}
              </select>
            </div>
          </div>
        </div>
        <div className="pb" style={{ paddingTop: 10 }}>
          {log.isError ? (
            <p className="ferr" role="alert">
              {applyProblemDetails(log.error).title}
            </p>
          ) : log.isPending ? (
            <Spinner block />
          ) : (
            <DataTable
              label={t('notifications.rules.log.title')}
              columns={columns}
              rows={log.data?.items ?? []}
              rowKey={(l) => String(l.id)}
              page={page}
              pageSize={pageSize}
              total={log.data?.total ?? 0}
              onPage={setPage}
              onPageSize={(s) => { setPageSize(s); setPage(1) }}
              empty={t('notifications.rules.log.empty')}
              exportRows={() => fetchAllPages<NotificationLogDto>(logPageReader(query))}
            />
          )}
        </div>
      </Panel>
    </FilterScope>
  )
}

export default function NotificationRulesScreen() {
  const t = useT()
  const [tab, setTab] = useState<'rules' | 'log'>('rules')
  return (
    <div className="nr-screen">
      <div className="head">
        <div>
          <h1>{t('notifications.rules.title')}</h1>
          <p>{t('notifications.rules.subtitle')}</p>
        </div>
      </div>
      <Tabs
        label={t('notifications.rules.title')}
        value={tab}
        onChange={(k) => setTab(k === 'log' ? 'log' : 'rules')}
        tabs={[
          { key: 'rules', label: t('notifications.rules.tabRules') },
          { key: 'log', label: t('notifications.rules.tabLog') },
        ]}
      />
      {tab === 'rules' ? <RulesTab /> : <LogTab />}
    </div>
  )
}
