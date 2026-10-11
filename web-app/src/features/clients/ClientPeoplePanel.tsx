// Panel «Personas de contacto»: tabla Nombre · Puesto · Teléfono · Correo · principal. Alta y edición en modal
// (/api/v1/clients/{publicId}/contacts, clients.update); «Quitar» = PATCH isActive:false (nunca DELETE); un solo principal
// activo (marcar otro quita la marca al anterior en el servidor). El teléfono y el correo de la persona son medios de
// contacto del dueño CLIENT_CONTACT y se escriben con contacts.manage.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Can, useCan } from '../../kernel/access'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import {
  Chip,
  ConfirmDialog,
  DataTable,
  type DataColumn,
  EmptyState,
  Field,
  Form,
  IconEdit,
  IconTrash,
  Modal,
  Panel,
  PhoneInput,
  TextInput,
  Toggle,
  toast,
  useElementWidth,
} from '../../kernel/ui'
import { IconUsers } from '../../kernel/ui/screenIcons'
import { useSaveClientContact, useSaveContactPoint } from './api'
import {
  CONTACT_NAME_MAX,
  CONTACT_ROLE_MAX,
  EMAIL_TYPE,
  personEmail,
  personPhone,
  personSchema,
  PHONE_TYPE,
  sortContacts,
  type ClientContact,
  type ClientDetail,
  type ContactPoint,
} from './clientRules'

function PersonModal({ client, person, open, onClose }: { client: ClientDetail; person: ClientContact | null; open: boolean; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const publicId = client.publicId ?? ''
  const canPoints = useCan('contacts.manage')
  const saveContact = useSaveClientContact(publicId)
  const savePoint = useSaveContactPoint(publicId)
  const formId = 'client-person'

  const schema = useMemo(
    () =>
      personSchema(
        { nameRequired: t('clients.errors.contactNameRequired'), nameMax: t('clients.errors.max', { max: CONTACT_NAME_MAX }), roleMax: t('clients.errors.max', { max: CONTACT_ROLE_MAX }) },
        f.isValidPhone,
        t('clients.errors.phoneInvalid'),
        t('clients.errors.emailInvalid'),
      ),
    [t, f],
  )
  const phone = person ? personPhone(person) : undefined
  const email = person ? personEmail(person) : undefined
  const form = useForm({
    resolver: zodResolver(schema),
    values: {
      fullName: person?.fullName ?? '',
      role: person?.role ?? '',
      isPrimary: person?.isPrimary ?? false,
      phone: phone?.value ? f.phoneInput(phone.value) : '',
      email: email?.value ?? '',
    },
  })

  /** Sincroniza un medio de contacto de la persona: crea, cambia o quita según el valor anterior y el nuevo. */
  const syncPoint = async (ownerId: number, existing: ContactPoint | undefined, next: string, contactType: string) => {
    const value = contactType === EMAIL_TYPE ? next.trim().toLowerCase() : f.normalizePhone(next)
    if (existing && !next.trim()) await savePoint.mutateAsync({ action: 'remove', id: existing.id })
    else if (existing && value !== existing.value) await savePoint.mutateAsync({ action: 'update', id: existing.id, body: { contactType: existing.contactType ?? contactType, value, extension: existing.extension, label: existing.label, isPrimary: existing.isPrimary } })
    else if (!existing && next.trim()) await savePoint.mutateAsync({ action: 'create', ownerEntity: 'CLIENT_CONTACT', ownerId, body: { contactType, value, isPrimary: true } })
  }

  return (
    <Modal
      open={open}
      title={person ? t('clients.people.edit') : t('clients.people.add')}
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
          if (!person) {
            const points = []
            if (v.phone.trim()) points.push({ contactType: PHONE_TYPE, value: f.normalizePhone(v.phone), isPrimary: true })
            if (v.email.trim()) points.push({ contactType: EMAIL_TYPE, value: v.email.trim().toLowerCase(), isPrimary: true })
            await saveContact.mutateAsync({
              action: 'create',
              body: { fullName: v.fullName.trim(), role: v.role.trim() || null, isPrimary: v.isPrimary, contactPoints: canPoints ? points : [] },
            })
          } else {
            await saveContact.mutateAsync({ action: 'update', id: person.id, body: { fullName: v.fullName.trim(), role: v.role.trim(), isPrimary: v.isPrimary } })
            if (canPoints) {
              await syncPoint(person.id, phone, v.phone, PHONE_TYPE)
              await syncPoint(person.id, email, v.email, EMAIL_TYPE)
            }
          }
          toast.success(t('clients.people.saved'))
          onClose()
        }}
      >
        <Field name="fullName" label={t('clients.people.name')} required>
          <TextInput maxLength={CONTACT_NAME_MAX} />
        </Field>
        <Field name="role" label={t('clients.people.role')}>
          <TextInput maxLength={CONTACT_ROLE_MAX} />
        </Field>
        <div className="r2">
          <Field name="phone" label={t('clients.people.phone')}>
            <PhoneInput />
          </Field>
          <Field name="email" label={t('clients.people.email')}>
            <TextInput type="email" maxLength={200} />
          </Field>
        </div>
        {!canPoints && <p className="help">{t('clients.people.pointsNeedPermission')}</p>}
        <Field name="isPrimary" label={t('clients.people.primary')} help={t('clients.people.primaryHelp')}>
          <Toggle />
        </Field>
      </Form>
    </Modal>
  )
}

