// Sistema → Trabajos programados (Lote 50, P2.2): los trabajos automáticos de la compañía (limpieza del historial, revisión diaria de
// inventario…). Por trabajo: interruptor, horario (cada cierto tiempo o todos los días a una hora), próxima corrida, último
// resultado, «Ejecutar ahora» (el sistema lo toma en unos segundos, aunque el trabajo esté apagado), «Cambiar horario» e historial.
// Permiso jobs.manage, módulo SYSTEM (la ruta lo exige). Sin alta ni baja: los trabajos los define el sistema.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useLang, useT } from '../../kernel/i18n'
import { Chip, DataTable, Field, IconEdit, IconPlay, Modal, NumberInput, Panel, Select, Spinner, TextInput, toast, type ChipTone, type DataColumn } from '../../kernel/ui'
import { IconClock } from '../../kernel/ui/screenIcons'
import { FormModal } from '../clients/contractUi'
import { buildScheduleRequest, jobScheduleSchema, jobScheduleValuesOf, scheduleText, type JobScheduleValues } from './jobRules'
import { useJobMutations, useJobRuns, useJobs, type ScheduledJobDto, type ScheduledJobRunDto } from './jobsApi'

const NO_ROWS: ScheduledJobDto[] = []

const STATUS_TONE: Record<string, ChipTone> = { OK: 'deliv', FAILED: 'fail', RUNNING: 'warn', NEVER: 'neutral' }

function jobName(j: ScheduledJobDto, lang: string) {
  return (lang === 'en' ? j.nameEn : j.name) ?? j.key ?? ''
}

function ScheduleModal({ job, onClose }: { job: ScheduledJobDto; onClose: () => void }) {
  const t = useT()
  const lang = useLang()
  const { update } = useJobMutations()
  const schema = useMemo(() => jobScheduleSchema(t), [t])
  const form = useForm<JobScheduleValues>({ resolver: zodResolver(schema), defaultValues: jobScheduleValuesOf(job) })
  const kind = useWatch({ control: form.control, name: 'scheduleKind' })
  return (
    <FormModal
      id="job-schedule"
      size="sm"
      title={t('jobs.schedule.title', { name: jobName(job, lang) })}
      open
      onClose={onClose}
      form={form}
      onSubmit={async (v: JobScheduleValues) => {
        await update.mutateAsync({ key: job.key!, body: buildScheduleRequest(v, job) })
        toast.success(t('jobs.schedule.saved', { name: jobName(job, lang) }))
        onClose()
      }}
    >
      <Field name="scheduleKind" label={t('jobs.schedule.kind')}>
        <Select
          options={[
            { value: 'INTERVAL', label: t('jobs.schedule.kindInterval') },
            { value: 'DAILY', label: t('jobs.schedule.kindDaily') },
          ]}
        />
      </Field>
      {kind === 'INTERVAL' ? (
        <div className="r2">
          <Field name="intervalValue" label={t('jobs.schedule.every')} required help={t('jobs.schedule.intervalHelp')}>
            <NumberInput min={1} step={1} />
          </Field>
          <Field name="intervalUnit" label={t('jobs.schedule.unit')}>
            <Select
              options={[
                { value: 'm', label: t('jobs.unit.m') },
                { value: 'h', label: t('jobs.unit.h') },
                { value: 'd', label: t('jobs.unit.d') },
              ]}
            />
          </Field>
        </div>
      ) : (
        <Field name="dailyAt" label={t('jobs.schedule.at')} required help={t('jobs.schedule.atHelp')}>
          <TextInput type="time" />
        </Field>
      )}
    </FormModal>
  )
}

function HistoryModal({ job, onClose }: { job: ScheduledJobDto; onClose: () => void }) {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const runs = useJobRuns(job.key ?? null)
  const columns = useMemo<DataColumn<ScheduledJobRunDto>[]>(
    () => [
      { id: 'started', header: t('jobs.history.started'), cell: (r) => f.dateTime(r.startedUtc), sortValue: (r) => r.startedUtc ?? '', card: 'title' },
      { id: 'finished', header: t('jobs.history.finished'), cell: (r) => (r.finishedUtc ? f.dateTime(r.finishedUtc) : '—'), sortValue: (r) => r.finishedUtc ?? '' },
      { id: 'by', header: t('jobs.history.by'), cell: (r) => t(`jobs.trigger.${r.triggeredBy}`), sortValue: (r) => r.triggeredBy ?? '' },
      { id: 'status', header: t('jobs.history.status'), cell: (r) => <Chip tone={STATUS_TONE[r.status ?? 'NEVER']}>{t(`jobs.status.${r.status}`)}</Chip>, sortValue: (r) => r.status ?? '' },
      { id: 'detail', header: t('jobs.history.detail'), cell: (r) => r.error || r.message || '—', sortValue: (r) => r.error || r.message || '' },
    ],
    [t, f],
  )
  return (
    <Modal open size="lg" title={t('jobs.history.title', { name: jobName(job, lang) })} onClose={onClose} footer={<button type="button" className="btn" onClick={onClose}>{t('jobs.close')}</button>}>
      <div className="pb">
        {runs.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(runs.error).title}</p>
        ) : runs.isPending ? (
          <Spinner block />
        ) : (
          <DataTable label={t('jobs.history.title', { name: jobName(job, lang) })} columns={columns} rows={runs.data} rowKey={(r) => String(r.id)} defaultSort={{ id: 'started', desc: true }} empty={t('jobs.history.empty')} />
        )}
      </div>
    </Modal>
  )
}

