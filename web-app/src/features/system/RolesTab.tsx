// Pestaña Roles de /system/users (usuariosScreen de la maqueta): tabla ordenable, alta/edición con permisos
// agrupados por categoría y baja con guarda de uso. El botón "Nuevo rol" vive en la cabecera de `UsersPage` (una
// sola fila con las pestañas, como la maqueta): la página avisa con `creating` y el editor se abre aquí. PUT/DELETE llevan [RequireAal2]: el cliente del API pide
// reautenticación sola si el servidor responde 403 `aal2_required`, y reintenta — no hace falta pedirla aquí.
import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { applyProblemDetails } from '../../kernel/api/client'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n/useT'
import {
  Chip,
  ConfirmDialog,
  DataTable,
  Field,
  Form,
  IconEdit,
  IconShield,
  IconTrash,
  Modal,
  Panel,
  TextArea,
  TextInput,
  toast,
  type DataColumn,
  type RowAction,
} from '../../kernel/ui'
import { useDeleteRole, useRoles, useRoleTemplates, useSaveRole, usePermissions, type PermissionDto, type RoleDto } from './api'
import { groupPermissionsByCategory } from './permissionGroups'
import { categoryIcon } from './permissionIcons'
import './system.css'

/** Casilla "todo el grupo": marcada si todos los permisos del grupo están elegidos, indeterminada si solo algunos. */
function GroupCheckbox({ checked, indeterminate, onChange, label }: { checked: boolean; indeterminate: boolean; onChange: () => void; label: string }) {
  const ref = useRef<HTMLInputElement>(null)
  useEffect(() => {
    if (ref.current) ref.current.indeterminate = indeterminate && !checked
  }, [indeterminate, checked])
  return (
    <label className="permgrp-all">
      <input ref={ref} type="checkbox" checked={checked} onChange={onChange} />
      {label}
    </label>
  )
}

function RoleEditorModal({
  open,
  role,
  permissions,
  onClose,
}: {
  open: boolean
  role: RoleDto | null
  permissions: PermissionDto[]
  onClose: () => void
}) {
  const t = useT()
  const save = useSaveRole()
  const { data: categories } = useLookups('PermissionCategory')
  const categoryLabel = (code: string) => categories?.find((c) => c.code === code)?.label ?? code
  const [selected, setSelected] = useState<string[]>([])
  const isEdit = role !== null

  useEffect(() => {
    if (open) setSelected(role?.permissions ?? [])
  }, [open, role])

  const groups = useMemo(() => groupPermissionsByCategory(permissions), [permissions])

  const schema = useMemo(
    () => z.object({ name: z.string().trim().min(1, t('system.users.roles.nameRequired')), descriptionEs: z.string(), descriptionEn: z.string() }),
    [t],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    values: { name: role?.name ?? '', descriptionEs: role?.descriptions?.es ?? '', descriptionEn: role?.descriptions?.en ?? '' },
  })
  const formId = 'role-editor'

  const toggle = (code: string) =>
    setSelected((prev) => (prev.includes(code) ? prev.filter((c) => c !== code) : [...prev, code]))
  const toggleGroup = (items: PermissionDto[]) => {
    const codes = items.map((p) => p.code ?? '')
    const allIn = codes.every((c) => selected.includes(c))
    setSelected((prev) => (allIn ? prev.filter((c) => !codes.includes(c)) : Array.from(new Set([...prev, ...codes]))))
  }

  const close = () => {
    form.reset()
    onClose()
  }

  return (
    <Modal
      open={open}
      title={isEdit ? t('system.users.roles.editTitle') : t('system.users.roles.newTitle')}
      onClose={close}
      dismissible={!form.formState.isSubmitting}
      // 'md' (640 px) y no 'lg' (880 px): es la medida del kit más cercana a los 560 px de la maqueta. Los permisos
      // van uno por renglón y las dos descripciones caben lado a lado; bajo 480 px `.r2` pasa a una columna.
      size="md"
      footer={
        <>
          <button type="button" className="btn" onClick={close}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('system.users.roles.save')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          const descriptions = v.descriptionEs || v.descriptionEn ? { es: v.descriptionEs, en: v.descriptionEn } : null
          await save.mutateAsync({ id: role?.id ?? null, body: { name: v.name, descriptions, permissions: selected } })
          toast.success(isEdit ? t('system.users.roles.saved') : t('system.users.roles.created'))
          close()
        }}
      >
        <Field name="name" label={t('system.users.roles.name')} required>
          <TextInput maxLength={100} />
        </Field>
        <div className="r2">
          <Field name="descriptionEs" label={t('system.users.roles.descriptionEs')}>
            <TextArea rows={2} />
          </Field>
          <Field name="descriptionEn" label={t('system.users.roles.descriptionEn')}>
            <TextArea rows={2} />
          </Field>
        </div>
        <p className="permissions-label">{t('system.users.roles.permissions')}</p>
        {groups.map((g) => {
          const codes = g.items.map((p) => p.code ?? '')
          const allIn = codes.length > 0 && codes.every((c) => selected.includes(c))
          const someIn = codes.some((c) => selected.includes(c))
          const Icon = categoryIcon(g.category)
          return (
            <div key={g.category} className="permgrp">
              <div className="permgrp-h">
                <Icon />
                <span>{categoryLabel(g.category)}</span>
                <GroupCheckbox checked={allIn} indeterminate={someIn} onChange={() => toggleGroup(g.items)} label={t('system.users.roles.selectAllGroup')} />
              </div>
              <div className="permgrp-items">
                {g.items.map((p) => (
                  <label key={p.code} className="permrow">
                    <input type="checkbox" checked={selected.includes(p.code ?? '')} onChange={() => toggle(p.code ?? '')} />
                    {p.label}
                  </label>
                ))}
              </div>
            </div>
          )
        })}
      </Form>
    </Modal>
  )
}

