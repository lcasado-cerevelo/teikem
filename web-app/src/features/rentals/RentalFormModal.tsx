// Lote F17 (Rentas F-R1) — alta de una renta (nace en Borrador y no reserva nada) y edición del encabezado (Borrador o
// Programada), `rental.manage`. Encabezado: cliente (activo), localidad PROPIA del cliente, contacto opcional, almacén de
// origen, inicio y recogido, contrato (número y fecha de firma), costo de transporte estimado y moneda (solo datos) y notas.
// Alta: además los equipos por serie (`EquipmentPicker`, con tarifa opcional) que van en el mismo POST; se pueden agregar
// después en la ficha. Edición: el cliente no se cambia; el almacén solo sin equipos; las fechas solo sin extensiones; el PATCH
// lleva solo lo que cambió (`rentalPatchBody`). Los errores del servidor quedan bajo su campo o en el aviso del formulario,
// con el mensaje exacto (p. ej. 409 'La serie {s} ya está en la renta {REN-n}.').
import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useNavigate } from 'react-router-dom'
import { z } from 'zod'
import { isAccessDenied } from '../warehouse/accessDenied'
import { useLookups, useTenantSettings } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import {
  ClientPickerInput,
  ComboSelectInput,
  DataTable,
  DateInput,
  EmptyState,
  Field,
  Form,
  Modal,
  NumberInput,
  Select,
  TextArea,
  TextInput,
  toast,
  type DataColumn,
  type RowAction,
} from '../../kernel/ui'
import { IconTrash } from '../../kernel/ui/actionIcons'
import { useFormat } from '../../kernel/format/useFormat'
import { useWarehouses, warehouseLabel } from '../warehouse/api'
import { WarehousePickerInput } from '../warehouse/pickers'
import { useClientContacts, useClientLocations, useCreateRental, useRentalAction } from './api'
import { EquipmentPicker } from './EquipmentPicker'
import {
  activeLines,
  addEquipment,
  createRentalBody,
  datesIssue,
  headerValuesOf,
  RENTAL_LIMITS,
  rentalPatchBody,
  serialKey,
  type PickedEquipment,
  type RentalDto,
  type RentalHeaderValues,
} from './rentalRules'
import { useFrequencyLabel } from './useFrequencyOptions'
import './rentals.css'

export interface RentalFormModalProps {
  open: boolean
  /** null = alta (Borrador). */
  rental: RentalDto | null
  onClose: () => void
}

const FORM_ID = 'rental-header-form'

function emptyValues(today: string): RentalHeaderValues {
  return {
    clientPublicId: null,
    locationPublicId: '',
    clientContactId: '',
    warehousePublicId: null,
    startDate: today,
    pickupDate: '',
    contractNumber: '',
    contractSignedOn: '',
    estimatedDeliveryCost: null,
    transportCurrency: '',
    notes: '',
  }
}

export function RentalFormModal({ open, rental, onClose }: RentalFormModalProps) {
  if (!open) return null
  return <RentalFormModalBody rental={rental} onClose={onClose} />
}

