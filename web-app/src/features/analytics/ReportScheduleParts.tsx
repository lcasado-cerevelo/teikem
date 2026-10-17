// Lote 92 (P12) — bloque «Enviar por correo» del constructor de vistas: frecuencia (diaria, semanal o mensual), hora de la compañía, período
// que se manda, destinatarios y el resultado del último envío, con «Enviar ahora» para las vistas ya guardadas. El servidor valida de nuevo
// (cron, correos, período) y manda el mensaje exacto; esta pantalla solo arma el cron y revisa lo básico para avisar antes de guardar.
import { useId, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { parseApiDate } from '../../kernel/api/dates'
import { formatDate, formatTime } from '../../kernel/format'
import { useLang, useT } from '../../kernel/i18n/useT'
import { toast } from '../../kernel/ui/toast'
import { useDeliverReport } from './reportsApi'
import {
  DEFAULT_DELIVERY_RANGE,
  DEFAULT_SCHEDULE,
  DELIVERY_RANGES,
  buildCron,
  checkEmails,
  deliveryTone,
  parseSchedule,
  type ScheduleForm,
  type ScheduleKind,
} from './reportSchedule'
import type { ReportDto } from './viewRules'

export interface ScheduleValue {
  scheduleCron: string | null
  deliveryEmails: string | null
  deliveryRangeMode: string | null
}

export interface ReportScheduleFieldsProps {
  value: ScheduleValue
  onChange: (patch: Partial<ScheduleValue>) => void
  /** La vista ya guardada (para el último envío y «Enviar ahora»); `null` en el alta. */
  report: ReportDto | null
  /** Hay cambios sin guardar en la programación: «Enviar ahora» manda lo guardado. */
  dirty: boolean
}

const WEEKDAYS = [0, 1, 2, 3, 4, 5, 6] as const
const MONTH_DAYS = Array.from({ length: 28 }, (_, i) => i + 1)

export function ReportScheduleFields({ value, onChange, report, dirty }: ReportScheduleFieldsProps) {
  const t = useT()
  const lang = useLang()
  const id = useId()
  const deliver = useDeliverReport()
  const [sentNote, setSentNote] = useState<string | null>(null)
  const enabled = value.scheduleCron != null && value.scheduleCron !== ''
  const form = parseSchedule(value.scheduleCron)
  const emails = checkEmails(value.deliveryEmails)

  const setForm = (patch: Partial<ScheduleForm>) => onChange({ scheduleCron: buildCron({ ...(form ?? DEFAULT_SCHEDULE), ...patch }) })
  const toggle = (on: boolean) =>
    onChange(on ? { scheduleCron: buildCron(form ?? DEFAULT_SCHEDULE), deliveryRangeMode: value.deliveryRangeMode ?? DEFAULT_DELIVERY_RANGE } : { scheduleCron: null, deliveryEmails: null, deliveryRangeMode: null })

  const sendNow = async () => {
    if (report?.id == null) return
    setSentNote(null)
    try {
      const r = await deliver.mutateAsync(report.id)
      const note = t(`analytics.reports.schedule.result.${r.status}`, { n: r.recipients?.length ?? 0, rows: r.rows ?? 0, message: r.message ?? '' })
      setSentNote(note)
      if (r.status === 'SENT') toast.success(note)
      else toast.error(note)
    } catch (err) {
      const p = applyProblemDetails(err)
      setSentNote(p.title)
      toast.error(p.title)
    }
  }

  const last = report?.lastDeliveryUtc ? parseApiDate(report.lastDeliveryUtc) : null
  const tone = deliveryTone(report?.lastDeliveryStatus)

  return (
    <div className="rs-schedule">
      <label className="vw-check">
        <input type="checkbox" checked={enabled} onChange={(e) => toggle(e.target.checked)} /> {t('analytics.reports.schedule.enable')}
      </label>
      {!enabled && <p className="help">{t('analytics.reports.schedule.hint')}</p>}
      {enabled && (
        <>
          {form == null ? (
            <p className="help">{t('analytics.reports.schedule.custom', { cron: value.scheduleCron ?? '' })}</p>
          ) : (
            <div className="r2">
              <div className="f">
                <label htmlFor={`${id}-kind`}>{t('analytics.reports.schedule.frequency')}</label>
                <select id={`${id}-kind`} value={form.kind} onChange={(e) => setForm({ kind: e.target.value as ScheduleKind })}>
                  <option value="DAILY">{t('analytics.reports.schedule.daily')}</option>
                  <option value="WEEKLY">{t('analytics.reports.schedule.weekly')}</option>
                  <option value="MONTHLY">{t('analytics.reports.schedule.monthly')}</option>
                </select>
              </div>
              {form.kind === 'WEEKLY' && (
                <div className="f">
                  <label htmlFor={`${id}-dow`}>{t('analytics.reports.schedule.weekday')}</label>
                  <select id={`${id}-dow`} value={form.dayOfWeek} onChange={(e) => setForm({ dayOfWeek: Number(e.target.value) })}>
                    {WEEKDAYS.map((d) => (
                      <option key={d} value={d}>
                        {t(`analytics.reports.schedule.weekdays.${d}`)}
                      </option>
                    ))}
                  </select>
                </div>
              )}
              {form.kind === 'MONTHLY' && (
                <div className="f">
                  <label htmlFor={`${id}-dom`}>{t('analytics.reports.schedule.monthDay')}</label>
                  <select id={`${id}-dom`} value={form.dayOfMonth} onChange={(e) => setForm({ dayOfMonth: Number(e.target.value) })}>
                    {MONTH_DAYS.map((d) => (
                      <option key={d} value={d}>
                        {d}
                      </option>
                    ))}
                  </select>
                </div>
              )}
              <div className="f">
                <label htmlFor={`${id}-time`}>{t('analytics.reports.schedule.time')}</label>
                <input id={`${id}-time`} type="time" value={form.time} onChange={(e) => e.target.value && setForm({ time: e.target.value })} />
              </div>
            </div>
          )}
          <div className="f">
            <label htmlFor={`${id}-range`}>{t('analytics.reports.schedule.range')}</label>
            <select id={`${id}-range`} value={value.deliveryRangeMode ?? DEFAULT_DELIVERY_RANGE} onChange={(e) => onChange({ deliveryRangeMode: e.target.value })}>
              {DELIVERY_RANGES.map((r) => (
                <option key={r} value={r}>
                  {t(`analytics.reports.schedule.ranges.${r}`)}
                </option>
              ))}
            </select>
          </div>
          <div className="f">
            <label htmlFor={`${id}-emails`}>{t('analytics.reports.schedule.emails')}</label>
            <textarea
              id={`${id}-emails`}
              rows={2}
              value={value.deliveryEmails ?? ''}
              placeholder={t('analytics.reports.schedule.emailsPh')}
              onChange={(e) => onChange({ deliveryEmails: e.target.value })}
            />
          </div>
          {emails.bad ? (
            <p className="ferr" role="alert">
              {t('analytics.reports.schedule.badEmail', { email: emails.bad })}
            </p>
          ) : emails.tooMany ? (
            <p className="ferr" role="alert">
              {t('analytics.reports.schedule.tooMany')}
            </p>
          ) : emails.emails.length === 0 ? (
            <p className="ferr" role="alert">
              {t('analytics.reports.schedule.needEmails')}
            </p>
          ) : (
            <p className="help">{t('analytics.reports.schedule.recipients', { n: emails.emails.length })}</p>
          )}
          <p className="help">{t('analytics.reports.schedule.ownerHint')}</p>
          {report?.id != null && (
            <div className="rs-last">
              {report.lastDeliveryStatus ? (
                <p className={`help rs-${tone}`} data-testid="last-delivery">
                  {t('analytics.reports.schedule.last', {
                    status: t(`analytics.reports.schedule.status.${report.lastDeliveryStatus}`),
                    date: last ? `${formatDate(last)} ${formatTime(last, lang)}` : '',
                  })}
                  {report.lastDeliveryMessage ? ` — ${report.lastDeliveryMessage}` : ''}
                </p>
              ) : (
                <p className="help">{t('analytics.reports.schedule.neverSent')}</p>
              )}
              <button type="button" className="btn sm" disabled={deliver.isPending || dirty || emails.emails.length === 0} onClick={sendNow}>
                {deliver.isPending ? t('analytics.reports.schedule.sending') : t('analytics.reports.schedule.sendNow')}
              </button>
              {dirty && <span className="help"> {t('analytics.reports.schedule.saveFirst')}</span>}
              {sentNote && (
                <p className="help" role="status">
                  {sentNote}
                </p>
              )}
            </div>
          )}
        </>
      )}
    </div>
  )
}
