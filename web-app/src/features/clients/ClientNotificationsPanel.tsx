// Panel «Avisos al cliente» de la ficha del cliente (Lote 98, P10): la COMPAÑÍA decide, por cliente (sender), qué avisos quiere y por qué canal (correo, bandeja del portal, SMS); sin configurar todo está apagado.
// Los avisos son resúmenes (no el detalle de cada orden) y van al cliente, nunca al consignatario. Ver pide clients.read; cambiar, clients.update. Abajo, el registro de los resúmenes que ya se mandaron.
import { useEffect, useMemo, useState } from 'react'
import { Can, useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, Panel, PhoneListInput, Spinner, toast, type DataColumn } from '../../kernel/ui'
import { IconBolt } from '../../kernel/ui/screenIcons'
import { useClientNotificationLog, useClientNotificationSettings, useSaveClientNotificationSettings, type ClientNotificationDto } from './notificationsApi'
import { DEFAULT_DIGEST_HOUR, enabledCount, isDirty, rowError, rowsFrom, type NotificationRow } from './notificationRules'
import type { ClientDetail } from './clientRules'

export function ClientNotificationsPanel({ client }: { client: ClientDetail }) {
  const t = useT()
  const f = useFormat()
  const publicId = client.publicId ?? ''
  const canEdit = useCan('clients.update')
  const settings = useClientNotificationSettings(publicId)
  const save = useSaveClientNotificationSettings(publicId)
  const saved = useMemo(() => rowsFrom(settings.data?.settings), [settings.data])
  const [draft, setDraft] = useState<NotificationRow[] | null>(null)
  const [hourDraft, setHourDraft] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [page, setPage] = useState(1)
  const log = useClientNotificationLog(publicId, (page - 1) * 10, 10)
  const rows = draft ?? saved
  const savedHour = settings.data?.digestHour ?? DEFAULT_DIGEST_HOUR
  const hour = hourDraft ?? savedHour
  const dirty = (draft !== null && isDirty(draft, saved)) || hour !== savedHour

  // al cambiar de cliente o recargar lo guardado, el borrador vuelve a lo guardado
  useEffect(() => {
    setDraft(null)
    setHourDraft(null)
    setError(null)
  }, [publicId, settings.data])

  const patch = (kind: string, p: Partial<NotificationRow>) => setDraft((rows ?? saved).map((r) => (r.kind === kind ? { ...r, ...p } : r)))
  const problems = rows.map((r) => rowError(r))
  const hasProblem = problems.some(Boolean)

  const columns = useMemo<DataColumn<ClientNotificationDto>[]>(
    () => [
      { id: 'when', header: t('clients.notif.log.when'), cell: (n) => f.dateTime(n.createdAtUtc ?? ''), sortValue: (n) => n.createdAtUtc ?? '', exportValue: (n) => n.createdAtUtc ?? '' },
      { id: 'kind', header: t('clients.notif.log.kind'), cell: (n) => t(`clients.notif.kinds.${n.kind}.title`), sortValue: (n) => n.kind ?? '' },
      { id: 'text', header: t('clients.notif.log.text'), cell: (n) => n.bodyText, card: 'title' },
      {
        id: 'deliveries',
        header: t('clients.notif.log.deliveries'),
        cell: (n) => {
          const d = n.deliveries ?? []
          const sent = d.filter((x) => x.status === 'SENT').length
          const problem = d.filter((x) => x.status === 'FAILED').length
          const skipped = d.filter((x) => x.status === 'SKIPPED').length
          return (
            <span className="cl-notif-deliveries" title={d.map((x) => `${x.channel} ${x.recipient}: ${x.status}${x.message ? ` (${x.message})` : ''}`).join('\n')}>
              {n.inPortal && <Chip tone="route">{t('clients.notif.log.inPortal')}</Chip>}
              {sent > 0 && <Chip tone="deliv">{t('clients.notif.log.sent', { n: sent })}</Chip>}
              {skipped > 0 && <Chip tone="warn">{t('clients.notif.log.skipped', { n: skipped })}</Chip>}
              {problem > 0 && <Chip tone="fail">{t('clients.notif.log.failed', { n: problem })}</Chip>}
            </span>
          )
        },
      },
    ],
    [t, f],
  )

  const submit = async () => {
    if (hasProblem) return
    setError(null)
    try {
      await save.mutateAsync({ digestHour: hour, settings: rows.map((r) => ({ kind: r.kind, emailEnabled: r.emailEnabled, appEnabled: r.appEnabled, smsEnabled: r.smsEnabled, extraEmails: r.extraEmails.trim() || null, extraPhones: r.extraPhones.trim() || null })) })
      toast.success(t('clients.notif.saved'))
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  const data = settings.data
  return (
    <Panel
      icon={<IconBolt />}
      title={t('clients.notif.title')}
      collapsible
      defaultCollapsed
      summary={settings.isPending ? undefined : t('clients.notif.summary', { n: enabledCount(saved) })}
      actions={
        canEdit && dirty ? (
          <button type="button" className="btn sm flow" disabled={save.isPending || hasProblem} onClick={() => void submit()}>{t('clients.notif.save')}</button>
        ) : undefined
      }
    >
      <div className="pb">
        {settings.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(settings.error).title}</p>
        ) : settings.isPending || !data ? (
          <Spinner block />
        ) : (
          <>
            <p className="help">{t('clients.notif.intro')}</p>
            <p className="help">{t('clients.notif.recipients', { n: data.portalRecipients ?? 0 })}</p>
            {!data.emailConfigured && <p className="cl-notif-warn" role="status">{t('clients.notif.emailOff')}</p>}
            <div className="f">
              <label htmlFor="cn-hour">{t('clients.notif.digestHour.label')}</label>
              <select id="cn-hour" value={hour} disabled={!canEdit} onChange={(e) => setHourDraft(Number(e.target.value))}>
                {Array.from({ length: 24 }, (_, h) => (
                  <option key={h} value={h}>{`${String(h).padStart(2, '0')}:00`}</option>
                ))}
              </select>
              <p className="help">{t('clients.notif.digestHour.help')}</p>
            </div>
            <div className="cl-notif-list">
              {rows.map((r, i) => {
                const problem = problems[i]
                return (
                  <fieldset key={r.kind} className="cl-notif-row" disabled={!canEdit}>
                    <legend>{t(`clients.notif.kinds.${r.kind}.title`)}</legend>
                    <p className="help">{t(`clients.notif.kinds.${r.kind}.help`)}</p>
                    <div className="cl-notif-channels">
                      <label className="vw-check"><input type="checkbox" checked={r.emailEnabled} onChange={(e) => patch(r.kind, { emailEnabled: e.target.checked })} /> {t('clients.notif.channels.email')}</label>
                      <label className="vw-check"><input type="checkbox" checked={r.appEnabled} onChange={(e) => patch(r.kind, { appEnabled: e.target.checked })} /> {t('clients.notif.channels.app')}</label>
                      <label className="vw-check"><input type="checkbox" checked={r.smsEnabled} onChange={(e) => patch(r.kind, { smsEnabled: e.target.checked })} /> {t('clients.notif.channels.sms')}</label>
                    </div>
                    {(r.emailEnabled || r.smsEnabled) && (
                      <div className="cl-notif-extra">
                        {r.emailEnabled && (
                          <div className="f">
                            <label htmlFor={`ne-${r.kind}`}>{t('clients.notif.extraEmails')}</label>
                            <input id={`ne-${r.kind}`} value={r.extraEmails} placeholder={t('clients.notif.extraEmailsPh')} autoComplete="off" onChange={(e) => patch(r.kind, { extraEmails: e.target.value })} />
                          </div>
                        )}
                        {r.smsEnabled && (
                          <div className="f">
                            <label htmlFor={`np-${r.kind}`}>{t('clients.notif.extraPhones')}</label>
                            <PhoneListInput id={`np-${r.kind}`} value={r.extraPhones} onChange={(v) => patch(r.kind, { extraPhones: v })} />
                            <p className="help">{t('clients.notif.smsHelp')}</p>
                          </div>
                        )}
                      </div>
                    )}
                    {problem && <p className="ferr" role="alert">{t(`clients.notif.errors.${problem.error}`, { value: problem.value ?? '' })}</p>}
                  </fieldset>
                )
              })}
            </div>
            {error && <p className="ferr" role="alert">{error}</p>}
            <Can perm="clients.update">
              {dirty && (
                <div className="cl-notif-actions">
                  <button type="button" className="btn flow" disabled={save.isPending || hasProblem} onClick={() => void submit()}>{save.isPending ? t('common.loading') : t('clients.notif.save')}</button>
                  <button type="button" className="btn" disabled={save.isPending} onClick={() => { setDraft(null); setHourDraft(null) }}>{t('clients.notif.discard')}</button>
                </div>
              )}
            </Can>
            <h3 className="exp-h">{t('clients.notif.log.title')}</h3>
            {log.isError ? (
              <p className="ferr" role="alert">{applyProblemDetails(log.error).title}</p>
            ) : log.isPending ? (
              <Spinner block />
            ) : (
              <DataTable
                label={t('clients.notif.log.title')}
                columns={columns}
                rows={log.data?.items ?? []}
                rowKey={(n) => n.publicId ?? ''}
                page={page}
                pageSize={10}
                total={log.data?.total ?? 0}
                onPage={setPage}
                empty={t('clients.notif.log.empty')}
              />
            )}
          </>
        )}
      </div>
    </Panel>
  )
}
