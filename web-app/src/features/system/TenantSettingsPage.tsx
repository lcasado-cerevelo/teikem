// Ajustes de la compañía (/system/settings, `admin.tenant`, módulo SYSTEM; lote F9, maqueta `ajustesScreen`). Una cabecera con
// el segmento de pestañas (patrón `.seg` de Seguridad y auditoría): General, Región y formatos, Calendario, Módulos, Operación y
// Marca. La pestaña va en `?tab=` (General = sin parámetro). Lo que NO está aquí, a propósito: MFA, reautenticación y sesiones
// (Seguridad y auditoría) y el modo de recibo, que es de cada almacén (aquí solo se resume). Sin `admin.tenant` todo es de
// solo lectura (la ruta ya lo exige; la pantalla se defiende igual).
import { useContext, useState, type JSX, type ReactNode } from 'react'
import { useSearchParams } from 'react-router-dom'
import { SessionContext } from '../../app/session'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useTenantSettings } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { EmptyState, IconClock, IconGear, IconLayers, IconPin, IconPlus, IconRoute, IconTag, Spinner, Tabs } from '../../kernel/ui'
import { BrandTab } from './settings/BrandTab'
import { CalendarTab } from './settings/CalendarTab'
import { GeneralTab } from './settings/GeneralTab'
import { ModulesTab } from './settings/ModulesTab'
import { NewCompanyModal } from './settings/NewCompanyModal'
import { OperationsTab } from './settings/OperationsTab'
import { RegionTab } from './settings/RegionTab'
import { SETTINGS_TABS, settingsTabFromParam, type SettingsTab } from './settings/settingsTabs'
import './settings/settings.css'

const ICONS: Record<SettingsTab, () => JSX.Element> = {
  general: IconGear,
  region: IconPin,
  calendar: IconClock,
  modules: IconLayers,
  ops: IconRoute,
  brand: IconTag,
}

export default function TenantSettingsPage() {
  const t = useT()
  const canEdit = useCan('admin.tenant')
  // Módulos: solo el administrador de la plataforma enciende o apaga; el administrador de la compañía lo ve en solo lectura
  const isPlatformAdmin = !!useContext(SessionContext)?.me?.isPlatformAdmin
  const [params, setParams] = useSearchParams()
  const tab = settingsTabFromParam(params.get('tab'))
  const settings = useTenantSettings()
  const [creating, setCreating] = useState(false)

  const changeTab = (next: SettingsTab) => {
    const p = new URLSearchParams(params)
    if (next === 'general') p.delete('tab')
    else p.set('tab', next)
    setParams(p, { replace: true })
  }

  let body: ReactNode
  if (settings.isPending) body = <Spinner block label={t('common.loading')} />
  else if (settings.isError || !settings.data)
    body = <EmptyState title={t('system.settings.loadError')} body={settings.error ? applyProblemDetails(settings.error).title : undefined} />
  else if (tab === 'general') body = <GeneralTab settings={settings.data} canEdit={canEdit} isPlatformAdmin={isPlatformAdmin} />
  else if (tab === 'region') body = <RegionTab settings={settings.data} canEdit={canEdit} />
  else if (tab === 'calendar') body = <CalendarTab settings={settings.data} canEdit={canEdit} />
  else if (tab === 'modules') body = <ModulesTab canEdit={canEdit && isPlatformAdmin} />
  else if (tab === 'ops') body = <OperationsTab settings={settings.data} canEdit={canEdit} />
  else body = <BrandTab settings={settings.data} canEdit={canEdit} />

  return (
    <div className="wrap">
      <div className="head set-head">
        <div>
          <h1>{t('nav.settings.title')}</h1>
          <p>{t('nav.settings.subtitle')}</p>
        </div>
        <div className="act">
          {isPlatformAdmin && (
            <button type="button" className="btn flow" data-testid="new-company" onClick={() => setCreating(true)}>
              <IconPlus /> {t('system.settings.company.newButton')}
            </button>
          )}
          <Tabs<SettingsTab>
            label={t('system.settings.tabs.label')}
            value={tab}
            onChange={changeTab}
            tabs={SETTINGS_TABS.map((k) => {
              const Icon = ICONS[k]
              return {
                key: k,
                label: (
                  <>
                    <Icon /> {t(`system.settings.tabs.${k}`)}
                  </>
                ),
              }
            })}
          />
        </div>
      </div>
      {!canEdit && (
        <p className="note" role="status" style={{ marginBottom: 16 }}>
          {t('system.settings.readOnly')}
        </p>
      )}
      {body}
      {isPlatformAdmin && <NewCompanyModal open={creating} onClose={() => setCreating(false)} />}
      <p className="note set-gap">{t('system.settings.note')}</p>
    </div>
  )
}
