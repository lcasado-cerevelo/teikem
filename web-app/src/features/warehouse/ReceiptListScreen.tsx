// Pieza "Recibo" (Lote F6) — Recepción: lista de recibos y avisos de llegada (ASN). `/warehouse/receipts`.
// Lectura: inventory.view + WMS_LOTSERIAL (por la ruta). Alta de recibo y de aviso, recibir y cancelar aviso:
// warehouse.receive. Recibir contra una orden de compra exige además purchasing.receive y el módulo PURCHASING (sin ambos
// la opción no se ofrece). Manual 06 §4. Pestaña 'Acomodo pendiente' (?tab=putaway): cola de tareas PUTAWAY de todos los
// recibos (asignar, iniciar, completar con posición sugerida, cancelar; permisos en taskQueue.tsx).
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useFieldArray, useForm, useFormContext, useWatch } from 'react-hook-form'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { z } from 'zod'
import { Can, ModuleKeys, useCan, useModule } from '../../kernel/access'
import type { components } from '../../kernel/api/schema'
import { StatusChip, useLookups, useStatuses } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import {
  Chip,
  ClientPickerInput,
  ConfirmDialog,
  DataTable,
  DateInput,
  DateRangeFilter,
  EMPTY_RANGE,
  Field,
  Filters,
  Form,
  Modal,
  NumberInput,
  Panel,
  QBox,
  SearchSelect,
  Select,
  SelectFilter,
  Tabs,
  TextArea,
  TextInput,
  matchesQ,
  toast,
  type DataColumn,
  type DateRange,
  type RowAction,
} from '../../kernel/ui'
import {
  useAsns,
  useCreateReceipt,
  usePurchaseOrders,
  useReceipts,
  useSaveAsn,
  type AsnDto,
  type ReceiptListItemDto,
} from './api'
import { decimalsOf, formatDate, formatDateTime, formatNumber, parseSerials, receiptLineIssues, useDebounced, type LineIssue } from './lineRules'
import { BinPickerInput, ProductMultiFilter, ProductPickerInput, WarehousePicker, WarehousePickerInput, type ProductFilterItem } from './pickers'
import { TaskQueue } from './taskQueue'
import { IconCheckin } from '../../kernel/ui/screenIcons'

type Schemas = components['schemas']
const TAB_KEYS = ['receipts', 'asns', 'putaway'] as const
type TabKey = (typeof TAB_KEYS)[number]
const isTabKey = (v: string | null): v is TabKey => (TAB_KEYS as readonly string[]).includes(v ?? '')
const PUTAWAY_TYPES = ['PUTAWAY'] as const

const PAGE_SIZE = 25
const STATUS_DOMAIN = 'ReceiptStatus'
const ASN_STATUS_DOMAIN = 'AsnStatus'
const TYPE_DOMAIN = 'ReceiptType'
const MAX_LINES = 200
/** Tipos de zona donde se recibe (posición de staging del recibo). */
const RECEIVING_ZONES = ['STAGING', 'CROSSDOCK'] as const
const NO_ROWS: never[] = []

/** Traduce un problema de captura de línea (mensaje exacto del manual 06). */
function useIssueText() {
  const t = useT()
  return (issue: LineIssue) => t(`warehouse.lineRules.${issue.code}`, issue.params)
}

// =====================================================================================================================
// Alta de recibo: contra aviso de llegada, contra orden de compra, ciego o de devolución
// =====================================================================================================================
type Source = 'BLIND' | 'RETURN' | 'ASN' | 'PO'

interface ReceiptLineForm {
  productPublicId: string | null
  sku: string
  trackingTypeCode: string
  receivedQty: number | null
  lot: string
  lotExpiryDate: string
  serialNumbers: string
}

const EMPTY_LINE: ReceiptLineForm = {
  productPublicId: null,
  sku: '',
  trackingTypeCode: '',
  receivedQty: null,
  lot: '',
  lotExpiryDate: '',
  serialNumbers: '',
}

