// Panel «Direcciones» de la ficha del cliente (va justo después de la cabecera, antes del Perfil fiscal): UNA lista con la dirección
// física (una), la postal (una; si no tiene la suya es la física) y los puntos de recogida (los que quiera), cada fila con su
// etiqueta y sus botones Editar / Quitar; UN solo botón, «Agregar dirección», que pregunta de qué tipo es; y «Se recoge en», que puede
// ser cualquiera de ellas. Cada dirección es una localización (dbo.Location) del cliente de tipo CORPORATE / BILLING / PICKUP: se
// crea y se edita con /api/v1/locations (locations.create / locations.update) y el recogido por defecto se guarda con
// PATCH /clients/{id}/profile (clients.update). Quitar es baja lógica, nunca DELETE.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, Field, Form, IconEdit, IconPlus, IconTrash, Modal, Panel, Select, TextInput, toast } from '../../kernel/ui'
import { IconPin } from '../../kernel/ui/screenIcons'
import { PostalLocalityPickerInput } from '../warehouse/PostalLocalityPicker'
import {
  addressSchema,
  addressValuesOf,
  buildAddressCreate,
  buildAddressPatch,
  currentPickupValue,
  emptyAddressValues,
  LINE_MAX,
  NAME_MAX,
  pickupOptions,
  PICKUP_PHYSICAL,
  shortAddress,
  type AddressKind,
  type AddressValues,
  type ClientAddress,
} from './addressRules'
import { useDeactivateClientAddress, useSaveClientAddress, useUpdateClientProfile } from './api'
import { addressLine, type ClientDetail } from './clientRules'

interface AddressModalProps {
  client: ClientDetail
  /** Tipos que se pueden crear (la física y la postal solo si el cliente no la tiene); al editar, el tipo de esa dirección. */
  kinds: AddressKind[]
  /** La dirección que se edita; null = alta. */
  address: ClientAddress | null
  open: boolean
  onClose: () => void
}

function AddressModal({ client, kinds, address, open, onClose }: AddressModalProps) {
  const t = useT()
  const clientId = client.publicId ?? ''
  const save = useSaveClientAddress(clientId)
  const { data: countries = [] } = useLookups('Country')
  const editing = address !== null
  const [kind, setKind] = useState<AddressKind>(kinds[0] ?? 'pickup')
  const formId = 'client-address'
  const initial = useMemo<AddressValues>(() => (address ? addressValuesOf(address) : emptyAddressValues()), [address])

  const schema = useMemo(
    () =>
      addressSchema(
        {
          nameRequired: t('clients.addresses.errors.nameRequired'),
          line1Required: t('consignees.errors.line1Required'),
          cityRequired: t('consignees.errors.cityRequired'),
          countryRequired: t('consignees.errors.countryRequired'),
          max: (max) => t('consignees.errors.max', { max }),
        },
        kind === 'pickup',
      ),
    [t, kind],
  )
  const form = useForm({ resolver: zodResolver(schema), values: initial, resetOptions: { keepDirtyValues: true } })
  const countryOptions = useMemo(() => {
    const list = countries.map((o) => ({ value: o.code, label: o.label }))
    if (address?.country && !list.some((o) => o.value === address.country)) list.push({ value: address.country, label: address.countryLabel ?? address.country })
    return list
  }, [countries, address])

  const close = () => {
    form.reset(initial, { keepDirtyValues: false })
    onClose()
  }

  return (
    <Modal
      open={open}
      title={editing ? t(`clients.addresses.edit.${kind}`) : t('clients.addresses.addAddress')}
      onClose={close}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn sm" onClick={close}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn sm flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      {/* el tipo solo se pregunta al agregar; al editar es el de la dirección */}
      {!editing && (
        <div className="f cl-kind">
          <label htmlFor="client-address-kind">{t('clients.addresses.kind')}</label>
          <select id="client-address-kind" value={kind} onChange={(e) => setKind(e.target.value as AddressKind)}>
            {kinds.map((k) => (
              <option key={k} value={k}>
                {t(`clients.addresses.kinds.${k}`)}
              </option>
            ))}
          </select>
          <p className="help">{t(`clients.addresses.kindHelp.${kind}`)}</p>
        </div>
      )}
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          if (address?.publicId) {
            const { request, changed } = buildAddressPatch(kind, v, addressValuesOf(address))
            if (changed) await save.mutateAsync({ publicId: address.publicId, patch: request })
          } else {
            await save.mutateAsync({ create: buildAddressCreate(kind, clientId, client.name ?? '', v) })
          }
          toast.success(t('clients.addresses.saved'))
          onClose()
        }}
      >
        {kind === 'pickup' && (
          <Field name="name" label={t('clients.addresses.pointName')} required help={t('clients.addresses.pointNameHelp')}>
            <TextInput maxLength={NAME_MAX} autoComplete="off" />
          </Field>
        )}
        <Field name="line1" label={t('consignees.address')} required>
          <TextInput maxLength={LINE_MAX} />
        </Field>
        <Field name="line2" label={t('clients.addresses.line2')}>
          <TextInput maxLength={LINE_MAX} />
        </Field>
        <div className="r2">
          <Field name="city" label={t('consignees.town')} required help={kind === 'postal' ? t('clients.addresses.townHelpPostal') : t('consignees.townHelp')}>
            {/* la dirección postal admite P.O. Box; la física y los puntos de recogida, solo códigos de entrega */}
            <PostalLocalityPickerInput zipClass={kind === 'postal' ? undefined : 'DELIVERY'} />
          </Field>
          <Field name="country" label={t('clients.addresses.country')} required>
            <Select options={countryOptions} />
          </Field>
        </div>
      </Form>
    </Modal>
  )
}