function RentalFormModalBody({ rental, onClose }: { rental: RentalDto | null; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const navigate = useNavigate()
  const create = useCreateRental()
  const action = useRentalAction()
  const editing = rental !== null
  const r = rental?.rental
  const lockedWarehouse = editing && activeLines(rental).length > 0
  const lockedDates = editing && (r?.extensionCount ?? 0) > 0
  const { data: settings } = useTenantSettings()
  const { data: currencies = [] } = useLookups('Currency')
  const { data: warehouses = [] } = useWarehouses({ includeInactive: false }, { handleAccessDenied: false })
  // equipos elegidos con el almacén del que salen: si cambia el almacén, ya no aplican (se descartan sin efecto)
  const [picked, setPicked] = useState<{ warehouse: string | null; list: PickedEquipment[] }>({ warehouse: null, list: [] })
  const [notice, setNotice] = useState<string | null>(null)
  const frequencyLabel = useFrequencyLabel()

  const schema = useMemo(
    () =>
      z
        .object({
          clientPublicId: z.string().nullable().refine((v) => Boolean(v), t('rentals.errors.clientRequired')),
          locationPublicId: z.string().min(1, t('rentals.errors.locationRequired')),
          clientContactId: z.string(),
          warehousePublicId: z.string().nullable().refine((v) => Boolean(v), t('rentals.errors.warehouseRequired')),
          startDate: z.string().min(1, t('rentals.errors.startDateRequired')),
          pickupDate: z.string().min(1, t('rentals.errors.pickupDateRequired')),
          contractNumber: z.string().max(RENTAL_LIMITS.contractNumber, t('rentals.errors.contractTooLong')),
          contractSignedOn: z.string(),
          estimatedDeliveryCost: z
            .number(t('rentals.errors.numberInvalid'))
            .nullable()
            .refine((v) => v === null || v >= 0, t('rentals.errors.negativeDeliveryCost')),
          transportCurrency: z.string(),
          notes: z.string().max(RENTAL_LIMITS.notes, t('rentals.errors.notesTooLong')),
        })
        .superRefine((v, ctx) => {
          const issue = datesIssue(v.startDate, v.pickupDate)
          if (issue) ctx.addIssue({ code: 'custom', path: ['pickupDate'], message: t(`rentals.errors.${issue.code}`) })
        }),
    [t],
  )

  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: rental ? headerValuesOf(rental) : emptyValues(f.today()),
  })
  const clientPublicId = useWatch({ control: form.control, name: 'clientPublicId' })
  const warehousePublicId = useWatch({ control: form.control, name: 'warehousePublicId' })

  // alta: con un solo almacén activo, ya elegido (el servidor también lo toma por defecto)
  useEffect(() => {
    if (!editing && !form.getValues('warehousePublicId') && warehouses.length === 1 && warehouses[0].publicId) {
      form.setValue('warehousePublicId', warehouses[0].publicId)
    }
  }, [editing, warehouses, form])

  // otro cliente: la localidad y el contacto eran del anterior
  const lastClient = useRef(clientPublicId)
  useEffect(() => {
    if (lastClient.current === clientPublicId) return
    lastClient.current = clientPublicId
    form.setValue('locationPublicId', '')
    form.setValue('clientContactId', '')
  }, [clientPublicId, form])

  // otro almacén: los equipos elegidos eran de las posiciones del anterior
  const equipment = useMemo(() => (picked.warehouse === warehousePublicId ? picked.list : []), [picked, warehousePublicId])
  const cleared = picked.list.length > 0 && picked.warehouse !== warehousePublicId

  const locations = useClientLocations(clientPublicId)
  const contacts = useClientContacts(clientPublicId)
  const locationOptions = useMemo(
    () =>
      (locations.data ?? [])
        .filter((l) => l.isActive !== false || l.publicId === r?.locationPublicId)
        .map((l) => ({ value: l.publicId ?? '', label: [l.name, l.city].filter(Boolean).join(' · '), hint: l.code ?? undefined })),
    [locations.data, r?.locationPublicId],
  )
  const contactOptions = useMemo(
    () => (contacts.data ?? []).filter((c) => c.isActive !== false).map((c) => ({ value: String(c.id), label: [c.fullName, c.role].filter(Boolean).join(' · ') })),
    [contacts.data],
  )
  const currencyOptions = useMemo(() => currencies.map((c) => ({ value: c.code, label: `${c.code} · ${c.label}` })), [currencies])
  const warehouseCode = warehouses.find((w) => w.publicId === warehousePublicId)?.code ?? r?.warehouseCode ?? ''
  const excluded = useMemo(() => new Set(equipment.map((e) => serialKey(e.serialNumber))), [equipment])

  const equipmentColumns = useMemo<DataColumn<PickedEquipment>[]>(
    () => [
      { id: 'serial', header: t('rentals.lines.serial'), cell: (e) => <span className="mono">{e.serialNumber}</span>, sortValue: (e) => e.serialNumber, card: 'title' },
      { id: 'sku', header: t('rentals.lines.sku'), cell: (e) => <span className="ref">{e.sku}</span>, sortValue: (e) => e.sku },
      { id: 'product', header: t('rentals.lines.product'), cell: (e) => e.productName, sortValue: (e) => e.productName },
      { id: 'bin', header: t('rentals.lines.fromBin'), cell: (e) => e.binCode, sortValue: (e) => e.binCode },
      {
        id: 'rate',
        header: t('rentals.lines.rate'),
        cell: (e) =>
          e.rate ? `${f.money(e.rate.amount ?? 0, { currency: e.rate.currency ?? undefined })} · ${frequencyLabel(e.rate.frequency)}` : t('rentals.rate.noRate'),
        sortValue: (e) => e.rate?.amount ?? null,
      },
    ],
    [t, f, frequencyLabel],
  )
  const equipmentActions = useMemo<RowAction<PickedEquipment>[]>(
    () => [
      {
        key: 'remove',
        label: t('rentals.picker.remove'),
        icon: <IconTrash />,
        tone: 'danger',
        onClick: (e) => setPicked((cur) => ({ ...cur, list: cur.list.filter((x) => serialKey(x.serialNumber) !== serialKey(e.serialNumber)) })),
      },
    ],
    [t],
  )

  const submitting = form.formState.isSubmitting

  async function save(v: RentalHeaderValues) {
    if (!editing) {
      const dto = await create.mutateAsync(createRentalBody(v, equipment))
      toast.success(t('rentals.toast.created', { number: dto.rental?.number ?? '' }))
      onClose()
      if (dto.rental?.publicId) navigate(`/warehouse/rentals/${dto.rental.publicId}`)
      return
    }
    const body = rentalPatchBody(rental, v)
    if (body) {
      await action.mutateAsync({ action: 'patch', publicId: r?.publicId ?? '', body })
      toast.success(t('rentals.toast.saved'))
    }
    onClose()
  }

  return (
    <Modal
      open
      size="lg"
      title={editing ? t('rentals.form.editTitle', { number: r?.number ?? '' }) : t('rentals.form.newTitle')}
      onClose={onClose}
      dismissible={!submitting}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={submitting}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={FORM_ID} className="btn flow" disabled={submitting}>
            {submitting ? t('common.loading') : editing ? t('rentals.form.save') : t('rentals.form.create', { count: equipment.length })}
          </button>
        </>
      }
    >
      <Form id={FORM_ID} form={form} onSubmit={save}>
        <div className="r2">
          {editing ? (
            <div className="f">
              <label htmlFor="rental-client-ro">{t('rentals.fields.client')}</label>
              <input id="rental-client-ro" value={r?.clientName ?? ''} disabled readOnly aria-describedby="rental-client-ro-help" />
              <p className="help" id="rental-client-ro-help">
                {t('rentals.form.clientLocked')}
              </p>
            </div>
          ) : (
            <Field name="clientPublicId" label={t('rentals.fields.client')} required>
              <ClientPickerInput />
            </Field>
          )}
          <Field
            name="locationPublicId"
            label={t('rentals.fields.location')}
            required
            help={
              !clientPublicId
                ? t('rentals.form.pickClientFirst')
                : isAccessDenied(locations.error)
                  ? t('rentals.form.locationsForbidden')
                  : locations.data && locationOptions.length === 0
                    ? t('rentals.form.noLocations')
                    : undefined
            }
          >
            <ComboSelectInput options={locationOptions} loading={locations.isFetching} disabled={!clientPublicId} placeholder={t('rentals.form.locationPlaceholder')} />
          </Field>
        </div>
        <div className="r2">
          <Field
            name="clientContactId"
            label={t('rentals.fields.contact')}
            help={clientPublicId && isAccessDenied(contacts.error) ? t('rentals.form.contactsForbidden') : undefined}
          >
            <Select options={contactOptions} placeholder={t('rentals.form.noContact')} />
          </Field>
          <Field name="warehousePublicId" label={t('rentals.fields.warehouse')} required help={lockedWarehouse ? t('rentals.form.warehouseLocked') : undefined}>
            <WarehousePickerInput disabled={lockedWarehouse} placeholder={null} />
          </Field>
        </div>
        <div className="r2">
          <Field name="startDate" label={t('rentals.fields.startDate')} required help={lockedDates ? t('rentals.form.datesLocked') : undefined}>
            <DateInput readOnly={lockedDates} aria-readonly={lockedDates || undefined} />
          </Field>
          <Field name="pickupDate" label={t('rentals.fields.pickupDate')} required>
            <DateInput readOnly={lockedDates} aria-readonly={lockedDates || undefined} />
          </Field>
        </div>
        <div className="r2">
          <Field name="contractNumber" label={t('rentals.fields.contractNumber')}>
            <TextInput maxLength={RENTAL_LIMITS.contractNumber} autoComplete="off" />
          </Field>
          <Field name="contractSignedOn" label={t('rentals.fields.contractSignedOn')}>
            <DateInput />
          </Field>
        </div>
        <div className="r2">
          <Field name="estimatedDeliveryCost" label={t('rentals.fields.deliveryCost')} help={t('rentals.form.deliveryCostHelp')}>
            <NumberInput step="0.01" min={0} />
          </Field>
          <Field name="transportCurrency" label={t('rentals.fields.transportCurrency')}>
            <Select
              options={currencyOptions}
              placeholder={settings?.currencyCode ? t('rentals.rate.companyCurrencyCode', { code: settings.currencyCode }) : t('rentals.rate.companyCurrency')}
            />
          </Field>
        </div>
        <Field name="notes" label={t('rentals.fields.notes')}>
          <TextArea rows={2} maxLength={RENTAL_LIMITS.notes} />
        </Field>
      </Form>

      {!editing && (
        <section className="ren-equipment" aria-label={t('rentals.form.equipmentTitle', { count: equipment.length })}>
          <h3 className="ren-h3">{t('rentals.form.equipmentTitle', { count: equipment.length })}</h3>
          {(notice || cleared) && (
            <p className="note" role="status">
              {cleared ? t('rentals.form.equipmentCleared') : notice}
            </p>
          )}
          {equipment.length === 0 ? (
            <p className="help">{t('rentals.form.equipmentEmpty')}</p>
          ) : (
            <DataTable
              label={t('rentals.form.equipmentTitle', { count: equipment.length })}
              columns={equipmentColumns}
              rows={equipment}
              rowKey={(e) => serialKey(e.serialNumber)}
              rowActions={equipmentActions}
              pagination={false}
              exportable={false}
              empty={<EmptyState title={t('rentals.form.equipmentEmpty')} />}
            />
          )}
          <EquipmentPicker
            warehousePublicId={warehousePublicId}
            warehouseCode={warehouseCode || warehouseLabel(warehouses.find((w) => w.publicId === warehousePublicId))}
            excluded={excluded}
            onAdd={(items) => {
              const { list, duplicates } = addEquipment(equipment, items)
              if (list.length > RENTAL_LIMITS.maxLines) {
                setNotice(t('rentals.errors.tooManyLines'))
                return
              }
              setPicked({ warehouse: warehousePublicId ?? null, list })
              setNotice(duplicates.length > 0 ? t('rentals.picker.duplicates', { serials: duplicates.join(', ') }) : null)
            }}
          />
        </section>
      )}
    </Modal>
  )
}