/** Campos de una línea ciega/devolución: el lote y las series aparecen según el seguimiento del producto elegido. */
function ReceiptLineFields({ index, onRemove }: { index: number; onRemove?: () => void }) {
  const t = useT()
  const { setValue } = useFormContext()
  const tracking = useWatch({ name: `lines.${index}.trackingTypeCode` }) as string
  return (
    <div className="panel" style={{ padding: 12, marginBottom: 10 }}>
      <div className="r2">
        <Field name={`lines.${index}.productPublicId`} label={t('warehouse.receipts.fields.product')} required>
          <ProductPickerInput
            onPicked={(p) => {
              setValue(`lines.${index}.sku`, p?.sku ?? '')
              setValue(`lines.${index}.trackingTypeCode`, p?.trackingTypeCode ?? '')
            }}
          />
        </Field>
        <Field name={`lines.${index}.receivedQty`} label={t('warehouse.receipts.fields.receivedQty')} required>
          <NumberInput min={0} step="0.001" />
        </Field>
      </div>
      {(tracking === 'LOT' || tracking === 'SERIAL') && (
        <div className="r2">
          <Field name={`lines.${index}.lot`} label={t('warehouse.receipts.fields.lot')} required={tracking === 'LOT'}>
            <TextInput maxLength={60} />
          </Field>
          <Field name={`lines.${index}.lotExpiryDate`} label={t('warehouse.receipts.fields.expiryDate')}>
            <DateInput />
          </Field>
        </div>
      )}
      {tracking === 'SERIAL' && (
        <Field name={`lines.${index}.serialNumbers`} label={t('warehouse.receipts.fields.serials')} help={t('warehouse.receipts.fields.serialsHelp')} required>
          <TextArea rows={3} />
        </Field>
      )}
      {onRemove && (
        <button type="button" className="btn sm" onClick={onRemove}>
          {t('warehouse.receipts.removeLine')}
        </button>
      )}
    </div>
  )
}

