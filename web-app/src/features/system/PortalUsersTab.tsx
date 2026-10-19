// Pestañas Clientes y Consignatarios de /system/users (Lote 121; regla del dueño 2026-10-18: todos los usuarios los controlan el administrador de la plataforma y el de la compañía). Lista TODAS las personas de portal de la compañía
// con su tipo (persona de un cliente / acceso de un consignatario), su cliente y su estatus, y deja suspenderlas, reactivarlas, darlas de baja y reenviarles la invitación (las acciones de siempre del expediente del cliente, con el
// mismo permiso `portalusers.manage`). No se invita desde aquí: a las personas las invita la compañía desde el expediente del cliente y a los consignatarios, el cliente desde su portal. Busca y pagina en el cliente.
import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Chip, ConfirmDialog, DataTable, IconEdit, IconKey, IconRotateCcw, IconXCircle, IconShieldOff, Panel, QBox, matchesQ, toast, type DataColumn, type RowAction } from '../../kernel/ui'
import { useLang, useT } from '../../kernel/i18n/useT'
import { applyProblemDetails } from '../../kernel/api/client'
import { formatDateTime } from '../account/format'
import { usePortalDirectory, usePortalDirectoryAction, type PortalDirectoryKind, type PortalUserAdminDto } from './portalDirectoryApi'

const TONE: Record<string, 'deliv' | 'cap' | 'fail' | 'disp'> = { ACTIVE: 'deliv', INVITED: 'cap', SUSPENDED: 'fail', DISABLED: 'disp' }

