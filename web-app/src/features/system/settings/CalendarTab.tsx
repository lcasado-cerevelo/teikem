// Ajustes → Calendario (maqueta `tenantCalendarPanelsHtml`): días laborables (`WorkDaysMask`, al menos uno; se guarda al
// tocar cada día) en el orden del primer día de la semana de la compañía, el próximo día hábil desde "hoy" en su zona, y los
// feriados (fecha, nombre, "cada año"; alta y baja inmediatas con `POST`/`DELETE /tenant/holidays`).
import { useMemo, useState } from 'react'
import { formatDate, formatDayMonth, todayIso, toFormatSettings } from '../../../kernel/format'
import { useT } from '../../../kernel/i18n'
import { Chip, ConfirmDialog, DataTable, IconClock, IconDoc, Panel, toast, type DataColumn, type RowAction } from '../../../kernel/ui'
import { IconTrash } from '../../../kernel/ui/actionIcons'
import { problemText } from '../../warehouse/problemText'
import { holidayOnDate, nextWorkDay, toggleWorkDay, weekOrder } from '../tenantCalendar'
import { useHolidayAction, useHolidays, useSaveTenantSettings, type TenantHolidayDto, type TenantSettingsDto } from '../tenantSettingsApi'

const NO_HOLIDAYS: TenantHolidayDto[] = []

function holidayDateText(h: TenantHolidayDto): string {
  return h.isRecurring ? formatDayMonth(h.date) : formatDate(h.date)
}