export function CreateReceiptModal({ onClose, preset }: { onClose: () => void; preset?: { asnId: number; warehousePublicId: string | null } }) {
  const t = useT()
  const navigate = useNavigate()
  const issueText = useIssueText()
  const create = useCreateReceipt()
  // Recibir contra PO: además purchasing.receive y el módulo PURCHASING; sin ambos la opción no se pinta.
  const hasPoPerm = useCan('purchasing.receive')
  const purchasingOn = useModule(ModuleKeys.Purchasing)
  const canReceivePo = hasPoPerm && purchasingOn

  const schema = useMemo(
    () =>
      z
        .object({
          source: z.string(),
          warehousePublicId: z.string().nullable(),
          asnId: z.string(),
          purchaseOrderPublicId: z.string(),
          stagingBinId: z.string(),
          lines: z.array(
            z.object({
              productPublicId: z.string().nullable(),
              sku: z.string(),
              trackingTypeCode: z.string(),
              receivedQty: z.number().nullable(),
              lot: z.string(),
              lotExpiryDate: z.string(),
              serialNumbers: z.string(),
            }),
          ),
        })
        .superRefine((v, ctx) => {
          if (!v.warehousePublicId) ctx.addIssue({ code: 'custom', path: ['warehousePublicId'], message: t('warehouse.receipts.errors.warehouseRequired') })
          if (v.source === 'ASN' && !v.asnId) ctx.addIssue({ code: 'custom', path: ['asnId'], message: t('warehouse.receipts.errors.asnRequired') })
          if (v.source === 'PO' && !v.purchaseOrderPublicId)
            ctx.addIssue({ code: 'custom', path: ['purchaseOrderPublicId'], message: t('warehouse.receipts.errors.poRequired') })
          if (v.source !== 'BLIND' && v.source !== 'RETURN') return
          if (v.lines.length === 0) ctx.addIssue({ code: 'custom', path: ['source'], message: t('warehouse.receipts.errors.linesRequired') })
          if (v.lines.length > MAX_LINES) ctx.addIssue({ code: 'custom', path: ['source'], message: t('warehouse.receipts.errors.tooManyLines') })
          v.lines.forEach((l, i) => {
            if (!l.productPublicId) ctx.addIssue({ code: 'custom', path: ['lines', i, 'productPublicId'], message: t('warehouse.receipts.errors.productRequired') })
            for (const issue of receiptLineIssues({
              sku: l.sku,
              trackingTypeCode: l.trackingTypeCode,
              receivedQty: l.receivedQty,
              lot: l.lot,
              lotExpiryDate: l.lotExpiryDate,
              serials: parseSerials(l.serialNumbers),
            })) {
              ctx.addIssue({ code: 'custom', path: ['lines', i, issue.field], message: issueText(issue) })
            }
          })
        }),
    [t, issueText],
  )

  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: {
      source: preset ? 'ASN' : 'BLIND',
      warehousePublicId: preset?.warehousePublicId ?? null,
      asnId: preset ? String(preset.asnId) : '',
      purchaseOrderPublicId: '',
      stagingBinId: '',
      lines: [EMPTY_LINE],
    },
  })
  const { fields, append, remove } = useFieldArray({ control: form.control, name: 'lines' })
  const source = useWatch({ control: form.control, name: 'source' }) as Source
  const warehousePublicId = useWatch({ control: form.control, name: 'warehousePublicId' })
  const manual = source === 'BLIND' || source === 'RETURN'

  const asns = useAsns({ warehousePublicId: warehousePublicId ?? undefined, status: ['EXPECTED'] }, { enabled: source === 'ASN' && Boolean(warehousePublicId) })
  const pos = usePurchaseOrders(
    { warehousePublicId: warehousePublicId ?? undefined, status: ['SENT', 'PARTIAL'], take: 200 },
    { enabled: source === 'PO' && canReceivePo && Boolean(warehousePublicId), handleAccessDenied: false },
  )

  const sourceOptions = useMemo(
    () => [
      { value: 'BLIND', label: t('warehouse.receipts.sources.BLIND') },
      { value: 'RETURN', label: t('warehouse.receipts.sources.RETURN') },
      { value: 'ASN', label: t('warehouse.receipts.sources.ASN') },
      ...(canReceivePo ? [{ value: 'PO', label: t('warehouse.receipts.sources.PO') }] : []),
    ],
    [t, canReceivePo],
  )
  const asnOptions = useMemo(
    () =>
      (asns.data ?? [])
        .filter((a) => !a.receiptPublicId || a.id === preset?.asnId)
        .map((a) => ({ value: String(a.id), label: [`#${a.id}`, a.reference, a.clientName ?? a.purchaseOrderNumber].filter(Boolean).join(' · ') })),
    [asns.data, preset?.asnId],
  )
  const poOptions = useMemo(
    () => (pos.data?.items ?? []).map((p) => ({ value: p.publicId ?? '', label: [p.number, p.supplierName].filter(Boolean).join(' · ') })),
    [pos.data],
  )
  const formId = 'receipt-create'

  return (
    <Modal
      open
      size="lg"
      title={t('warehouse.receipts.new')}
      onClose={onClose}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('warehouse.receipts.create')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          const body: Schemas['ReceiptCreateRequest'] = {
            warehousePublicId: v.warehousePublicId,
            type: manual ? v.source : null,
            asnId: v.source === 'ASN' ? Number(v.asnId) : null,
            purchaseOrderPublicId: v.source === 'PO' ? v.purchaseOrderPublicId : null,
            stagingBinId: v.stagingBinId ? Number(v.stagingBinId) : null,
            lines: manual
              ? v.lines.map((l) => {
                  const serials = parseSerials(l.serialNumbers)
                  return {
                    productPublicId: l.productPublicId,
                    receivedQty: l.receivedQty,
                    lot: l.lot.trim() ? { number: l.lot.trim(), expiryDate: l.lotExpiryDate || null } : undefined,
                    serialNumbers: serials.length > 0 ? serials : null,
                  }
                })
              : null,
          }
          const created = await create.mutateAsync(body)
          toast.success(t('warehouse.receipts.created', { number: created.header?.number ?? '' }))
          onClose()
          if (created.header?.publicId) navigate(`/warehouse/receipts/${created.header.publicId}`)
        }}
      >
        <div className="r2">
          <Field name="source" label={t('warehouse.receipts.fields.source')} required>
            <Select options={sourceOptions} />
          </Field>
          <Field name="warehousePublicId" label={t('warehouse.receipts.fields.warehouse')} required>
            <WarehousePickerInput disabled={Boolean(preset)} />
          </Field>
        </div>
        <div className="r2">
          {source === 'ASN' && (
            <Field name="asnId" label={t('warehouse.receipts.fields.asn')} required help={warehousePublicId ? undefined : t('warehouse.receipts.pickWarehouseFirst')}>
              <Select options={asnOptions} placeholder={asns.isLoading ? t('common.loading') : t('warehouse.receipts.choose')} />
            </Field>
          )}
          {source === 'PO' && (
            <Field
              name="purchaseOrderPublicId"
              label={t('warehouse.receipts.fields.purchaseOrder')}
              required
              help={pos.error ? t('warehouse.receipts.noPoAccess') : warehousePublicId ? undefined : t('warehouse.receipts.pickWarehouseFirst')}
            >
              <Select options={poOptions} placeholder={pos.isLoading ? t('common.loading') : t('warehouse.receipts.choose')} />
            </Field>
          )}
          <Field name="stagingBinId" label={t('warehouse.receipts.fields.stagingBin')} help={t('warehouse.receipts.fields.stagingBinHelp')}>
            <BinPickerInput warehousePublicId={warehousePublicId} zoneTypeCodes={RECEIVING_ZONES} placeholder={t('warehouse.receipts.defaultStaging')} />
          </Field>
        </div>

        {manual && (
          <>
            <p className="help">{t('warehouse.receipts.linesTitle')}</p>
            {fields.map((f, i) => (
              <ReceiptLineFields key={f.id} index={i} onRemove={fields.length > 1 ? () => remove(i) : undefined} />
            ))}
            <button type="button" className="btn sm" disabled={fields.length >= MAX_LINES} onClick={() => append(EMPTY_LINE)}>
              {t('warehouse.receipts.addLine')}
            </button>
          </>
        )}
        {!manual && <p className="note">{t('warehouse.receipts.fromDocumentNote')}</p>}
      </Form>
    </Modal>
  )
}

