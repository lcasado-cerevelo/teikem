// Panel «Teléfonos y correos» de cualquier dueño (ContactPoint): clientes (CLIENT) y órdenes (TRANSPORT_ORDER). Las filas las
// entrega quien lo usa (la ficha del cliente trae `contactPoints`; la orden las lee de /contacts/{dueño}/{id}); la escritura va por
// /api/v1/contacts/{dueño}/{id} (contacts.manage; el servidor exige además el permiso de la entidad dueña: `perms`). El teléfono lleva
// la máscara de la compañía y se guarda solo con dígitos; un principal por tipo. Los textos son los de `clients.points.*`.
import { PhoneLink } from './PhoneLink'
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Can } from '../../kernel/access'
import { useLookups } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { CallingCodeInput, Chip, ConfirmDialog, EmptyState, Field, Form, IconEdit, IconTrash, Modal, PhoneInput, PhoneRow, Panel, Select, TextInput, Toggle, toast } from '../../kernel/ui'
import { isValidCallingCode } from '../../kernel/format'
import { IconPhone } from '../../kernel/ui/screenIcons'
import { buildPointRequest, EMAIL_TYPE, isEmailType, PHONE_TYPE, pointSchema, sortContactPoints, type ContactPoint } from '../clients/clientRules'
import '../clients/clients.css'
import { useContactPointActions } from './api'

type Editing = { point: ContactPoint | null; email: boolean }

export interface ContactPointsPanelProps {
  /** Entidad dueña (`CLIENT`, `TRANSPORT_ORDER`…) e id interno. */
  ownerEntity: string
  ownerId: number
  points: readonly ContactPoint[]
  /** Permisos que se exigen TODOS para escribir (`contacts.manage` + el de la entidad dueña). Sin ellos el panel es de lectura. */
  perms: string[]
  /** Se llama tras cada alta, edición o baja para releer lo que el dueño muestra. */
  onChanged: () => Promise<unknown> | void
  /** Prefijo del id del formulario del modal. */
  idPrefix?: string
  /** Claves de los textos que nombran al dueño (por defecto los del cliente). */
  emptyKey?: string
  removeBodyKey?: string
  /** Solo lectura aunque el usuario tenga los permisos (el dueño ya no admite cambios: un chofer eliminado). */
  readOnly?: boolean
  /** Panel desplegable: cerrado resume el teléfono y el correo principales y cuántos más hay. */
  collapsible?: boolean
  /** Arranca cerrado. */
  defaultCollapsed?: boolean
}

interface PointModalProps {
  owner: { entity: string; id: number }
  editing: Editing | null
  onClose: () => void
  onChanged: ContactPointsPanelProps['onChanged']
  idPrefix: string
}

