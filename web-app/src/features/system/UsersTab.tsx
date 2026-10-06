// Pestaña Usuarios de /system/users (usuariosScreen de la maqueta): `GET /api/v1/users` no pagina en el servidor
// (schema.d.ts sin query params), así que la pantalla busca y pagina en cliente (patrón "lista completa" del KIT).
// El botón "Nuevo usuario" vive en la cabecera de `UsersPage` (una sola fila con las pestañas): la página avisa con
// `creating` y el modal de alta se abre aquí. Sobre la maqueta se conservan multi-rol (modal de selección múltiple),
// MFA, último acceso, PIN y las acciones de seguridad por fila: son funcionalidad real, no decoración.
import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { useSession } from '../../app/session'
import { useCanAny, useModule, ModuleKeys } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/client'
import { useLookups } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n/useT'
import {
  Chip,
  ConfirmDialog,
  DataTable,
  Field,
  Form,
  IconEdit,
  IconEye,
  IconKey,
  IconLogOut,
  IconRotateCcw,
  IconShield,
  IconUsers,
  Modal,
  Panel,
  QBox,
  SearchMultiSelect,
  Select,
  TextInput,
  Toggle,
  matchesQ,
  toast,
  useRegisterFilter,
  type DataColumn,
  type RowAction,
} from '../../kernel/ui'
import { formatDateTime } from '../account/format'
import { problemText } from '../warehouse/problemText'
import { copyCode } from './EnrollCodeModal'
import { PinModal } from './PinModal'
import {
  useCloseUserSessions,
  useAssignableCompanies,
  useCreateUser,
  useResetUserMfa,
  useRoles,
  useSetMembership,
  useSetCountSeeExpected,
  useSetMfaRequired,
  useSetUserExtraPermissions,
  useSetUserRoles,
  useUpdateUser,
  useUsers,
  usePermissions,
  type UserSummaryDto,
} from './api'
// (useUpdateUser lo usa `EditUserModal`, useCreateUser lo usa `CreateUserModal`.)
import { groupPermissionsByCategory } from './permissionGroups'
import { categoryIcon } from './permissionIcons'
import './system.css'

/** Editar nombre y activo (`PUT /users/{id}`). "No puede desactivarse a sí mismo." si aplica sobre el propio usuario. */
function EditUserModal({ open, user, onClose }: { open: boolean; user: UserSummaryDto | null; onClose: () => void }) {
  const t = useT()
  const update = useUpdateUser()
  const schema = useMemo(() => z.object({ fullName: z.string().trim(), active: z.boolean() }), [])
  const form = useForm({
    resolver: zodResolver(schema),
    values: { fullName: user?.fullName ?? '', active: user?.isActive ?? true },
  })
  const formId = 'user-edit'

  const close = () => {
    form.reset()
    onClose()
  }

  return (
    <Modal
      open={open}
      title={t('system.users.users.editTitle')}
      onClose={close}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={close}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onError={(p) => toast.error(p.title)}
        onSubmit={async (v) => {
          if (!user?.id) return
          await update.mutateAsync({ id: user.id, body: { fullName: v.fullName, isActive: v.active } })
          toast.success(t('system.users.users.saved'))
          close()
        }}
      >
        <Field name="fullName" label={t('system.users.users.fullName')}>
          <TextInput maxLength={200} />
        </Field>
        <Field name="active" label={t('system.users.users.active')}>
          <Toggle />
        </Field>
      </Form>
    </Modal>
  )
}

