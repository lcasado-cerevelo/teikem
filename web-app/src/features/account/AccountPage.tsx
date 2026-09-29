// Mi cuenta (/account): perfil, contraseña, MFA y sesiones activas del usuario de la sesión. Sin permiso ni módulo:
// cualquier usuario autenticado la ve. La pestaña va en la URL (?tab=) para poder enlazarla.
import { useSearchParams } from 'react-router-dom'
import { useSession } from '../../app/session'
import { ModuleKeys, useModule } from '../../kernel/access'
import { useT } from '../../kernel/i18n/useT'
import { Spinner, Tabs } from '../../kernel/ui'
import { MfaTab } from './MfaTab'
import { PasswordTab } from './PasswordTab'
import { PinTab } from './PinTab'
import { ProfileTab } from './ProfileTab'
import { SessionsTab } from './SessionsTab'
import './account.css'

const ACCOUNT_TABS = ['profile', 'password', 'mfa', 'sessions', 'pin'] as const
type AccountTab = (typeof ACCOUNT_TABS)[number]

function isTab(value: string | null): value is AccountTab {
  return (ACCOUNT_TABS as readonly string[]).includes(value ?? '')
}

export default function AccountPage() {
  const t = useT()
  const { me } = useSession()
  // La pestaña "PIN de la app" (Lote 8A) solo existe con el módulo WMS_LOTSERIAL encendido para la compañía activa.
  const hasWms = useModule(ModuleKeys.WmsLotSerial)
  const visibleTabs = ACCOUNT_TABS.filter((key) => key !== 'pin' || hasWms)
  const [params, setParams] = useSearchParams()
  const raw = params.get('tab')
  const requested: AccountTab = isTab(raw) ? raw : 'profile'
  const tab: AccountTab = visibleTabs.includes(requested) ? requested : 'profile'

  if (!me) return <Spinner block />

  return (
    <div className="wrap">
      <div className="head">
        <div style={{ minWidth: 0 }}>
          <h1>{t('account.title')}</h1>
          <p>{[me.fullName, me.email].filter(Boolean).join(' · ')}</p>
        </div>
      </div>

      <div className="acct-tabs">
        <Tabs<AccountTab>
          label={t('account.title')}
          value={tab}
          onChange={(key) => setParams(key === 'profile' ? {} : { tab: key }, { replace: true })}
          tabs={visibleTabs.map((key) => ({ key, label: t(`account.tabs.${key}`) }))}
        />
      </div>

      {tab === 'profile' && <ProfileTab />}
      {tab === 'password' && <PasswordTab />}
      {tab === 'mfa' && <MfaTab />}
      {tab === 'sessions' && <SessionsTab />}
      {tab === 'pin' && hasWms && <PinTab />}
    </div>
  )
}