function PointModal({ owner, editing, onClose, onChanged, idPrefix }: PointModalProps) {
  const t = useT()
  const f = useFormat()
  const save = useContactPointActions(onChanged)
  const { data: types = [] } = useLookups('ContactType')
  const phoneTypes = types.filter((o) => !isEmailType(o.code))
  const formId = `${idPrefix}-point`
  const isEmail = editing?.email ?? false
  const point = editing?.point ?? null

  const schema = useMemo(
    () => pointSchema({ valueRequired: t('clients.errors.valueRequired'), emailInvalid: t('clients.errors.emailInvalid'), phoneInvalid: t('clients.errors.phoneInvalid'), countryCodeInvalid: t('ui.phone.countryCodeInvalid') }, f.isValidPhoneFor, isValidCallingCode),
    [t, f],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    values: {
      contactType: point?.contactType ?? (isEmail ? EMAIL_TYPE : PHONE_TYPE),
      value: point ? (isEmailType(point.contactType) ? (point.value ?? '') : f.usesCompanyMaskFor(point.countryCallingCode) ? f.phoneInput(point.value ?? '') : (point.value ?? '')) : '',
      countryCallingCode: f.callingCodeInput(point?.countryCallingCode),
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
          const body = buildPointRequest(v, f.normalizePhoneFor)
          if (point) await save.mutateAsync({ action: 'update', id: point.id ?? 0, body })
          else await save.mutateAsync({ action: 'create', ownerEntity: owner.entity, ownerId: owner.id, body })
          toast.success(t('clients.points.saved'))
          onClose()
        }}
      >
        {!isEmail && (
          <Field name="contactType" label={t('clients.points.type')}>
            <Select options={phoneTypes.map((o) => ({ value: o.code, label: o.label }))} />
          </Field>
        )}
        {isEmail ? (
          <Field name="value" label={t('clients.points.email')} required>
            <TextInput type="email" maxLength={200} />
          </Field>
        ) : (
          <PhoneRow>
            <Field name="countryCallingCode" label={t('ui.phone.countryCode')}>
              <CallingCodeInput />
            </Field>
            <Field name="value" label={t('clients.points.phone')} required>
              <PhoneInput />
            </Field>
          </PhoneRow>
        )}
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

export function ContactPointsPanel({ ownerEntity, ownerId, points: rows, perms, onChanged, idPrefix = 'owner', emptyKey = 'clients.points.empty', removeBodyKey = 'clients.points.removeBody', readOnly = false, collapsible = false, defaultCollapsed = false }: ContactPointsPanelProps) {
  const t = useT()
  const f = useFormat()
  const save = useContactPointActions(onChanged)
  const [editing, setEditing] = useState<Editing | null>(null)
  const [removing, setRemoving] = useState<ContactPoint | null>(null)
  const points = useMemo(() => sortContactPoints(rows), [rows])
  // solo lectura: ningún botón de escritura
  const writePerms = readOnly ? null : perms
  const phones = points.filter((p) => !isEmailType(p.contactType))
  const emails = points.filter((p) => isEmailType(p.contactType))
  // cerrado: el teléfono y el correo principales y cuántos más hay (una línea)
  const mainPhone = phones.find((p) => p.isPrimary) ?? phones[0]
  const mainEmail = emails.find((p) => p.isPrimary) ?? emails[0]
  const moreCount = points.length - (mainPhone ? 1 : 0) - (mainEmail ? 1 : 0)
  const pointsSummary =
    points.length === 0
      ? t('clients.summary.pointsNone')
      : [
          mainPhone ? t('clients.summary.pointsPhone', { value: f.phoneFor(mainPhone.value ?? '', mainPhone.countryCallingCode) }) : null,
          mainEmail ? t('clients.summary.pointsEmail', { value: mainEmail.value ?? '' }) : null,
          moreCount > 0 ? t('clients.summary.more', { n: moreCount }) : null,
        ]
          .filter(Boolean)
          .join(' · ')

  const renderList = (list: ContactPoint[], listEmptyKey: string) =>
    list.length === 0 ? (
      <p className="note">{t(listEmptyKey)}</p>
    ) : (
      <ul className="cl-points">
        {list.map((p) => (
          <li key={p.id} className="unrow">
            <span className="cl-point-main">
              <span className="cl-point-value">
                <span className="mono">{isEmailType(p.contactType) ? p.value : <PhoneLink value={p.value ?? ''} countryCallingCode={p.countryCallingCode} />}</span>
                {p.extension && <span className="meta"> {t('clients.points.ext', { ext: p.extension })}</span>}
              </span>
              <span className="meta">{[p.contactTypeLabel, p.label].filter(Boolean).join(' · ')}</span>
            </span>
            {p.isPrimary && <Chip tone="deliv">{t('clients.points.primaryChip')}</Chip>}
            {writePerms && (
            <Can perm={writePerms}>
              <button type="button" className="rowbtn" aria-label={t('clients.points.edit', { value: p.value ?? '' })} title={t('clients.edit')} onClick={() => setEditing({ point: p, email: isEmailType(p.contactType) })}>
                <IconEdit />
              </button>
              <button type="button" className="rowbtn danger" aria-label={t('clients.points.remove', { value: p.value ?? '' })} title={t('clients.remove')} onClick={() => setRemoving(p)}>
                <IconTrash />
              </button>
            </Can>
            )}
          </li>
        ))}
      </ul>
    )

  return (
    <Panel
      icon={<IconPhone />}
      title={t('clients.points.title')}
      collapsible={collapsible}
      defaultCollapsed={defaultCollapsed}
      summary={pointsSummary}
      actions={
        writePerms ? (
          <Can perm={writePerms}>
            <button type="button" className="btn sm" onClick={() => setEditing({ point: null, email: false })}>
              {t('clients.points.addPhone')}
            </button>
            <button type="button" className="btn sm" onClick={() => setEditing({ point: null, email: true })}>
              {t('clients.points.addEmail')}
            </button>
          </Can>
        ) : undefined
      }
    >
      {points.length === 0 ? (
        <EmptyState icon={<IconPhone />} title={t(emptyKey)} />
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
      <PointModal
        key={editing ? `${editing.point?.id ?? 'new'}-${editing.email}` : 'closed'}
        owner={{ entity: ownerEntity, id: ownerId }}
        editing={editing}
        onClose={() => setEditing(null)}
        onChanged={onChanged}
        idPrefix={idPrefix}
      />
      <ConfirmDialog
        open={removing !== null}
        tone="danger"
        title={t('clients.points.removeTitle')}
        message={t(removeBodyKey, { value: removing?.value ?? '' })}
        confirmLabel={t('clients.remove')}
        onConfirm={async () => {
          if (!removing) return
          await save.mutateAsync({ action: 'remove', id: removing.id ?? 0 })
          toast.success(t('clients.points.removed'))
        }}
        onClose={() => setRemoving(null)}
      />
    </Panel>
  )
}