export function PortalUsersTab({ kind }: { kind: PortalDirectoryKind }) {
  const t = useT()
  const lang = useLang()
  const navigate = useNavigate()
  const [q, setQ] = useState('')
  const [includeRemoved, setIncludeRemoved] = useState(false)
  const { data, isLoading, error } = usePortalDirectory(kind, includeRemoved)
  const act = usePortalDirectoryAction()
  const [confirming, setConfirming] = useState<{ user: PortalUserAdminDto; action: 'suspend' | 'remove' } | null>(null)
  const consignees = kind === 'CONSIGNEE'

  const rows = useMemo(() => (data ?? []).filter((u) => matchesQ(q, u.fullName, u.email, u.clientName, u.clientCode, u.consigneeName, u.roleLabel)), [data, q])

  const run = async (user: PortalUserAdminDto, action: 'suspend' | 'reactivate' | 'remove' | 'resend') => {
    try {
      await act.mutateAsync({ clientPublicId: user.clientPublicId ?? '', id: user.id ?? 0, action })
      toast.success(t(`system.users.portal.done.${action}`, { name: user.fullName || user.email || '' }))
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }

  const columns = useMemo<DataColumn<PortalUserAdminDto>[]>(() => {
    const cols: DataColumn<PortalUserAdminDto>[] = [
      { id: 'fullName', header: t('system.users.portal.colName'), sortValue: (u) => (u.fullName ?? u.email ?? '').toLowerCase(), cell: (u) => <b>{u.fullName || '—'}</b> },
      { id: 'email', header: t('system.users.portal.colEmail'), sortValue: (u) => (u.email ?? '').toLowerCase(), cell: (u) => u.email },
      { id: 'client', header: t('system.users.portal.colClient'), sortValue: (u) => (u.clientName ?? '').toLowerCase(), cell: (u) => `${u.clientName ?? ''}${u.clientCode ? ` · ${u.clientCode}` : ''}` },
    ]
    if (consignees) cols.push({ id: 'consignee', header: t('system.users.portal.colConsignee'), sortValue: (u) => (u.consigneeName ?? '').toLowerCase(), cell: (u) => u.consigneeName ?? '—' })
    cols.push(
      { id: 'type', header: t('system.users.portal.colType'), sortValue: (u) => u.kind, cell: (u) => <Chip tone={u.kind === 'CONSIGNEE' ? 'route' : 'wh'}>{t(u.kind === 'CONSIGNEE' ? 'system.users.portal.typeConsignee' : 'system.users.portal.typeClient')}</Chip> },
      { id: 'role', header: t('system.users.portal.colRole'), sortValue: (u) => u.roleLabel, cell: (u) => u.roleLabel },
      { id: 'status', header: t('system.users.portal.colStatus'), sortValue: (u) => u.status, cell: (u) => <Chip tone={TONE[u.status ?? ''] ?? 'disp'}>{u.statusLabel}</Chip> },
      { id: 'lastLogin', header: t('system.users.portal.colLastLogin'), sortValue: (u) => u.lastLoginUtc, cell: (u) => formatDateTime(u.lastLoginUtc, lang) || '—' },
    )
    return cols
  }, [t, lang, consignees])

  const actions = useMemo<RowAction<PortalUserAdminDto>[]>(
    () => [
      { key: 'open', label: t('system.users.portal.openClient'), perm: 'portalusers.manage', icon: <IconEdit />, onClick: (u) => navigate(`/catalog/clients?client=${u.clientPublicId}`) },
      { key: 'resend', label: t('system.users.portal.resend'), perm: 'portalusers.manage', icon: <IconKey />, visible: (u) => u.status === 'INVITED', onClick: (u) => void run(u, 'resend') },
      { key: 'suspend', label: t('system.users.portal.suspend'), perm: 'portalusers.manage', icon: <IconShieldOff />, visible: (u) => u.status === 'ACTIVE', onClick: (u) => setConfirming({ user: u, action: 'suspend' }) },
      { key: 'reactivate', label: t('system.users.portal.reactivate'), perm: 'portalusers.manage', icon: <IconRotateCcw />, visible: (u) => u.status === 'SUSPENDED', onClick: (u) => void run(u, 'reactivate') },
      { key: 'remove', label: t('system.users.portal.remove'), perm: 'portalusers.manage', icon: <IconXCircle />, visible: (u) => u.status !== 'DISABLED', onClick: (u) => setConfirming({ user: u, action: 'remove' }) },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, navigate],
  )

  return (
    <>
      <Panel title={t(consignees ? 'system.users.portal.titleConsignees' : 'system.users.portal.titleClients')} badge={rows.length}>
        <p className="note">{t(consignees ? 'system.users.portal.helpConsignees' : 'system.users.portal.helpClients')}</p>
        <div className="toolbar" style={{ display: 'flex', gap: 12, alignItems: 'center', flexWrap: 'wrap' }}>
          <QBox value={q} onChange={setQ} placeholder={t('system.users.portal.search')} />
          <label className="sw">
            <input type="checkbox" checked={includeRemoved} onChange={(e) => setIncludeRemoved(e.target.checked)} />
            <span className="tk" />
            {t('system.users.portal.includeRemoved')}
          </label>
        </div>
        {!!error && <p className="ferr" role="alert">{applyProblemDetails(error).title}</p>}
        <DataTable
          label={t(consignees ? 'system.users.portal.titleConsignees' : 'system.users.portal.titleClients')}
          columns={columns}
          rows={rows}
          rowKey={(u) => u.id ?? 0}
          defaultSort={{ id: 'fullName', desc: false }}
          pageSize={25}
          loading={isLoading}
          rowActions={actions}
        />
      </Panel>
      <ConfirmDialog
        open={confirming !== null}
        title={t(confirming?.action === 'remove' ? 'system.users.portal.removeTitle' : 'system.users.portal.suspendTitle')}
        message={t(confirming?.action === 'remove' ? 'system.users.portal.removeBody' : 'system.users.portal.suspendBody', { name: confirming?.user.fullName || confirming?.user.email || '' })}
        confirmLabel={t(confirming?.action === 'remove' ? 'system.users.portal.remove' : 'system.users.portal.suspend')}
        onConfirm={async () => {
          if (confirming) await run(confirming.user, confirming.action)
        }}
        onClose={() => setConfirming(null)}
      />
    </>
  )
}
