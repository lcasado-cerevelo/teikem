// Mi cuenta (/account): perfil, contraseña, MFA y sesiones activas del usuario de la sesión. Sin permiso ni módulo:
// cualquier usuario autenticado la ve. La pestaña va en la URL (?tab=) para poder enlazarla.
import { useSearchParams } from 'react-router-dom'
import { useSession } from '../../app/session'
import { useT } from '../../kernel/i18n/useT'
import { Spinner, Tabs } from '../../kernel/ui'
import { MfaTab } from './MfaTab'
import { PasswordTab } from './PasswordTab'
import { ProfileTab } from './ProfileTab'
import { SessionsTab } from './SessionsTab'
import './account.css'

const ACCOUNT_TABS = ['profile', 'password', 'mfa', 'sessions'] as const
type AccountTab = (typeof ACCOUNT_TABS)[number]

function isTab(value: string | null): value is AccountTab {
  return (ACCOUNT_TABS as readonly string[]).includes(value ?? '')
}

export default function AccountPage() {
  const t = useT()
  const { me } = useSession()
  const [params, setParams] = useSearchParams()
  const raw = params.get('tab')
  const tab: AccountTab = isTab(raw) ? raw : 'profile'

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
          tabs={ACCOUNT_TABS.map((key) => ({ key, label: t(`account.tabs.${key}`) }))}
        />
      </div>

      {tab === 'profile' && <ProfileTab />}
      {tab === 'password' && <PasswordTab />}
      {tab === 'mfa' && <MfaTab />}
      {tab === 'sessions' && <SessionsTab />}
    </div>
  )
}