// =====================================================================================================================
// Alta de aviso de llegada (ASN) de un cliente 3PL
// =====================================================================================================================
interface AsnLineForm {
  productPublicId: string | null
  expectedQty: number | null
  lotNumber: string
}
const EMPTY_ASN_LINE: AsnLineForm = { productPublicId: null, expectedQty: null, lotNumber: '' }

function CreateAsnModal({ onClose }: { onClose: () => void }) {
  const t = useT()
  const save = useSaveAsn()
  const schema = useMemo(
    () =>
      z
        .object({
          warehousePublicId: z.string().nullable(),
          clientPublicId: z.string().nullable(),
          reference: z.string().max(80, t('warehouse.asns.errors.referenceMax')),
          expectedDate: z.string(),
          lines: z.array(z.object({ productPublicId: z.string().nullable(), expectedQty: z.number().nullable(), lotNumber: z.string().max(60, t('warehouse.lineRules.lotTooLong')) })),
        })
        .superRefine((v, ctx) => {
          if (!v.warehousePublicId) ctx.addIssue({ code: 'custom', path: ['warehousePublicId'], message: t('warehouse.receipts.errors.warehouseRequired') })
          if (!v.clientPublicId) ctx.addIssue({ code: 'custom', path: ['clientPublicId'], message: t('warehouse.asns.errors.clientRequired') })
          if (v.lines.length === 0) ctx.addIssue({ code: 'custom', path: ['reference'], message: t('warehouse.receipts.errors.linesRequired') })
          if (v.lines.length > MAX_LINES) ctx.addIssue({ code: 'custom', path: ['reference'], message: t('warehouse.asns.errors.tooManyLines') })
          v.lines.forEach((l, i) => {
            if (!l.productPublicId) ctx.addIssue({ code: 'custom', path: ['lines', i, 'productPublicId'], message: t('warehouse.receipts.errors.productRequired') })
            if (l.expectedQty == null || l.expectedQty <= 0)
              ctx.addIssue({ code: 'custom', path: ['lines', i, 'expectedQty'], message: t('warehouse.asns.errors.expectedQtyPositive') })
            else if (decimalsOf(l.expectedQty) > 3) ctx.addIssue({ code: 'custom', path: ['lines', i, 'expectedQty'], message: t('warehouse.lineRules.qtyDecimals') })
          })
        }),
    [t],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: { warehousePublicId: null, clientPublicId: null, reference: '', expectedDate: '', lines: [EMPTY_ASN_LINE] },
  })
  const { fields, append, remove } = useFieldArray({ control: form.control, name: 'lines' })
  const clientPublicId = useWatch({ control: form.control, name: 'clientPublicId' })
  const formId = 'asn-create'

  return (
    <Modal
      open
      size="lg"
      title={t('warehouse.asns.new')}
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
          await save.mutateAsync({
            action: 'create',
            body: {
              warehousePublicId: v.warehousePublicId,
              clientPublicId: v.clientPublicId,
              reference: v.reference.trim() || null,
              expectedDate: v.expectedDate || null,
              lines: v.lines.map((l) => ({ productPublicId: l.productPublicId, expectedQty: l.expectedQty, lotNumber: l.lotNumber.trim() || null })),
            },
          })
          toast.success(t('warehouse.asns.created'))
          onClose()
        }}
      >
        <div className="r2">
          <Field name="warehousePublicId" label={t('warehouse.receipts.fields.warehouse')} required>
            <WarehousePickerInput />
          </Field>
          <Field name="clientPublicId" label={t('warehouse.asns.fields.client')} required>
            <ClientPickerInput />
          </Field>
        </div>
        <div className="r2">
          <Field name="reference" label={t('warehouse.asns.fields.reference')}>
            <TextInput maxLength={80} />
          </Field>
          <Field name="expectedDate" label={t('warehouse.asns.fields.expectedDate')}>
            <DateInput />
          </Field>
        </div>
        <p className="help">{t('warehouse.receipts.linesTitle')}</p>
        {fields.map((f, i) => (
          <div key={f.id} className="panel" style={{ padding: 12, marginBottom: 10 }}>
            <div className="r3">
              <Field name={`lines.${i}.productPublicId`} label={t('warehouse.receipts.fields.product')} required>
                <ProductPickerInput ownerClientPublicId={clientPublicId} disabled={!clientPublicId} placeholder={clientPublicId ? undefined : t('warehouse.asns.pickClientFirst')} />
              </Field>
              <Field name={`lines.${i}.expectedQty`} label={t('warehouse.asns.fields.expectedQty')} required>
                <NumberInput min={0} step="0.001" />
              </Field>
              <Field name={`lines.${i}.lotNumber`} label={t('warehouse.receipts.fields.lot')}>
                <TextInput maxLength={60} />
              </Field>
            </div>
            {fields.length > 1 && (
              <button type="button" className="btn sm" onClick={() => remove(i)}>
                {t('warehouse.receipts.removeLine')}
              </button>
            )}
          </div>
        ))}
        <button type="button" className="btn sm" disabled={fields.length >= MAX_LINES} onClick={() => append(EMPTY_ASN_LINE)}>
          {t('warehouse.receipts.addLine')}
        </button>
      </Form>
    </Modal>
  )
}

