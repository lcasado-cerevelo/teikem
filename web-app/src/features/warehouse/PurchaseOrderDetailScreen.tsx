// Pantalla D (Lote F6) — Compras: ficha de la orden de compra. `/warehouse/purchase-orders/:publicId`.
// Lectura: purchasing.view + PURCHASING (por la ruta). Edición/enviar/cancelar/eliminar: purchasing.manage.
// Resolver un faltante: inventory.adjust, con `ResolveShortageModal` (compartido con 'Ajustes de inventario'; REORDER exige
// además purchasing.manage y MANUAL_ADJUSTMENT el módulo WMS_LOTSERIAL).
// Pipeline: solo SENT y CANCELLED son manuales; PARTIAL/RECEIVED los pone la confirmación del recibo o la resolución.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useFieldArray, useForm } from 'react-hook-form'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { z } from 'zod'
import { Can, useCan } from '../../kernel/access'
import { parseApiDate } from '../../kernel/api/dates'
import { ApiError } from '../../kernel/api/problem'
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
  NumberInput,
  Panel,
  Spinner,
  Tabs,
  TextArea,
  toast,
  type DataColumn,
  type RowAction,
} from '../../kernel/ui'
import {
  usePurchaseOrder,
  usePurchaseOrderAction,
  usePurchaseOrderShortageLines,
  type PurchaseOrderDto,
  type ShortageLineDto,
} from './api'
import { decimalsOf } from './lineRules'
import { ProductPickerInput } from './pickers'
import { ResolveShortageModal } from './ResolveShortageModal'
import { IconCart } from '../../kernel/ui/screenIcons'

const STATUS_DOMAIN = 'PurchaseOrderStatus'
const ENTITY_TYPE = 'PURCHASE_ORDER'
/** Transiciones que se piden desde la pantalla (POST send / cancel); PARTIAL y RECEIVED las dispara el sistema. */
const MANUAL_TARGETS = ['SENT', 'CANCELLED'] as const
type TabKey = 'lines' | 'shortages'

function formatDate(iso: string | null | undefined, lang: string): string {
  if (!iso) return ''
  const date = parseApiDate(iso)
  if (Number.isNaN(date.getTime())) return ''
  return new Intl.DateTimeFormat(lang, { dateStyle: 'medium' }).format(date)
}

// =====================================================================================================================
// Pestaña Líneas: fecha esperada, notas y líneas (una columna a 480 px, `.r3`).
// =====================================================================================================================

interface LineFormValues {
  productPublicId: string
  sku: string
  productName: string
  qtyReceived: number
  qtyOrdered: number | null
  unitCost: number | null
}

