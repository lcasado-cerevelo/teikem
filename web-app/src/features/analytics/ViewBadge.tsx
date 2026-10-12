// Insignia de una vista: «Por defecto» (de sistema) o «Creada por X» (de otra persona); las propias y las de la compañía, sin insignia.
import { useSession } from '../../app/session'
import { useT } from '../../kernel/i18n/useT'
import { Chip } from '../../kernel/ui/Chip'
import type { ReportDto } from './viewRules'

export function ViewBadge({ report }: { report: ReportDto }) {
  const t = useT()
  const { me } = useSession()
  if (report.isSystem) return <Chip tone="cap" title={t('analytics.reports.systemHint')}>{t('analytics.reports.systemBadge')}</Chip>
  if (report.ownerUserId != null && report.ownerUserId !== me?.userId && report.ownerName)
    return (
      <Chip tone="cap" title={report.canEdit ? undefined : t('analytics.reports.notYoursHint')}>
        {t('analytics.reports.createdBy', { name: report.ownerName })}
      </Chip>
    )
  return null
}