export function ClientPeoplePanel({ client }: { client: ClientDetail }) {
  const t = useT()
  const f = useFormat()
  const publicId = client.publicId ?? ''
  const save = useSaveClientContact(publicId)
  const [editing, setEditing] = useState<ClientContact | null | 'new'>(null)
  const [removing, setRemoving] = useState<ClientContact | null>(null)
  const ref = useRef<HTMLDivElement>(null)
  const width = useElementWidth(ref)
  const people = useMemo(() => sortContacts((client.contacts ?? []).filter((c) => c.isActive)), [client.contacts])

  const columns = useMemo<DataColumn<ClientContact>[]>(
    () => [
      { id: 'name', header: t('clients.people.name'), cell: (c) => <b>{c.fullName}</b>, sortValue: (c) => c.fullName, card: 'title' },
      { id: 'role', header: t('clients.people.role'), cell: (c) => c.role || '—', sortValue: (c) => c.role },
      {
        id: 'phone',
        header: t('clients.people.phone'),
        cell: (c) => {
          const p = personPhone(c)
          return p?.value ? <span className="mono">{f.phone(p.value)}</span> : '—'
        },
        sortValue: (c) => personPhone(c)?.value,
        exportValue: (c) => personPhone(c)?.value ?? '',
      },
      { id: 'email', header: t('clients.people.email'), cell: (c) => personEmail(c)?.value || '—', sortValue: (c) => personEmail(c)?.value },
      {
        id: 'primary',
        header: t('clients.people.primary'),
        cell: (c) => (c.isPrimary ? <Chip tone="deliv">{t('clients.people.primaryChip')}</Chip> : null),
        sortValue: (c) => (c.isPrimary ? 0 : 1),
      },
    ],
    [t, f],
  )

  return (
    <Panel
      flush
      icon={<IconUsers />}
      title={t('clients.people.title')}
      badge={people.length}
      actions={
        <Can perm="clients.update">
          <button type="button" className="btn sm flow" onClick={() => setEditing('new')}>
            {t('clients.people.add')}
          </button>
        </Can>
      }
    >
      <div ref={ref}>
        {people.length === 0 ? (
          <EmptyState icon={<IconUsers />} title={t('clients.people.empty')} />
        ) : (
          <DataTable
            label={t('clients.people.title')}
            columns={columns}
            rows={people}
            rowKey={(c) => String(c.id)}
            pagination={false}
            exportable={false}
            forceCards={width > 0 && width < 640}
            rowActions={[
              { key: 'edit', label: t('clients.edit'), icon: <IconEdit />, perm: 'clients.update', onClick: (c) => setEditing(c) },
              { key: 'remove', label: t('clients.remove'), icon: <IconTrash />, tone: 'danger', perm: 'clients.update', onClick: (c) => setRemoving(c) },
            ]}
          />
        )}
      </div>

      <PersonModal key={editing === null ? 'closed' : editing === 'new' ? 'new' : editing.id} client={client} person={editing === 'new' ? null : editing} open={editing !== null} onClose={() => setEditing(null)} />
      <ConfirmDialog
        open={removing !== null}
        tone="danger"
        title={t('clients.people.removeTitle')}
        message={t('clients.people.removeBody', { name: removing?.fullName ?? '' })}
        confirmLabel={t('clients.remove')}
        onConfirm={async () => {
          if (!removing) return
          await save.mutateAsync({ action: 'update', id: removing.id, body: { isActive: false } })
          toast.success(t('clients.people.removed'))
        }}
        onClose={() => setRemoving(null)}
      />
    </Panel>
  )
}