type ModalState = { kinds: AddressKind[]; address: ClientAddress | null } | null

interface RowProps {
  tag: string
  tone: 'wh' | 'deliv' | 'cap'
  name?: string | null
  text: string
  muted?: boolean
  actions?: React.ReactNode
}

function AddressRow({ tag, tone, name, text, muted, actions }: RowProps) {
  return (
    <li className="cl-adr-row">
      <Chip tone={tone}>{tag}</Chip>
      <div className="cl-adr-main">
        {name && <b className="cl-adr-name">{name}</b>}
        <span className={muted ? 'cl-adr-empty' : ''}>{text}</span>
      </div>
      {actions && <div className="cl-adr-acts">{actions}</div>}
    </li>
  )
}

export function ClientAddressesPanel({ client }: { client: ClientDetail }) {
  const t = useT()
  const clientId = client.publicId ?? ''
  const canAdd = useCan('locations.create')
  const canEdit = useCan('locations.update')
  const canPickup = useCan('clients.update')
  const profile = useUpdateClientProfile(clientId)
  const deactivate = useDeactivateClientAddress(clientId)
  const [modal, setModal] = useState<ModalState>(null)
  const [removing, setRemoving] = useState<{ address: ClientAddress; kind: 'postal' | 'pickup' } | null>(null)

  const physical = client.physicalAddress ?? null
  const postal = client.postalAddress ?? null
  // `pickupLocations` ya trae solo los puntos de recogida (PICKUP / BOTH)
  const points = client.pickupLocations ?? []
  const options = useMemo(
    () => pickupOptions({ postalAddress: postal, pickupLocations: client.pickupLocations }, { physical: t('clients.addresses.pickupPhysical'), postal: t('clients.addresses.pickupPostal') }),
    [postal, client.pickupLocations, t],
  )
  const pickupValue = currentPickupValue(client)
  const pickupLabel = options.find((o) => o.value === pickupValue)?.label ?? t('clients.addresses.pickupPhysical')

  // tipos que se pueden agregar: la física y la postal solo una vez; los puntos de recogida, siempre
  const addable: AddressKind[] = [...(physical ? [] : (['physical'] as const)), ...(postal ? [] : (['postal'] as const)), 'pickup']

  const summary = physical
    ? t('clients.summary.addresses', { physical: shortAddress(physical), pickup: pickupValue === PICKUP_PHYSICAL ? t('clients.addresses.pickupSame') : (client.pickupAddress?.name ?? pickupLabel) })
    : t('clients.addresses.none')

  const choosePickup = async (value: string) => {
    try {
      await profile.mutateAsync(
        value === PICKUP_PHYSICAL
          ? { clearDefaultPickup: true, rowVersion: client.rowVersion }
          : { clearDefaultPickup: false, defaultPickupLocationPublicId: value, rowVersion: client.rowVersion },
      )
      toast.success(t('clients.addresses.pickupSaved'))
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }

  const editBtn = (kind: AddressKind, address: ClientAddress) =>
    canEdit ? (
      <button type="button" className="btn sm" aria-label={t('clients.addresses.editAria', { what: address.name ?? t(`clients.addresses.kinds.${kind}`) })} onClick={() => setModal({ kinds: [kind], address })}>
        <IconEdit /> {t('clients.addresses.editBtn')}
      </button>
    ) : null
  const removeBtn = (kind: 'postal' | 'pickup', address: ClientAddress) =>
    canEdit ? (
      <button type="button" className="btn sm" aria-label={t('clients.addresses.removeAria', { what: address.name ?? t(`clients.addresses.kinds.${kind}`) })} onClick={() => setRemoving({ address, kind })}>
        <IconTrash /> {t('clients.addresses.remove')}
      </button>
    ) : null

  return (
    <Panel
      icon={<IconPin />}
      title={t('clients.addresses.title')}
      collapsible
      defaultCollapsed
      summary={summary}
      actions={
        canAdd && (
          <button type="button" className="btn sm flow" onClick={() => setModal({ kinds: addable, address: null })}>
            <IconPlus /> {t('clients.addresses.addAddress')}
          </button>
        )
      }
    >
      <ul className="cl-adr-list">
        <AddressRow
          tag={t('clients.addresses.tag.physical')}
          tone="wh"
          text={physical ? addressLine(physical) : t('clients.addresses.none')}
          muted={!physical}
          actions={physical ? editBtn('physical', physical) : null}
        />
        {postal ? (
          <AddressRow tag={t('clients.addresses.tag.postal')} tone="deliv" text={addressLine(postal)} actions={<>{editBtn('postal', postal)}{removeBtn('postal', postal)}</>} />
        ) : (
          <AddressRow tag={t('clients.addresses.tag.postal')} tone="deliv" text={physical ? t('clients.addresses.samePostal') : t('clients.addresses.none')} muted />
        )}
        {points.map((p) => (
          <AddressRow
            key={p.publicId}
            tag={t('clients.addresses.tag.pickup')}
            tone="cap"
            name={p.name}
            text={addressLine({ ...p, name: null })}
            actions={<>{editBtn('pickup', p)}{removeBtn('pickup', p)}</>}
          />
        ))}
      </ul>

      <div className="f cl-adr-pickup">
        <label htmlFor="client-pickup">{t('clients.addresses.pickup')}</label>
        <select id="client-pickup" value={pickupValue} disabled={!canPickup || profile.isPending} onChange={(e) => void choosePickup(e.target.value)}>
          {options.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
        <p className="help">{t('clients.addresses.pickupHelp')}</p>
      </div>

      <AddressModal
        key={modal ? `${modal.kinds.join('-')}-${modal.address?.publicId ?? 'new'}` : 'closed'}
        client={client}
        kinds={modal?.kinds ?? ['pickup']}
        address={modal?.address ?? null}
        open={modal !== null}
        onClose={() => setModal(null)}
      />
      <ConfirmDialog
        open={removing !== null}
        tone="danger"
        title={t(removing?.kind === 'pickup' ? 'clients.addresses.removeTitle' : 'clients.addresses.removePostalTitle')}
        message={t(removing?.kind === 'pickup' ? 'clients.addresses.removeBody' : 'clients.addresses.removePostalBody', { name: removing?.address.name ?? '' })}
        confirmLabel={t('clients.addresses.remove')}
        onClose={() => setRemoving(null)}
        onConfirm={async () => {
          const target = removing
          if (!target?.address.publicId) return
          // si falla, el diálogo muestra el mensaje del servidor y no se cierra
          await deactivate.mutateAsync(target.address.publicId)
          toast.success(t(target.kind === 'pickup' ? 'clients.addresses.removed' : 'clients.addresses.postalNowPhysical'))
        }}
      />
    </Panel>
  )
}