export default function JobsScreen() {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const jobs = useJobs()
  const { update, runNow } = useJobMutations()
  const [editing, setEditing] = useState<ScheduledJobDto | null>(null)
  const [history, setHistory] = useState<ScheduledJobDto | null>(null)

  const run = async (p: Promise<unknown>, ok: string) => {
    try {
      await p
      toast.success(ok)
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }

  const columns = useMemo<DataColumn<ScheduledJobDto>[]>(
    () => [
      {
        id: 'name',
        header: t('jobs.col.name'),
        cell: (j) => (
          <>
            <b>{jobName(j, lang)}</b>
            <div className="help">{lang === 'en' ? j.descriptionEn : j.description}</div>
          </>
        ),
        exportValue: (j) => jobName(j, lang),
        sortValue: (j) => jobName(j, lang),
        card: 'title',
      },
      {
        id: 'enabled',
        header: t('jobs.col.enabled'),
        cell: (j) => (
          <label className="sw">
            <input
              type="checkbox"
              role="switch"
              aria-label={t('jobs.col.enabledFor', { name: jobName(j, lang) })}
              checked={!!j.isEnabled}
              disabled={update.isPending}
              onChange={(e) => {
                const next = e.target.checked
                void run(update.mutateAsync({ key: j.key!, body: { isEnabled: next, rowVersion: j.rowVersion } }), t(next ? 'jobs.enabledOn' : 'jobs.enabledOff', { name: jobName(j, lang) }))
              }}
            />
            <span className="tk" aria-hidden="true" />
          </label>
        ),
        exportValue: (j) => (j.isEnabled ? t('jobs.yes') : t('jobs.no')),
        sortValue: (j) => (j.isEnabled ? 1 : 0),
      },
      { id: 'schedule', header: t('jobs.col.schedule'), cell: (j) => scheduleText(j, t, f.timeOfDay), sortValue: (j) => scheduleText(j, t, f.timeOfDay) },
      {
        id: 'next',
        header: t('jobs.col.next'),
        cell: (j) => (j.manualRequested ? <Chip tone="warn">{t('jobs.requested')}</Chip> : j.nextRunUtc && j.isEnabled ? f.dateTime(j.nextRunUtc) : '—'),
        exportValue: (j) => (j.nextRunUtc && j.isEnabled ? f.dateTime(j.nextRunUtc) : ''),
        sortValue: (j) => (j.isEnabled ? (j.nextRunUtc ?? '') : ''),
      },
      {
        id: 'last',
        header: t('jobs.col.last'),
        cell: (j) => {
          const status = j.isRunning ? 'RUNNING' : (j.lastStatus ?? 'NEVER')
          return (
            <>
              <Chip tone={STATUS_TONE[status] ?? 'neutral'}>{t(`jobs.status.${status}`)}</Chip>
              {j.lastFinishedUtc && !j.isRunning && <span className="help"> {f.dateTime(j.lastFinishedUtc)}</span>}
              {(j.lastError || j.lastMessage) && <div className="help">{j.lastError || j.lastMessage}</div>}
            </>
          )
        },
        exportValue: (j) => `${t(`jobs.status.${j.lastStatus}`)} ${j.lastError || j.lastMessage || ''}`.trim(),
        sortValue: (j) => j.lastFinishedUtc ?? '',
      },
    ],
    [t, lang, f, update],
  )

  return (
    <div className="jobs-screen">
      <div className="head">
        <div>
          <h1>{t('jobs.title')}</h1>
          <p>{t('jobs.subtitle')}</p>
        </div>
      </div>
      <Panel flush icon={<IconClock />} title={t('jobs.panel')} badge={jobs.isPending ? undefined : (jobs.data ?? NO_ROWS).length}>
        <div className="pb">
          {jobs.isError ? (
            <p className="ferr" role="alert">{applyProblemDetails(jobs.error).title}</p>
          ) : jobs.isPending ? (
            <Spinner block />
          ) : (
            <DataTable
              label={t('jobs.panel')}
              columns={columns}
              rows={jobs.data}
              rowKey={(j) => j.key ?? ''}
              defaultSort={{ id: 'name', desc: false }}
              empty={t('jobs.empty')}
              rowActions={[
                {
                  key: 'run',
                  label: t('jobs.runNow'),
                  icon: <IconPlay />,
                  onClick: (j) => void run(runNow.mutateAsync(j.key!), t('jobs.runRequested', { name: jobName(j, lang) })),
                  visible: (j) => !j.isRunning && !j.manualRequested,
                },
                { key: 'schedule', label: t('jobs.changeSchedule'), icon: <IconEdit />, onClick: (j) => setEditing(j) },
                { key: 'history', label: t('jobs.history.open'), icon: <IconClock />, onClick: (j) => setHistory(j) },
              ]}
            />
          )}
          <p className="help">{t('jobs.help')}</p>
        </div>
      </Panel>
      {editing && <ScheduleModal key={editing.key} job={editing} onClose={() => setEditing(null)} />}
      {history && <HistoryModal key={history.key} job={history} onClose={() => setHistory(null)} />}
    </div>
  )
}
