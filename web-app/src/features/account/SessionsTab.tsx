import { useMemo, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/client'
import { useLang, useT } from '../../kernel/i18n/useT'
import { Chip, ConfirmDialog, DataTable, EmptyState, Panel, toast, type DataColumn, type RowAction } from '../../kernel/ui'
import { useRevokeSession, useSessions, type SessionDto } from './api'
import { describeDevice, formatDateTime } from './format'

const NO_SESSIONS: SessionDto[] = []

/** Pestaña Sesiones: GET /api/v1/auth/sessions y revocar otra sesión (DELETE /sessions/{id}); la actual se cierra con "Salir". */
export function SessionsTab() {
  const t = useT()
  const lang = useLang()
  const sessions = useSessions()
  const revoke = useRevokeSession()
  const [target, setTarget] = useState<SessionDto | null>(null)

  const deviceOf = (s: SessionDto) => describeDevice(s.deviceInfo) ?? t('account.sessions.unknownDevice')

  const columns = useMemo<DataColumn<SessionDto>[]>(
    () => [
      {
        id: 'device',
        header: t('account.sessions.device'),
        card: 'title',
        sortValue: (s) => describeDevice(s.deviceInfo) ?? '',
        cell: (s) => (
          <span title={s.deviceInfo ?? undefined}>
            {describeDevice(s.deviceInfo) ?? t('account.sessions.unknownDevice')}{' '}
            {s.isCurrent && <Chip tone="disp">{t('account.sessions.current')}</Chip>}
          </span>
        ),
      },
      {
        id: 'issued',
        header: t('account.sessions.issued'),
        sortValue: (s) => s.issuedAtUtc ?? '',
        cell: (s) => formatDateTime(s.issuedAtUtc, lang),
      },
      {
        id: 'expires',
        header: t('account.sessions.expires'),
        sortValue: (s) => s.expiresAtUtc ?? '',
        cell: (s) => formatDateTime(s.expiresAtUtc, lang),
      },
      {
        id: 'aal2',
        header: t('account.sessions.aal2'),
        sortValue: (s) => s.aal2VerifiedAtUtc ?? '',
        cell: (s) => formatDateTime(s.aal2VerifiedAtUtc, lang) || '—',
      },
    ],
    [t, lang],
  )

  const actions = useMemo<RowAction<SessionDto>[]>(
    () => [
      {
        key: 'revoke',
        label: t('account.sessions.revoke'),
        tone: 'danger',
        visible: (s) => !s.isCurrent,
        disabled: () => revoke.isPending,
        onClick: (s) => setTarget(s),
      },
    ],
    [t, revoke.isPending],
  )

  return (
    <>
      <Panel flush title={t('account.sessions.title')} subtitle={t('account.sessions.subtitle')}>
        {sessions.isError ? (
          <EmptyState
            title={applyProblemDetails(sessions.error).title}
            action={
              <button type="button" className="btn" onClick={() => void sessions.refetch()}>
                {t('common.retry')}
              </button>
            }
          />
        ) : (
          <DataTable
            label={t('account.sessions.title')}
            columns={columns}
            rows={sessions.data ?? NO_SESSIONS}
            rowKey={(s) => s.id ?? 0}
            defaultSort={{ id: 'issued', desc: true }}
            rowActions={actions}
            loading={sessions.isPending}
            empty={<EmptyState title={t('account.sessions.empty')} />}
          />
        )}
      </Panel>

      <ConfirmDialog
        open={target !== null}
        tone="danger"
        title={t('account.sessions.revokeTitle')}
        message={t('account.sessions.revokeBody', { device: target ? deviceOf(target) : '' })}
        confirmLabel={t('account.sessions.revoke')}
        onConfirm={async () => {
          if (target?.id == null) return
          await revoke.mutateAsync(target.id)
          toast.success(t('account.sessions.revoked'))
        }}
        onClose={() => setTarget(null)}
      />
    </>
  )
}
