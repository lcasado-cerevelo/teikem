// Panel «Direcciones» de la ficha del cliente (va justo después de la cabecera, antes del Perfil fiscal): la dirección FÍSICA (una),
// la dirección POSTAL (una; «igual a la física» mientras no tenga la suya, y entonces puede ser un P.O. Box), los PUNTOS DE RECOGIDA
// (los que quiera) y «Se recoge en», que puede ser cualquiera de ellas. Cada dirección es una localización (dbo.Location) del cliente
// de tipo CORPORATE / BILLING / PICKUP: se crea y se edita con /api/v1/locations (locations.create / locations.update) y el recogido
// por defecto se guarda con PATCH /clients/{id}/profile (clients.update). Quitar es baja lógica, nunca DELETE.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { ConfirmDialog, EmptyState, Field, Form, IconEdit, IconPlus, IconTrash, Modal, Panel, Select, TextInput, toast } from '../../kernel/ui'
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
  kind: AddressKind
  /** La dirección que se edita; null = alta. */
  address: ClientAddress | null
  open: boolean
  onClose: () => void
}

function AddressModal({ client, kind, address, open, onClose }: AddressModalProps) {
  const t = useT()
  const clientId = client.publicId ?? ''
  const save = useSaveClientAddress(clientId)
  const { data: countries = [] } = useLookups('Country')
  const formId = `client-address-${kind}`
  const editing = address !== null
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

  const title = t(`clients.addresses.modal.${kind}${editing ? 'Edit' : 'New'}`)

  return (
    <Modal
      open={open}
      title={title}
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

type ModalState = { kind: AddressKind; address: ClientAddress | null } | null

function AddressRow({
  title,
  address,
  fallback,
  actions,
}: {
  title: string
  address: ClientAddress | null | undefined
  fallback: string
  actions: React.ReactNode
}) {
  return (
    <div className="cl-adr-row">
      <div className="cl-adr-main">
        <b>{title}</b>
        {address?.name && address.locationType !== 'CORPORATE' && address.locationType !== 'BILLING' && <span className="cl-adr-name">{address.name}</span>}
        <span className={address ? '' : 'cl-adr-empty'}>{address ? addressLine(address) : fallback}</span>
      </div>
      <div className="cl-adr-acts">{actions}</div>
    </div>
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

  const edit = (kind: AddressKind, address: ClientAddress | null) =>
    canEdit && address ? (
      <button type="button" className="btn sm" onClick={() => setModal({ kind, address })}>
        <IconEdit /> {t('clients.addresses.edit')}
      </button>
    ) : null

  return (
    <Panel icon={<IconPin />} title={t('clients.addresses.title')} collapsible storageKey="clients.addresses" summary={summary}>
      <div className="cl-adr">
        <AddressRow
          title={t('clients.addresses.physical')}
          address={physical}
          fallback={t('clients.addresses.none')}
          actions={
            physical
              ? edit('physical', physical)
              : canAdd && (
                  <button type="button" className="btn sm flow" onClick={() => setModal({ kind: 'physical', address: null })}>
                    <IconPlus /> {t('clients.addresses.add')}
                  </button>
                )
          }
        />
        <AddressRow
          title={t('clients.addresses.postal')}
          address={postal}
          fallback={physical ? t('clients.addresses.samePostal') : t('clients.addresses.none')}
          actions={
            postal ? (
              <>
                {edit('postal', postal)}
                {canEdit && (
                  <button type="button" className="btn sm" onClick={() => setRemoving({ address: postal, kind: 'postal' })}>
                    {t('clients.addresses.usePhysical')}
                  </button>
                )}
              </>
            ) : (
              canAdd && (
                <button type="button" className="btn sm" onClick={() => setModal({ kind: 'postal', address: null })}>
                  <IconPlus /> {t('clients.addresses.otherPostal')}
                </button>
              )
            )
          }
        />

        <div className="cl-adr-points">
          <div className="cl-adr-head">
            <b>{t('clients.addresses.points')}</b>
            {canAdd && (
              <button type="button" className="btn sm" onClick={() => setModal({ kind: 'pickup', address: null })}>
                <IconPlus /> {t('clients.addresses.addPoint')}
              </button>
            )}
          </div>
          {points.length === 0 ? (
            <EmptyState icon={<IconPin />} title={t('clients.addresses.noPoints')} />
          ) : (
            <ul className="cl-adr-list">
              {points.map((p) => (
                <li key={p.publicId}>
                  <AddressRow
                    title={p.name ?? ''}
                    address={{ ...p, name: null }}
                    fallback=""
                    actions={
                      <>
                        {edit('pickup', p)}
                        {canEdit && (
                          <button type="button" className="btn sm" aria-label={t('clients.addresses.removePoint', { name: p.name ?? '' })} onClick={() => setRemoving({ address: p, kind: 'pickup' })}>
                            <IconTrash /> {t('clients.addresses.remove')}
                          </button>
                        )}
                      </>
                    }
                  />
                </li>
              ))}
            </ul>
          )}
        </div>

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
      </div>

      <AddressModal
        key={modal ? `${modal.kind}-${modal.address?.publicId ?? 'new'}` : 'closed'}
        client={client}
        kind={modal?.kind ?? 'physical'}
        address={modal?.address ?? null}
        open={modal !== null}
        onClose={() => setModal(null)}
      />
      <ConfirmDialog
        open={removing !== null}
        tone={removing?.kind === 'pickup' ? 'danger' : 'flow'}
        title={t(removing?.kind === 'pickup' ? 'clients.addresses.removeTitle' : 'clients.addresses.usePhysicalTitle')}
        message={t(removing?.kind === 'pickup' ? 'clients.addresses.removeBody' : 'clients.addresses.usePhysicalBody', { name: removing?.address.name ?? '' })}
        confirmLabel={t(removing?.kind === 'pickup' ? 'clients.addresses.remove' : 'clients.addresses.usePhysical')}
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