// =====================================================================================================================
// Pestaña Avisos de llegada
// =====================================================================================================================
function AsnsTab({ onReceive }: { onReceive: (asn: AsnDto) => void }) {
  const t = useT()
  const lang = useLang()
  const [warehousePublicId, setWarehousePublicId] = useState<string | null>(null)
  const [status, setStatus] = useState<string[]>([])
  const [q, setQ] = useState('')
  const [creating, setCreating] = useState(false)
  const [toCancel, setToCancel] = useState<AsnDto | null>(null)
  const save = useSaveAsn()
  const { data, isLoading, error } = useAsns({ warehousePublicId: warehousePublicId ?? undefined, status: status.length > 0 ? status : undefined })
  const { data: statuses = [] } = useStatuses(ASN_STATUS_DOMAIN)
  const statusOptions = useMemo(() => statuses.map((s) => ({ value: s.code, label: s.label })), [statuses])

  const rows = useMemo(
    () => (data ?? []).filter((a) => matchesQ(q, `#${a.id}`, a.reference, a.clientName, a.purchaseOrderNumber, a.warehouseCode, a.receiptNumber, a.status)),
    [data, q],
  )

  const columns = useMemo<DataColumn<AsnDto>[]>(
    () => [
      {
        id: 'asn',
        header: t('warehouse.asns.columns.asn'),
        cell: (a) => (
          <span className="ref">
            #{a.id}
            {a.reference ? ` · ${a.reference}` : ''}
          </span>
        ),
        sortValue: (a) => a.id ?? 0,
        card: 'title',
      },
      { id: 'warehouse', header: t('warehouse.asns.columns.warehouse'), cell: (a) => a.warehouseCode, sortValue: (a) => a.warehouseCode },
      {
        id: 'owner',
        header: t('warehouse.asns.columns.owner'),
        cell: (a) => a.clientName ?? (a.purchaseOrderNumber ? `${t('warehouse.receipts.origin.PO')} ${a.purchaseOrderNumber}` : '—'),
        sortValue: (a) => a.clientName ?? a.purchaseOrderNumber,
      },
      { id: 'expected', header: t('warehouse.asns.columns.expectedDate'), cell: (a) => formatDate(a.expectedDate, lang) || '—', sortValue: (a) => a.expectedDate },
      { id: 'status', header: t('warehouse.asns.columns.status'), cell: (a) => <StatusChip domain={ASN_STATUS_DOMAIN} code={a.statusCode} label={a.status} />, sortValue: (a) => a.status },
      { id: 'lines', header: t('warehouse.asns.columns.lines'), cell: (a) => a.lines?.length ?? 0, sortValue: (a) => a.lines?.length ?? 0, align: 'end' },
      {
        id: 'receipt',
        header: t('warehouse.asns.columns.receipt'),
        cell: (a) =>
          a.receiptPublicId ? (
            <Link className="ref" to={`/warehouse/receipts/${a.receiptPublicId}`} onClick={(e) => e.stopPropagation()}>
              {a.receiptNumber}
            </Link>
          ) : (
            '—'
          ),
        sortValue: (a) => a.receiptNumber,
      },
    ],
    [t, lang],
  )

  const actions = useMemo<RowAction<AsnDto>[]>(
    () => [
      {
        key: 'receive',
        label: t('warehouse.asns.receive'),
        perm: 'warehouse.receive',
        visible: (a) => a.statusCode === 'EXPECTED' && !a.receiptPublicId,
        onClick: onReceive,
        tone: 'flow',
      },
      {
        key: 'cancel',
        label: t('warehouse.asns.cancel'),
        perm: 'warehouse.receive',
        visible: (a) => a.statusCode === 'EXPECTED',
        onClick: (a) => setToCancel(a),
        tone: 'danger',
      },
    ],
    [t, onReceive],
  )

  return (
    <>
      <Filters
        onClear={() => {
          setWarehousePublicId(null)
          setStatus([])
          setQ('')
        }}
      >
        <div className="f">
          <label>{t('warehouse.receipts.filters.warehouse')}</label>
          <WarehousePicker value={warehousePublicId} onChange={(v) => setWarehousePublicId(v)} placeholder={t('warehouse.receipts.filters.anyWarehouse')} />
        </div>
        <SearchSelect label={t('warehouse.receipts.filters.status')} options={statusOptions} value={status} onChange={setStatus} />
      </Filters>
      <Panel
        flush
        icon={<IconCheckin />}
        title={t('warehouse.asns.title')}
        badge={data ? rows.length : undefined}
        actions={
          <Can perm="warehouse.receive">
            <button type="button" className="btn flow sm" onClick={() => setCreating(true)}>
              {t('warehouse.asns.new')}
            </button>
          </Can>
        }
      >
        <div className="qrow">
          <QBox value={q} onChange={setQ} />
        </div>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('warehouse.asns.title')}
            columns={columns}
            rows={rows}
            rowKey={(a) => a.id ?? 0}
            defaultSort={{ id: 'asn', desc: true }}
            pageSize={PAGE_SIZE}
            loading={isLoading}
            rowActions={actions}
          />
        )}
      </Panel>
      {creating && <CreateAsnModal onClose={() => setCreating(false)} />}
      <ConfirmDialog
        open={toCancel !== null}
        tone="danger"
        title={t('warehouse.asns.cancelTitle')}
        message={t('warehouse.asns.cancelBody', { id: toCancel?.id ?? '' })}
        confirmLabel={t('warehouse.asns.cancel')}
        onConfirm={async () => {
          if (!toCancel?.id) return
          await save.mutateAsync({ action: 'cancel', id: toCancel.id })
          toast.success(t('warehouse.asns.cancelled'))
        }}
        onClose={() => setToCancel(null)}
      />
    </>
  )
}

