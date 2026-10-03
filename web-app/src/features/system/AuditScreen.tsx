// Seguridad y auditoría (/system/audit, `admin.audit`, módulo SYSTEM; lote F10, maqueta `auditoriaScreen`). Cabecera con el
// segmento de dos pestañas (Actividad / Sesiones y MFA) en `?tab=` (Actividad = sin parámetro; `?tab=sessions` lo abre
// Ajustes de la compañía → General) y la nota de la maqueta abajo.
import { useSearchParams } from 'react-router-dom'
import { useT } from '../../kernel/i18n'
import { IconClock, IconShield, Tabs } from '../../kernel/ui'
import { ActivityTab } from './audit/ActivityTab'
import { auditTabFromParam, type AuditTab } from './audit/auditView'
import { CompanySessionsTab } from './audit/CompanySessionsTab'
import './audit/audit.css'
import './settings/settings.css'

export default function AuditScreen() {
  const t = useT()
  const [params, setParams] = useSearchParams()
  const tab = auditTabFromParam(params.get('tab'))

  const changeTab = (next: AuditTab) => {
    const p = new URLSearchParams(params)
    if (next === 'activity') p.delete('tab')
    else p.set('tab', next)
    setParams(p, { replace: true })
  }

  return (
    <div className="wrap">
      <div className="head set-head">
        <div>
          <h1>{t('nav.audit.title')}</h1>
          <p>{t('nav.audit.subtitle')}</p>
        </div>
        <div className="act">
          <Tabs<AuditTab>
            label={t('system.audit.tabs.label')}
            value={tab}
            onChange={changeTab}
            tabs={[
              {
                key: 'activity',
                label: (
                  <>
                    <IconClock /> {t('system.audit.tabs.activity')}
                  </>
                ),
              },
              {
                key: 'sessions',
                label: (
                  <>
                    <IconShield /> {t('system.audit.tabs.sessions')}
                  </>
                ),
              },
            ]}
          />
        </div>
      </div>
      {tab === 'activity' ? <ActivityTab /> : <CompanySessionsTab />}
      <p className="note set-gap">{t('system.audit.note')}</p>
    </div>
  )
}
