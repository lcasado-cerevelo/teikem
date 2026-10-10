// Panel «Teléfonos y correos» del propio cliente (ContactPoint con dueño CLIENT). Se lee de la ficha (`contactPoints`);
// la escritura va por /api/v1/contacts/CLIENT/{id} (contacts.manage; el servidor exige además clients.update, el permiso
// de la entidad dueña). El teléfono lleva la máscara de la compañía y se guarda solo con dígitos; un principal por tipo.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Can } from '../../kernel/access'
import { useLookups } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, EmptyState, Field, Form, IconEdit, IconTrash, Modal, PhoneInput, Panel, Select, TextInput, Toggle, toast } from '../../kernel/ui'
import { IconPhoneFormat } from '../../kernel/ui/screenIcons'
import { useSaveContactPoint } from './api'
import { buildPointRequest, EMAIL_TYPE, isEmailType, PHONE_TYPE, pointSchema, sortContactPoints, type ClientDetail, type ContactPoint } from './clientRules'

type Editing = { point: ContactPoint | null; email: boolean }

function PointModal({ client, editing, onClose }: { client: ClientDetail; editing: Editing | null; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const save = useSaveContactPoint(client.publicId ?? '')
  const { data: types = [] } = useLookups('ContactType')
  const phoneTypes = types.filter((o) => !isEmailType(o.code))
  const formId = 'client-point'
  const isEmail = editing?.email ?? false
  const point = editing?.point ?? null

  const schema = useMemo(
    () => pointSchema({ valueRequired: t('clients.errors.valueRequired'), emailInvalid: t('clients.errors.emailInvalid'), phoneInvalid: t('clients.errors.phoneInvalid') }, f.isValidPhone),
    [t, f],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    values: {
      contactType: point?.contactType ?? (isEmail ? EMAIL_TYPE : PHONE_TYPE),
      value: point ? (isEmailType(point.contactType) ? (point.value ?? '') : f.phoneInput(point.value ?? '')) : '',
      extension: point?.extension ?? '',
      label: point?.label ?? '',
      isPrimary: point?.isPrimary ?? false,
    },
  })

  return (
    <Modal
      open={editing !== null}
      title={point ? t(isEmail ? 'clients.points.editEmail' : 'clients.points.editPhone') : t(isEmail ? 'clients.points.addEmail' : 'clients.points.addPhone')}
      onClose={onClose}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>
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
        onSubmit={async (v) => {
          const body = buildPointRequest(v, f.normalizePhone)
          if (point) await save.mutateAsync({ action: 'update', id: point.id, body })
          else await save.mutateAsync({ action: 'create', ownerEntity: 'CLIENT', ownerId: client.id, body })
          toast.success(t('clients.points.saved'))
          onClose()
        }}
      >
        {!isEmail && (
          <Field name="contactType" label={t('clients.points.type')}>
            <Select options={phoneTypes.map((o) => ({ value: o.code, label: o.label }))} />
          </Field>
        )}
        <Field name="value" label={isEmail ? t('clients.points.email') : t('clients.points.phone')} required>
          {isEmail ? <TextInput type="email" maxLength={200} /> : <PhoneInput />}
        </Field>
        <div className="r2">
          {!isEmail && (
            <Field name="extension" label={t('clients.points.extension')}>
              <TextInput maxLength={20} />
            </Field>
          )}
          <Field name="label" label={t('clients.points.label')}>
            <TextInput maxLength={60} />
          </Field>
        </div>
        <Field name="isPrimary" label={t('clients.points.primary')} help={t('clients.points.primaryHelp')}>
          <Toggle />
        </Field>
      </Form>
    </Modal>
  )
}

export function ClientContactPointsPanel({ client }: { client: ClientDetail }) {
  const t = useT()
  const f = useFormat()
  const save = useSaveContactPoint(client.publicId ?? '')
  const [editing, setEditing] = useState<Editing | null>(null)
  const [removing, setRemoving] = useState<ContactPoint | null>(null)
  const points = useMemo(() => sortContactPoints(client.contactPoints ?? []), [client.contactPoints])
  const phones = points.filter((p) => !isEmailType(p.contactType))
  const emails = points.filter((p) => isEmailType(p.contactType))

  const renderList = (list: ContactPoint[], emptyKey: string) =>
    list.length === 0 ? (
      <p className="note">{t(emptyKey)}</p>
    ) : (
      <ul className="cl-points">
        {list.map((p) => (
          <li key={p.id} className="unrow">
            <span className="cl-point-main">
              <span className="cl-point-value">
                <span className="mono">{isEmailType(p.contactType) ? p.value : f.phone(p.value ?? '')}</span>
                {p.extension && <span className="meta"> {t('clients.points.ext', { ext: p.extension })}</span>}
              </span>
              <span className="meta">{[p.contactTypeLabel, p.label].filter(Boolean).join(' · ')}</span>
            </span>
            {p.isPrimary && <Chip tone="deliv">{t('clients.points.primaryChip')}</Chip>}
            <Can perm={['contacts.manage', 'clients.update']}>
              <button type="button" className="rowbtn" aria-label={t('clients.points.edit', { value: p.value ?? '' })} title={t('clients.edit')} onClick={() => setEditing({ point: p, email: isEmailType(p.contactType) })}>
                <IconEdit />
              </button>
              <button type="button" className="rowbtn danger" aria-label={t('clients.points.remove', { value: p.value ?? '' })} title={t('clients.remove')} onClick={() => setRemoving(p)}>
                <IconTrash />
              </button>
            </Can>
          </li>
        ))}
      </ul>
    )

  return (
    <Panel
      icon={<IconPhoneFormat />}
      title={t('clients.points.title')}
      actions={
        <Can perm={['contacts.manage', 'clients.update']}>
          <button type="button" className="btn sm" onClick={() => setEditing({ point: null, email: false })}>
            {t('clients.points.addPhone')}
          </button>
          <button type="button" className="btn sm" onClick={() => setEditing({ point: null, email: true })}>
            {t('clients.points.addEmail')}
          </button>
        </Can>
      }
    >
      {points.length === 0 ? (
        <EmptyState icon={<IconPhoneFormat />} title={t('clients.points.empty')} />
      ) : (
        <div className="cl-points-cols">
          <div>
            <b className="cl-sub">{t('clients.points.phones')}</b>
            {renderList(phones, 'clients.points.noPhones')}
          </div>
          <div>
            <b className="cl-sub">{t('clients.points.emails')}</b>
            {renderList(emails, 'clients.points.noEmails')}
          </div>
        </div>
      )}

      {/* se vuelve a montar por punto: el formulario arranca con los valores de lo que se edita */}
      <PointModal key={editing ? `${editing.point?.id ?? 'new'}-${editing.email}` : 'closed'} client={client} editing={editing} onClose={() => setEditing(null)} />
      <ConfirmDialog
        open={removing !== null}
        tone="danger"
        title={t('clients.points.removeTitle')}
        message={t('clients.points.removeBody', { value: removing?.value ?? '' })}
        confirmLabel={t('clients.remove')}
        onConfirm={async () => {
          if (!removing) return
          await save.mutateAsync({ action: 'remove', id: removing.id })
          toast.success(t('clients.points.removed'))
        }}
        onClose={() => setRemoving(null)}
      />
    </Panel>
  )
}
