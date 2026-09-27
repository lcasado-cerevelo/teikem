// Pieza "Recolección y empaque" (Lote F6) — ficha de la recolección. `/warehouse/pick-batches/:publicId`.
// Lectura: inventory.view + WMS_LOTSERIAL (por la ruta). Empacar: warehouse.pick + orders.create (crea la orden real con el
// número de la recolección como número de empaque). Eliminar: warehouse.pick, y si ya está PACKED además orders.cancel
// (revierte el inventario a su posición original; bloqueado si la orden ya avanzó — 422). Sin transición manual: COLLECTED →
// PACKED → CANCELLED se disparan desde las acciones. Manual 06 §7.
import { zodResolver } from '@hookform/resolvers/zod'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useFieldArray, useForm, useWatch } from 'react-hook-form'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { z } from 'zod'
import { Can, useCan } from '../../kernel/access'
import { api, unwrap } from '../../kernel/api/client'
import { ApiError } from '../../kernel/api/problem'
import type { components } from '../../kernel/api/schema'
import { StatusChip, StatusPipeline, useLookups } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import {
  Chip,
  ClientPickerInput,
  ConfirmDialog,
  DataTable,
  EmptyState,
  Field,
  Form,
  Modal,
  NumberInput,
  Panel,
  Select,
  Spinner,
  TextArea,
  TextInput,
  toast,
  type DataColumn,
} from '../../kernel/ui'
import { productLabel, useDeletePickBatch, usePackPickBatch, usePickBatch, warehouseKeys, type PickBatchDto } from './api'
import { formatDateTime, formatNumber, remapProblemFields } from './lineRules'

type Schemas = components['schemas']
type PickLine = Schemas['PickBatchLineDto']

const STATUS_DOMAIN = 'PickBatchStatus'
const ENTITY_TYPE = 'PICK_BATCH'

interface PackageForm {
  packageType: string
  description: string
  pieces: number | null
  weightKg: number | null
}
const EMPTY_PACKAGE: PackageForm = { packageType: '', description: '', pieces: 1, weightKg: null }

// =====================================================================================================================
// Empacar: datos de la orden de transporte que nace del empaque
// =====================================================================================================================
function PackModal({ batch, onClose }: { batch: PickBatchDto; onClose: () => void }) {
  const t = useT()
  const pack = usePackPickBatch()
  const { data: serviceTypes = [] } = useLookups('ServiceType')
  const { data: packageTypes = [] } = useLookups('PackageType')
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
          if (v.packages.length === 0) ctx.addIssue({ code: 'custom', path: ['serviceType'], message: t('warehouse.pickBatches.pack.errors.packagesRequired') })
          v.packages.forEach((p, i) => {
            if (p.pieces == null || p.pieces < 1 || !Number.isInteger(p.pieces))
              ctx.addIssue({ code: 'custom', path: ['packages', i, 'pieces'], message: t('warehouse.pickBatches.pack.errors.pieces') })
            if (p.weightKg != null && p.weightKg < 0) ctx.addIssue({ code: 'custom', path: ['packages', i, 'weightKg'], message: t('warehouse.pickBatches.pack.errors.weight') })
          })
        }),
    [t],
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
          <Field name="serviceType" label={t('warehouse.pickBatches.pack.serviceType')}>
            <Select options={serviceTypes.map((s) => ({ value: s.code, label: s.label }))} placeholder={t('warehouse.pickBatches.pack.defaultServiceType')} />
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
              <Field name={`packages.${i}.packageType`} label={t('warehouse.pickBatches.pack.packageType')}>
                <Select options={packageTypes.map((p) => ({ value: p.code, label: p.label }))} placeholder="—" />
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

