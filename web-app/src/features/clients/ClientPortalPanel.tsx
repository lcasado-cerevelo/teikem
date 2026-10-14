// Panel «Usuarios de portal» de la ficha del cliente (F-A3, manual 02 cap. 7): invitar por correo, reenviar la invitación, suspender y
// reactivar, dar de baja y volver a invitar. Se pinta solo con `portalusers.manage` y el módulo CLIENT_PORTAL. No es el portal del cliente
// (/portal/*): es la administración que hace la compañía de las personas del cliente que lo usarán.
// La cuenta nace sin contraseña; el invitado la fija con el enlace (el correo lo envía el servidor, nunca se muestra el token).
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Can, useCan, useModule } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useLookups } from '../../kernel/catalogs'
import { StatusChip } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { ConfirmDialog, DataTable, type DataColumn, EmptyState, Field, Panel, Select, Spinner, TextInput, toast, useElementWidth } from '../../kernel/ui'
import { IconUsers } from '../../kernel/ui/screenIcons'
import { lookupOptions } from './contractRules'
import { FormModal } from './contractUi'
import { usePortalMutations, usePortalUsers } from './portalApi'
import {
  buildInviteRequest,
  emptyInvite,
  inviteSchema,
  PORTAL_EMAIL_MAX,
  PORTAL_NAME_MAX,
  PORTAL_ROLE_DOMAIN,
  PORTAL_STATUS_DOMAIN,
  portalActions,
  reinviteValues,
  sortPortalUsers,
  wasAddedWithoutLink,
  type InviteValues,
  type PortalInviteResult,
  type PortalUser,
} from './portalRules'
import type { ClientDetail } from './clientRules'

const FORM_ID = 'portal-invite'