// =====================================================================================================================
// Pestaña Recibos (paginada en el servidor)
// =====================================================================================================================
function ReceiptsTab() {
  const t = useT()
  const lang = useLang()
  const navigate = useNavigate()
  const [warehousePublicId, setWarehousePublicId] = useState<string | null>(null)
  const [status, setStatus] = useState<string[]>([])
  const [types, setTypes] = useState<string[]>([])
  const [range, setRange] = useState<DateRange>(EMPTY_RANGE)
  const [products, setProducts] = useState<ProductFilterItem[]>([])
  const [variance, setVariance] = useState('')
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const search = useDebounced(q.trim())

  const { data: statuses = [] } = useStatuses(STATUS_DOMAIN)
  const { data: typeLookups = [] } = useLookups(TYPE_DOMAIN)
  const statusOptions = useMemo(() => statuses.map((s) => ({ value: s.code, label: s.label })), [statuses])
  const typeOptions = useMemo(() => typeLookups.map((s) => ({ value: s.code, label: s.label })), [typeLookups])

  function reset<T>(setter: (v: T) => void) {
    return (v: T) => {
      setPage(1)
      setter(v)
    }
  }

  const query = useMemo(
    () => ({
      warehousePublicId: warehousePublicId ?? undefined,
      status: status.length > 0 ? status : undefined,
      types: types.length > 0 ? types : undefined,
      from: range.from || undefined,
      to: range.to || undefined,
      productPublicIds: products.length > 0 ? products.map((p) => p.publicId) : undefined,
      hasVariance: variance === '' ? undefined : variance === 'yes',
      search: search || undefined,
      skip: (page - 1) * PAGE_SIZE,
      take: PAGE_SIZE,
    }),
    [warehousePublicId, status, types, range, products, variance, search, page],
  )
  const { data, isLoading, error } = useReceipts(query)

  const columns = useMemo<DataColumn<ReceiptListItemDto>[]>(() => {
    // "Origen · Referencia · Remitente": se ordena por el mismo texto que se ve
    const receiptFrom = (r: ReceiptListItemDto) =>
      [r.origin ? t(`warehouse.receipts.origin.${r.origin}`) : null, r.originRef, r.senderName].filter(Boolean).join(' · ')
    return [
      // Orden en el cliente: la lista es paginada por el servidor (sin parámetro de orden), así que solo reacomoda la página visible.
      { id: 'number', header: t('warehouse.receipts.columns.number'), cell: (r) => <span className="ref">{r.number}</span>, sortValue: (r) => r.number, card: 'title' },
      { id: 'type', header: t('warehouse.receipts.columns.type'), cell: (r) => r.type ?? r.typeCode, sortValue: (r) => r.type ?? r.typeCode },
      { id: 'warehouse', header: t('warehouse.receipts.columns.warehouse'), cell: (r) => r.warehouseCode, sortValue: (r) => r.warehouseCode },
      {
        id: 'status',
        header: t('warehouse.receipts.columns.status'),
        cell: (r) => <StatusChip domain={STATUS_DOMAIN} code={r.statusCode} label={r.status} />,
        sortValue: (r) => r.status ?? r.statusCode,
      },
      {
        id: 'from',
        header: t('warehouse.receipts.columns.from'),
        cell: (r) => receiptFrom(r),
        sortValue: (r) => receiptFrom(r),
      },
      { id: 'createdAt', header: t('warehouse.receipts.columns.createdAt'), cell: (r) => formatDateTime(r.createdAtUtc, lang), sortValue: (r) => r.createdAtUtc },
      {
        id: 'variance',
        header: t('warehouse.receipts.columns.variance'),
        cell: (r) => (r.hasVariance ? <Chip tone="warn">{formatNumber(r.varianceQty, lang)}</Chip> : '—'),
        sortValue: (r) => (r.hasVariance ? r.varianceQty : 0),
        align: 'end',
      },
    ]
  }, [t, lang])

  return (
    <>
      <Filters
        onClear={() => {
          setPage(1)
          setWarehousePublicId(null)
          setStatus([])
          setTypes([])
          setRange(EMPTY_RANGE)
          setProducts([])
          setVariance('')
          setQ('')
        }}
      >
        <div className="f">
          <label>{t('warehouse.receipts.filters.warehouse')}</label>
          <WarehousePicker value={warehousePublicId} onChange={reset((v: string | null) => setWarehousePublicId(v))} placeholder={t('warehouse.receipts.filters.anyWarehouse')} />
        </div>
        <SearchSelect label={t('warehouse.receipts.filters.status')} options={statusOptions} value={status} onChange={reset(setStatus)} />
        <SearchSelect label={t('warehouse.receipts.filters.types')} options={typeOptions} value={types} onChange={reset(setTypes)} />
        <DateRangeFilter label={t('warehouse.receipts.filters.created')} value={range} onChange={reset(setRange)} />
        <ProductMultiFilter label={t('warehouse.receipts.filters.product')} value={products} onChange={reset(setProducts)} includeInactive />
        <SelectFilter
          label={t('warehouse.receipts.filters.variance')}
          value={variance}
          onChange={reset(setVariance)}
          options={[
            { value: 'yes', label: t('warehouse.receipts.filters.withVariance') },
            { value: 'no', label: t('warehouse.receipts.filters.withoutVariance') },
          ]}
        />
      </Filters>
      <Panel flush icon={<IconCheckin />} title={t('warehouse.receipts.title')} badge={data ? (data.total ?? 0) : undefined}>
        <div className="qrow">
          <QBox value={q} onChange={reset(setQ)} placeholder={t('warehouse.receipts.searchPlaceholder')} />
        </div>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('warehouse.receipts.title')}
            columns={columns}
            rows={data?.items ?? NO_ROWS}
            rowKey={(r) => r.publicId ?? String(r.id)}
            loading={isLoading}
            page={page}
            pageSize={PAGE_SIZE}
            total={data?.total ?? 0}
            onPage={setPage}
            onRowClick={(r) => navigate(`/warehouse/receipts/${r.publicId}`)}
            rowActions={[{ key: 'open', label: t('warehouse.receipts.open'), onClick: (r) => navigate(`/warehouse/receipts/${r.publicId}`) }]}
          />
        )}
      </Panel>
    </>
  )
}