// =====================================================================================================================
// Pantalla
// =====================================================================================================================
export default function PickBatchDetailScreen() {
  const t = useT()
  const lang = useLang()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const { publicId = '' } = useParams()
  const { data: batch, isLoading, error } = usePickBatch(publicId)
  const del = useDeletePickBatch()
  const canCancelOrder = useCan('orders.cancel')
  const [packing, setPacking] = useState(false)
  const [deleting, setDeleting] = useState(false)

  const lines = useMemo(() => batch?.lines ?? [], [batch?.lines])
  const columns = useMemo<DataColumn<PickLine>[]>(
    () => [
      { id: 'product', header: t('warehouse.pickBatches.detail.product'), cell: (l) => productLabel({ sku: l.sku, name: l.productName }), sortValue: (l) => l.sku, card: 'title' },
      { id: 'qty', header: t('warehouse.pickBatches.detail.quantity'), cell: (l) => formatNumber(l.quantity, lang), sortValue: (l) => l.quantity ?? 0, align: 'end' },
      { id: 'bin', header: t('warehouse.pickBatches.detail.bin'), cell: (l) => l.binCode ?? '—', sortValue: (l) => l.binCode },
      { id: 'lot', header: t('warehouse.pickBatches.detail.lot'), cell: (l) => l.lotNumber ?? '—', sortValue: (l) => l.lotNumber },
      { id: 'serial', header: t('warehouse.pickBatches.detail.serial'), cell: (l) => l.serialNumber ?? '—', sortValue: (l) => l.serialNumber },
      { id: 'cost', header: t('warehouse.pickBatches.detail.unitCost'), cell: (l) => (l.unitCost != null ? formatNumber(l.unitCost, lang) : '—'), align: 'end', card: 'hidden', sortValue: (l) => l.unitCost },
      {
        id: 'reversed',
        header: t('warehouse.pickBatches.detail.reversal'),
        cell: (l) => (l.reversalTxnId != null ? <Chip tone="warn">{t('warehouse.pickBatches.detail.reversed')}</Chip> : '—'),
        sortValue: (l) => l.reversalTxnId != null,
      },
    ],
    [t, lang],
  )

  if (isLoading) return <Spinner block />
  if (error || !batch) {
    const notFound = error instanceof ApiError && error.code === 'not_found'
    return (
      <EmptyState
        title={notFound ? t('warehouse.pickBatches.notFound') : (error?.message ?? t('errors.generic'))}
        action={
          <Link className="btn" to="/warehouse/pick-batches">
            {t('warehouse.pickBatches.back')}
          </Link>
        }
      />
    )
  }

  const packed = batch.statusCode === 'PACKED'
  // Eliminar una recolección ya empacada exige además orders.cancel (la orden se borra con ella)
  const canDelete = batch.canDelete === true && (!packed || canCancelOrder)

  return (
    <div className="wrap">
      <div className="head">
        <div style={{ minWidth: 0 }}>
          <h1>
            <span className="ref">{batch.number}</span> · {batch.warehouseCode}
          </h1>
          <p>
            <StatusChip domain={STATUS_DOMAIN} code={batch.statusCode} label={batch.status} />
            {batch.isActive === false && (
              <>
                {' '}
                <Chip tone="fail">{t('warehouse.pickBatches.deletedChip')}</Chip>
              </>
            )}{' '}
            {batch.clientName ?? t('warehouse.pickBatches.own')}
          </p>
        </div>
        <div className="act">
          <Can perm="warehouse.pick">
            {canDelete && (
              <button type="button" className="btn" onClick={() => setDeleting(true)}>
                {t('warehouse.pickBatches.detail.delete')}
              </button>
            )}
          </Can>
          <Can perm={['warehouse.pick', 'orders.create']}>
            {batch.canPack && (
              <button type="button" className="btn flow" onClick={() => setPacking(true)}>
                {t('warehouse.pickBatches.detail.pack')}
              </button>
            )}
          </Can>
        </div>
      </div>

      <div style={{ marginBottom: 14 }}>
        {/* Sin transición manual: PACKED y CANCELLED se disparan desde Empacar y Eliminar. */}
        <StatusPipeline domain={STATUS_DOMAIN} entityType={ENTITY_TYPE} entityId={batch.id} currentCode={batch.statusCode} />
      </div>

      <Panel title={t('warehouse.pickBatches.detail.summary')}>
        <div className="r3">
          <div className="f">
            <label>{t('warehouse.pickBatches.columns.collectedAt')}</label>
            <p>
              {formatDateTime(batch.collectedAtUtc, lang)}
              {batch.collectedBy ? ` · ${batch.collectedBy}` : ''}
            </p>
          </div>
          <div className="f">
            <label>{t('warehouse.pickBatches.detail.packedAt')}</label>
            <p>{formatDateTime(batch.packedAtUtc, lang) || '—'}</p>
          </div>
          <div className="f">
            <label>{t('warehouse.pickBatches.detail.order')}</label>
            <p>
              {batch.orderPublicId ? (
                <Can perm="orders.view" fallback={<span className="ref">{batch.orderNumber}</span>}>
                  <Link className="ref" to={`/orders/${batch.orderPublicId}`}>
                    {batch.orderNumber}
                  </Link>
                </Can>
              ) : (
                '—'
              )}
              {batch.orderStatus ? ` · ${batch.orderStatus}` : ''}
            </p>
          </div>
          <div className="f">
            <label>{t('warehouse.pickBatches.detail.invoice')}</label>
            <p>{batch.clientInvoiceNumber ?? '—'}</p>
          </div>
          <div className="f">
            <label>{t('warehouse.pickBatches.columns.totalQty')}</label>
            <p>{formatNumber(batch.totalQty, lang)}</p>
          </div>
          <div className="f">
            <label>{t('warehouse.pickBatches.detail.totalCost')}</label>
            <p>{batch.totalCost != null ? formatNumber(batch.totalCost, lang) : '—'}</p>
          </div>
        </div>
      </Panel>

      <Panel flush title={t('warehouse.pickBatches.detail.lines')} subtitle={t('warehouse.receipts.detail.lineCount', { count: lines.length })}>
        <DataTable label={t('warehouse.pickBatches.detail.lines')} columns={columns} rows={lines} rowKey={(l) => l.id ?? 0} pageSize={50} />
      </Panel>

      {packing && <PackModal batch={batch} onClose={() => setPacking(false)} />}

      <ConfirmDialog
        open={deleting}
        tone="danger"
        title={t('warehouse.pickBatches.detail.deleteTitle')}
        message={packed ? t('warehouse.pickBatches.detail.deletePackedBody', { number: batch.number ?? '', order: batch.orderNumber ?? '' }) : t('warehouse.pickBatches.detail.deleteBody', { number: batch.number ?? '' })}
        confirmLabel={t('warehouse.pickBatches.detail.delete')}
        onConfirm={async () => {
          try {
            await del.mutateAsync({ publicId, body: { rowVersion: batch.rowVersion ?? null } })
          } catch (err) {
            if (err instanceof ApiError && err.code === 'conflict') void qc.invalidateQueries({ queryKey: warehouseKeys.pickBatch })
            throw err
          }
          toast.success(t('warehouse.pickBatches.detail.deleted', { number: batch.number ?? '' }))
          navigate('/warehouse/pick-batches')
        }}
        onClose={() => setDeleting(false)}
      />
    </div>
  )
}
