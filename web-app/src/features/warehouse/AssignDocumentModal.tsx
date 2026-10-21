// Lote 171 (B2) — «Asignar» un aviso de llegada o un aviso de salida a la persona que lo va a recibir o surtir (usuarios de la compañía; `GET /api/v1/users` exige admin.users: sin ese
// permiso se avisa sin sacar de la pantalla, como en `AssignTaskModal`). Quien lo abre pasa `onSave` con su propia llamada (ASN o aviso de salida).
import { useQuery } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { api, unwrap } from '../../kernel/api/client'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Field, Form, Modal, Select, toast } from '../../kernel/ui'

export function AssignDocumentModal({ title, currentUserId, onSave, onClose }: {
  title: string
  currentUserId: number | null | undefined
  /** `null` = quitar la asignación. */
  onSave: (userId: number | null) => Promise<unknown>
  onClose: () => void
}) {
  const t = useT()
  const users = useQuery({ queryKey: ['/api/v1/users'], queryFn: () => unwrap(api.GET('/api/v1/users')), meta: { handleAccessDenied: false } })
  const form = useForm({ values: { userId: currentUserId != null ? String(currentUserId) : '' } })
  const formId = 'warehouse-document-assign'
  const options = (users.data ?? []).filter((u) => u.isActive !== false).map((u) => ({ value: String(u.id), label: u.fullName ?? u.email ?? String(u.id) }))

  return (
    <Modal
      open
      title={title}
      onClose={onClose}
      size="sm"
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={form.formState.isSubmitting || users.isError}>
            {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      {users.error ? (
        <p className="note">{t('warehouse.tasks.assign.noAccess')}</p>
      ) : (
        <Form
          id={formId}
          form={form}
          onSubmit={async (v) => {
            try {
              await onSave(v.userId ? Number(v.userId) : null)
              toast.success(t('warehouse.tasks.assign.saved'))
              onClose()
            } catch (e) {
              toast.error(applyProblemDetails(e).title)
            }
          }}
        >
          <Field name="userId" label={t('warehouse.tasks.assign.user')}>
            <Select options={options} placeholder={t('warehouse.tasks.assign.none')} />
          </Field>
        </Form>
      )}
    </Modal>
  )
}