// =====================================================================================================================
// Pantalla
// =====================================================================================================================
export default function ReceiptListScreen() {
  const t = useT()
  // La pestaña va en la URL (?tab=asns|putaway) para poder enlazarla (Actividad reciente, ficha del recibo).
  const [params, setParams] = useSearchParams()
  const raw = params.get('tab')
  const tab: TabKey = isTabKey(raw) ? raw : 'receipts'
  const setTab = (key: TabKey) => setParams(key === 'receipts' ? {} : { tab: key }, { replace: true })
  const [creating, setCreating] = useState<null | { asnId: number; warehousePublicId: string | null } | 'new'>(null)

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('warehouse.receipts.title')}</h1>
          <p>{t('warehouse.receipts.subtitle')}</p>
        </div>
        <div className="act">
          <Can perm="warehouse.receive">
            <button type="button" className="btn flow" onClick={() => setCreating('new')}>
              {t('warehouse.receipts.new')}
            </button>
          </Can>
        </div>
      </div>

      <div style={{ marginBottom: 14 }}>
        <Tabs<TabKey>
          label={t('warehouse.receipts.title')}
          value={tab}
          onChange={setTab}
          tabs={[
            { key: 'receipts', label: t('warehouse.receipts.tabReceipts') },
            { key: 'asns', label: t('warehouse.receipts.tabAsns') },
            { key: 'putaway', label: t('warehouse.receipts.tabPutaway') },
          ]}
        />
      </div>

      {tab === 'receipts' && <ReceiptsTab />}
      {tab === 'asns' && <AsnsTab onReceive={(a) => setCreating({ asnId: a.id ?? 0, warehousePublicId: a.warehousePublicId ?? null })} />}
      {/* Acomodo (PUTAWAY) de todos los recibos: antes en 'Tareas de almacén', que no existe en la maqueta. */}
      {tab === 'putaway' && <TaskQueue types={PUTAWAY_TYPES} title={t('warehouse.receipts.putawayTitle')} icon={<IconCheckin />} />}

      {creating !== null && <CreateReceiptModal onClose={() => setCreating(null)} preset={creating === 'new' ? undefined : creating} />}
    </div>
  )
}