function InviteModal({ clientPublicId, initial, open, onClose }: { clientPublicId: string; initial: InviteValues; open: boolean; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const { invite } = usePortalMutations(clientPublicId)
  const { data: roles = [] } = useLookups(PORTAL_ROLE_DOMAIN)
  const schema = useMemo(
    () =>
      inviteSchema({
        emailRequired: t('clients.portal.errors.emailRequired'),
        emailInvalid: t('clients.portal.errors.emailInvalid'),
        emailMax: t('clients.portal.errors.max', { max: PORTAL_EMAIL_MAX }),
        nameMax: t('clients.portal.errors.max', { max: PORTAL_NAME_MAX }),
        roleRequired: t('clients.portal.errors.roleRequired'),
      }),
    [t],
  )
  const form = useForm({ resolver: zodResolver(schema), defaultValues: initial })
  const reinviting = initial.email !== ''

  return (
    <FormModal
      id={FORM_ID}
      title={reinviting ? t('clients.portal.reinviteTitle') : t('clients.portal.inviteTitle')}
      saveLabel={t('clients.portal.send')}
      open={open}
      onClose={onClose}
      form={form}
      onSubmit={async (v) => {
        const result = await invite.mutateAsync(buildInviteRequest(v))
        toast.success(inviteMessage(t, f.dateTime, result))
        onClose()
      }}
    >
      <p className="help">{reinviting ? t('clients.portal.reinviteHelp') : t('clients.portal.inviteHelp')}</p>
      <Field name="email" label={t('clients.portal.email')} required>
        <TextInput type="email" maxLength={PORTAL_EMAIL_MAX} autoComplete="off" readOnly={reinviting} />
      </Field>
      <Field name="fullName" label={t('clients.portal.name')}>
        <TextInput maxLength={PORTAL_NAME_MAX} autoComplete="off" />
      </Field>
      <Field name="role" label={t('clients.portal.role')} required>
        <Select options={lookupOptions(roles, initial.role)} placeholder={t('clients.portal.chooseRole')} />
      </Field>
    </FormModal>
  )
}

/** Aviso de éxito de invitar o reenviar: con enlace (vence) o, si la cuenta ya existía y estaba activa, sin enlace. */
function inviteMessage(t: ReturnType<typeof useT>, dateTime: (v: string) => string, r: PortalInviteResult): string {
  const email = r.user?.email ?? ''
  if (wasAddedWithoutLink(r)) return t('clients.portal.addedNoLink', { email })
  return r.expiresAtUtc ? t('clients.portal.sent', { email, date: dateTime(r.expiresAtUtc) }) : t('clients.portal.sentNoDate', { email })
}

type Confirm = { kind: 'suspend' | 'reactivate' | 'remove' | 'resend'; user: PortalUser }

export function ClientPortalPanel({ client }: { client: ClientDetail }) {
  const t = useT()
  const f = useFormat()
  const publicId = client.publicId ?? ''
  const moduleOn = useModule('CLIENT_PORTAL')
  const canManage = useCan('portalusers.manage')
  const enabled = moduleOn && canManage
  const { data, isPending, error } = usePortalUsers(publicId, enabled)
  const mutations = usePortalMutations(publicId)
  const [inviting, setInviting] = useState<InviteValues | null>(null)
  const [confirm, setConfirm] = useState<Confirm | null>(null)
  const ref = useRef<HTMLDivElement>(null)
  const width = useElementWidth(ref)
  const clientActive = client.isActive
  const users = useMemo(() => sortPortalUsers(data ?? []), [data])

  const columns = useMemo<DataColumn<PortalUser>[]>(
    () => [
      {
        id: 'email',
        header: t('clients.portal.user'),
        cell: (u) => (
          <span className="cl-portal-user">
            <b className="cl-portal-mail">{u.email}</b>
            {u.fullName && <span className="meta">{u.fullName}</span>}
            {u.clientsCount > 1 && <span className="meta">{t('clients.portal.alsoIn', { n: u.clientsCount - 1 })}</span>}
          </span>
        ),
        sortValue: (u) => u.email,
        exportValue: (u) => u.email ?? '',
        card: 'title',
      },
      { id: 'role', header: t('clients.portal.role'), cell: (u) => u.roleLabel ?? u.role ?? '—', sortValue: (u) => u.roleLabel ?? u.role },
      { id: 'status', header: t('clients.portal.status'), cell: (u) => <StatusChip domain={PORTAL_STATUS_DOMAIN} code={u.status} label={u.statusLabel} />, sortValue: (u) => u.statusLabel ?? u.status },
      {
        id: 'login',
        header: t('clients.portal.lastLogin'),
        cell: (u) => (u.lastLoginUtc ? f.dateTime(u.lastLoginUtc) : <span className="meta">{t('clients.portal.neverLogin')}</span>),
        sortValue: (u) => u.lastLoginUtc,
        exportValue: (u) => u.lastLoginUtc ?? '',
      },
    ],
    [t, f],
  )

  if (!enabled) return null

  const confirmCopy = (c: Confirm | null) => {
    if (!c) return { title: '', body: '', label: '' }
    const name = c.user.fullName || c.user.email || ''
    return {
      suspend: { title: t('clients.portal.suspendTitle'), body: t('clients.portal.suspendBody', { name }), label: t('clients.portal.suspend') },
      reactivate: { title: t('clients.portal.reactivateTitle'), body: t('clients.portal.reactivateBody', { name }), label: t('clients.portal.reactivate') },
      remove: { title: t('clients.portal.removeTitle'), body: t('clients.portal.removeBody', { name }), label: t('clients.portal.remove') },
      resend: { title: t('clients.portal.resendTitle'), body: t('clients.portal.resendBody', { email: c.user.email ?? '' }), label: t('clients.portal.resend') },
    }[c.kind]
  }
  const copy = confirmCopy(confirm)

  return (
    <Panel
      flush
      icon={<IconUsers />}
      title={t('clients.portal.title')}
      badge={users.length}
      collapsible
      defaultCollapsed
      summary={users.length === 0 ? t('clients.summary.empty') : t('clients.summary.portal', { n: users.length })}
      actions={
        <Can perm="portalusers.manage">
          <button type="button" className="btn sm flow" disabled={!clientActive} onClick={() => setInviting(emptyInvite())}>
            {t('clients.portal.invite')}
          </button>
        </Can>
      }
    >
      <div ref={ref}>
        {!clientActive && <p className="note cl-portal-note">{t('clients.portal.clientInactive')}</p>}
        <p className="help cl-portal-note">{t('clients.portal.help')}</p>
        {isPending ? (
          <Spinner block />
        ) : error ? (
          <EmptyState icon={<IconUsers />} title={applyProblemDetails(error).title} />
        ) : users.length === 0 ? (
          <EmptyState icon={<IconUsers />} title={t('clients.portal.empty')} />
        ) : (
          <DataTable
            label={t('clients.portal.title')}
            columns={columns}
            rows={users}
            rowKey={(u) => String(u.id)}
            pagination={false}
            exportable={false}
            forceCards={width > 0 && width < 640}
            rowClassName={(u) => (u.status === 'DISABLED' ? 'dim' : undefined)}
            rowActions={[
              { key: 'resend', label: t('clients.portal.resend'), onClick: (u) => setConfirm({ kind: 'resend', user: u }), visible: (u) => portalActions(u, clientActive).resend },
              { key: 'reinvite', label: t('clients.portal.reinvite'), onClick: (u) => setInviting(reinviteValues(u)), visible: (u) => portalActions(u, clientActive).reinvite },
              { key: 'suspend', label: t('clients.portal.suspend'), onClick: (u) => setConfirm({ kind: 'suspend', user: u }), visible: (u) => portalActions(u, clientActive).suspend },
              { key: 'reactivate', label: t('clients.portal.reactivate'), onClick: (u) => setConfirm({ kind: 'reactivate', user: u }), visible: (u) => portalActions(u, clientActive).reactivate },
              { key: 'remove', label: t('clients.portal.remove'), tone: 'danger', onClick: (u) => setConfirm({ kind: 'remove', user: u }), visible: (u) => portalActions(u, clientActive).remove },
            ]}
          />
        )}
      </div>

      <InviteModal key={inviting ? `invite-${inviting.email}` : 'closed'} clientPublicId={publicId} initial={inviting ?? emptyInvite()} open={inviting !== null} onClose={() => setInviting(null)} />
      <ConfirmDialog
        open={confirm !== null}
        tone={confirm?.kind === 'remove' ? 'danger' : 'flow'}
        title={copy.title}
        message={copy.body}
        confirmLabel={copy.label}
        onConfirm={async () => {
          if (!confirm) return
          const id = confirm.user.id
          if (confirm.kind === 'resend') {
            const r = await mutations.resend.mutateAsync(id)
            toast.success(inviteMessage(t, f.dateTime, r))
          } else if (confirm.kind === 'suspend') {
            await mutations.suspend.mutateAsync(id)
            toast.success(t('clients.portal.suspended'))
          } else if (confirm.kind === 'reactivate') {
            await mutations.reactivate.mutateAsync(id)
            toast.success(t('clients.portal.reactivated'))
          } else {
            await mutations.remove.mutateAsync(id)
            toast.success(t('clients.portal.removed'))
          }
        }}
        onClose={() => setConfirm(null)}
      />
    </Panel>
  )
}
