// Pieza "Recolección y empaque" — Empacar una recolección: datos de la orden de transporte que nace del empaque
// (`POST /api/v1/pick-batches/{publicId}/pack`, warehouse.pick + orders.create; crea la orden real con el número de la
// recolección como número de empaque). Lo abren la acción de fila "Empacar" de la lista (`PickBatchesPanel`) y la ficha
// (`PickBatchDetailBody`). Manual 06 §7.
// Empacar (decisión del 2026-09-30): Tipo de servicio y Tipo de paquete ofrecen "Predeterminado de la compañía ({etiqueta})"
// solo si la compañía lo tiene (`useTenantSettings`: `defaultServiceType`/`defaultPackageType`, etiqueta de `useLookups` o,
// si no está en el catálogo, el código); sin predeterminado esa opción no aparece y el campo es obligatorio.
import { zodResolver } from '@hookform/resolvers/zod'
import { useQuery } from '@tanstack/react-query'
import { useMemo } from 'react'
import { useFieldArray, useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'
import { lookupLabelOrCode, useLookups, useTenantSettings } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { ClientPickerInput, Field, Form, Modal, NumberInput, Select, TextArea, TextInput, toast } from '../../kernel/ui'
import { usePackPickBatch, type PickBatchDto } from './api'
import { remapProblemFields } from './lineRules'

type Schemas = components['schemas']

interface PackageForm {
  packageType: string
  description: string
  pieces: number | null
  weightKg: number | null
}
const EMPTY_PACKAGE: PackageForm = { packageType: '', description: '', pieces: 1, weightKg: null }

/** Recolección a empacar: basta con lo que trae la fila de la lista o la ficha. */
export type PackableBatch = Pick<PickBatchDto, 'publicId' | 'number' | 'clientName' | 'rowVersion'>

export function PackModal({ batch, onClose }: { batch: PackableBatch; onClose: () => void }) {
  const t = useT()
  const pack = usePackPickBatch()
  const { data: serviceTypes = [] } = useLookups('ServiceType')
  const { data: packageTypes = [] } = useLookups('PackageType')
  // Predeterminados de la compañía (código o null). Mientras cargan o si no se pueden leer, se tratan como ausentes: el campo
  // pide un valor explícito, que el API siempre acepta.
  const { data: settings } = useTenantSettings()
  const defaultService = lookupLabelOrCode(settings?.defaultServiceType, serviceTypes)
  const defaultPackage = lookupLabelOrCode(settings?.defaultPackageType, packageTypes)
  const serviceOptions = useMemo(() => serviceTypes.map((s) => ({ value: s.code, label: s.label })), [serviceTypes])
  const packageOptions = useMemo(() => packageTypes.map((p) => ({ value: p.code, label: p.label })), [packageTypes])
  const schema = useMemo(
    () =>
      z
        .object({
          clientPublicId: z.string().nullable(),
          consignee: z.string(),
          consigneeLocationPublicId: z.string(),
          newConsignee: z.object({
            name: z.string().max(200),
            line1: z.string().max(200),
            line2: z.string().max(200),
            city: z.string().max(100),
            state: z.string().max(100),
            postalCode: z.string().max(20),
            country: z.string().max(2, t('warehouse.pickBatches.pack.errors.country')),
          }),
          serviceType: z.string(),
          orderNumber: z.string().max(50),
          clientInvoiceNumber: z.string().max(50),
          notes: z.string().max(1000),
          packages: z.array(
            z.object({ packageType: z.string(), description: z.string().max(200), pieces: z.number().nullable(), weightKg: z.number().nullable() }),
          ),
        })
        .superRefine((v, ctx) => {
          if (!v.clientPublicId) ctx.addIssue({ code: 'custom', path: ['clientPublicId'], message: t('warehouse.pickBatches.pack.errors.clientRequired') })
          if (v.consignee === 'existing' && !v.consigneeLocationPublicId)
            ctx.addIssue({ code: 'custom', path: ['consigneeLocationPublicId'], message: t('warehouse.pickBatches.pack.errors.consigneeRequired') })
          if (v.consignee === 'new') {
            if (!v.newConsignee.name.trim()) ctx.addIssue({ code: 'custom', path: ['newConsignee', 'name'], message: t('warehouse.pickBatches.pack.errors.nameRequired') })
            if (!v.newConsignee.line1.trim()) ctx.addIssue({ code: 'custom', path: ['newConsignee', 'line1'], message: t('warehouse.pickBatches.pack.errors.line1Required') })
            if (!v.newConsignee.city.trim()) ctx.addIssue({ code: 'custom', path: ['newConsignee', 'city'], message: t('warehouse.pickBatches.pack.errors.cityRequired') })
          }
          if (!defaultService && !v.serviceType)
            ctx.addIssue({ code: 'custom', path: ['serviceType'], message: t('warehouse.pickBatches.pack.errors.serviceTypeRequired') })
          if (v.packages.length === 0) ctx.addIssue({ code: 'custom', path: ['serviceType'], message: t('warehouse.pickBatches.pack.errors.packagesRequired') })
          v.packages.forEach((p, i) => {
            if (!defaultPackage && !p.packageType)
              ctx.addIssue({ code: 'custom', path: ['packages', i, 'packageType'], message: t('warehouse.pickBatches.pack.errors.packageTypeRequired') })
            if (p.pieces == null || p.pieces < 1 || !Number.isInteger(p.pieces))
              ctx.addIssue({ code: 'custom', path: ['packages', i, 'pieces'], message: t('warehouse.pickBatches.pack.errors.pieces') })
            if (p.weightKg != null && p.weightKg < 0) ctx.addIssue({ code: 'custom', path: ['packages', i, 'weightKg'], message: t('warehouse.pickBatches.pack.errors.weight') })
          })
        }),
    [t, defaultService, defaultPackage],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: {
      clientPublicId: null as string | null,
      consignee: 'existing',
      consigneeLocationPublicId: '',
      newConsignee: { name: '', line1: '', line2: '', city: '', state: '', postalCode: '', country: '' },
      serviceType: '',
      orderNumber: '',
      clientInvoiceNumber: '',
      notes: '',
      packages: [EMPTY_PACKAGE],
    },
  })
  const { fields, append, remove } = useFieldArray({ control: form.control, name: 'packages' })
  const clientPublicId = useWatch({ control: form.control, name: 'clientPublicId' })
  const mode = useWatch({ control: form.control, name: 'consignee' })
  // Directorio de consignatarios del cliente (propios + compartidos). Sin `locations.read` o sin CATALOG se avisa y se
  // captura uno nuevo; no saca de la pantalla.
  const locations = useQuery({
    queryKey: ['/api/v1/locations', { clientId: clientPublicId, includeShared: true }],
    queryFn: () => unwrap(api.GET('/api/v1/locations', { params: { query: { clientId: clientPublicId ?? undefined, includeShared: true } } })),
    enabled: mode === 'existing' && Boolean(clientPublicId),
    meta: { handleAccessDenied: false },
  })
  const locationOptions = useMemo(
    () =>
      (locations.data ?? [])
        .filter((l) => l.isActive !== false)
        .map((l) => ({ value: l.publicId ?? '', label: [l.code, l.name, l.city].filter(Boolean).join(' · ') })),
    [locations.data],
  )
  const formId = 'pick-batch-pack'

  return (
    <Modal
      open
      size="lg"
      title={t('warehouse.pickBatches.pack.title', { number: batch.number ?? '' })}
      onClose={onClose}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('warehouse.pickBatches.pack.submit')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          const body: Schemas['PickBatchPackRequest'] = {
            rowVersion: batch.rowVersion ?? null,
            order: {
              clientPublicId: v.clientPublicId,
              consigneeLocationPublicId: v.consignee === 'existing' ? v.consigneeLocationPublicId || null : null,
              newConsignee:
                v.consignee === 'new'
                  ? {
                      name: v.newConsignee.name.trim(),
                      line1: v.newConsignee.line1.trim(),
                      line2: v.newConsignee.line2.trim() || null,
                      city: v.newConsignee.city.trim(),
                      state: v.newConsignee.state.trim() || null,
                      postalCode: v.newConsignee.postalCode.trim() || null,
                      country: v.newConsignee.country.trim() || null,
                    }
                  : undefined,
              serviceType: v.serviceType || null,
              packages: v.packages.map((p) => ({
                packageType: p.packageType || null,
                description: p.description.trim() || null,
                pieces: p.pieces ?? 1,
                weightKg: p.weightKg,
              })),
              orderNumber: v.orderNumber.trim() || null,
              clientInvoiceNumber: v.clientInvoiceNumber.trim() || null,
              notes: v.notes.trim() || null,
              // un empaque no es entrega especial ni lleva chofer (400 si no)
              isSpecialDelivery: false,
              confirmDuplicateInvoice: false,
              confirmNow: false,
              overrideCredit: false,
            },
          }
          let result: Schemas['PickBatchPackResultDto']
          try {
            result = await pack.mutateAsync({ publicId: batch.publicId ?? '', body })
          } catch (err) {
            // `order.clientPublicId` (dueño del inventario) y `order` vienen con prefijo; el resto, con el nombre del campo
            throw remapProblemFields(err, (k) => (k.startsWith('order.') ? k.slice(6) : k === 'order' ? 'clientPublicId' : null))
          }
          toast.success(t('warehouse.pickBatches.pack.done', { order: result.order?.orderNumber ?? '' }))
          onClose()
        }}
      >
        <div className="r2">
          <Field
            name="clientPublicId"
            label={t('warehouse.pickBatches.pack.client')}
            required
            help={batch.clientName ? t('warehouse.pickBatches.pack.ownerHelp', { client: batch.clientName }) : undefined}
          >
            <ClientPickerInput />
          </Field>
          <Field name="serviceType" label={t('warehouse.pickBatches.pack.serviceType')} required={!defaultService}>
            <Select
              options={serviceOptions}
              placeholder={
                defaultService
                  ? t('warehouse.pickBatches.pack.defaultServiceType', { label: defaultService })
                  : t('warehouse.pickBatches.pack.chooseServiceType')
              }
            />
          </Field>
        </div>
        <div className="r2">
          <Field name="consignee" label={t('warehouse.pickBatches.pack.consignee')} required>
            <Select
              options={[
                { value: 'existing', label: t('warehouse.pickBatches.pack.consigneeExisting') },
                { value: 'new', label: t('warehouse.pickBatches.pack.consigneeNew') },
              ]}
            />
          </Field>
          {mode === 'existing' && (
            <Field
              name="consigneeLocationPublicId"
              label={t('warehouse.pickBatches.pack.location')}
              required
              help={locations.error ? t('warehouse.pickBatches.pack.noLocations') : clientPublicId ? undefined : t('warehouse.pickBatches.pack.pickClientFirst')}
            >
              <Select options={locationOptions} placeholder={locations.isLoading ? t('common.loading') : t('warehouse.receipts.choose')} />
            </Field>
          )}
        </div>
        {mode === 'new' && (
          <>
            <div className="r2">
              <Field name="newConsignee.name" label={t('warehouse.pickBatches.pack.name')} required>
                <TextInput maxLength={200} />
              </Field>
              <Field name="newConsignee.line1" label={t('warehouse.pickBatches.pack.line1')} required>
                <TextInput maxLength={200} />
              </Field>
            </div>
            <div className="r3">
              <Field name="newConsignee.line2" label={t('warehouse.pickBatches.pack.line2')}>
                <TextInput maxLength={200} />
              </Field>
              <Field name="newConsignee.city" label={t('warehouse.pickBatches.pack.city')} required>
                <TextInput maxLength={100} />
              </Field>
              <Field name="newConsignee.state" label={t('warehouse.pickBatches.pack.state')}>
                <TextInput maxLength={100} />
              </Field>
            </div>
            <div className="r3">
              <Field name="newConsignee.postalCode" label={t('warehouse.pickBatches.pack.postalCode')}>
                <TextInput maxLength={20} />
              </Field>
              <Field name="newConsignee.country" label={t('warehouse.pickBatches.pack.country')} help={t('warehouse.pickBatches.pack.countryHelp')}>
                <TextInput maxLength={2} />
              </Field>
            </div>
          </>
        )}
        <div className="r2">
          <Field name="orderNumber" label={t('warehouse.pickBatches.pack.orderNumber')} help={t('warehouse.pickBatches.pack.autoHelp')}>
            <TextInput maxLength={50} />
          </Field>
          <Field name="clientInvoiceNumber" label={t('warehouse.pickBatches.pack.invoice')} help={t('warehouse.pickBatches.pack.autoHelp')}>
            <TextInput maxLength={50} />
          </Field>
        </div>
        <p className="help">{t('warehouse.pickBatches.pack.packages')}</p>
        {fields.map((f, i) => (
          <div key={f.id} className="panel" style={{ padding: 12, marginBottom: 10 }}>
            <div className="r2">
              <Field name={`packages.${i}.packageType`} label={t('warehouse.pickBatches.pack.packageType')} required={!defaultPackage}>
                <Select
                  options={packageOptions}
                  placeholder={
                    defaultPackage
                      ? t('warehouse.pickBatches.pack.defaultPackageType', { label: defaultPackage })
                      : t('warehouse.pickBatches.pack.choosePackageType')
                  }
                />
              </Field>
              <Field name={`packages.${i}.description`} label={t('warehouse.pickBatches.pack.description')}>
                <TextInput maxLength={200} />
              </Field>
            </div>
            <div className="r2">
              <Field name={`packages.${i}.pieces`} label={t('warehouse.pickBatches.pack.pieces')} required>
                <NumberInput min={1} step="1" />
              </Field>
              <Field name={`packages.${i}.weightKg`} label={t('warehouse.pickBatches.pack.weightKg')}>
                <NumberInput min={0} step="0.01" />
              </Field>
            </div>
            {fields.length > 1 && (
              <button type="button" className="btn sm" onClick={() => remove(i)}>
                {t('warehouse.receipts.removeLine')}
              </button>
            )}
          </div>
        ))}
        <button type="button" className="btn sm" onClick={() => append(EMPTY_PACKAGE)}>
          {t('warehouse.pickBatches.pack.addPackage')}
        </button>
        <Field name="notes" label={t('warehouse.pickBatches.pack.notes')}>
          <TextArea rows={2} />
        </Field>
      </Form>
    </Modal>
  )
}
