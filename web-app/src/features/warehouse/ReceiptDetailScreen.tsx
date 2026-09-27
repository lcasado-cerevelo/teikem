// Pieza "Recibo" (Lote F6) — ficha del recibo. `/warehouse/receipts/:publicId`.
// Lectura: inventory.view + WMS_LOTSERIAL (por la ruta). Captura de líneas, línea extra, quitar línea, confirmar y eliminar:
// warehouse.receive, solo con el recibo OPEN. El estatus no se cambia a mano: OPEN → RECEIVED al confirmar y → PUTAWAY cuando
// el sistema termina la última tarea PUTAWAY (se pinta como historial). Manual 06 §4.
import { zodResolver } from '@hookform/resolvers/zod'
import { useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { z } from 'zod'
import { Can } from '../../kernel/access'
import { ApiError } from '../../kernel/api/problem'
import type { components } from '../../kernel/api/schema'
import { StatusChip, StatusPipeline } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import {
  Chip,
  ConfirmDialog,
  DataTable,
  DateInput,
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
  type RowAction,
} from '../../kernel/ui'
import {
  productLabel,
  useConfirmReceipt,
  useDeleteReceipt,
  useReceipt,
  useSaveReceiptLine,
  useWarehouseBins,
  warehouseKeys,
  type ReceiptDetailDto,
  type WarehouseTaskDto,
} from './api'
import { formatDateTime, formatNumber, lineErrorsByIndex, parseSerials, receiptLineIssues, remapProblemFields, type LineIssue } from './lineRules'
import { ProductPickerInput } from './pickers'

type Schemas = components['schemas']
type ReceiptLine = Schemas['ReceiptLineDto']

const STATUS_DOMAIN = 'ReceiptStatus'
const ENTITY_TYPE = 'RECEIPT'
const TASK_STATUS_DOMAIN = 'WarehouseTaskStatus'
const MAX_LINES = 200
const RECEIVING_ZONES = new Set(['STAGING', 'CROSSDOCK'])

function useIssueText() {
  const t = useT()
  return (issue: LineIssue) => t(`warehouse.lineRules.${issue.code}`, issue.params)
}

/** Posiciones de recepción del almacén (zonas STAGING/CROSSDOCK activas) como opciones de <Select>. */
function useStagingOptions(warehousePublicId: string | null | undefined) {
  const bins = useWarehouseBins(warehousePublicId, {}, { handleAccessDenied: false })
  return useMemo(
    () =>
      (bins.data ?? [])
        .filter((b) => b.isActive !== false && RECEIVING_ZONES.has(b.zoneTypeCode ?? ''))
        .map((b) => ({ value: String(b.id), label: [b.code, b.zoneCode].filter(Boolean).join(' · ') })),
    [bins.data],
  )
}

/**
 * El error `line` del servidor (ValidateCapture) no nombra campo: se pone bajo el que lo causa según el seguimiento
 * (SERIAL: cantidad no entera; LOT: series no admitidas; NONE: lote o series no admitidos).
 */
function captureFieldFor(tracking: string | null | undefined, hasLot: boolean): string {
  if (tracking === 'SERIAL') return 'receivedQty'
  if (tracking === 'LOT') return 'serialNumbers'
  return hasLot ? 'lot' : 'serialNumbers'
}

const lineSchema = (t: (k: string) => string, issueText: (i: LineIssue) => string, needsProduct: boolean) =>
  z
    .object({
      productPublicId: z.string().nullable(),
      sku: z.string(),
      trackingTypeCode: z.string(),
      receivedQty: z.number().nullable(),
      lot: z.string(),
      lotManufactureDate: z.string(),
      lotExpiryDate: z.string(),
      serialNumbers: z.string(),
      stagingBinId: z.string(),
    })
    .superRefine((v, ctx) => {
      if (needsProduct && !v.productPublicId) ctx.addIssue({ code: 'custom', path: ['productPublicId'], message: t('warehouse.receipts.errors.productRequired') })
      for (const issue of receiptLineIssues({
        sku: v.sku,
        trackingTypeCode: v.trackingTypeCode,
        receivedQty: v.receivedQty,
        lot: v.lot,
        lotManufactureDate: v.lotManufactureDate,
        lotExpiryDate: v.lotExpiryDate,
        serials: parseSerials(v.serialNumbers),
      })) {
        ctx.addIssue({ code: 'custom', path: [issue.field], message: issueText(issue) })
      }
    })

// =====================================================================================================================
// Captura (PUT .../lines/{lineId}) y línea extra (POST .../lines): un solo modal, una columna a 480 px
// =====================================================================================================================
function LineModal({ receipt, line, onClose }: { receipt: ReceiptDetailDto; line: ReceiptLine | null; onClose: () => void }) {
  const t = useT()
  const lang = useLang()
  const issueText = useIssueText()
  const save = useSaveReceiptLine()
  const header = receipt.header ?? {}
  const publicId = header.publicId ?? ''
  const adding = line === null
  const stagingOptions = useStagingOptions(header.warehousePublicId)
  const schema = useMemo(() => lineSchema(t, issueText, adding), [t, issueText, adding])
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: {
      productPublicId: line?.productPublicId ?? null,
      sku: line?.sku ?? '',
      trackingTypeCode: line?.trackingTypeCode ?? '',
      receivedQty: line ? (line.receivedQty ?? 0) : null,
      lot: line?.lotNumber ?? '',
      lotManufactureDate: '',
      lotExpiryDate: line?.expiryDate ?? '',
      serialNumbers: (line?.serialNumbers ?? []).join('\n'),
      stagingBinId: line?.stagingBinId != null ? String(line.stagingBinId) : '',
    },
  })
  const tracking = useWatch({ control: form.control, name: 'trackingTypeCode' })
  const formId = adding ? 'receipt-line-add' : 'receipt-line-capture'

  return (
    <Modal
      open
      title={adding ? t('warehouse.receipts.detail.addLine') : t('warehouse.receipts.detail.captureTitle', { product: productLabel({ sku: line?.sku, name: line?.productName }) })}
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
          const serials = parseSerials(v.serialNumbers)
          const lotNumber = v.lot.trim()
          const lot = lotNumber ? { number: lotNumber, manufactureDate: v.lotManufactureDate || null, expiryDate: v.lotExpiryDate || null } : undefined
          const stagingBinId = v.stagingBinId ? Number(v.stagingBinId) : null
          const captureField = captureFieldFor(v.trackingTypeCode, Boolean(lotNumber))
          try {
            if (adding) {
              await save.mutateAsync({
                publicId,
                action: 'add',
                body: { productPublicId: v.productPublicId, receivedQty: v.receivedQty, lot, serialNumbers: serials.length > 0 ? serials : null, stagingBinId },
              })
            } else {
              await save.mutateAsync({
                publicId,
                action: 'update',
                lineId: line.id ?? 0,
                body: {
                  receivedQty: v.receivedQty,
                  lot,
                  clearLot: !lotNumber && line.lotId != null ? true : null,
                  // SERIAL: la lista completa (vacía la borra); en otro seguimiento solo si se capturó algo (el servidor lo rechaza)
                  serialNumbers: v.trackingTypeCode === 'SERIAL' || serials.length > 0 ? serials : null,
                  stagingBinId: stagingBinId !== (line.stagingBinId ?? null) ? stagingBinId : null,
                },
              })
            }
          } catch (err) {
            throw remapProblemFields(err, (k) => {
              if (k === 'line') return captureField
              if (k.startsWith('line.')) return k.slice(5)
              return null
            })
          }
          toast.success(adding ? t('warehouse.receipts.detail.lineAdded') : t('warehouse.receipts.detail.lineSaved'))
          onClose()
        }}
      >
        {adding ? (
          <Field name="productPublicId" label={t('warehouse.receipts.fields.product')} required>
            <ProductPickerInput
              onPicked={(p) => {
                form.setValue('sku', p?.sku ?? '')
                form.setValue('trackingTypeCode', p?.trackingTypeCode ?? '')
              }}
            />
          </Field>
        ) : (
          <p className="note" style={{ marginBottom: 10 }}>
            {t('warehouse.receipts.detail.expected')}: {line.expectedQty != null ? formatNumber(line.expectedQty, lang) : '—'} ·{' '}
            {t(`warehouse.receipts.tracking.${line.trackingTypeCode ?? 'NONE'}`)}
          </p>
        )}
        <div className="r2">
          <Field name="receivedQty" label={t('warehouse.receipts.fields.receivedQty')} required>
            <NumberInput min={0} step={tracking === 'SERIAL' ? '1' : '0.001'} />
          </Field>
          <Field name="stagingBinId" label={t('warehouse.receipts.fields.stagingBin')}>
            <Select options={stagingOptions} placeholder={t('warehouse.receipts.defaultStaging')} />
          </Field>
        </div>
        {(tracking === 'LOT' || tracking === 'SERIAL') && (
          <div className="r3">
            <Field name="lot" label={t('warehouse.receipts.fields.lot')} required={tracking === 'LOT'}>
              <TextInput maxLength={60} />
            </Field>
            <Field name="lotManufactureDate" label={t('warehouse.receipts.fields.manufactureDate')}>
              <DateInput />
            </Field>
            <Field name="lotExpiryDate" label={t('warehouse.receipts.fields.expiryDate')}>
              <DateInput />
            </Field>
          </div>
        )}
        {tracking === 'SERIAL' && (
          <Field name="serialNumbers" label={t('warehouse.receipts.fields.serials')} help={t('warehouse.receipts.fields.serialsHelp')} required>
            <TextArea rows={5} />
          </Field>
        )}
      </Form>
    </Modal>
  )
}