export interface RolesTabProps {
  /** true = la cabecera de la página pidió "Nuevo rol": se abre el editor vacío. */
  creating?: boolean
  /** Se llama al cerrar el editor abierto por `creating`. */
  onCreateClose?: () => void
}

export function RolesTab({ creating = false, onCreateClose }: RolesTabProps) {
  const t = useT()
  const { data: roles, isLoading } = useRoles()
  const { data: permissions = [] } = usePermissions()
  const deleteRole = useDeleteRole()
  const [editing, setEditing] = useState<RoleDto | null>(null)
  const [toDelete, setToDelete] = useState<RoleDto | null>(null)

  const { data: templates = [] } = useRoleTemplates()

  const rows = roles ?? []
  const totalPermissions = permissions.length
  // `GET /roles` sin `includeTemplates` solo trae los roles de la compañía (nunca `isTemplate`): los que "vienen ya armados"
  // son las plantillas de sistema de las que se clonaron. Se nombran por su descripción (traducida; el nombre es la clave
  // interna, p. ej. 'TenantAdmin'). Sin plantillas legibles, la nota sale sin la lista.
  const templateNames = templates.map((r) => r.description || r.name).filter((n): n is string => !!n)

  const columns = useMemo<DataColumn<RoleDto>[]>(
    () => [
      {
        id: 'name',
        header: t('system.users.roles.colName'),
        card: 'title',
        sortValue: (r) => r.name,
        cell: (r) => (
          <>
            <b>{r.name}</b>{' '}
            {r.isSystem && <Chip tone="cap">{t('system.users.roles.system')}</Chip>}{' '}
            {r.isTemplate && <Chip tone="cap">{t('system.users.roles.template')}</Chip>}
          </>
        ),
      },
      {
        id: 'permCount',
        header: t('system.users.roles.colPermissions'),
        align: 'end',
        sortValue: (r) => r.permissions?.length ?? 0,
        cell: (r) => (
          <span className="mono">
            {r.permissions?.length ?? 0} / {totalPermissions}
          </span>
        ),
      },
      {
        id: 'userCount',
        header: t('system.users.roles.colUsers'),
        align: 'end',
        sortValue: (r) => r.userCount ?? 0,
        cell: (r) => <span className="mono">{r.userCount ?? 0}</span>,
      },
    ],
    [t, totalPermissions],
  )

  const actions = useMemo<RowAction<RoleDto>[]>(
    () => [
      { key: 'edit', label: t('system.users.roles.edit'), perm: 'admin.roles', icon: <IconEdit />, onClick: (r) => setEditing(r) },
      { key: 'delete', label: t('system.users.roles.delete'), perm: 'admin.roles', tone: 'danger', icon: <IconTrash />, onClick: (r) => setToDelete(r) },
    ],
    [t],
  )

  return (
    <>
      <Panel flush icon={<IconShield />} title={t('system.users.roles.title')} badge={rows.length}>
        <DataTable
          label={t('system.users.roles.title')}
          columns={columns}
          rows={rows}
          rowKey={(r) => r.id ?? 0}
          defaultSort={{ id: 'name', desc: false }}
          pageSize={25}
          loading={isLoading}
          rowActions={actions}
        />
      </Panel>

      {/* nota al pie del panel (maqueta `rolesNote`), siempre visible como la de Usuarios */}
      <p className="note" style={{ marginTop: 16 }}>
        {templateNames.length > 0
          ? t('system.users.roles.note', { count: templateNames.length, names: templateNames.join(', ') })
          : t('system.users.roles.noteGeneric')}
      </p>

      <RoleEditorModal
        open={editing !== null || creating}
        role={editing}
        permissions={permissions}
        onClose={() => {
          setEditing(null)
          onCreateClose?.()
        }}
      />

      <ConfirmDialog
        open={toDelete !== null}
        tone="danger"
        title={t('system.users.roles.deleteTitle')}
        message={t('system.users.roles.deleteBody', { name: toDelete?.name ?? '' })}
        confirmLabel={t('system.users.roles.delete')}
        onConfirm={async () => {
          if (!toDelete?.id) return
          try {
            await deleteRole.mutateAsync(toDelete.id)
            toast.success(t('system.users.roles.deleted'))
          } catch (err) {
            toast.error(applyProblemDetails(err).title)
          }
        }}
        onClose={() => setToDelete(null)}
      />
    </>
  )
}
