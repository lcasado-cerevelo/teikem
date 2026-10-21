// Lote 169 (F3) — «Quién lo ve» de un indicador o gráfico (`analytics.manage`): privado / toda la compañía / compartido con personas y roles. Vale también para los «Por
// defecto» (el sistema los creó, pero la compañía decide quién los ve); es lo único que se les puede cambiar. Mismos selectores de personas y roles que el editor.
import { useState } from 'react'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n/useT'
import { Modal } from '../../kernel/ui/Modal'
import { SearchMultiSelect } from '../../kernel/ui/SearchSelect'
import { toast } from '../../kernel/ui/toast'
import { useRoles } from '../system/api'
import { useAnalyticsShareUsers, useSetVisibility, type PulseItemKind } from './api'
import type { ShareDto } from './definitions'

export interface VisibilityModalProps {
  kind: PulseItemKind
  id: number
  name: string
  visibility: string | null | undefined
  shares: ShareDto[] | null | undefined
  onClose: () => void
}

export function VisibilityModal({ kind, id, name, visibility, shares: initialShares, onClose }: VisibilityModalProps) {
  const t = useT()
  const save = useSetVisibility()
  const visibilities = useLookups('ReportVisibility').data ?? []
  const canBrowseUsers = useCan('admin.users')
  const shareUsers = useAnalyticsShareUsers(canBrowseUsers)
  const shareRoles = useRoles()
  const [mode, setMode] = useState(visibility || 'TENANT')
  const [shares, setShares] = useState<ShareDto[]>(() => [...(initialShares ?? [])])
  const formId = `vis-${kind}-${id}`

  const userOptions = (shareUsers.data ?? []).map((u) => ({ value: String(u.id), label: u.email ? `${u.fullName ?? ''} · ${u.email}` : (u.fullName ?? '') }))
  const userValue = shares.filter((s) => s.userId != null).map((s) => String(s.userId))
  const roleValue = shares.filter((s) => s.roleId != null).map((s) => String(s.roleId))
  const roleOptions = (shareRoles.data ?? [])
    .filter((r) => r.id != null && (r.isActive !== false || roleValue.includes(String(r.id))))
    .map((r) => ({ value: String(r.id), label: r.name ?? String(r.id) }))
  // Cada selector reemplaza solo su tipo de compartición: el de personas conserva las de rol y viceversa.
  const setUsers = (value: string[]) => setShares((prev) => [...prev.filter((s) => s.userId == null), ...value.map((v) => ({ userId: Number(v), roleId: null, canEdit: false }))])
  const setRoles = (value: string[]) => setShares((prev) => [...prev.filter((s) => s.roleId == null), ...value.map((v) => ({ userId: null, roleId: Number(v), canEdit: false }))])

  const submit = async () => {
    try {
      await save.mutateAsync({ kind, id, body: { visibility: mode, shares: mode === 'SHARED' ? shares : [] } })
      toast.success(t('analytics.visibility.saved'))
      onClose()
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }

  return (
    <Modal
      open
      size="md"
      title={t('analytics.visibility.title', { name })}
      onClose={onClose}
      dismissible={!save.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="button" className="btn flow" disabled={save.isPending} onClick={() => void submit()}>
            {save.isPending ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      <p className="note">{t('analytics.visibility.help')}</p>
      <div className="f">
        <label htmlFor={`${formId}-mode`}>{t('analytics.indicators.visibilityLabel')}</label>
        <select id={`${formId}-mode`} value={mode} onChange={(e) => setMode(e.target.value)}>
          {visibilities.map((v) => (
            <option key={v.code} value={v.code}>
              {v.label}
            </option>
          ))}
        </select>
      </div>
      {mode === 'SHARED' && (
        <div className="r2">
          {canBrowseUsers ? (
            <div className="f">
              <label htmlFor={`${formId}-users`} id={`${formId}-users-lbl`}>
                {t('analytics.indicators.shareWithUsersLabel')}
              </label>
              <SearchMultiSelect
                id={`${formId}-users`}
                labelledBy={`${formId}-users-lbl`}
                options={userOptions}
                value={userValue}
                onChange={setUsers}
                placeholder={t('analytics.indicators.shareWithUsersPh')}
              />
            </div>
          ) : (
            <p className="help">{t('analytics.editor.shareNeedsAdminUsers')}</p>
          )}
          <div className="f">
            <label htmlFor={`${formId}-roles`} id={`${formId}-roles-lbl`}>
              {t('analytics.indicators.shareWithRolesLabel')}
            </label>
            <SearchMultiSelect
              id={`${formId}-roles`}
              labelledBy={`${formId}-roles-lbl`}
              options={roleOptions}
              value={roleValue}
              onChange={setRoles}
              placeholder={t('analytics.indicators.shareWithRolesPh')}
            />
          </div>
        </div>
      )}
    </Modal>
  )
}