/** Alta de usuario interno: correo, nombre, roles y contraseña opcional. Portal solo muestra un aviso (Lote 2). */
function CreateUserModal({ open, roleNames, onClose }: { open: boolean; roleNames: string[]; onClose: () => void }) {
  const t = useT()
  const create = useCreateUser()
  const [selectedRoles, setSelectedRoles] = useState<string[]>([])
  // 2026-10-01: otras compañías a las que se agrega también (mismos roles por nombre en cada una).
  const { data: companies = [] } = useAssignableCompanies(open)
  const [alsoCompanies, setAlsoCompanies] = useState<string[]>([])
  const [addedNote, setAddedNote] = useState<string | null>(null)
  // Se muestra una sola vez, justo después del alta: el servidor no la vuelve a devolver en ninguna otra respuesta.
  const [tempPassword, setTempPassword] = useState<string | null>(null)
  const schema = useMemo(
    () =>
      z.object({
        email: z.string().trim().min(1, t('system.users.users.emailRequired')),
        fullName: z.string().trim(),
        password: z.string(),
        userKind: z.enum(['INTERNAL', 'PORTAL']),
      }),
    [t],
  )
  const form = useForm({ resolver: zodResolver(schema), defaultValues: { email: '', fullName: '', password: '', userKind: 'INTERNAL' as const } })
  const formId = 'user-create'
  const isPortal = form.watch('userKind') === 'PORTAL'

  const close = () => {
    form.reset()
    setSelectedRoles([])
    setAlsoCompanies([])
    setAddedNote(null)
    setTempPassword(null)
    onClose()
  }

  if (tempPassword != null) {
    return (
      <Modal
        open={open}
        title={t('system.users.users.tempPasswordTitle')}
        onClose={close}
        dismissible={false}
        footer={
          <button type="button" className="btn flow" onClick={close}>
            {t('common.done')}
          </button>
        }
      >
        {addedNote && <p className="note">{addedNote}</p>}
        <p className="help">{t('system.users.users.tempPasswordHint')}</p>
        <div className="secret" style={{ fontSize: 22, fontWeight: 700, letterSpacing: '.08em', textAlign: 'center' }}>
          {tempPassword}
        </div>
        <button
          type="button"
          className="btn sm"
          onClick={() => void copyCode(tempPassword, t('system.users.users.copied'), t('system.users.users.copyFailed'))}
        >
          {t('common.copy')}
        </button>
      </Modal>
    )
  }

  return (
    <Modal
      open={open}
      title={t('system.users.users.newTitle')}
      onClose={close}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={close}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={form.formState.isSubmitting || isPortal}>
            {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onError={(p) => toast.error(p.title)}
        onSubmit={async (v) => {
          if (isPortal) return
          const res = await create.mutateAsync({
            email: v.email,
            fullName: v.fullName || null,
            password: v.password || null,
            roles: selectedRoles,
            userKind: 'INTERNAL',
            alsoTenantIds: alsoCompanies.map(Number),
          })
          toast.success(t('system.users.users.created'))
          const added = res.alsoAddedTo ?? []
          const already = res.alreadyMemberOf ?? []
          const note = [
            added.length ? t('system.users.users.alsoAdded', { companies: added.join(', ') }) : '',
            already.length ? t('system.users.users.alsoAlready', { companies: already.join(', ') }) : '',
          ].filter(Boolean).join(' ')
          if (res.temporaryPassword) {
            setAddedNote(note || null)
            setTempPassword(res.temporaryPassword)
          } else {
            if (note) toast.info(note)
            close()
          }
        }}
      >
        <Field name="userKind" label={t('system.users.users.kind')}>
          <Select options={[{ value: 'INTERNAL', label: t('system.users.users.kindInternal') }, { value: 'PORTAL', label: t('system.users.users.kindPortal') }]} />
        </Field>
        {isPortal ? (
          <p className="note">{t('system.users.users.portalHint')}</p>
        ) : (
          <>
            <Field name="email" label={t('system.users.users.email')} required>
              <TextInput type="email" />
            </Field>
            <Field name="fullName" label={t('system.users.users.fullName')}>
              <TextInput maxLength={200} />
            </Field>
            <Field name="password" label={t('system.users.users.password')} help={t('system.users.users.passwordHelp')}>
              <TextInput type="password" autoComplete="new-password" />
            </Field>
            <div className="f">
              <label id="user-create-roles-lbl" htmlFor="user-create-roles">{t('system.users.roles.title')}</label>
              <SearchMultiSelect id="user-create-roles" labelledBy="user-create-roles-lbl" options={roleNames.map((r) => ({ value: r, label: r }))} value={selectedRoles} onChange={setSelectedRoles} />
            </div>
            {companies.length > 0 && (
              <div className="f" data-testid="also-companies">
                <label id="user-create-also-lbl" htmlFor="user-create-also">{t('system.users.users.alsoCompanies')}</label>
                <SearchMultiSelect
                  id="user-create-also"
                  labelledBy="user-create-also-lbl"
                  options={companies.map((c) => ({ value: String(c.tenantId), label: c.name ?? '' }))}
                  value={alsoCompanies}
                  onChange={setAlsoCompanies}
                />
                <p className="help">{t('system.users.users.alsoCompaniesHelp')}</p>
              </div>
            )}
          </>
        )}
      </Form>
    </Modal>
  )
}

/** Multi-selección de roles asignados a un usuario. `PUT /users/{id}/roles` (AAL2: el cliente del API pide
 *  reautenticación sola si el servidor responde 403 `aal2_required`, y reintenta — no hace falta pedirla aquí). */
function RolesModal({ open, user, roleNames, onClose }: { open: boolean; user: UserSummaryDto | null; roleNames: string[]; onClose: () => void }) {
  const t = useT()
  const setRoles = useSetUserRoles()
  const [selected, setSelected] = useState<string[]>([])
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (open) setSelected(user?.roles ?? [])
  }, [open, user])

  const close = () => {
    setSelected([])
    onClose()
  }

  return (
    <Modal
      open={open}
      title={t('system.users.users.rolesModalTitle', { name: user?.fullName ?? '' })}
      onClose={close}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={close} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button
            type="button"
            className="btn flow"
            disabled={busy}
            onClick={async () => {
              if (!user?.id) return
              setBusy(true)
              try {
                await setRoles.mutateAsync({ id: user.id, roles: selected })
                toast.success(t('system.users.users.saved'))
                close()
              } catch (err) {
                toast.error(applyProblemDetails(err).title)
              } finally {
                setBusy(false)
              }
            }}
          >
            {busy ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      <SearchMultiSelect options={roleNames.map((r) => ({ value: r, label: r }))} value={selected} onChange={setSelected} />
    </Modal>
  )
}