export function CalendarTab({ settings, canEdit }: { settings: TenantSettingsDto; canEdit: boolean }) {
  const t = useT()
  const save = useSaveTenantSettings()
  const holidaysQuery = useHolidays()
  const holidayAction = useHolidayAction()
  const holidays = holidaysQuery.data ?? NO_HOLIDAYS
  const format = toFormatSettings(settings)
  const mask = settings.workDaysMask ?? 62
  const next = nextWorkDay(todayIso(new Date(), format), mask, holidays)

  const [date, setDate] = useState('')
  const [name, setName] = useState('')
  const [recurring, setRecurring] = useState(false)
  const [addError, setAddError] = useState<string | null>(null)
  const [removing, setRemoving] = useState<TenantHolidayDto | null>(null)

  const toggle = async (day: number) => {
    const nextMask = toggleWorkDay(mask, day)
    if (nextMask === null) {
      toast.error(t('system.settings.calendar.needOneWorkDay'))
      return
    }
    try {
      await save.mutateAsync({ workDaysMask: nextMask })
      toast.success(t('system.settings.calendar.workdaysSaved'))
    } catch (err) {
      toast.error(problemText(err))
    }
  }

  const add = async () => {
    setAddError(null)
    if (!date || !name.trim()) {
      setAddError(t('system.settings.calendar.needHolidayName'))
      return
    }
    if (holidayOnDate(date, holidays)) {
      setAddError(t('system.settings.calendar.dupHoliday'))
      return
    }
    try {
      await holidayAction.mutateAsync({ kind: 'add', body: { date, name: name.trim(), isRecurring: recurring } })
      toast.success(t('system.settings.calendar.holidayAdded'))
      setDate('')
      setName('')
      setRecurring(false)
    } catch (err) {
      setAddError(problemText(err))
    }
  }

  const columns = useMemo<DataColumn<TenantHolidayDto>[]>(
    () => [
      { id: 'date', header: t('system.settings.calendar.colDate'), cell: (h) => <span className="mono">{holidayDateText(h)}</span>, sortValue: (h) => (h.isRecurring ? `0000${(h.date ?? '').slice(4)}` : h.date) },
      { id: 'name', header: t('system.settings.calendar.colName'), cell: (h) => h.name ?? '', sortValue: (h) => h.name, card: 'title' },
      {
        id: 'recurring',
        header: t('system.settings.calendar.colRecurring'),
        cell: (h) => (h.isRecurring ? <Chip tone="route">{t('system.settings.calendar.colRecurring')}</Chip> : ''),
        sortValue: (h) => (h.isRecurring ? 1 : 0),
        exportValue: (h) => (h.isRecurring ? t('system.settings.calendar.colRecurring') : ''),
      },
    ],
    [t],
  )
  const rowActions = useMemo<RowAction<TenantHolidayDto>[]>(
    () => [
      {
        key: 'remove',
        label: t('system.settings.calendar.remove'),
        icon: <IconTrash />,
        tone: 'danger',
        perm: 'admin.tenant',
        onClick: (h) => setRemoving(h),
      },
    ],
    [t, setRemoving],
  )

  const dayNames = weekOrder(format.weekStartDay)

  return (
    <div className="set-cols calendar">
      <Panel icon={<IconClock />} title={t('system.settings.calendar.workdaysTitle')}>
        <div className="set-days" role="group" aria-label={t('system.settings.calendar.workdaysTitle')}>
          {dayNames.map((d) => (
            <label key={d} className="sw">
              <input
                type="checkbox"
                role="switch"
                checked={(mask & (1 << d)) !== 0}
                disabled={!canEdit || save.isPending}
                aria-label={t(`system.settings.calendar.daysLong.${d}`)}
                onChange={() => void toggle(d)}
              />
              <span className="tk" aria-hidden="true" />
              <span aria-hidden="true">{t(`system.settings.calendar.days.${d}`)}</span>
            </label>
          ))}
        </div>
        <p className="set-d" style={{ marginBottom: 12 }}>
          {t('system.settings.calendar.workdaysHint')}
        </p>
        <div className="set-next">
          <span>{t('system.settings.calendar.nextBizDay')}</span>
          <b className="mono" data-testid="next-business-day">
            {next ? formatDate(next) : '—'}
          </b>
        </div>
      </Panel>

      <Panel flush icon={<IconDoc />} title={t('system.settings.calendar.holidaysTitle')} badge={holidays.length}>
        <DataTable
          columns={columns}
          rows={holidays}
          rowKey={(h) => h.id ?? `${h.date}-${h.name}`}
          defaultSort={{ id: 'date', desc: false }}
          rowActions={canEdit ? rowActions : undefined}
          loading={holidaysQuery.isPending}
          empty={t('system.settings.calendar.noHolidays')}
          label={t('system.settings.calendar.holidaysTitle')}
        />
        {canEdit && (
          <div className="set-hol-add">
            <div className="f">
              <label htmlFor="hol-date">{t('system.settings.calendar.colDate')}</label>
              <input id="hol-date" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
            </div>
            <div className="f grow">
              <label htmlFor="hol-name">{t('system.settings.calendar.holidayName')}</label>
              <input id="hol-name" value={name} maxLength={100} onChange={(e) => setName(e.target.value)} />
            </div>
            <label className="sw">
              <input type="checkbox" role="switch" checked={recurring} onChange={(e) => setRecurring(e.target.checked)} />
              <span className="tk" aria-hidden="true" />
              <span>{t('system.settings.calendar.colRecurring')}</span>
            </label>
            <button type="button" className="btn flow" disabled={holidayAction.isPending} onClick={() => void add()}>
              {t('system.settings.calendar.addHoliday')}
            </button>
            {addError && (
              <p className="ferr" role="alert" style={{ flexBasis: '100%' }}>
                {addError}
              </p>
            )}
          </div>
        )}
      </Panel>

      <ConfirmDialog
        open={removing !== null}
        title={t('system.settings.calendar.removeTitle')}
        message={removing ? t('system.settings.calendar.removeMessage', { name: removing.name ?? '', date: holidayDateText(removing) }) : ''}
        confirmLabel={t('system.settings.calendar.remove')}
        tone="danger"
        onConfirm={async () => {
          if (!removing?.id) return
          await holidayAction.mutateAsync({ kind: 'remove', id: removing.id })
          toast.success(t('system.settings.calendar.holidayRemoved'))
          setRemoving(null)
        }}
        onClose={() => setRemoving(null)}
      />
    </div>
  )
}
