import { useSession } from '../../app/session'
import { LANGS } from '../../kernel/i18n'
import { useLang, useT } from '../../kernel/i18n/useT'
import { Chip, Panel } from '../../kernel/ui'
import { formatDateTime } from './format'

/** Pestaña Perfil: datos de la sesión (`GET /api/v1/me`). Solo lectura: el alta y edición de usuarios es de Administración. */
export function ProfileTab() {
  const t = useT()
  const lang = useLang()
  const { me } = useSession()
  if (!me) return null

  const meLang = (me.lang ?? '').slice(0, 2).toLowerCase()
  const langLabel = (LANGS as readonly string[]).includes(meLang) ? t(`lang.${meLang}`) : me.lang
  const memberships = me.memberships ?? []
  const aal2 = formatDateTime(me.aal2VerifiedAtUtc, lang)

  return (
    <Panel title={t('account.profile.title')} subtitle={t('account.profile.subtitle')}>
      <dl className="acct-kv">
        <dt>{t('account.profile.fullName')}</dt>
        <dd>{me.fullName || '—'}</dd>
        <dt>{t('account.profile.email')}</dt>
        <dd>{me.email || '—'}</dd>
        <dt>{t('account.profile.tenant')}</dt>
        <dd>{me.tenantName || '—'}</dd>
        <dt>{t('account.profile.lang')}</dt>
        <dd>{langLabel || '—'}</dd>
        <dt>{t('account.profile.mfa')}</dt>
        <dd>
          <Chip tone={me.mfaEnabled ? 'deliv' : 'warn'}>
            {me.mfaEnabled ? t('account.mfa.on') : t('account.mfa.off')}
          </Chip>
        </dd>
        <dt>{t('account.profile.aal2')}</dt>
        <dd>{aal2 || t('account.profile.aal2Never')}</dd>
        {me.isPlatformAdmin && (
          <>
            <dt>{t('account.profile.role')}</dt>
            <dd>
              <Chip tone="route">{t('account.profile.platformAdmin')}</Chip>
            </dd>
          </>
        )}
        <dt>{t('account.profile.memberships')}</dt>
        <dd>
          {memberships.length === 0 ? (
            '—'
          ) : (
            <ul className="acct-list">
              {memberships.map((m) => (
                <li key={m.tenantId}>
                  <span>{m.tenantName}</span>
                  {m.tenantId === me.tenantId && <Chip tone="disp">{t('account.profile.current')}</Chip>}
                  {m.isDefault && <Chip tone="route">{t('account.profile.default')}</Chip>}
                  {m.status && <Chip>{m.status}</Chip>}
                </li>
              ))}
            </ul>
          )}
        </dd>
      </dl>
    </Panel>
  )
}