// =====================================================================================================================
// Tareas PUTAWAY del recibo (solo lectura; se trabajan en Tareas de almacén)
// =====================================================================================================================
function PutawayTasks({ tasks }: { tasks: readonly WarehouseTaskDto[] }) {
  const t = useT()
  const lang = useLang()
  const columns = useMemo<DataColumn<WarehouseTaskDto>[]>(
    () => [
      { id: 'product', header: t('warehouse.receipts.detail.columns.product'), cell: (r) => productLabel({ sku: r.sku, name: r.productName }), card: 'title', sortValue: (r) => r.sku },
      { id: 'status', header: t('warehouse.receipts.columns.status'), cell: (r) => <StatusChip domain={TASK_STATUS_DOMAIN} code={r.statusCode} label={r.status} />, sortValue: (r) => r.status ?? r.statusCode },
      { id: 'qty', header: t('warehouse.receipts.detail.columns.quantity'), cell: (r) => formatNumber(r.quantity, lang), align: 'end', sortValue: (r) => r.quantity },
      { id: 'bins', header: t('warehouse.receipts.detail.columns.bins'), cell: (r) => [r.fromBinCode, r.toBinCode].filter(Boolean).join(' → ') || '—', sortValue: (r) => r.fromBinCode ?? r.toBinCode },
      { id: 'assigned', header: t('warehouse.receipts.detail.columns.assignedTo'), cell: (r) => r.assignedToName ?? '—', sortValue: (r) => r.assignedToName },
    ],
    [t, lang],
  )
  return (
    <Panel
      flush
      title={t('warehouse.receipts.detail.putaway')}
      actions={
        <Link className="btn sm" to="/warehouse/tasks">
          {t('warehouse.receipts.detail.goTasks')}
        </Link>
      }
    >
      <DataTable label={t('warehouse.receipts.detail.putaway')} columns={columns} rows={tasks} rowKey={(r) => r.id ?? 0} pageSize={25} />
    </Panel>
  )
}