function LinesTab({ po }: { po: PurchaseOrderDto }) {
  const t = useT()
  const action = usePurchaseOrderAction()
  const canManage = useCan('purchasing.manage')
  const canEdit = canManage && po.canEdit === true
  const initialCount = po.lines?.length ?? 0

  const lineSchema = useMemo(
    () =>
      z.object({
        productPublicId: z.string().min(1, t('warehouse.purchaseOrders.errors.productRequired')),
        sku: z.string(),
        productName: z.string(),
        qtyReceived: z.number(),
        qtyOrdered: z
          .number(t('warehouse.purchaseOrders.errors.qtyInvalid'))
          .nullable()
          .superRefine((v, ctx) => {
            if (v === null || v <= 0) ctx.addIssue({ code: z.ZodIssueCode.custom, message: t('warehouse.purchaseOrders.errors.qtyRequired') })
            else if (decimalsOf(v) > 3) ctx.addIssue({ code: z.ZodIssueCode.custom, message: t('warehouse.purchaseOrders.errors.qtyDecimals') })
          }),
        unitCost: z
          .number(t('warehouse.purchaseOrders.errors.costInvalid'))
          .nullable()
          .superRefine((v, ctx) => {
            if (v === null) return
            if (v < 0) ctx.addIssue({ code: z.ZodIssueCode.custom, message: t('warehouse.purchaseOrders.errors.costNegative') })
            else if (decimalsOf(v) > 4) ctx.addIssue({ code: z.ZodIssueCode.custom, message: t('warehouse.purchaseOrders.errors.costDecimals') })
          }),
      }),
    [t],
  )
  const schema = useMemo(
    () =>
      z.object({
        expectedDate: z.string(),
        notes: z.string(),
        lines: z
          .array(lineSchema)
          .min(1, t('warehouse.purchaseOrders.errors.linesRequired'))
          .max(200, t('warehouse.purchaseOrders.errors.tooManyLines'))
          .superRefine((lines, ctx) => {
            const seen = new Set<string>()
            lines.forEach((l, i) => {
              if (!l.productPublicId) return
              if (seen.has(l.productPublicId)) {
                ctx.addIssue({ code: z.ZodIssueCode.custom, message: t('warehouse.purchaseOrders.errors.duplicateProduct'), path: [i, 'productPublicId'] })
              }
              seen.add(l.productPublicId)
            })
          }),
      }),
    [t, lineSchema],
  )

  const form = useForm({
    resolver: zodResolver(schema),
    values: {
      expectedDate: po.expectedDate ?? '',
      notes: po.notes ?? '',
      lines: (po.lines ?? []).map((l) => ({
        productPublicId: l.productPublicId ?? '',
        sku: l.sku ?? '',
        productName: l.productName ?? '',
        qtyReceived: l.qtyReceived ?? 0,
        qtyOrdered: l.qtyOrdered ?? null,
        unitCost: l.unitCost ?? null,
      })),
    },
  })
  const { fields, append, remove } = useFieldArray({ control: form.control, name: 'lines' })
  const emptyLine: LineFormValues = { productPublicId: '', sku: '', productName: '', qtyReceived: 0, qtyOrdered: null, unitCost: null }

  return (
    <Form
      form={form}
      onSubmit={async (v) => {
        await action.mutateAsync({
          publicId: po.publicId ?? '',
          action: 'update',
          body: {
            expectedDate: v.expectedDate || null,
            notes: v.notes,
            lines: v.lines.map((l) => ({ productPublicId: l.productPublicId, qtyOrdered: l.qtyOrdered, unitCost: l.unitCost })),
            rowVersion: po.rowVersion ?? null,
          },
        })
        toast.success(t('warehouse.purchaseOrders.saved'))
      }}
    >
      <fieldset disabled={!canEdit} style={{ border: 0, padding: 0, margin: 0, minWidth: 0 }}>
        <div className="r2">
          <div className="f">
            <label>{t('warehouse.purchaseOrders.fields.supplier')}</label>
            <p>{po.supplierName}</p>
          </div>
          <div className="f">
            <label>{t('warehouse.purchaseOrders.fields.warehouse')}</label>
            <p>{po.warehouseCode}</p>
          </div>
        </div>
        <div className="r2">
          <Field name="expectedDate" label={t('warehouse.purchaseOrders.fields.expectedDate')}>
            <DateInput />
          </Field>
        </div>
        <Field name="notes" label={t('warehouse.purchaseOrders.fields.notes')}>
          <TextArea rows={2} />
        </Field>

        <p className="help">{t('warehouse.purchaseOrders.fields.lines')}</p>
        {fields.map((f, i) => {
          const locked = f.qtyReceived > 0
          return (
            <div key={f.id} style={{ padding: 12, marginBottom: 10, border: '1px solid var(--line)', borderRadius: 9 }}>
              <div className="r3">
                {i < initialCount ? (
                  <div className="f">
                    <label>{t('warehouse.purchaseOrders.fields.product')}</label>
                    <p>
                      {f.sku} · {f.productName}
                    </p>
                  </div>
                ) : (
                  <Field name={`lines.${i}.productPublicId`} label={t('warehouse.purchaseOrders.fields.product')} required>
                    <ProductPickerInput ownOnly />
                  </Field>
                )}
                <Field name={`lines.${i}.qtyOrdered`} label={t('warehouse.purchaseOrders.fields.qtyOrdered')} required>
                  <NumberInput step="0.001" disabled={locked} />
                </Field>
                <Field name={`lines.${i}.unitCost`} label={t('warehouse.purchaseOrders.fields.unitCost')}>
                  <NumberInput step="0.0001" disabled={locked} />
                </Field>
              </div>
              {locked ? (
                <p className="note">{t('warehouse.purchaseOrders.lineHasReceipts')}</p>
              ) : (
                fields.length > 1 && (
                  <button type="button" className="btn sm danger" onClick={() => remove(i)}>
                    {t('warehouse.purchaseOrders.fields.removeLine')}
                  </button>
                )
              )}
            </div>
          )
        })}
        {canEdit && (
          <button type="button" className="btn sm" disabled={fields.length >= 200} onClick={() => append(emptyLine)}>
            {t('warehouse.purchaseOrders.fields.addLine')}
          </button>
        )}
      </fieldset>
      {canEdit && (
        <div className="form-acts">
          <button type="submit" className="btn flow" disabled={form.formState.isSubmitting || !form.formState.isDirty}>
            {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
          </button>
        </div>
      )}
    </Form>
  )
}

// =====================================================================================================================
// Pestaña Faltantes (el modal de resolución vive en ResolveShortageModal.tsx)
// =====================================================================================================================

function ShortagesTab({ po }: { po: PurchaseOrderDto }) {
  const t = useT()
  const { data = [], isLoading, error } = usePurchaseOrderShortageLines(po.publicId)
  const [toResolve, setToResolve] = useState<ShortageLineDto | null>(null)

  const columns = useMemo<DataColumn<ShortageLineDto>[]>(
    () => [
      { id: 'sku', header: t('warehouse.purchaseOrders.shortages.columns.sku'), cell: (l) => <span className="ref">{l.sku}</span>, sortValue: (l) => l.sku, card: 'title' },
      { id: 'product', header: t('warehouse.purchaseOrders.shortages.columns.product'), cell: (l) => l.productName, sortValue: (l) => l.productName },
      { id: 'qtyOrdered', header: t('warehouse.purchaseOrders.shortages.columns.qtyOrdered'), cell: (l) => l.qtyOrdered, sortValue: (l) => l.qtyOrdered, align: 'end' },
      { id: 'qtyReceived', header: t('warehouse.purchaseOrders.shortages.columns.qtyReceived'), cell: (l) => l.qtyReceived, sortValue: (l) => l.qtyReceived, align: 'end' },
      { id: 'qtyResolved', header: t('warehouse.purchaseOrders.shortages.columns.qtyResolved'), cell: (l) => l.qtyResolved, sortValue: (l) => l.qtyResolved, align: 'end' },
      {
        id: 'qtyPending',
        header: t('warehouse.purchaseOrders.shortages.columns.pending'),
        cell: (l) => (l.qtyPending ?? 0) > 0 ? <Chip tone="warn">{l.qtyPending}</Chip> : l.qtyPending,
        sortValue: (l) => l.qtyPending,
        align: 'end',
      },
    ],
    [t],
  )

  const actions = useMemo<RowAction<ShortageLineDto>[]>(
    () => [
      {
        key: 'resolve',
        label: t('warehouse.purchaseOrders.shortages.resolve'),
        perm: 'inventory.adjust',
        visible: (l) => (l.qtyPending ?? 0) > 0,
        onClick: (l) => setToResolve(l),
      },
    ],
    [t],
  )

  return (
    <Panel flush icon={<IconCart />} title={t('warehouse.purchaseOrders.shortages.title')}>
      {error ? (
        <p className="pb ferr" role="alert">
          {error.message}
        </p>
      ) : (
        <DataTable
          label={t('warehouse.purchaseOrders.shortages.title')}
          columns={columns}
          rows={data}
          rowKey={(l) => l.purchaseOrderLineId ?? 0}
          loading={isLoading}
          rowActions={actions}
          empty={<EmptyState title={t('warehouse.purchaseOrders.shortages.empty')} />}
        />
      )}
      <ResolveShortageModal open={toResolve !== null} onClose={() => setToResolve(null)} po={po} line={toResolve} />
    </Panel>
  )
}

// =====================================================================================================================
// Pantalla
// =====================================================================================================================

export default function PurchaseOrderDetailScreen() {
  const t = useT()
  const lang = useLang()
  const navigate = useNavigate()
  const { publicId = '' } = useParams()
  const { data: po, isLoading, error } = usePurchaseOrder(publicId)
  const action = usePurchaseOrderAction()
  const canManage = useCan('purchasing.manage')
  const [tab, setTab] = useState<TabKey>('lines')
  const [confirmDelete, setConfirmDelete] = useState(false)

  if (isLoading) return <Spinner block />
  if (error || !po) {
    const notFound = error instanceof ApiError && error.code === 'not_found'
    return (
      <EmptyState
        title={notFound ? t('warehouse.purchaseOrders.notFound') : (error?.message ?? t('errors.generic'))}
        action={
          <Link className="btn" to="/warehouse/purchase-orders">
            {t('warehouse.purchaseOrders.back')}
          </Link>
        }
      />
    )
  }

  return (
    <div className="wrap">
      <div className="head">
        <div style={{ minWidth: 0 }}>
          <h1>
            <span className="ref">{po.number}</span> · {po.supplierName}
          </h1>
          <p>
            <StatusChip domain={STATUS_DOMAIN} code={po.statusCode} label={po.status} /> {formatDate(po.orderDate, lang)} ·{' '}
            {t('warehouse.purchaseOrders.fields.expectedDate')}: {formatDate(po.expectedDate, lang) || '—'}
          </p>
        </div>
        <div className="act">
          <Can perm="purchasing.manage">
            {po.canDelete && (
              <button type="button" className="btn danger" onClick={() => setConfirmDelete(true)}>
                {t('warehouse.purchaseOrders.delete')}
              </button>
            )}
          </Can>
        </div>
      </div>

      <div style={{ marginBottom: 14 }}>
        <StatusPipeline
          domain={STATUS_DOMAIN}
          entityType={ENTITY_TYPE}
          entityId={po.id}
          currentCode={po.statusCode}
          disabled={!canManage}
          manualTargets={MANUAL_TARGETS}
          onTransition={(toCode, comment) => {
            if (toCode === 'SENT') return action.mutateAsync({ publicId, action: 'send', body: { comment, rowVersion: po.rowVersion ?? null } })
            if (toCode === 'CANCELLED') return action.mutateAsync({ publicId, action: 'cancel', body: { comment, rowVersion: po.rowVersion ?? null } })
            return Promise.resolve()
          }}
        />
      </div>

      <div style={{ marginBottom: 14 }}>
        <Tabs<TabKey>
          label={t('warehouse.purchaseOrders.title')}
          value={tab}
          onChange={setTab}
          tabs={[
            { key: 'lines', label: t('warehouse.purchaseOrders.tabLines') },
            { key: 'shortages', label: t('warehouse.purchaseOrders.tabShortages') },
          ]}
        />
      </div>

      {tab === 'lines' && (
        <Panel icon={<IconCart />} title={t('warehouse.purchaseOrders.tabLines')}>
          <LinesTab po={po} />
        </Panel>
      )}
      {tab === 'shortages' && <ShortagesTab po={po} />}

      <ConfirmDialog
        open={confirmDelete}
        tone="danger"
        title={t('warehouse.purchaseOrders.deleteTitle')}
        message={t('warehouse.purchaseOrders.deleteBody', { number: po.number ?? '' })}
        confirmLabel={t('warehouse.purchaseOrders.delete')}
        onConfirm={async () => {
          await action.mutateAsync({ publicId, action: 'delete' })
          toast.success(t('warehouse.purchaseOrders.deleted'))
          navigate('/warehouse/purchase-orders')
        }}
        onClose={() => setConfirmDelete(false)}
      />
    </div>
  )
}
