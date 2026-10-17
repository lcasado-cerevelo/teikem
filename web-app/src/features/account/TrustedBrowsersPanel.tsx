// Mi cuenta → Sesiones → «Mis computadoras de confianza» (Lote 100): las computadoras desde las que se puede entrar sin volver a confirmar por correo (30 días sin uso). Quitar una la
// manda a pedir confirmación la próxima vez.
import { useMemo, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/client'
import { useLang, useT } from '../../kernel/i18n/useT'
import { Chip, ConfirmDialog, DataTable, EmptyState, IconShield, Panel, toast, type DataColumn, type RowAction } from '../../kernel/ui'
import { useRevokeTrustedBrowser, useTrustedBrowsers, type TrustedBrowserDto } from './api'
import { formatDateTime } from './format'

const NONE: TrustedBrowserDto[] = []

export function TrustedBrowsersPanel() {
  const t = useT()
  const lang = useLang()
  const list = useTrustedBrowsers()
  const revoke = useRevokeTrustedBrowser()
  const [target, setTarget] = useState<TrustedBrowserDto | null>(null)

  const columns = useMemo<DataColumn<TrustedBrowserDto>[]>(
    () => [
      {
        id: 'label',
        header: t('account.trusted.computer'),
        card: 'title',
        sortValue: (b) => b.label ?? '',
        cell: (b) => (
          <span>
            {b.label} {b.isCurrent && <Chip tone="disp">{t('account.trusted.current')}</Chip>}
          </span>
        ),
      },
      { id: 'ip', header: t('account.trusted.ip'), sortValue: (b) => b.ipAddress ?? '', cell: (b) => b.ipAddress ?? '—' },
      { id: 'created', header: t('account.trusted.since'), sortValue: (b) => b.createdAtUtc ?? '', cell: (b) => formatDateTime(b.createdAtUtc, lang) },
      { id: 'used', header: t('account.trusted.lastUsed'), sortValue: (b) => b.lastUsedAtUtc ?? '', cell: (b) => formatDateTime(b.lastUsedAtUtc, lang) || '—' },
      { id: 'expires', header: t('account.trusted.expires'), sortValue: (b) => b.expiresAtUtc ?? '', cell: (b) => formatDateTime(b.expiresAtUtc, lang) || '—' },
    ],
    [t, lang],
  )

  const actions = useMemo<RowAction<TrustedBrowserDto>[]>(
    () => [{ key: 'revoke', label: t('account.trusted.revoke'), tone: 'danger', disabled: () => revoke.isPending, onClick: (b) => setTarget(b) }],
    [t, revoke.isPending],
  )

  return (
    <>
      <Panel flush icon={<IconShield />} title={t('account.trusted.title')} subtitle={t('account.trusted.subtitle')}>
        {list.isError ? (
          <EmptyState title={applyProblemDetails(list.error).title} action={<button type="button" className="btn" onClick={() => void list.refetch()}>{t('common.retry')}</button>} />
        ) : (
          <DataTable
            label={t('account.trusted.title')}
            columns={columns}
            rows={list.data ?? NONE}
            rowKey={(b) => b.publicId ?? ''}
            defaultSort={{ id: 'used', desc: true }}
            rowActions={actions}
            loading={list.isPending}
            empty={<EmptyState title={t('account.trusted.empty')} />}
          />
        )}
      </Panel>
      <ConfirmDialog
        open={target !== null}
        tone="danger"
        title={t('account.trusted.revokeTitle')}
        message={t('account.trusted.revokeBody', { computer: target?.label ?? '' })}
        confirmLabel={t('account.trusted.revoke')}
        onConfirm={async () => {
          if (!target?.publicId) return
          await revoke.mutateAsync(target.publicId)
          toast.success(t('account.trusted.revoked'))
        }}
        onClose={() => setTarget(null)}
      />
    </>
  )
}