// =====================================================================================================================
// Pantalla
// =====================================================================================================================
export default function ReceiptDetailScreen() {
  const t = useT()
  const lang = useLang()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const { publicId = '' } = useParams()
  const { data: receipt, isLoading, error } = useReceipt(publicId)
  const confirm = useConfirmReceipt()
  const del = useDeleteReceipt()
  const saveLine = useSaveReceiptLine()
  const [editing, setEditing] = useState<ReceiptLine | 'new' | null>(null)
  const [toRemove, setToRemove] = useState<ReceiptLine | null>(null)
  const [confirming, setConfirming] = useState(false)
  const [deleting, setDeleting] = useState(false)
  // errores por línea devueltos al confirmar (400 `lines[i]`), por id de línea
  const [lineErrors, setLineErrors] = useState<Record<number, string[]>>({})

  const header = receipt?.header
  const lines = useMemo(() => receipt?.lines ?? [], [receipt?.lines])
  const isOpen = header?.statusCode === 'OPEN'
  const hasCrossDock = lines.some((l) => (l.allocatedToCrossDock ?? 0) > 0)

  const columns = useMemo<DataColumn<ReceiptLine>[]>(
    () => [
      {
        id: 'product',
        header: t('warehouse.receipts.detail.columns.product'),
        cell: (l) => (
          <>
            {productLabel({ sku: l.sku, name: l.productName })}
            {(lineErrors[l.id ?? 0] ?? []).map((m) => (
              <span key={m} className="ferr" role="alert" style={{ display: 'block' }}>
                {m}
              </span>
            ))}
          </>
        ),
        sortValue: (l) => l.sku,
        card: 'title',
      },
      {
        id: 'tracking',
        header: t('warehouse.receipts.detail.columns.tracking'),
        cell: (l) => t(`warehouse.receipts.tracking.${l.trackingTypeCode ?? 'NONE'}`),
        sortValue: (l) => l.trackingTypeCode,
      },
      { id: 'expected', header: t('warehouse.receipts.detail.columns.expected'), cell: (l) => (l.expectedQty != null ? formatNumber(l.expectedQty, lang) : '—'), sortValue: (l) => l.expectedQty ?? 0, align: 'end' },
      { id: 'received', header: t('warehouse.receipts.detail.columns.received'), cell: (l) => formatNumber(l.receivedQty, lang), sortValue: (l) => l.receivedQty ?? 0, align: 'end' },
      {
        id: 'variance',
        header: t('warehouse.receipts.detail.columns.variance'),
        cell: (l) => ((l.varianceQty ?? 0) !== 0 ? <Chip tone="warn">{formatNumber(l.varianceQty, lang)}</Chip> : '0'),
        sortValue: (l) => l.varianceQty ?? 0,
        align: 'end',
      },
      {
        id: 'lot',
        header: t('warehouse.receipts.detail.columns.lot'),
        cell: (l) => (l.lotNumber ? `${l.lotNumber}${l.expiryDate ? ` (${l.expiryDate})` : ''}` : '—'),
        sortValue: (l) => l.lotNumber,
      },
      {
        id: 'serials',
        header: t('warehouse.receipts.detail.columns.serials'),
        cell: (l) => (l.trackingTypeCode === 'SERIAL' ? t('warehouse.receipts.detail.serialCount', { count: l.serialNumbers?.length ?? 0 }) : '—'),
        card: 'hidden',
        sortValue: (l) => l.serialNumbers?.length,
      },
      { id: 'staging', header: t('warehouse.receipts.detail.columns.staging'), cell: (l) => l.stagingBinCode ?? '—', sortValue: (l) => l.stagingBinCode },
      {
        id: 'crossDock',
        header: t('warehouse.receipts.detail.columns.crossDock'),
        cell: (l) => ((l.allocatedToCrossDock ?? 0) > 0 ? <Chip tone="route">{formatNumber(l.allocatedToCrossDock, lang)}</Chip> : '—'),
        align: 'end',
        card: 'hidden',
        sortValue: (l) => l.allocatedToCrossDock,
      },
    ],
    [t, lang, lineErrors],
  )

  const actions = useMemo<RowAction<ReceiptLine>[]>(
    () => [
      { key: 'capture', label: t('warehouse.receipts.detail.capture'), perm: 'warehouse.receive', visible: () => isOpen, onClick: (l) => setEditing(l), tone: 'flow' },
      {
        key: 'remove',
        label: t('warehouse.receipts.detail.removeLine'),
        perm: 'warehouse.receive',
        // las líneas del aviso no se quitan (se captura 0) y las que tienen cruce de muelle tampoco
        visible: (l) => isOpen && l.asnLineId == null && !((l.allocatedToCrossDock ?? 0) > 0),
        onClick: (l) => setToRemove(l),
        tone: 'danger',
      },
    ],
    [t, isOpen],
  )

  if (isLoading) return <Spinner block />
  if (error || !receipt || !header) {
    const notFound = error instanceof ApiError && error.code === 'not_found'
    return (
      <EmptyState
        title={notFound ? t('warehouse.receipts.notFound') : (error?.message ?? t('errors.generic'))}
        action={
          <Link className="btn" to="/warehouse/receipts">
            {t('warehouse.receipts.back')}
          </Link>
        }
      />
    )
  }

  const origin = [header.origin ? t(`warehouse.receipts.origin.${header.origin}`) : null, header.originRef, header.senderName].filter(Boolean).join(' · ')

  return (
    <div className="wrap">
      <div className="head">
        <div style={{ minWidth: 0 }}>
          <h1>
            <span className="ref">{header.number}</span> · {header.type ?? header.typeCode}
          </h1>
          <p>
            <StatusChip domain={STATUS_DOMAIN} code={header.statusCode} label={header.status} /> {header.warehouseCode}
            {origin ? ` · ${origin}` : ''}
          </p>
        </div>
        <div className="act">
          <Can perm="warehouse.receive">
            {isOpen && !hasCrossDock && (
              <button type="button" className="btn" onClick={() => setDeleting(true)}>
                {t('warehouse.receipts.detail.delete')}
              </button>
            )}
            {isOpen && lines.length > 0 && (
              <button type="button" className="btn flow" onClick={() => setConfirming(true)}>
                {t('warehouse.receipts.detail.confirm')}
              </button>
            )}
          </Can>
        </div>
      </div>

      <div style={{ marginBottom: 14 }}>
        {/* Sin transición manual: OPEN → RECEIVED al confirmar; → PUTAWAY lo dispara el sistema. */}
        <StatusPipeline domain={STATUS_DOMAIN} entityType={ENTITY_TYPE} entityId={header.id} currentCode={header.statusCode} />
        <p className="help">{t('warehouse.receipts.detail.statusHelp')}</p>
      </div>

      <Panel title={t('warehouse.receipts.detail.summary')}>
        <div className="r3">
          <div className="f">
            <label>{t('warehouse.receipts.detail.expected')}</label>
            <p>{formatNumber(header.expectedQty, lang)}</p>
          </div>
          <div className="f">
            <label>{t('warehouse.receipts.detail.received')}</label>
            <p>{formatNumber(header.receivedQty, lang)}</p>
          </div>
          <div className="f">
            <label>{t('warehouse.receipts.detail.variance')}</label>
            <p>{header.hasVariance ? <Chip tone="warn">{formatNumber(header.varianceQty, lang)}</Chip> : '0'}</p>
          </div>
          <div className="f">
            <label>{t('warehouse.receipts.detail.dock')}</label>
            <p>{header.dockCode ?? '—'}</p>
          </div>
          <div className="f">
            <label>{t('warehouse.receipts.columns.createdAt')}</label>
            <p>{formatDateTime(header.createdAtUtc, lang)}</p>
          </div>
          <div className="f">
            <label>{t('warehouse.receipts.detail.receivedAt')}</label>
            <p>{formatDateTime(header.receivedAtUtc, lang) || '—'}</p>
          </div>
        </div>
      </Panel>

      <Panel
        flush
        title={t('warehouse.receipts.detail.lines')}
        subtitle={t('warehouse.receipts.detail.lineCount', { count: lines.length })}
        actions={
          isOpen ? (
            <Can perm="warehouse.receive">
              <button type="button" className="btn sm" disabled={lines.length >= MAX_LINES} onClick={() => setEditing('new')}>
                {t('warehouse.receipts.detail.addLine')}
              </button>
            </Can>
          ) : undefined
        }
      >
        <DataTable
          label={t('warehouse.receipts.detail.lines')}
          columns={columns}
          rows={lines}
          rowKey={(l) => l.id ?? 0}
          pageSize={50}
          rowActions={actions}
          empty={<EmptyState title={t('warehouse.receipts.detail.noLines')} />}
        />
      </Panel>

      {(receipt.putawayTasks ?? []).length > 0 && <PutawayTasks tasks={receipt.putawayTasks ?? []} />}

      {editing !== null && <LineModal receipt={receipt} line={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}

      <ConfirmDialog
        open={toRemove !== null}
        tone="danger"
        title={t('warehouse.receipts.detail.removeTitle')}
        message={t('warehouse.receipts.detail.removeBody', { product: productLabel({ sku: toRemove?.sku, name: toRemove?.productName }) })}
        confirmLabel={t('warehouse.receipts.detail.removeLine')}
        onConfirm={async () => {
          if (!toRemove?.id) return
          await saveLine.mutateAsync({ publicId, action: 'remove', lineId: toRemove.id })
          toast.success(t('warehouse.receipts.detail.lineRemoved'))
        }}
        onClose={() => setToRemove(null)}
      />

      <ConfirmDialog
        open={confirming}
        title={t('warehouse.receipts.detail.confirmTitle')}
        message={t('warehouse.receipts.detail.confirmBody', { number: header.number ?? '' })}
        confirmLabel={t('warehouse.receipts.detail.confirm')}
        onConfirm={async () => {
          setLineErrors({})
          try {
            await confirm.mutateAsync({ publicId, body: { rowVersion: receipt.rowVersion ?? null } })
          } catch (err) {
            // 400 con errores por línea (`lines[i]`, en el orden de las líneas por id): se muestran en la tabla
            const byIndex = lineErrorsByIndex(err)
            const sorted = [...lines].sort((a, b) => (a.id ?? 0) - (b.id ?? 0))
            const byId: Record<number, string[]> = {}
            for (const [i, msgs] of Object.entries(byIndex)) {
              const l = sorted[Number(i)]
              if (l?.id != null) byId[l.id] = msgs
            }
            setLineErrors(byId)
            if (err instanceof ApiError && err.code === 'conflict') void qc.invalidateQueries({ queryKey: warehouseKeys.receipt })
            throw err
          }
          toast.success(t('warehouse.receipts.detail.confirmed', { number: header.number ?? '' }))
        }}
        onClose={() => setConfirming(false)}
      />

      <ConfirmDialog
        open={deleting}
        tone="danger"
        title={t('warehouse.receipts.detail.deleteTitle')}
        message={t('warehouse.receipts.detail.deleteBody', { number: header.number ?? '' })}
        confirmLabel={t('warehouse.receipts.detail.delete')}
        onConfirm={async () => {
          await del.mutateAsync(publicId)
          toast.success(t('warehouse.receipts.detail.deleted', { number: header.number ?? '' }))
          navigate('/warehouse/receipts')
        }}
        onClose={() => setDeleting(false)}
      />
    </div>
  )
}