/** "Permisos de {name}": los del rol se ven marcados y bloqueados ("(del rol)"); lo demás son extras togglables. */
function ExtraPermissionsModal({
  open,
  user,
  roles,
  permissions,
  onClose,
}: {
  open: boolean
  user: UserSummaryDto | null
  roles: readonly { name?: string | null; permissions?: string[] | null }[]
  permissions: readonly { code?: string | null; label?: string | null; category?: string | null }[]
  onClose: () => void
}) {
  const t = useT()
  const setExtra = useSetUserExtraPermissions()
  const [selected, setSelected] = useState<string[] | null>(null)
  const [busy, setBusy] = useState(false)
  const { data: categories } = useLookups('PermissionCategory')
  const categoryLabel = (code: string) => categories?.find((c) => c.code === code)?.label ?? code

  const fromRole = new Set((user?.roles ?? []).flatMap((rn) => roles.find((r) => r.name === rn)?.permissions ?? []))
  const extra = selected ?? user?.extraPermissions ?? []
  const groups = useMemo(() => groupPermissionsByCategory(permissions), [permissions])

  const toggle = (code: string) => setSelected((prev) => {
    const base = prev ?? user?.extraPermissions ?? []
    return base.includes(code) ? base.filter((c) => c !== code) : [...base, code]
  })

  const close = () => {
    setSelected(null)
    onClose()
  }

  return (
    <Modal
      open={open}
      title={t('system.users.users.extraModalTitle', { name: user?.fullName ?? '' })}
      onClose={close}
      dismissible={!busy}
      size="lg"
      footer={
        <>
          <button type="button" className="btn" onClick={close} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button
            type="button"
            className="btn flow"
            disabled={busy}
            onClick={async () => {
              if (!user?.id) return
              setBusy(true)
              try {
                await setExtra.mutateAsync({ id: user.id, permissions: extra })
                toast.success(t('system.users.users.saved'))
                close()
              } catch (err) {
                toast.error(applyProblemDetails(err).title)
              } finally {
                setBusy(false)
              }
            }}
          >
            {busy ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      <p className="note">{t('system.users.users.extraHint')}</p>
      {groups.map((g) => {
        const Icon = categoryIcon(g.category)
        return (
          <div key={g.category} className="permgrp">
            <div className="permgrp-h">
              <Icon />
              <span>{categoryLabel(g.category)}</span>
            </div>
            <div className="permgrp-items">
              {g.items.map((p) => {
                const locked = fromRole.has(p.code ?? '')
                const checked = locked || extra.includes(p.code ?? '')
                return (
                  <label key={p.code} className={locked ? 'permrow fromrole' : 'permrow'}>
                    <input type="checkbox" checked={checked} disabled={locked} onChange={() => toggle(p.code ?? '')} />
                    {p.label} {locked && <span className="tag">({t('system.users.users.fromRole')})</span>}
                  </label>
                )
              })}
            </div>
          </div>
        )
      })}
    </Modal>
  )
}

export interface UsersTabProps {
  /** true = la cabecera de la página pidió "Nuevo usuario": se abre el modal de alta. */
  creating?: boolean
  /** Se llama al cerrar el modal de alta abierto por `creating`. */
  onCreateClose?: () => void
}

export function UsersTab({ creating = false, onCreateClose }: UsersTabProps) {
  const t = useT()
  const lang = useLang()
  const { me } = useSession()
  const { data: users, isLoading } = useUsers()
  const { data: roles = [] } = useRoles()
  const { data: permissions = [] } = usePermissions()
  const setMembership = useSetMembership()
  const closeSessions = useCloseUserSessions()
  const setMfaRequired = useSetMfaRequired()
  const setCountSeeExpected = useSetCountSeeExpected()
  const resetMfa = useResetUserMfa()
  const canManagePin = useCanAny('devices.manage', 'admin.users')
  const wmsOn = useModule(ModuleKeys.WmsLotSerial)
  const canPin = canManagePin && wmsOn

  const [q, setQ] = useState('')
  const [includeSuspended, setIncludeSuspended] = useState(false)
  // "Incluir suspendidos" encendido va en la línea de filtros de la exportación
  const suspendedRef = useRef<HTMLLabelElement>(null)
  useRegisterFilter(t('system.users.users.includeSuspended'), includeSuspended ? '' : null, suspendedRef)
  const [editing, setEditing] = useState<UserSummaryDto | null>(null)
  const [rolesFor, setRolesFor] = useState<UserSummaryDto | null>(null)
  const [permsFor, setPermsFor] = useState<UserSummaryDto | null>(null)
  const [pinFor, setPinFor] = useState<UserSummaryDto | null>(null)
  const [closingSessions, setClosingSessions] = useState<UserSummaryDto | null>(null)
  const [mfaRequiredFor, setMfaRequiredFor] = useState<UserSummaryDto | null>(null)
  const [resettingMfaFor, setResettingMfaFor] = useState<UserSummaryDto | null>(null)

  const roleNames = useMemo(() => roles.map((r) => r.name ?? '').filter(Boolean), [roles])

  const rows = useMemo(() => {
    const all = users ?? []
    return all
      .filter((u) => includeSuspended || u.membershipStatus !== 'SUSPENDED')
      .filter((u) => matchesQ(q, u.fullName, u.email))
  }, [users, includeSuspended, q])

  const columns = useMemo<DataColumn<UserSummaryDto>[]>(() => {
    const cols: DataColumn<UserSummaryDto>[] = [
      { id: 'fullName', header: t('system.users.users.colName'), card: 'title', sortValue: (u) => u.fullName ?? '', cell: (u) => <b>{u.fullName || '—'}</b> },
      {
        id: 'email',
        header: t('system.users.users.colEmail'),
        sortValue: (u) => u.email ?? '',
        cell: (u) => (
          <span className="mono user-email" title={u.email || undefined}>
            {u.email || '—'}
          </span>
        ),
      },
      {
        id: 'roles',
        header: t('system.users.users.colRoles'),
        sortValue: (u) => (u.roles ?? []).join(', '),
        // Multi-rol: los roles del usuario como píldoras; clic abre la selección múltiple (no un <select> de uno solo).
        cell: (u) => (
          <button type="button" className="linklike" onClick={() => setRolesFor(u)}>
            {(u.roles ?? []).length ? (u.roles ?? []).map((r) => <Chip key={r} tone="wh">{r}</Chip>) : t('system.users.users.roles')}
          </button>
        ),
      },
      {
        id: 'extra',
        header: t('system.users.users.colExtra'),
        sortValue: (u) => (u.extraPermissions ?? []).length,
        // Como la maqueta: botón de fila con texto, "escudo +n" si tiene extras o "Añadir" tenue si no.
        cell: (u) => {
          const extra = (u.extraPermissions ?? []).length
          return (
            <button
              type="button"
              className="rowbtn rowbtn-txt"
              title={t('system.users.users.extraModalTitle', { name: u.fullName ?? '' })}
              onClick={() => setPermsFor(u)}
            >
              {extra ? (
                <>
                  <IconShield /> +{extra}
                </>
              ) : (
                t('system.users.users.addExtra')
              )}
            </button>
          )
        },
      },
      {
        id: 'status',
        header: t('system.users.users.colStatus'),
        sortValue: (u) => u.membershipStatus ?? 'ACTIVE',
        cell: (u) => {
          const status = u.membershipStatus ?? 'ACTIVE'
          const label =
            status === 'ACTIVE'
              ? t('system.users.users.statusActive')
              : status === 'SUSPENDED'
                ? t('system.users.users.statusSuspended')
                : t('system.users.users.statusInvited')
          // Como la maqueta: el interruptor lleva su estado como texto (y así tiene nombre accesible); un invitado
          // todavía no tiene membresía que activar o suspender, así que solo se ve su píldora.
          if (status === 'INVITED') return <Chip tone="cap">{label}</Chip>
          return (
            <label className="sw">
              <input
                type="checkbox"
                checked={status === 'ACTIVE'}
                disabled={u.id === me?.userId}
                onChange={async (e) => {
                  if (!u.id) return
                  try {
                    await setMembership.mutateAsync({ id: u.id, status: e.target.checked ? 'ACTIVE' : 'SUSPENDED' })
                    toast.success(t('system.users.users.membershipSaved'))
                  } catch (err) {
                    toast.error(applyProblemDetails(err).title)
                  }
                }}
              />
              <span className="tk" />
              {label}
            </label>
          )
        },
      },
      {
        id: 'mfa',
        header: t('system.users.users.colMfa'),
        sortValue: (u) => u.mfaEnabled,
        cell: (u) => (
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
            <Chip tone={u.mfaEnabled ? 'deliv' : 'fail'}>{u.mfaEnabled ? t('system.users.users.yes') : t('system.users.users.no')}</Chip>
            {u.mfaRequired && !u.mfaEnabled && <Chip tone="cap">{t('system.users.users.mfaPending')}</Chip>}
          </span>
        ),
      },
      ...(wmsOn
        ? [
            {
              id: 'countSee',
              header: t('system.users.users.colCountSee'),
              sortValue: (u: UserSummaryDto) => (u.countSeeExpected === true ? 2 : u.countSeeExpected === false ? 0 : 1),
              cell: (u: UserSummaryDto) =>
                u.countSeeExpected == null ? (
                  '—'
                ) : (
                  <Chip tone={u.countSeeExpected ? 'deliv' : 'cap'}>{u.countSeeExpected ? t('system.users.users.yes') : t('system.users.users.no')}</Chip>
                ),
            } satisfies DataColumn<UserSummaryDto>,
          ]
        : []),
      {
        id: 'lastLogin',
        header: t('system.users.users.colLastLogin'),
        cell: (u) => formatDateTime(u.lastLoginUtc, lang) || '—',
        sortValue: (u) => u.lastLoginUtc,
      },
    ]
    if (canPin) {
      cols.push({
        id: 'pin',
        header: t('system.users.users.colPin'),
        sortValue: (u) => u.hasPin,
        cell: (u) => <Chip tone={u.hasPin ? 'deliv' : 'fail'}>{u.hasPin ? t('system.users.users.yes') : t('system.users.users.no')}</Chip>,
      })
    }
    return cols
  }, [t, lang, canPin, wmsOn, setMembership, me?.userId])

  const actions = useMemo<RowAction<UserSummaryDto>[]>(() => {
    const list: RowAction<UserSummaryDto>[] = [
      { key: 'edit', label: t('system.users.users.edit'), perm: 'admin.users', icon: <IconEdit />, onClick: (u) => setEditing(u) },
      {
        key: 'closeSessions',
        label: t('system.users.users.closeSessions'),
        perm: 'admin.users',
        icon: <IconLogOut />,
        onClick: (u) => setClosingSessions(u),
      },
    ]
    if (canPin) {
      list.push(
        { key: 'assignPin', label: t('system.users.users.assignPin'), visible: (u) => !u.hasPin, icon: <IconKey />, onClick: (u) => setPinFor(u) },
        {
          key: 'resetPin',
          label: t('system.users.users.resetPin'),
          visible: (u) => Boolean(u.hasPin),
          icon: <IconKey />,
          onClick: (u) => setPinFor(u),
        },
      )
    }
    if (wmsOn) {
      const mark = (u: UserSummaryDto, value: boolean | null) => {
        if (!u.id) return
        setCountSeeExpected
          .mutateAsync({ id: u.id, value })
          .then(() => toast.success(t('system.users.users.countSeeSaved')))
          .catch((err) => toast.error(problemText(err)))
      }
      list.push(
        { key: 'countSeeYes', label: t('system.users.users.countSeeYes'), perm: 'admin.users', visible: (u) => u.countSeeExpected !== true, icon: <IconEye />, onClick: (u) => mark(u, true) },
        { key: 'countSeeNo', label: t('system.users.users.countSeeNo'), perm: 'admin.users', visible: (u) => u.countSeeExpected !== false, icon: <IconEye />, onClick: (u) => mark(u, false) },
        { key: 'countSeeClear', label: t('system.users.users.countSeeClear'), perm: 'admin.users', visible: (u) => u.countSeeExpected != null, icon: <IconEye />, onClick: (u) => mark(u, null) },
      )
    }
    list.push(
      {
        key: 'requireMfa',
        label: t('system.users.users.requireMfa'),
        perm: 'admin.users',
        visible: (u) => !u.mfaRequired,
        icon: <IconShield />,
        onClick: (u) => setMfaRequiredFor(u),
      },
      {
        key: 'unrequireMfa',
        label: t('system.users.users.unrequireMfa'),
        perm: 'admin.users',
        visible: (u) => Boolean(u.mfaRequired),
        icon: <IconShield />,
        onClick: (u) => setMfaRequiredFor(u),
      },
      {
        key: 'resetMfa',
        label: t('system.users.users.resetMfa'),
        perm: 'admin.users',
        visible: (u) => Boolean(u.mfaEnabled),
        icon: <IconRotateCcw />,
        onClick: (u) => setResettingMfaFor(u),
      },
    )
    return list
  }, [t, canPin, wmsOn, setCountSeeExpected])

  return (
    <>
      <Panel flush icon={<IconUsers />} title={t('system.users.users.title')} badge={rows.length}>
        <div className="qrow">
          <QBox value={q} onChange={setQ} placeholder={t('system.users.users.searchPlaceholder')} />
          <label className="sw" ref={suspendedRef}>
            <input type="checkbox" checked={includeSuspended} onChange={(e) => setIncludeSuspended(e.target.checked)} />
            <span className="tk" />
            {t('system.users.users.includeSuspended')}
          </label>
        </div>
        <DataTable
          label={t('system.users.users.title')}
          columns={columns}
          rows={rows}
          rowKey={(u) => u.id ?? 0}
          defaultSort={{ id: 'fullName', desc: false }}
          pageSize={25}
          loading={isLoading}
          rowActions={actions}
        />
      </Panel>

      <p className="note" style={{ marginTop: 16 }}>
        {t('system.users.users.note')}
      </p>

      <CreateUserModal open={creating} roleNames={roleNames} onClose={() => onCreateClose?.()} />
      <EditUserModal open={editing !== null} user={editing} onClose={() => setEditing(null)} />
      <RolesModal open={rolesFor !== null} user={rolesFor} roleNames={roleNames} onClose={() => setRolesFor(null)} />
      <ExtraPermissionsModal open={permsFor !== null} user={permsFor} roles={roles} permissions={permissions} onClose={() => setPermsFor(null)} />
      <PinModal open={pinFor !== null} user={pinFor} onClose={() => setPinFor(null)} />

      <ConfirmDialog
        open={closingSessions !== null}
        title={t('system.users.users.closeSessionsTitle')}
        message={t('system.users.users.closeSessionsBody', { name: closingSessions?.fullName ?? '' })}
        confirmLabel={t('system.users.users.closeSessions')}
        onConfirm={async () => {
          if (!closingSessions?.id) return
          await closeSessions.mutateAsync(closingSessions.id)
          toast.success(t('system.users.users.closeSessionsDone'))
        }}
        onClose={() => setClosingSessions(null)}
      />

      <ConfirmDialog
        open={mfaRequiredFor !== null}
        title={t(mfaRequiredFor?.mfaRequired ? 'system.users.users.unrequireMfaTitle' : 'system.users.users.requireMfaTitle')}
        message={t(mfaRequiredFor?.mfaRequired ? 'system.users.users.unrequireMfaBody' : 'system.users.users.requireMfaBody', {
          name: mfaRequiredFor?.fullName ?? '',
        })}
        confirmLabel={t(mfaRequiredFor?.mfaRequired ? 'system.users.users.unrequireMfa' : 'system.users.users.requireMfa')}
        onConfirm={async () => {
          if (!mfaRequiredFor?.id) return
          await setMfaRequired.mutateAsync({ id: mfaRequiredFor.id, required: !mfaRequiredFor.mfaRequired })
          toast.success(t('system.users.users.mfaRequiredSaved'))
        }}
        onClose={() => setMfaRequiredFor(null)}
      />

      <ConfirmDialog
        open={resettingMfaFor !== null}
        title={t('system.users.users.resetMfaTitle')}
        message={t('system.users.users.resetMfaBody', { name: resettingMfaFor?.fullName ?? '' })}
        confirmLabel={t('system.users.users.resetMfa')}
        onConfirm={async () => {
          if (!resettingMfaFor?.id) return
          await resetMfa.mutateAsync(resettingMfaFor.id)
          toast.success(t('system.users.users.resetMfaDone'))
        }}
        onClose={() => setResettingMfaFor(null)}
      />
    </>
  )
}
